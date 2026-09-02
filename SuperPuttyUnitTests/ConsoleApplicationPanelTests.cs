using System;
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
    }
}
