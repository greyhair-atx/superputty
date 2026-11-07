using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class LaunchRegressionTests
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW(string command, out int count);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr pointer);

        [TestCase("ssh://[2001:db8::1]:2222", "2001:db8::1", 2222)]
        [TestCase("[::1]:2200", "::1", 2200)]
        [TestCase("ssh://server.example:2222/", "server.example", 2222)]
        public void HostParserSeparatesExplicitPorts(string input, string host, int port)
        {
            var parsed = new HostConnectionString(input);
            Assert.AreEqual(host, parsed.Host);
            Assert.AreEqual(port, parsed.Port);
        }

        [TestCase("[2001:db8::1]", "2001:db8::1")]
        [TestCase("2001:db8::1", "2001:db8::1")]
        [TestCase("server.example", "server.example")]
        public void HostParserPreservesAddressesWithoutPorts(string input, string host)
        {
            var parsed = new HostConnectionString(input);
            Assert.AreEqual(host, parsed.Host);
            Assert.IsNull(parsed.Port);
        }

        [Test]
        public void VncDisplaySyntaxIsPreserved()
        {
            var parsed = new HostConnectionString("server:2", true);
            Assert.AreEqual("server:2", parsed.Host);
            Assert.IsNull(parsed.Port);
        }

        [TestCase("Folder/Server \"Production\"")]
        [TestCase("Folder/Space and trailing\\")]
        public void NewInstanceSessionArgumentsRoundTripThroughWindows(string id)
        {
            string command = "SuperPutty.exe " + SuperPutty.SuperPuTTY.BuildNewInstanceArguments("-session", id);
            int count;
            IntPtr argv = CommandLineToArgvW(command, out count);
            try
            {
                Assert.AreEqual(3, count);
                Assert.AreEqual(id, Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, 2 * IntPtr.Size)));
            }
            finally { LocalFree(argv); }
        }

        [Test]
        public void StandaloneLaunchFailureIsReportedWithoutThrowing()
        {
            var start = new PuttyStartInfo(new SessionData { Proto = ConnectionProtocol.SSH, Host = "server", Port = 22 });
            string error;
            Assert.IsFalse(start.TryStartStandalone(out error, info => { throw new Win32Exception(2); }));
            StringAssert.Contains("Tools > Options", error);
            bool launched = false;
            Assert.IsTrue(start.TryStartStandalone(out error, info => launched = true));
            Assert.IsTrue(launched);
            Assert.IsNull(error);
        }
    }
}
