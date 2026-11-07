using System;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Scp;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class RemainingReviewRegressionTests
    {
        private sealed class TestProvider : PortableSettingsProvider
        {
            public string DirectoryPath;
            public override string[] GetAppSettingsPaths() { return new[] { DirectoryPath }; }
            public override string GetAppSettingsFileName() { return "settings.xml"; }
        }

        [Test]
        public void ProviderReportsFailedSaveAndPreservesOriginalFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPutty-SaveFailure-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.xml");
            try
            {
                File.WriteAllText(path, "<Settings/>");
                var provider = new TestProvider { DirectoryPath = directory };
                provider.Initialize("test", null);
                var document = provider.SettingsXML;
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.Throws<ConfigurationErrorsException>(() => provider.SetPropertyValues(
                        new SettingsContext(), new SettingsPropertyValueCollection()));
                }
                Assert.AreEqual("<Settings/>", File.ReadAllText(path));
                Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void NullOrUnloadedTransferTargetsAreRejected()
        {
            using (var presenter = new FileTransferPresenter(new PscpOptions()))
            {
                var source = new BrowserFileInfo { Source = SourceType.Local, Path = "source" };
                Assert.IsFalse(presenter.CanTransferFile(source, null));
                Assert.IsFalse(presenter.CanTransferFile(null, new BrowserFileInfo { Path = "." }));
                Assert.IsFalse(presenter.CanTransferFile(source, new BrowserFileInfo()));
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void MissingDroppedFileIsRejectedWithoutLeavingStaleDragState()
        {
            using (var transfers = new FileTransferPresenter(new PscpOptions()))
            using (var presenter = new BrowserPresenter("Local", new LocalBrowserModel(), new SessionData(), transfers))
            using (var view = new BrowserView())
            {
                typeof(BrowserView).GetProperty("Presenter", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(view, presenter);
                var payload = new DataObject(DataFormats.FileDrop, new[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing") });
                var args = new DragEventArgs(payload, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
                Assert.DoesNotThrow(() => typeof(BrowserView).GetMethod("listViewFiles_DragEnter",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { view, args }));
                Assert.AreEqual(DragDropEffects.None, args.Effect);
                Assert.IsNull(typeof(BrowserView).GetField("dragDropFileTransfer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view));
            }
        }

        [Test]
        public void ForwardedLayoutLoadsLayoutInsteadOfOpeningSession()
        {
            string loaded = null;
            SingleInstanceHelper.DispatchCommand(new[] { "-layout", "Work Layout" },
                name => loaded = name, session => Assert.Fail("A layout command must not open a session."));
            Assert.AreEqual("Work Layout", loaded);
        }

        [Test]
        public void InvalidForwardedArgumentsDoNotExecutePartialCommand()
        {
            SingleInstanceHelper.DispatchCommand(new[] { "-layout", "Work", "-P", "invalid" },
                name => Assert.Fail("Invalid command executed."), session => Assert.Fail("Invalid command executed."));
        }

        [Test]
        public void FailedIpcForwardingReturnsFailureWithoutThrowing()
        {
            Assert.IsFalse(SingleInstanceHelper.TryLaunchInExistingInstance(new string[0],
                args => { throw new RemotingException("Endpoint unavailable"); }));
            bool sent = false;
            Assert.IsTrue(SingleInstanceHelper.TryLaunchInExistingInstance(new[] { "-layout", "Work" }, args => sent = true));
            Assert.IsTrue(sent);
        }
    }
}
