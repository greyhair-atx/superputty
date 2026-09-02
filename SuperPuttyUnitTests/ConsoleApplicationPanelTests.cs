using System;
using System.Collections.Generic;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class ConsoleApplicationPanelTests
    {
        [TestCase(ConnectionProtocol.WINCMD, true)]
        [TestCase(ConnectionProtocol.PS, true)]
        [TestCase(ConnectionProtocol.SSH, false)]
        [TestCase(ConnectionProtocol.RDP, false)]
        [TestCase(ConnectionProtocol.VNC, false)]
        public void OnlyLocalWindowsShellsUseTheConsolePanel(ConnectionProtocol protocol, bool expected)
        {
            Assert.AreEqual(expected, ConsoleApplicationPanel.Supports(protocol));
        }

        [Test]
        public void ConsoleWindowStyleBecomesBorderlessChild()
        {
            const int wsCaption = 0x00C00000;
            const int wsBorder = 0x00800000;
            const int wsDlgFrame = 0x00400000;
            const int wsThickFrame = 0x00040000;
            const int wsSysMenu = 0x00080000;
            const int wsMinimizeBox = 0x00020000;
            const int wsMaximizeBox = 0x00010000;
            const int wsPopup = unchecked((int)0x80000000);
            const int wsChild = 0x40000000;
            const int unrelatedStyle = 0x00000020;
            int desktopStyle = wsCaption | wsBorder | wsDlgFrame | wsThickFrame |
                wsSysMenu | wsMinimizeBox | wsMaximizeBox | wsPopup | unrelatedStyle;

            int embeddedStyle = ConsoleApplicationPanel.GetEmbeddedWindowStyle(desktopStyle);

            Assert.AreEqual(wsChild, embeddedStyle & wsChild);
            Assert.AreEqual(unrelatedStyle, embeddedStyle & unrelatedStyle);
            Assert.AreEqual(0, embeddedStyle & (wsCaption | wsBorder | wsDlgFrame |
                wsThickFrame | wsSysMenu | wsMinimizeBox | wsMaximizeBox | wsPopup));
        }

        [TestCase(ConnectionProtocol.WINCMD, "cmd.exe", "/d /q")]
        [TestCase(ConnectionProtocol.PS, "powershell.exe", "-NoLogo")]
        public void ConsoleSessionsLaunchThroughDedicatedConsoleHost(
            ConnectionProtocol protocol,
            string clientName,
            string clientArguments)
        {
            PuttyStartInfo startInfo = new PuttyStartInfo(new SessionData { Proto = protocol });

            Assert.AreEqual("conhost.exe", System.IO.Path.GetFileName(startInfo.Executable).ToLowerInvariant());
            StringAssert.Contains(clientName, startInfo.Args.ToLowerInvariant());
            StringAssert.Contains(clientArguments.ToLowerInvariant(), startInfo.Args.ToLowerInvariant());
            Assert.True(System.IO.Directory.Exists(startInfo.WorkingDir));
        }

        [TestCase(ConnectionProtocol.WINCMD, "cmd-1")]
        [TestCase(ConnectionProtocol.PS, "ps-1")]
        public void LocalToolbarStartsWithEditableNumberedName(
            ConnectionProtocol protocol,
            string expected)
        {
            Assert.AreEqual(expected, frmSuperPutty.GetNextLocalToolbarName(protocol, new string[0]));
        }

        [TestCase(ConnectionProtocol.WINCMD, "cmd-3")]
        [TestCase(ConnectionProtocol.PS, "ps-3")]
        public void LocalToolbarUsesFirstAvailableNumberIgnoringCase(
            ConnectionProtocol protocol,
            string expected)
        {
            string prefix = expected.Substring(0, expected.LastIndexOf('-'));
            List<string> names = new List<string>
            {
                prefix + "-1",
                prefix.ToUpperInvariant() + "-2",
                "unrelated"
            };

            Assert.AreEqual(expected, frmSuperPutty.GetNextLocalToolbarName(protocol, names));
        }

        [Test]
        public void LocalToolbarRejectsNetworkProtocols()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                frmSuperPutty.GetNextLocalToolbarName(ConnectionProtocol.SSH, new string[0]));
        }

        [TestCase(ConnectionProtocol.WINCMD, "custom-cmd")]
        [TestCase(ConnectionProtocol.PS, "custom-ps")]
        public void LocalToolbarUsesEditableNameWithoutNetworkHost(
            ConnectionProtocol protocol,
            string name)
        {
            SessionData session = frmSuperPutty.CreateLocalToolbarSession(protocol, "  " + name + "  ");

            Assert.AreEqual(name, session.SessionName);
            Assert.AreEqual(String.Empty, session.Host);
            Assert.AreEqual(protocol, session.Proto);
            Assert.AreEqual(0, session.Port);
        }
    }
}
