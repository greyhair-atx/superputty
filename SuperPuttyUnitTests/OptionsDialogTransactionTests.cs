using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;
using App = SuperPutty.SuperPuTTY;
using Preferences = SuperPutty.Properties.Settings;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class OptionsDialogTransactionTests
    {
        private sealed class IsolatedProvider : PortableSettingsProvider
        {
            internal string DirectoryPath;
            public override string[] GetAppSettingsPaths() { return new[] { DirectoryPath }; }
            public override string GetAppSettingsFileName() { return "isolated.settings"; }
        }

        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo DefaultSettings = typeof(Preferences).GetField("defaultInstance", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo FirstRun = typeof(App).GetField("isFirstRun", BindingFlags.Static | BindingFlags.NonPublic);
        private Preferences originalSettings;
        private App.PreparedCatalog originalCatalog;
        private List<LayoutData> originalLayouts;
        private object originalFirstRun;
        private string directory, first, second, preferencesFile;

        [SetUp]
        public void SetUp()
        {
            originalSettings = App.Settings;
            originalCatalog = App.CaptureCatalog();
            originalLayouts = new List<LayoutData>(App.Layouts);
            originalFirstRun = FirstRun.GetValue(null);
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-OptionsDialog-" + Guid.NewGuid().ToString("N"));
            first = Path.Combine(directory, "first");
            second = Path.Combine(directory, "second");
            preferencesFile = Path.Combine(directory, "isolated.settings");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);

            // Redirect every settings property before the instance reads or writes
            // any values, including if the regression unexpectedly commits Options.
            var settings = new Preferences();
            var provider = new IsolatedProvider { DirectoryPath = directory };
            provider.Initialize("IsolatedOptions", new NameValueCollection());
            foreach (SettingsProperty property in settings.Properties) property.Provider = provider;
            settings.Providers.Clear();
            settings.Providers.Add(provider);
            DefaultSettings.SetValue(null, settings);
            FirstRun.SetValue(null, false);
            settings.SettingsFolder = first;
            settings.PuttyExe = typeof(App).Assembly.Location;
            App.ReplaceSessionCatalog(new SessionData[0]);
            App.LoadInitialSessions();
        }

        [TearDown]
        public void TearDown()
        {
            DefaultSettings.SetValue(null, originalSettings);
            FirstRun.SetValue(null, originalFirstRun);
            App.AcceptCatalog(originalCatalog, true);
            bool notify = App.Layouts.RaiseListChangedEvents;
            try
            {
                App.Layouts.RaiseListChangedEvents = false;
                App.Layouts.Clear();
                foreach (var layout in originalLayouts) App.Layouts.Add(layout);
            }
            finally { App.Layouts.RaiseListChangedEvents = notify; }
            if (notify) App.Layouts.ResetBindings();
            Directory.Delete(directory, true);
        }

        [Test]
        public void LayoutSaveCannotCreateOrReplaceTheActiveSessionCatalog()
        {
            string catalog = Path.Combine(first, "Sessions.XML");
            string alias = Path.Combine(first, ".", "sessions.xml");
            Assert.Throws<IOException>(() => App.SaveLayoutFile(alias, path => Assert.Fail("Must reject before staging."), bytes => { }));
            Assert.False(File.Exists(catalog));

            const string original = "<ArrayOfSessionData><SessionData SessionId='Keep' SessionName='Keep'/></ArrayOfSessionData>";
            File.WriteAllText(catalog, original);
            Assert.Throws<IOException>(() => App.SaveLayoutFile(alias, path => Assert.Fail("Must reject before staging."), bytes => { }));
            Assert.AreEqual(original, File.ReadAllText(catalog));
            Assert.AreEqual(1, Directory.GetFiles(first).Length);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LayoutSaveCannotReplacePreferencesOrTheirBackup(bool backup)
        {
            Assert.AreEqual(preferencesFile, App.Settings.SettingsFilePath);
            string path = backup ? preferencesFile + ".bak" : preferencesFile;
            File.WriteAllText(path, "damaged preferences");
            Assert.Throws<IOException>(() => App.SaveLayoutFile(path, staged => Assert.Fail("Must reject before staging."), bytes => { }));
            Assert.AreEqual("damaged preferences", File.ReadAllText(path));
            Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        }

        [Test]
        public void CancellingModalOptionsDuringCatalogPreparationDoesNotCommitWhenTheReadCompletes()
        {
            var original = new SessionData { SessionId = "Original", SessionName = "Original" };
            App.AddSession(original);
            string destination = Path.Combine(second, "Sessions.XML");
            new SessionStorage().Save(new List<SessionData> {
                new SessionData { SessionId = "Destination", SessionName = "Destination" }
            }, destination);
            byte[] destinationBefore = File.ReadAllBytes(destination);
            bool cancelledWhileLoading = false;
            using (var held = File.Open(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var dialog = new dlgFindPutty())
            using (var watchdog = new System.Windows.Forms.Timer { Interval = 20 })
            {
                var tabs = Field<TabControl>(dialog, "tabControl");
                var ok = Field<Button>(dialog, "buttonOk");
                Field<TextBox>(dialog, "textBoxSettingsFolder").Text = second;
                dialog.Shown += delegate { ok.PerformClick(); };
                var elapsed = Stopwatch.StartNew();
                watchdog.Tick += delegate
                {
                    if (!tabs.Enabled)
                    {
                        cancelledWhileLoading = true;
                        Field<Button>(dialog, "buttonCancel").PerformClick();
                        held.Dispose();
                        watchdog.Stop();
                    }
                    else if (elapsed.ElapsedMilliseconds > 5000) dialog.Close();
                };
                watchdog.Start();
                Assert.AreEqual(DialogResult.Cancel, dialog.ShowDialog());
                held.Dispose();
                PumpUntil(() => ok.Enabled, 5000);
                Assert.IsTrue(cancelledWhileLoading, "The modal dialog did not enter its asynchronous load.");
                Assert.IsTrue(ok.Enabled, "The cancelled load did not finish.");
                Assert.AreEqual(first, App.Settings.SettingsFolder);
                Assert.AreSame(original, App.GetSessionById("Original"));
                Assert.IsNull(App.GetSessionById("Destination"));
                Assert.IsFalse(File.Exists(preferencesFile), "Cancelling must not write preferences.");
                CollectionAssert.AreEqual(destinationBefore, File.ReadAllBytes(destination));
            }
        }

        [Test]
        public void FailedPreferenceWriteKeepsTheTreeAppearanceUntilAWriteSucceeds()
        {
            var settings = App.Settings;
            settings.SessionsTreeShowLines = true;
            int applied = 0;
            settings.SettingsApplied += delegate { applied++; };
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                var tree = (TreeView)typeof(SessionTreeview).GetField("treeView1", PrivateInstance).GetValue(view);
                Assert.IsTrue(tree.ShowLines);
                var changes = new Dictionary<string, object> {
                    { nameof(Preferences.SessionsTreeShowLines), false }
                };
                // Simulate another instance creating preferences after this instance
                // accepted the missing file. The real provider must reject its write.
                const string otherPreferences = "<Settings><UpdateChannel>Other instance</UpdateChannel></Settings>";
                File.WriteAllText(preferencesFile, otherPreferences);
                Assert.Throws<ConfigurationErrorsException>(() =>
                    dlgFindPutty.CommitSettingsChanges(changes, settings.Save));
                Assert.AreEqual(otherPreferences, File.ReadAllText(preferencesFile));
                Assert.IsTrue(settings.SessionsTreeShowLines);
                Assert.IsTrue(tree.ShowLines);
                Assert.AreEqual(0, applied);

                File.Delete(preferencesFile);
                Assert.DoesNotThrow(() => dlgFindPutty.CommitSettingsChanges(changes, settings.Save));
                Assert.IsFalse(settings.SessionsTreeShowLines);
                Assert.IsFalse(tree.ShowLines);
                Assert.AreEqual(1, applied);
            }
        }

        [Test]
        public void CancelledPreferenceWriteRollsBackAndNeverRaisesSettingsApplied()
        {
            var settings = App.Settings;
            bool original = settings.SessionsTreeShowLines;
            int applied = 0;
            settings.SettingsApplied += delegate { applied++; };
            SettingsSavingEventHandler cancel = (sender, args) => args.Cancel = true;
            settings.SettingsSaving += cancel;
            try
            {
                Assert.Throws<InvalidOperationException>(() => dlgFindPutty.CommitSettingsChanges(
                    new Dictionary<string, object> { { nameof(Preferences.SessionsTreeShowLines), !original } }, settings.Save));
                Assert.AreEqual(original, settings.SessionsTreeShowLines);
                Assert.AreEqual(0, applied);
                Assert.IsFalse(File.Exists(preferencesFile));
            }
            finally { settings.SettingsSaving -= cancel; }
        }

        [Test]
        public void SuccessfulPreferenceWriteSurvivesARefreshFailureAndNotifiesRemainingControls()
        {
            int applied = 0;
            var settings = App.Settings;
            settings.SettingsApplied += delegate { throw new InvalidOperationException("A test control could not refresh"); };
            settings.SettingsApplied += delegate { applied++; };
            Assert.DoesNotThrow(settings.Save);
            Assert.IsTrue(File.Exists(preferencesFile));
            Assert.AreEqual(1, applied);
        }

        [Test]
        public void FirstRunAcceptsAnUnchangedFolderBeforeTheFirstSessionSave()
        {
            App.AcceptCatalog(new App.PreparedCatalog { Path = null, Storage = new SessionStorage(),
                Sessions = new List<SessionData>(), Ready = true }, true);
            FirstRun.SetValue(null, true);
            using (var dialog = new dlgFindPutty())
            using (var watchdog = new System.Windows.Forms.Timer { Interval = 100 })
            {
                var elapsed = Stopwatch.StartNew();
                watchdog.Tick += delegate { if (elapsed.ElapsedMilliseconds > 10000) dialog.Close(); };
                dialog.Shown += delegate { Field<Button>(dialog, "buttonOk").PerformClick(); };
                watchdog.Start();
                Assert.AreEqual(DialogResult.OK, dialog.ShowDialog());
                Assert.IsTrue(App.HasAcceptedCatalog(first));
                Assert.IsTrue(File.Exists(preferencesFile));
                App.AddSession(new SessionData { SessionId = "First", SessionName = "First" });
                Assert.DoesNotThrow(App.SaveSessions);
                Assert.IsTrue(File.Exists(Path.Combine(first, "Sessions.XML")));
            }
        }

        private static T Field<T>(dlgFindPutty dialog, string name)
        {
            return (T)typeof(dlgFindPutty).GetField(name, PrivateInstance).GetValue(dialog);
        }

        private static void PumpUntil(Func<bool> completed, int timeoutMilliseconds)
        {
            var elapsed = Stopwatch.StartNew();
            while (!completed() && elapsed.ElapsedMilliseconds < timeoutMilliseconds)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }
    }
}
