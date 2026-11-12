using System;
using System.Linq;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using NUnit.Framework;
using SuperPutty.Scp;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable]
    public class WindowsCommandLineTests
    {
        [TestCase("has spaces")]
        [TestCase("a\"b")]
        [TestCase("trailing slash\\")]
        [TestCase("slashes\\\\\" and words")]
        [TestCase("-pw")]
        [TestCase("-password")]
        [TestCase("/p")]
        public void ExtractionRedactionAndRemovalAgreeForOneWindowsArgument(string password)
        {
            string command = "-N -pw " + CommandLineOptions.QuoteArgument(password) + " -agent";
            Assert.AreEqual(password, CommandLineOptions.getcommand(command, "-pw"));
            Assert.AreEqual("-N -pw XXXXX -agent", CommandLineOptions.RedactSensitiveArguments(command));
            Assert.AreEqual("-N  -agent", CommandLineOptions.RemoveSensitiveArguments(command));
        }

        [TestCase("-pw=\"\"")]
        [TestCase("-pw=")]
        [TestCase("-pw:\"\"")]
        [TestCase("/password:")]
        public void AttachedEmptyValueDoesNotConsumeTheNextArgument(string option)
        {
            string command = option + " -N";
            Assert.AreEqual("", CommandLineOptions.getcommand(command, option.StartsWith("/") ? "/password" : "-pw"));
            Assert.AreEqual("-N", CommandLineOptions.RemoveSensitiveArguments(command));
            StringAssert.EndsWith(" -N", CommandLineOptions.RedactSensitiveArguments(command));
            StringAssert.EndsWith(" -N", CommandLineOptions.RedactSensitiveArguments(new[] { option.Replace("\"", ""), "-N" }));
        }

        [TestCase("-pw \"private tail -N")]
        [TestCase("-pw=\"private tail -N")]
        [TestCase("/password:\"private tail -N")]
        public void UnclosedCredentialValueIsCompletelyRemovedAndNeverExtracted(string command)
        {
            Assert.IsTrue(CommandLineOptions.HasSensitiveArguments(command));
            Assert.AreEqual("", CommandLineOptions.RemoveSensitiveArguments(command));
            StringAssert.DoesNotContain("private", CommandLineOptions.RedactSensitiveArguments(command));
            StringAssert.DoesNotContain("tail", CommandLineOptions.RedactSensitiveArguments(command));
            Assert.AreEqual("", CommandLineOptions.getcommand(command, "-pw"));
        }

        [TestCase("-password")]
        [TestCase("-pw")]
        [TestCase("/p")]
        [TestCase("literal \"unmatched quote")]
        [TestCase("value -pw embedded words")]
        public void AlreadyDecodedPasswordRemainsIndivisibleEvenWhenItLooksLikeAnOption(string password)
        {
            Assert.AreEqual("-ssh -pw XXXXX server", CommandLineOptions.RedactSensitiveArguments(
                new[] { "-ssh", "-pw", password, "server" }));
        }

        [Test]
        public void UnrelatedRawArgumentsKeepTheirExactQuotingWhitespaceAndShellFragments()
        {
            const string unchanged = "  -m \"C:\\my commands\\start.cmd\"\t\"echo ^& literal -pw value\"  ";
            Assert.AreEqual(unchanged, CommandLineOptions.RemoveSensitiveArguments(unchanged));
            Assert.AreEqual(unchanged, CommandLineOptions.RedactSensitiveArguments(unchanged));
            const string prefix = "-m \"C:\\my commands\\start.cmd\"\t\"echo ^& literal -pw value\"";
            Assert.AreEqual(prefix + "  -N", CommandLineOptions.RemoveSensitiveArguments(prefix + " -pw secret -N"));
            Assert.IsFalse(CommandLineOptions.HasSensitiveArguments("\"echo -pw literal\" -pwx=literal"));
            Assert.IsTrue(CommandLineOptions.HasSensitiveArguments("-pw \"\""));
        }

        [Test]
        public void TokenSpansMatchTheirOriginalTextAndKeepEmptyValues()
        {
            const string command = "  first\t\"two words\"  \"\" last";
            var tokens = WindowsCommandLine.Parse(command);
            CollectionAssert.AreEqual(new[] { "first", "two words", "", "last" }, tokens.Select(token => token.Value));
            CollectionAssert.AreEqual(new[] { "first", "\"two words\"", "\"\"", "last" },
                tokens.Select(token => command.Substring(token.Start, token.End - token.Start)));
            Assert.IsTrue(tokens.All(token => token.Complete));
        }

        [Test]
        public void InteractiveAuthenticationRemovesOnlyCredentialFileAndBatchSwitches()
        {
            const string command = "-batch -pw \"private value\" -pwfile=\"C:\\secret path\" -hostkey \"key with spaces\"\t-v";
            string sanitized = InteractivePscpDialog.SanitizeExtraArguments(command);
            StringAssert.DoesNotContain("private", sanitized);
            StringAssert.DoesNotContain("secret", sanitized);
            StringAssert.DoesNotContain("-batch", sanitized);
            StringAssert.Contains("-hostkey \"key with spaces\"\t-v", sanitized);
        }

        [Test]
        public void EncodedSshLinkUsesTheSameQuotingRules()
        {
            var options = new CommandLineOptions(new[] { "ssh://host%20-pw%20%22two%20words%22/" });
            Assert.IsTrue(options.IsValid);
            Assert.AreEqual("two words", options.Password);
            Assert.AreEqual("ssh://host", options.Host);
        }

        [Test]
        public void MalformedEncodedSshLinkIsInvalidAndItsDecodedPasswordIsRedacted()
        {
            var logger = (Logger)LogManager.GetLogger(typeof(CommandLineOptions).Assembly, typeof(CommandLineOptions)).Logger;
            var oldLevel = logger.Level;
            var oldThreshold = logger.Hierarchy.Threshold;
            bool oldConfigured = logger.Hierarchy.Configured;
            var appender = new MemoryAppender();
            appender.ActivateOptions();
            logger.AddAppender(appender);
            logger.Level = Level.Info;
            logger.Hierarchy.Threshold = Level.All;
            logger.Hierarchy.Configured = true;
            try
            {
                var options = new CommandLineOptions(new[] { "ssh://host%20-pw%20%22private%20tail/" });
                Assert.IsFalse(options.IsValid);
                string logged = String.Join("\n", appender.GetEvents().Select(item => item.RenderedMessage));
                StringAssert.DoesNotContain("private", logged);
                StringAssert.DoesNotContain("tail", logged);
                StringAssert.Contains("XXXXX", logged);
            }
            finally
            {
                logger.RemoveAppender(appender);
                logger.Level = oldLevel;
                logger.Hierarchy.Threshold = oldThreshold;
                logger.Hierarchy.Configured = oldConfigured;
                appender.Close();
            }
        }
    }
}
