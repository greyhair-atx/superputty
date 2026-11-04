using System.Threading;
using System.Reflection;
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
        [Apartment(ApartmentState.STA)]
        public void ConnectionEventsDistinguishPendingRetriesFromAnIdleControl()
        {
            RdpClientPanel panel;
            Assert.IsTrue(RdpClientPanel.TryCreate(CreateRdpSession(), delegate { }, out panel));
            using (panel)
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(RdpClientPanel).GetMethod("Client_OnConnecting", flags).Invoke(panel, new object[] { panel, System.EventArgs.Empty });
                Assert.AreEqual(SessionConnectionState.Connecting, panel.ConnectionState);
                Assert.IsTrue(panel.IsSessionActive);
                typeof(RdpClientPanel).GetMethod("Client_OnAutoReconnecting", flags).Invoke(panel, new object[] { panel, null });
                Assert.AreEqual(SessionConnectionState.Reconnecting, panel.ConnectionState);
                var disconnected = typeof(RdpClientPanel).GetMethod("Client_OnDisconnected", flags);
                var args = System.Activator.CreateInstance(disconnected.GetParameters()[1].ParameterType, new object[] { 1 });
                disconnected.Invoke(panel, new object[] { panel, args });
                Assert.IsTrue(panel.ExternalProcessCaptured, "Disconnecting does not destroy the ActiveX window.");
                Assert.AreEqual(SessionConnectionState.Disconnected, panel.ConnectionState);
                Assert.IsFalse(panel.IsSessionActive);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void BackgroundInspectionDoesNotCallTheActiveXControlAcrossThreads()
        {
            RdpClientPanel panel;
            Assert.IsTrue(RdpClientPanel.TryCreate(CreateRdpSession(), delegate { }, out panel));
            using (panel)
            {
                SessionConnectionState observed = SessionConnectionState.Disconnected;
                var worker = new Thread(() => observed = panel.ConnectionState) { IsBackground = true };
                worker.Start();
                Assert.IsTrue(worker.Join(2000), "State inspection attempted a blocking COM call.");
                Assert.AreEqual(SessionConnectionState.Unknown, observed);
            }
        }

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
                Assert.AreEqual(SessionConnectionState.Disconnected, panel.ConnectionState,
                    "An existing ActiveX window without a connection is not an active session.");
                Assert.IsFalse(panel.IsSessionActive);
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
