using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SessionConnectionStateTests
    {
        [Test]
        public void RunningExternalClientIsUnknownRatherThanProofOfConnection()
        {
            using (var panel = new ApplicationPanel(ConnectionProtocol.SSH))
            using (var process = Process.GetCurrentProcess())
            {
                SetProcess(panel, process);
                Assert.AreEqual(SessionConnectionState.Unknown, panel.ConnectionState);
                Assert.IsTrue(panel.IsSessionActive, "Uncertainty must retain the close warning.");
            }
        }

        [Test]
        public void ExitedExternalClientIsDisconnected()
        {
            using (var panel = new ApplicationPanel(ConnectionProtocol.SSH))
            using (var process = Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("COMSPEC"), "/c exit 0")
                { UseShellExecute = false, CreateNoWindow = true }))
            {
                Assert.IsTrue(process.WaitForExit(5000));
                SetProcess(panel, process);
                Assert.AreEqual(SessionConnectionState.Disconnected, panel.ConnectionState);
                Assert.IsFalse(panel.IsSessionActive);
            }
        }

        [Test]
        public void UnreadableExternalProcessRetainsTheWarning()
        {
            using (var panel = new ApplicationPanel(ConnectionProtocol.SSH))
            using (var process = new Process())
            {
                SetProcess(panel, process);
                Assert.AreEqual(SessionConnectionState.Unknown, panel.ConnectionState);
                Assert.IsTrue(panel.IsSessionActive);
            }
        }

        [Test]
        public void PendingExternalStartupRetainsTheWarningBeforeAProcessExists()
        {
            using (var panel = new ApplicationPanel(ConnectionProtocol.SSH))
            {
                Assert.AreEqual(SessionConnectionState.Disconnected, panel.ConnectionState);
                typeof(ApplicationPanel).GetField("startupInProgress", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, true);
                Assert.AreEqual(SessionConnectionState.Connecting, panel.ConnectionState);
                Assert.IsTrue(panel.IsSessionActive);
            }
        }

        [Test]
        public void RdpConnectingAndReconnectWindowsMustNotBeMistakenForDisconnection()
        {
            var tracker = new RdpConnectionStateTracker();
            Assert.AreEqual(SessionConnectionState.Disconnected, tracker.Resolve(0));
            tracker.Connecting();
            Assert.AreEqual(SessionConnectionState.Connecting, tracker.Resolve(0));
            Assert.AreEqual(SessionConnectionState.Connecting, tracker.Resolve(2));
            tracker.Connected();
            Assert.AreEqual(SessionConnectionState.Connected, tracker.Resolve(1));
            tracker.Reconnecting();
            tracker.Connecting(); // Some clients also raise OnConnecting during a retry.
            foreach (short nativeState in new short[] { 0, 1, 2 })
                Assert.AreEqual(SessionConnectionState.Reconnecting, tracker.Resolve(nativeState));
            tracker.Connected();
            Assert.AreEqual(SessionConnectionState.Connected, tracker.Resolve(1));
            tracker.Disconnected();
            Assert.AreEqual(SessionConnectionState.Disconnected, tracker.Resolve(0));
        }

        [Test]
        public void NativeRdpStateCorrectsMissedEventsButUnavailableStateIsUnknown()
        {
            var tracker = new RdpConnectionStateTracker();
            Assert.AreEqual(SessionConnectionState.Connected, tracker.Resolve(1));
            tracker.Connected();
            Assert.AreEqual(SessionConnectionState.Disconnected, tracker.Resolve(0));
            Assert.AreEqual(SessionConnectionState.Reconnecting, tracker.Resolve(2));
            Assert.AreEqual(SessionConnectionState.Unknown, tracker.Resolve(null));
            Assert.AreEqual(SessionConnectionState.Unknown, tracker.Resolve(99));
        }

        private static void SetProcess(ApplicationPanel panel, Process process)
        {
            typeof(ApplicationPanel).GetField("m_Process", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, process);
        }
    }
}
