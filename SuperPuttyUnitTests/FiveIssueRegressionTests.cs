using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Scp;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class FiveIssueRegressionTests
    {
        [Test]
        public void CancelFromStaleMenuDoesNotThrowForCompletedTransfer()
        {
            using (var presenter = new FileTransferPresenter(new PscpOptions()))
            {
                var transfer = new FileTransfer(new PscpOptions(), new FileTransferRequest { Session = new SessionData(), TargetFile = new BrowserFileInfo { Path = "." } });
                typeof(FileTransfer).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(transfer, FileTransfer.Status.Complete);
                typeof(FileTransferPresenter).GetMethod("AddTransfer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(presenter, new object[] { transfer });
                Assert.DoesNotThrow(() => presenter.Cancel(transfer.Id));
                Assert.AreEqual(FileTransfer.Status.Complete, transfer.TransferStatus);
            }
        }

        [Test]
        public void LockedLayoutStaysListedUntilDeletionSucceeds()
        {
            string path = Path.GetTempFileName();
            var layout = new LayoutData(path) { Name = Guid.NewGuid().ToString() };
            SuperPutty.SuperPuTTY.Layouts.Add(layout);
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.Throws<IOException>(() => SuperPutty.SuperPuTTY.RemoveLayout(layout.Name, true));
                    Assert.Contains(layout, SuperPutty.SuperPuTTY.Layouts);
                    Assert.IsTrue(File.Exists(path));
                }
                SuperPutty.SuperPuTTY.RemoveLayout(layout.Name, true);
                Assert.IsFalse(SuperPutty.SuperPuTTY.Layouts.Contains(layout));
                Assert.IsFalse(File.Exists(path));
            }
            finally
            {
                SuperPutty.SuperPuTTY.Layouts.Remove(layout);
                File.Delete(path);
            }
        }

        private const string ValidExport = "<root><container type='folder' name='Folder'><connection type='PuTTY'>" +
            "<connection_info><name>Example</name><host>localhost</host><port>22</port>" +
            "<protocol>ssh</protocol><session>Default Settings</session></connection_info>" +
            "<login><login>user</login></login></connection></container></root>";

        [TestCase("<root>")]
        [TestCase("<root><connection type='PuTTY'/></root>")]
        public void InvalidExportsProduceDescriptiveErrors(string xml)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, xml);
                Assert.That(() => PuttyDataHelper.GetAllSessionsFromPuTTYCM(path),
                    Throws.Exception.TypeOf<System.Xml.XmlException>().Or.TypeOf<InvalidDataException>());
            }
            finally { File.Delete(path); }
        }

        [TestCase("<port>22</port>", "<port>not-a-number</port>")]
        [TestCase("<port>22</port>", "<port>65536</port>")]
        [TestCase("<protocol>ssh</protocol>", "<protocol>unsupported</protocol>")]
        [TestCase("<host>localhost</host>", "")]
        public void InvalidSessionFieldsAreRejected(string original, string replacement)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, ValidExport.Replace(original, replacement));
                Assert.Throws<InvalidDataException>(() => PuttyDataHelper.GetAllSessionsFromPuTTYCM(path));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ValidExportPreservesFolderAndCaseInsensitiveProtocol()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, ValidExport);
                var sessions = PuttyDataHelper.GetAllSessionsFromPuTTYCM(path);
                Assert.AreEqual(1, sessions.Count);
                Assert.AreEqual("Folder/Example", sessions[0].SessionId);
                Assert.AreEqual(ConnectionProtocol.SSH, sessions[0].Proto);
                Assert.AreEqual("user", sessions[0].Username);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void RegistryImportSkipsInvalidSessionsAndKeepsValidOnes()
        {
            string path = @"Software\SuperPuttyTests\" + Guid.NewGuid();
            try
            {
                using (var root = Registry.CurrentUser.CreateSubKey(path))
                {
                    using (var valid = root.CreateSubKey("Valid"))
                    {
                        valid.SetValue("HostName", "localhost");
                        valid.SetValue("Protocol", "ssh");
                        valid.SetValue("PortNumber", 22);
                    }
                    using (var unknown = root.CreateSubKey("Unsupported")) unknown.SetValue("Protocol", "unknown");
                    using (var badPort = root.CreateSubKey("BadPort")) badPort.SetValue("PortNumber", "22");
                    using (var badHost = root.CreateSubKey("BadHost")) badHost.SetValue("HostName", 42);
                    var sessions = PuttyDataHelper.ReadRegistrySessions(root);
                    Assert.AreEqual(1, sessions.Count);
                    Assert.AreEqual("Valid", sessions[0].SessionName);
                }
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        }

        [TestCase(30000000, 50, 60000000L)]
        [TestCase(int.MaxValue, 1, 214748364700L)]
        [TestCase(100, 50, 200L)]
        public void LargeTransferEstimatesDoNotOverflow(int transferred, int percent, long expected)
        {
            Assert.AreEqual(expected, FileTransfer.EstimateSizeKilobytes(transferred, percent));
        }
    }
}
