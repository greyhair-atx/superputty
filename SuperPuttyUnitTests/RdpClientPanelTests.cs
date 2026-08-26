using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class RdpClientPanelTests
    {
        [Test]
        public void UsesActiveXForOrdinaryMstscSession()
        {
            SessionData session = CreateRdpSession();

            Assert.IsTrue(RdpClientPanel.ShouldUseActiveX(session, @"C:\Windows\System32\mstsc.exe"));
        }

        [Test]
        public void KeepsFreeRdpAndCustomMstscArgumentsOnExternalHost()
        {
            SessionData session = CreateRdpSession();
            Assert.IsFalse(RdpClientPanel.ShouldUseActiveX(session, @"C:\Tools\wfreerdp.exe"));

            session.ExtraArgs = "/admin";
            Assert.IsFalse(RdpClientPanel.ShouldUseActiveX(session, @"C:\Windows\System32\mstsc.exe"));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ActiveXControlCanBeCreatedWithoutStartingAConnection()
        {
            RdpClientPanel panel = null;
            try
            {
                Assert.IsTrue(RdpClientPanel.TryCreate(CreateRdpSession(), delegate { }, out panel));
                Assert.IsNotNull(panel);
                Assert.AreNotEqual(System.IntPtr.Zero, panel.AppWindowHandle);
            }
            finally
            {
                if (panel != null)
                {
                    panel.Dispose();
                }
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ActiveXControlCanBeRepeatedlyCreatedShownHiddenAndDisposed()
        {
            for (int i = 0; i < 10; i++)
            {
                RdpClientPanel panel = null;
                Form host = null;
                try
                {
                    Assert.IsTrue(RdpClientPanel.TryCreate(CreateRdpSession(), delegate { }, out panel));
                    host = new Form();
                    host.Controls.Add(panel);
                    panel.Show();
                    panel.Hide();
                }
                finally
                {
                    if (host != null)
                    {
                        host.Dispose();
                    }
                    else if (panel != null)
                    {
                        panel.Dispose();
                    }

                    Application.DoEvents();
                }
            }
        }

        private static SessionData CreateRdpSession()
        {
            return new SessionData
            {
                Proto = ConnectionProtocol.RDP,
                Host = "rdp.example.com",
                Port = 3389
            };
        }
    }
}
