using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using Microsoft.CSharp;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Scp;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests.Scp
{
    [TestFixture, NonParallelizable]
    public class PscpOutputSafetyTests
    {
        private string directory, executable;

        [OneTimeSetUp]
        public void BuildOutputFixture()
        {
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-Output-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            executable = Path.Combine(directory, "FakePscp.exe");
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = executable };
                options.ReferencedAssemblies.Add("System.dll");
                var compiled = compiler.CompileAssemblyFromSource(options, @"
using System; using System.IO; using System.Threading;
class FakePscp {
 static int Main(string[] args) {
  int pid = Array.IndexOf(args, ""-pid"");
  if(pid >= 0) File.WriteAllText(args[pid+1], System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
  if(Array.IndexOf(args, ""-long-later"") >= 0)
   for(int i=0;i<12;i++) Console.WriteLine(""diagnostic"");
  if(Array.IndexOf(args, ""-long-first"") >= 0 || Array.IndexOf(args, ""-long-later"") >= 0) {
   Console.Write(new string('x', 65540)); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite);
  }
  if(Array.IndexOf(args, ""-many-lines"") >= 0) {
   Console.Write(new string('\n', 100005)); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite);
  }
  if(Array.IndexOf(args, ""-early-error"") >= 0) Console.WriteLine(""Unable to open remote file"");
  if(Array.IndexOf(args, ""-many-characters"") >= 0)
   for(int i=0;i<1025;i++) Console.WriteLine(new string('x', 8192));
  Console.WriteLine(""-rw-r--r-- 1 user group 123 Nov  9 12:30 normal file.txt"");
  return 0;
 }
}");
                Assert.IsFalse(compiled.Errors.HasErrors, String.Join("\n", compiled.Errors.Cast<CompilerError>()));
            }
        }

        [OneTimeTearDown]
        public void RemoveOutputFixture()
        {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }

        private PscpClient NewClient(string extraArguments)
        {
            return new PscpClient(new PscpOptions { PscpLocation = executable, TimeoutMs = 10000 },
                new SessionData { Host = "example.invalid", Username = "user", Port = 22, ExtraArgs = extraArguments });
        }

        [TestCase("prefix\" private-tail")]
        [TestCase("secret with trailing slash\\")]
        [TestCase("slashes\\\\\" private-tail")]
        public void DirectoryListingLogsPlaceholderInsteadOfParsingThePassword(string password)
        {
            var logger = (Logger)LogManager.GetLogger(typeof(PscpClient).Assembly, typeof(PscpClient)).Logger;
            var oldLevel = logger.Level;
            var oldThreshold = logger.Hierarchy.Threshold;
            bool oldConfigured = logger.Hierarchy.Configured;
            bool oldPlaintext = SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg;
            var appender = new MemoryAppender();
            appender.ActivateOptions();
            logger.AddAppender(appender);
            logger.Level = Level.Info;
            logger.Hierarchy.Threshold = Level.All;
            logger.Hierarchy.Configured = true;
            try
            {
                SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg = true;
                var client = NewClient(null);
                client.Session.Password = password;
                Assert.AreEqual(ResultStatusCode.Success, client.ListDirectory(RemoteBrowserModel.NewDirectory("/")).StatusCode);
                string logged = appender.GetEvents().Select(e => e.RenderedMessage)
                    .Single(message => message.StartsWith("Starting process:"));
                StringAssert.Contains("-pw XXXXX", logged);
                StringAssert.DoesNotContain("private-tail", logged);
                StringAssert.DoesNotContain(password, logged);
            }
            finally
            {
                SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg = oldPlaintext;
                logger.RemoveAppender(appender);
                logger.Level = oldLevel;
                logger.Hierarchy.Threshold = oldThreshold;
                logger.Hierarchy.Configured = oldConfigured;
                appender.Close();
            }
        }

        [Test]
        public void LegacyReplacementUsesEscapingAwareRedactionAndLiteralReplacementText()
        {
            string arguments = "-pw " + CommandLineOptions.QuoteArgument("prefix\" private-tail") + " -P 22";
            string replaced = CommandLineOptions.replacePassword(arguments, "$1");
            Assert.AreEqual("-pw $1 -P 22", replaced);
            StringAssert.DoesNotContain("private-tail", CommandLineOptions.RedactSensitiveArguments(arguments));
        }

        [TestCase("-long-first")]
        [TestCase("-long-later")]
        [TestCase("-many-lines")]
        [TestCase("-many-characters")]
        public void OversizedListingFailsWithoutPublishingPartialFilesAndStopsItsProcess(string mode)
        {
            string pidPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pid");
            var elapsed = Stopwatch.StartNew();
            var result = NewClient(mode + " -pid " + CommandLineOptions.QuoteArgument(pidPath))
                .ListDirectory(RemoteBrowserModel.NewDirectory("/"));
            Assert.AreEqual(ResultStatusCode.Error, result.StatusCode);
            Assert.AreEqual(ScpOutputLimitException.SafeMessage, result.ErrorMsg);
            Assert.IsEmpty(result.Files, "A partial directory must never appear successful.");
            Assert.Less(elapsed.ElapsedMilliseconds, 8000, "The output limit must stop the process before its inactivity timeout.");
            int processId = Int32.Parse(File.ReadAllText(pidPath));
            try
            {
                using (var child = Process.GetProcessById(processId))
                    Assert.IsTrue(child.WaitForExit(2000), "PSCP survived an output-limit failure.");
            }
            catch (ArgumentException) { }
        }

        [Test]
        public void NormalListingStillPreservesNamesAndParsesMetadata()
        {
            var result = NewClient(null).ListDirectory(RemoteBrowserModel.NewDirectory("/"));
            Assert.AreEqual(ResultStatusCode.Success, result.StatusCode, result.ErrorMsg);
            Assert.AreEqual("normal file.txt", result.Files.Single().Name);
            Assert.AreEqual("/normal file.txt", result.Files.Single().Path);
            Assert.AreEqual(123, result.Files.Single().Size);
        }

        [Test]
        public void LongTransferKeepsStreamingProgressWithoutRetainingEveryOutputLine()
        {
            var result = NewClient("-many-characters").CopyFiles(new List<BrowserFileInfo>
                { new BrowserFileInfo { Path = "/test.txt", Source = SourceType.Remote } },
                new BrowserFileInfo { Path = directory, Source = SourceType.Local, Type = FileType.Directory }, null);
            Assert.AreEqual(ResultStatusCode.Success, result.StatusCode, result.ErrorMsg);
        }

        [Test]
        public void TransferRemembersAnEarlyErrorAfterItsDiagnosticTailHasBeenEvicted()
        {
            var result = NewClient("-early-error -many-characters").CopyFiles(new List<BrowserFileInfo>
                { new BrowserFileInfo { Path = "/test.txt", Source = SourceType.Remote } },
                new BrowserFileInfo { Path = directory, Source = SourceType.Local, Type = FileType.Directory }, null);
            Assert.AreEqual(ResultStatusCode.Error, result.StatusCode);
            Assert.AreEqual("Unable to open remote file", result.ErrorMsg);
        }

        [Test]
        public void CompleteCaptureRejectsOverflowAndDiagnosticCaptureKeepsTheNewestLines()
        {
            var complete = new ScpOutputCapture(true, 12, 3);
            complete.Add("one"); complete.Add("two"); complete.Add("tri");
            Assert.Throws<ScpOutputLimitException>(() => complete.Add("four"));
            CollectionAssert.AreEqual(new[] { "one", "two", "tri" }, complete.ToArray());
            var diagnostic = new ScpOutputCapture(false, 12, 3);
            for (int i = 0; i < 10000; i++) diagnostic.Add("line " + i);
            CollectionAssert.AreEqual(new[] { "line 9999" }, diagnostic.ToArray());
        }

        [Test]
        public void EmptyLinesConsumeTheCaptureLineBudget()
        {
            var output = new ScpOutputCapture(true, 100, 2);
            output.Add(""); output.Add("");
            Assert.Throws<ScpOutputLimitException>(() => output.Add(""));
        }

        [TestCase("\r")]
        [TestCase("\r\n")]
        public void ShortProgressLinesExceedingTheLineLimitInAggregateRemainValid(string separator)
        {
            string progress = "file | 123 kB | 2.5 kB/s | ETA: 00:01 | 50%";
            string data = String.Join(separator, Enumerable.Repeat(progress, 3000)) + separator;
            Assert.Greater(data.Length, ScpOutputCapture.MaximumLineCharacters);
            int notifications = 0;
            using (var input = new MemoryStream(Encoding.UTF8.GetBytes(data)))
            using (var text = new StreamReader(input))
            using (var reader = new PscpClient.AsyncStreamReader("OUT", text,
                line => { Assert.AreEqual(progress, line); notifications++; return true; }, false, false))
            {
                string[] retained = reader.StopAndGetData();
                Assert.IsNull(reader.ReadError);
                Assert.AreEqual(3000, notifications);
                Assert.AreEqual(ScpOutputCapture.MaximumDiagnosticLines, retained.Length);
                Assert.IsTrue(retained.All(line => line == progress));
            }
        }

        [Test]
        public void BoundedLineReaderHandlesNewlinesAndRejectsUnterminatedOversizedLines()
        {
            using (var input = new StringReader("first\r\nsecond\rthird\nfourth"))
            {
                CollectionAssert.AreEqual(new[] { "first", "second", "third", "fourth" },
                    Enumerable.Range(0, 4).Select(i => ScpOutputCapture.ReadLine(input)).ToArray());
                Assert.IsNull(ScpOutputCapture.ReadLine(input));
            }
            using (var input = new StringReader(new string('x', ScpOutputCapture.MaximumLineCharacters + 1)))
                Assert.Throws<ScpOutputLimitException>(() => ScpOutputCapture.ReadLine(input));
        }

        [Test]
        public void PipeStringReaderRejectsOversizedFramesBeforeReadingTheirContent()
        {
            using (var input = new MemoryStream(new byte[] { 0xff, 0xff, 0xff, 0x7f }))
            using (var reader = new BinaryReader(input))
                Assert.Throws<ScpOutputLimitException>(() => ScpOutputCapture.ReadString(reader));
            using (var input = new MemoryStream())
            {
                using (var writer = new BinaryWriter(input, Encoding.UTF8, true)) writer.Write("file-\u03bb\u6587");
                input.Position = 0;
                using (var reader = new BinaryReader(input))
                    Assert.AreEqual("file-\u03bb\u6587", ScpOutputCapture.ReadString(reader));
            }
        }
    }
}
