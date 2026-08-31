using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Utils;
using SuperPuTTY.Scripting;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class ArchitectureAndScriptingTests
    {
        [Test]
        public void TerminalCommandsAcceptPointerSizedWindowHandles()
        {
            MethodInfo method = typeof(CommandData).GetMethod("SendToTerminal");

            Assert.IsNotNull(method);
            Assert.AreEqual(typeof(IntPtr), method.GetParameters().Single().ParameterType);
        }

        [Test]
        public void WindowMessagingDoesNotExposeIntegerWindowHandleOverloads()
        {
            MethodInfo[] unsafeOverloads = typeof(NativeMethods)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "SendMessage" || method.Name == "PostMessage")
                .Where(method => method.GetParameters().Length > 0)
                .Where(method => method.GetParameters()[0].ParameterType != typeof(IntPtr))
                .ToArray();

            Assert.IsEmpty(unsafeOverloads, "Window handles must remain pointer-sized in x64 builds.");
        }

        [Test]
        public void SpslExecutionThreadsCannotKeepTheApplicationAlive()
        {
            ExecuteScriptEventArgs args = new ExecuteScriptEventArgs
            {
                Script = "#!/bin/spsl\nSLEEP 60000",
                Handle = new IntPtr(long.MaxValue)
            };

            System.Threading.Thread thread = SPSL.CreateExecutionThread(
                args,
                args.Script.Split('\n'));

            Assert.True(thread.IsBackground);
            Assert.AreEqual("SPSL script execution", thread.Name);
        }

        [TestCase("http://example.com/script.spsl", false)]
        [TestCase("https://example.com/script.spsl", true)]
        [TestCase("file:///C:/script.spsl", false)]
        [TestCase("https://user:password@example.com/script.spsl", false)]
        public void RemoteSpslOnlyAcceptsHttps(string location, bool expected)
        {
            Uri uri;
            Assert.AreEqual(expected, RemoteSpslLoader.TryGetSecureUri(location, out uri));
        }

        [TestCase("http://api.github.com/releases/latest", false)]
        [TestCase("https://api.github.com/releases/latest", true)]
        [TestCase("https://user:password@api.github.com/releases/latest", false)]
        public void UpdateRequestsOnlyAcceptCredentialFreeHttps(string location, bool expected)
        {
            Uri uri;
            Assert.AreEqual(expected, UpdateRequestClient.TryGetSecureUri(location, out uri));
        }
    }
}
