using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using Microsoft.CSharp;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Scp;

namespace SuperPuttyUnitTests.Scp
{
    [TestFixture, NonParallelizable]
    public class PscpRejectedPasswordTests
    {
        private string directory;
        private string executable;

        [OneTimeSetUp]
        public void BuildPscpFailureFixture()
        {
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-PscpRejected-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            executable = Path.Combine(directory, "FakePscp.exe");
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = executable };
                options.ReferencedAssemblies.Add("System.dll");
                var compiled = compiler.CompileAssemblyFromSource(options, @"
using System;
class FakePscp {
    static int Main(string[] args) {
        var output = Array.IndexOf(args, ""-stdout"") >= 0 ? Console.Out : Console.Error;
        if (Array.IndexOf(args, ""-fragmented"") >= 0) {
            output.WriteLine(""A"");
            output.WriteLine(""ccess denied"");
        }
        if (Array.IndexOf(args, ""-path-error"") >= 0)
            output.WriteLine(""FATAL ERROR: /home/test does not exist"");
        else
            output.WriteLine(""FATAL ERROR: Configured password was not accepted"");
        return 1;
    }
}");
                Assert.IsFalse(compiled.Errors.HasErrors, "Could not compile the PSCP failure fixture.");
            }
        }

        [OneTimeTearDown]
        public void RemovePscpFailureFixture()
        {
            File.Delete(executable);
            Directory.Delete(directory);
        }

        [TestCase(false, "")]
        [TestCase(false, "-fragmented")]
        [TestCase(false, "-stdout")]
        [TestCase(true, "-fragmented")]
        public void ConfiguredPasswordFailureRequestsAuthenticationAndExplainsAdditionalChallenges(bool transfer, string extraArguments)
        {
            PscpResult result = RunFailure(transfer, extraArguments);
            Assert.AreEqual(ResultStatusCode.RetryAuthentication, result.StatusCode);
            Assert.That(result.ErrorMsg, Does.Contain("authentication").And.Contain("Interactive SCP authentication (MFA)"));
            Assert.That(result.ErrorMsg, Does.Not.Contain("Unknown error").And.Not.Contain(executable));
        }

        [Test]
        public void MissingDirectoryStillReportsAFileOperationError()
        {
            PscpResult result = RunFailure(false, "-path-error");
            Assert.AreEqual(ResultStatusCode.Error, result.StatusCode);
            Assert.That(result.ErrorMsg, Does.Contain("/home/test does not exist"));
        }

        private PscpResult RunFailure(bool transfer, string extraArguments)
        {
            var client = new PscpClient(new PscpOptions { PscpLocation = executable, TimeoutMs = 10000 },
                new SessionData { Host = "example.invalid", Username = "test", Port = 22, ExtraArgs = extraArguments });
            if (!transfer)
                return client.ListDirectory(RemoteBrowserModel.NewDirectory("/"));
            return client.CopyFiles(new List<BrowserFileInfo>
                { new BrowserFileInfo { Path = "/test.txt", Source = SourceType.Remote } },
                new BrowserFileInfo { Path = directory, Source = SourceType.Local, Type = FileType.Directory }, null);
        }
    }
}
