using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class DocumentSelectorTests
    {
        [Test]
        public void UserCloseHidesAndKeepsSelectorReusable()
        {
            using (TestableDocumentSelector selector = new TestableDocumentSelector())
            {
                FormClosingEventArgs args = new FormClosingEventArgs(CloseReason.UserClosing, false);

                selector.SimulateClosing(args);

                Assert.True(args.Cancel);
            }
        }

        [TestCase(CloseReason.FormOwnerClosing)]
        [TestCase(CloseReason.ApplicationExitCall)]
        [TestCase(CloseReason.WindowsShutDown)]
        public void ApplicationCloseIsNotCanceled(CloseReason reason)
        {
            using (TestableDocumentSelector selector = new TestableDocumentSelector())
            {
                FormClosingEventArgs args = new FormClosingEventArgs(reason, false);

                selector.SimulateClosing(args);

                Assert.False(args.Cancel);
            }
        }

        private sealed class TestableDocumentSelector : SuperPutty.frmDocumentSelector
        {
            public TestableDocumentSelector()
                : base(new DockPanel())
            {
            }

            public void SimulateClosing(FormClosingEventArgs args)
            {
                base.OnFormClosing(args);
            }
        }
    }
}
