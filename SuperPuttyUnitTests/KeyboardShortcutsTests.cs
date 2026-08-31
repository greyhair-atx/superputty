using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using System.Windows.Forms;
using SuperPutty.Gui;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class KeyboardShortcutsTets
    {
        [Test]
        public void KeysToStringTest()
        {
            String s = Keys.None.ToString();
            Assert.AreEqual(s, "None");

            Keys k = Keys.Control;
            Assert.AreEqual("Control", k.ToString());
            k |= Keys.M;
            Assert.AreEqual("M, Control", k.ToString());

            k |= Keys.Shift;
            Assert.AreEqual("M, Shift, Control", k.ToString());

            Keys k2 = (Keys) Enum.Parse(typeof(Keys), k.ToString());
            Assert.AreEqual(k, k2);
        }

        [Test]
        public void ToStringTest()
        {
            KeyboardShortcut ks = new KeyboardShortcut();

            Assert.AreEqual("", ks.ShortcutString);

            ks.Key = Keys.F11;
            Assert.AreEqual("F11", ks.ShortcutString);

            ks.Key = Keys.PageUp;
            ks.Modifiers = Keys.Control;
            Assert.AreEqual("Ctrl+PageUp", ks.ShortcutString);

            ks.Modifiers |= Keys.Shift;
            Assert.AreEqual("Ctrl+Shift+PageUp", ks.ShortcutString);

        }

        [Test]
        public void getcommandTest()
        {
            String command = CommandLineOptions.getcommand("-pw 12sa12 -we aasd", "-pw");
            Assert.AreEqual("12sa12", command);

            command = CommandLineOptions.getcommand(" -pw 12sa12 -we aasd", "-pw");
            Assert.AreEqual("12sa12", command);

            command = CommandLineOptions.getcommand("-pw \"12sa12\" -we aasd", "-pw");
            Assert.AreEqual("12sa12", command);

            command = CommandLineOptions.getcommand(" -pw  -pw \"12sa12\" -we aasd", "-pw");
            Assert.AreEqual("12sa12", command);


            command = CommandLineOptions.getcommand("-pw \"12sa12\" -we aasd", "-pw");
            Assert.AreEqual("12sa12", command);

            command = CommandLineOptions.getcommand("  -pw  \"12sa12\" -we aasd", "-pw");
            Assert.AreEqual("", command);

            command = CommandLineOptions.getcommand("  -pw: \"12sa12\" -we aasd", "-pw");
            Assert.AreEqual("", command);

            command = CommandLineOptions.getcommand(" -pw  -pw \"12sa12 -we aasd", "-pw");
            Assert.AreEqual("", command);

            command = CommandLineOptions.getcommand(" -pw  -pw \"12sa12 -we aasd\"", "-pw");
            Assert.AreEqual("12sa12 -we aasd", command);

            command = CommandLineOptions.getcommand(@" -pw  -pw \+**jioi12sa12'k*+/\ -we aasd'""", "-pw");
            Assert.AreEqual(@"\+**jioi12sa12'k*+/\", command);

        }

        [Test]
        public void SensitiveArgumentsAreRedactedBeforeLogging()
        {
            const string secret = "do-not-log-this";
            string redacted = CommandLineOptions.RedactSensitiveArguments(
                "-ssh -pw \"" + secret + "\" sftp://user:" + secret + "@example.com -password=" + secret);

            StringAssert.DoesNotContain(secret, redacted);
            StringAssert.Contains("-pw XXXXX", redacted);
            StringAssert.Contains("sftp://user:XXXXX@example.com", redacted);
            StringAssert.Contains("-password=XXXXX", redacted);

            string[] args = { "-ssh", "-pw", "secret with spaces", "example.com" };
            string redactedArray = CommandLineOptions.RedactSensitiveArguments(args);
            StringAssert.DoesNotContain("secret", redactedArray);
            StringAssert.DoesNotContain("spaces", redactedArray);
        }

        [Test]
        public void WindowsArgumentsAreQuotedWithoutLosingEmbeddedQuotes()
        {
            Assert.AreEqual("simple", CommandLineOptions.QuoteArgument("simple"));
            Assert.AreEqual("\"two words\"", CommandLineOptions.QuoteArgument("two words"));
            Assert.AreEqual("\"a\\\"b\"", CommandLineOptions.QuoteArgument("a\"b"));
        }

        [Test]
        public void PasswordSwitchesAreRemovedFromForwardedExtraArguments()
        {
            string sanitized = CommandLineOptions.RemoveSensitiveArguments(
                "-batch -pw secret -password=other +clipboard");

            StringAssert.Contains("-batch", sanitized);
            StringAssert.Contains("+clipboard", sanitized);
            StringAssert.DoesNotContain("secret", sanitized);
            StringAssert.DoesNotContain("other", sanitized);
        }

        [TestView]
        public void DialogBasicTest()
        {
            KeyboardShortcutEditor form = new KeyboardShortcutEditor();
            form.ShowDialog(null, new KeyboardShortcut { Name = "test", Key = Keys.A, Modifiers = Keys.Control });
        }




    }
}
