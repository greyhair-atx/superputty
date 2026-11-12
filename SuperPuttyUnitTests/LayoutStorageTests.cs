using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class LayoutStorageTests
    {
        private string directory;
        private string layoutPath;
        private byte[] original;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-LayoutStorage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            layoutPath = Path.Combine(directory, "Work.xml");
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "SuperPutty.sln"))) root = root.Parent;
            Assert.IsNotNull(root, "The legacy layout fixture must be available in the repository.");
            original = File.ReadAllBytes(Path.Combine(root.FullName, "SuperPuttyUnitTests", "Fixtures", "LegacyLayout.xml"));
        }

        [TearDown]
        public void TearDown() { Directory.Delete(directory, true); }

        private byte[] ChangedLayout()
        {
            return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("DockLeftPortion=\"0.25\"", "DockLeftPortion=\"0.3\""));
        }

        private string Save(byte[] content)
        {
            return LayoutStorage.SaveAtomically(layoutPath, path => File.WriteAllBytes(path, content), frmSuperPutty.ValidateDockLayout);
        }

        [Test]
        public void LegacyDockPanelFormatLoadsWithoutOpeningApplicationContents()
        {
            File.WriteAllBytes(layoutPath, original);
            var loaded = LayoutStorage.LoadValidated(layoutPath, frmSuperPutty.ValidateDockLayout);
            CollectionAssert.AreEqual(original, loaded.Content);
            Assert.IsNull(loaded.BackupFilePath);
        }

        [Test]
        public void AtomicSaveKeepsExactlyThePreviousValidatedSave()
        {
            Save(original);
            Assert.False(File.Exists(layoutPath + ".bak"));
            Save(ChangedLayout());
            CollectionAssert.AreEqual(original, File.ReadAllBytes(layoutPath + ".bak"));
            CollectionAssert.AreEqual(ChangedLayout(), File.ReadAllBytes(layoutPath));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedSerializationOrReplacementPreservesPrimaryAndBackup(bool replacementFailure)
        {
            File.WriteAllBytes(layoutPath, original);
            File.WriteAllBytes(layoutPath + ".bak", ChangedLayout());
            if (replacementFailure)
            {
                using (var locked = new FileStream(layoutPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Assert.Throws<IOException>(() => Save(ChangedLayout()));
            }
            else
            {
                Assert.Throws<IOException>(() => LayoutStorage.SaveAtomically(layoutPath, path =>
                {
                    File.WriteAllText(path, "<DockPanel>");
                    throw new IOException("Interrupted save");
                }, frmSuperPutty.ValidateDockLayout));
            }
            CollectionAssert.AreEqual(original, File.ReadAllBytes(layoutPath));
            CollectionAssert.AreEqual(ChangedLayout(), File.ReadAllBytes(layoutPath + ".bak"));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Test]
        public void SavingOverDamagedPrimaryDoesNotPoisonKnownGoodBackup()
        {
            File.WriteAllText(layoutPath, "<DockPanel>");
            File.WriteAllBytes(layoutPath + ".bak", original);
            string preserved = Save(ChangedLayout());
            Assert.AreEqual("<DockPanel>", File.ReadAllText(preserved));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(layoutPath + ".bak"));
            CollectionAssert.AreEqual(ChangedLayout(), File.ReadAllBytes(layoutPath));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDamagedPrimaryRecoversExactBackupBytes(bool damagedPrimary)
        {
            if (damagedPrimary) File.WriteAllText(layoutPath, "<DockPanel>");
            File.WriteAllBytes(layoutPath + ".bak", original);

            var loaded = LayoutStorage.LoadValidated(layoutPath, frmSuperPutty.ValidateDockLayout);

            CollectionAssert.AreEqual(original, loaded.Content);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(layoutPath));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(layoutPath + ".bak"));
            Assert.AreEqual(layoutPath + ".bak", loaded.BackupFilePath);
            if (damagedPrimary) Assert.AreEqual("<DockPanel>", File.ReadAllText(loaded.PreservedFilePath));
            else Assert.IsNull(loaded.PreservedFilePath);
        }

        [Test]
        public void BothInvalidLayoutsRemainUntouched()
        {
            File.WriteAllText(layoutPath, "<DockPanel>");
            File.WriteAllText(layoutPath + ".bak", "<NotDockPanel/>");
            Assert.Throws<InvalidDataException>(() => LayoutStorage.LoadValidated(layoutPath, frmSuperPutty.ValidateDockLayout));
            Assert.AreEqual("<DockPanel>", File.ReadAllText(layoutPath));
            Assert.AreEqual("<NotDockPanel/>", File.ReadAllText(layoutPath + ".bak"));
            Assert.AreEqual(2, Directory.GetFiles(directory).Length);
        }

        [TestCase("2147483647")]
        [TestCase("-1")]
        [TestCase("2")]
        public void UnsafeDeclaredCountsFailBeforeCallingDockPanel(string count)
        {
            File.WriteAllText(layoutPath, "<DockPanel><Contents Count='" + count + "'><Content ID='0'/></Contents></DockPanel>");
            bool validated = false;
            Assert.Throws<InvalidDataException>(() => LayoutStorage.LoadValidated(layoutPath, bytes => validated = true));
            Assert.False(validated);
        }

        [TestCase("SuperPutty.SessionTreeview")]
        [TestCase("SuperPutty.LayoutsList")]
        [TestCase("SuperPutty.Log4netLogViewer")]
        [TestCase("SuperPutty.SessionDetail")]
        public void DuplicateSingletonToolsFailBeforeCallingDockPanel(string name)
        {
            File.WriteAllText(layoutPath, "<DockPanel><Contents Count='2'><Content ID='0' PersistString='" + name +
                "'/><Content ID='1' PersistString='" + name + "'/></Contents></DockPanel>");
            bool validated = false;
            Assert.Throws<InvalidDataException>(() => LayoutStorage.LoadValidated(layoutPath, bytes => validated = true));
            Assert.False(validated);
        }

        [Test]
        public void OversizedPrimaryRecoversWithinBoundedReadLimit()
        {
            using (var stream = File.Create(layoutPath)) stream.SetLength(LayoutStorage.MaximumLayoutBytes + 1L);
            File.WriteAllBytes(layoutPath + ".bak", original);
            var result = LayoutStorage.LoadValidated(layoutPath, frmSuperPutty.ValidateDockLayout);
            CollectionAssert.AreEqual(original, result.Content);
            Assert.AreEqual(LayoutStorage.MaximumLayoutBytes + 1L, new FileInfo(result.PreservedFilePath).Length);
        }

        [Test]
        public void InvalidPreflightDoesNotCloseOrReplaceTheCurrentWorkspace()
        {
            File.WriteAllText(layoutPath, "<DockPanel FormatVersion='1.0'/>");
            bool closed = false;
            bool applied = false;
            bool fallback = false;
            LayoutLoadResult prepared;
            Exception failure;
            bool teardown;

            bool loaded = LayoutStorage.TryApply(layoutPath, frmSuperPutty.ValidateDockLayout,
                () => closed = true, content => applied = true, () => fallback = true,
                out prepared, out failure, out teardown);

            Assert.False(loaded);
            Assert.False(teardown);
            Assert.False(closed);
            Assert.False(applied);
            Assert.False(fallback);
            Assert.IsNotNull(failure);
        }

        [Test]
        public void RuntimeFailureAfterTeardownRequestsDefaultWorkspace()
        {
            File.WriteAllBytes(layoutPath, original);
            var steps = new List<string>();
            LayoutLoadResult prepared;
            Exception failure;
            bool teardown;

            bool loaded = LayoutStorage.TryApply(layoutPath, frmSuperPutty.ValidateDockLayout,
                () => steps.Add("closed"), content => { steps.Add("apply"); throw new InvalidOperationException("Session restore failed"); },
                () => steps.Add("default"), out prepared, out failure, out teardown);

            Assert.False(loaded);
            Assert.True(teardown);
            CollectionAssert.AreEqual(new[] { "closed", "apply", "default" }, steps);
            StringAssert.Contains("Session restore", failure.Message);
        }

        [Test]
        public void DiscoveryListsBackupOnlyLayoutsWithoutRecoveryArtifacts()
        {
            foreach (string file in new[] { "Work.xml", "Work.xml.bak", "Cold.xml.bak", "Work.xml.123.tmp", "Work.xml.corrupt.123", "notes.txt" })
                File.WriteAllText(Path.Combine(directory, file), "fixture");
            CollectionAssert.AreEquivalent(new[] { layoutPath, Path.Combine(directory, "Cold.xml") }, LayoutStorage.Discover(directory));
        }

        [Test]
        public void BackupOnlyLayoutCanBeRenamedAndRecovered()
        {
            File.WriteAllBytes(layoutPath + ".bak", original);
            string renamed = Path.Combine(directory, "Renamed.xml");
            LayoutStorage.MoveFiles(layoutPath, renamed);
            Assert.False(File.Exists(layoutPath + ".bak"));
            var result = LayoutStorage.LoadValidated(renamed, frmSuperPutty.ValidateDockLayout);
            CollectionAssert.AreEqual(original, result.Content);
        }

        [Test]
        public void BackupRenameCollisionRollsBackPrimaryRename()
        {
            File.WriteAllBytes(layoutPath, original);
            File.WriteAllBytes(layoutPath + ".bak", original);
            string renamed = Path.Combine(directory, "Renamed.xml");
            File.WriteAllText(renamed + ".bak", "existing destination");
            Assert.Throws<IOException>(() => LayoutStorage.MoveFiles(layoutPath, renamed));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(layoutPath));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(layoutPath + ".bak"));
            Assert.False(File.Exists(renamed));
            Assert.AreEqual("existing destination", File.ReadAllText(renamed + ".bak"));
        }

        [Test]
        public void FloatingPreflightNeverShowsFloatingWindowsAndDisposesThem()
        {
            var document = new XmlDocument();
            using (var stream = new MemoryStream(original)) document.Load(stream);
            document.SelectSingleNode("/DockPanel/Contents/Content").Attributes["IsFloat"].Value = "True";
            document.SelectSingleNode("/DockPanel/Panes/Pane").Attributes["DockState"].Value = "Float";
            document.SelectSingleNode("/DockPanel/DockWindows/DockWindow[@DockState='DockLeft']/NestedPanes").InnerXml = "";
            document.SelectSingleNode("/DockPanel/DockWindows/DockWindow[@DockState='DockLeft']/NestedPanes").Attributes["Count"].Value = "0";
            var floats = (XmlElement)document.SelectSingleNode("/DockPanel/FloatWindows");
            floats.SetAttribute("Count", "1");
            floats.InnerXml = "<FloatWindow ID='0' Bounds='100, 100, 400, 300' ZOrderIndex='0'><NestedPanes Count='1'><Pane ID='0' RefID='0' PrevPane='-1' Alignment='Left' Proportion='0.5'/></NestedPanes></FloatWindow>";
            var windows = new List<Form>();
            bool shown = false;

            frmSuperPutty.ValidateDockLayout(Encoding.UTF8.GetBytes(document.OuterXml), window =>
            {
                windows.Add(window);
                window.Shown += (sender, args) => shown = true;
            });

            Assert.That(windows.Count, Is.GreaterThanOrEqualTo(1));
            Assert.False(shown);
            Assert.True(windows.All(window => window.IsDisposed));
        }
    }
}
