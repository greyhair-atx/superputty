using System;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CSharp;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable]
    public class ReviewFixTests
    {
        private sealed class DeferredPanel : ApplicationPanel
        {
            private bool captured;
            public DeferredPanel() : base(ConnectionProtocol.SSH) { }
            public override bool ExternalProcessCaptured { get { return captured; } }
            public void CompleteCapture() { captured = true; NotifyWindowCaptured(); }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void SessionActionsWaitForCaptureAndRunOnlyOnce()
        {
            using (var panel = new DeferredPanel())
            {
                int runs = 0;
                panel.WhenCaptured(() => runs++);
                Assert.AreEqual(0, runs);
                panel.CompleteCapture();
                panel.CompleteCapture();
                Assert.AreEqual(1, runs);
                panel.WhenCaptured(() => runs++);
                Assert.AreEqual(2, runs);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExternalClientArgumentsEncodePathsAndKeepLocalDirectoryOneArgument(bool winScp)
        {
            SessionData session = new SessionData
            {
                Host = "2001:db8::1", Port = 22, Username = "a@b",
                RemotePath = "/reports x/\" -command=bad/#?%",
                LocalPath = "C:\\local files\\", Password = "secret"
            };
            string args = ExternalApplications.BuildArguments(session, winScp, false);
            StringAssert.StartsWith("sftp://a%40b@[2001:db8::1]:22/reports%20x/%22%20-command%3Dbad/%23%3F%25", args);
            StringAssert.DoesNotContain("secret", args);
            StringAssert.Contains(winScp ? "\"LocalDirectory=C:\\local files\\\\\"" : "\"--local=C:\\local files\\\\\"", args);
            session.Host = "server -command=bad";
            Assert.Throws<ArgumentException>(() => ExternalApplications.BuildArguments(session, winScp, false));
        }

        [Test]
        public void DisabledPasswordPolicyCoversDefaultsAndEnvironmentExpansion()
        {
            var settings = SuperPutty.SuperPuTTY.Settings;
            bool original = settings.AllowPlainTextPuttyPasswordArg;
            string defaults = settings.PuttyDefaultParameters;
            string variable = "SUPERPUTTY_REVIEW_" + Guid.NewGuid().ToString("N");
            try
            {
                Environment.SetEnvironmentVariable(variable, "-pw default-secret");
                settings.AllowPlainTextPuttyPasswordArg = false;
                settings.PuttyDefaultParameters = "-batch %" + variable + "%";
                var info = new PuttyStartInfo(new SessionData
                {
                    Host = "server", Port = 22, Proto = ConnectionProtocol.SSH,
                    ExtraArgs = "-pw extra-secret -N", Password = "session-secret"
                });
                StringAssert.DoesNotContain("secret", info.Args);
                StringAssert.DoesNotContain("-pw", info.Args);
                StringAssert.Contains("-batch", info.Args);
                StringAssert.Contains("-N", info.Args);
            }
            finally
            {
                settings.AllowPlainTextPuttyPasswordArg = original;
                settings.PuttyDefaultParameters = defaults;
                Environment.SetEnvironmentVariable(variable, null);
            }
        }

        [Test]
        public void SavingLegacySessionSanitizesXmlAndNewBackupWithoutChangingLiveArguments()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyReview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "Sessions.xml");
            try
            {
                File.WriteAllText(file, "<ArrayOfSessionData><SessionData SessionId=\"test\" SessionName=\"test\" Host=\"server\" ExtraArgs=\"-pw legacy-secret -N\" /></ArrayOfSessionData>");
                var sessions = SessionData.LoadSessionsFromFile(file);
                Assert.AreEqual(1, sessions.Count);
                StringAssert.Contains("legacy-secret", sessions[0].ExtraArgs);
                SessionData.SaveSessionsToFile(sessions, file);
                Assert.Greater(Directory.GetFiles(directory).Length, 1);
                foreach (string saved in Directory.GetFiles(directory))
                {
                    StringAssert.DoesNotContain("legacy-secret", File.ReadAllText(saved));
                    StringAssert.Contains("-N", File.ReadAllText(saved));
                }
                StringAssert.Contains("legacy-secret", sessions[0].ExtraArgs);
                Assert.AreEqual("-N", SessionData.LoadSessionsFromFile(file)[0].ExtraArgs);
            }
            finally
            {
                foreach (string saved in Directory.GetFiles(directory)) File.Delete(saved);
                Directory.Delete(directory);
            }
        }

        [Test]
        public void PasswordWithEscapedQuoteIsFullyRemoved()
        {
            string args = "-pw " + CommandLineOptions.QuoteArgument("first\" secret remainder") + " -N";
            Assert.AreEqual("-N", CommandLineOptions.RemoveSensitiveArguments(args));
        }

        [Test]
        public async Task HungGuiStartupIsAsynchronousBoundedAndCancelable()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyStartup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string executable = Path.Combine(directory, "DelayedGui.exe");
            try
            {
                using (var compiler = new CSharpCodeProvider())
                {
                    var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = executable,
                        CompilerOptions = "/target:winexe" };
                    options.ReferencedAssemblies.Add("System.dll");
                    options.ReferencedAssemblies.Add("System.Drawing.dll");
                    options.ReferencedAssemblies.Add("System.Windows.Forms.dll");
                    var compiled = compiler.CompileAssemblyFromSource(options,
                        "class DelayedGui { [System.STAThread] static void Main() { System.Threading.Thread.Sleep(60000); System.Windows.Forms.Application.Run(new System.Windows.Forms.Form()); } }");
                    Assert.IsFalse(compiled.Errors.HasErrors, String.Join("; ", compiled.Errors.Cast<CompilerError>().Select(e => e.ToString())));
                }
                using (Process process = Process.Start(new ProcessStartInfo(executable)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    try
                    {
                        Stopwatch elapsed = Stopwatch.StartNew();
                        Task wait = ApplicationPanel.WaitForInputIdleAsync(process, 400, CancellationToken.None);
                        Assert.Less(elapsed.ElapsedMilliseconds, 200, "Starting the wait must not block the caller.");
                        Assert.ThrowsAsync<TimeoutException>(async () => await wait);
                        using (var cancellation = new CancellationTokenSource())
                        {
                            Task canceled = ApplicationPanel.WaitForInputIdleAsync(process, 10000, cancellation.Token);
                            cancellation.Cancel();
                            try { await canceled; Assert.Fail("Startup should cancel."); }
                            catch (OperationCanceledException) { }
                        }
                    }
                    finally { if (!process.HasExited) process.Kill(); process.WaitForExit(5000); }
                }
            }
            finally { File.Delete(executable); Directory.Delete(directory); }
        }
    }
}

