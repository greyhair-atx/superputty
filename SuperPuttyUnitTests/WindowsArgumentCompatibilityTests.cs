using System;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class WindowsArgumentCompatibilityTests
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static string[] DecodeWithWindows(string arguments)
        {
            int count;
            IntPtr pointer = CommandLineToArgvW("probe.exe " + arguments, out count);
            Assert.AreNotEqual(IntPtr.Zero, pointer);
            try
            {
                return Enumerable.Range(1, count - 1)
                    .Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size))).ToArray();
            }
            finally { LocalFree(pointer); }
        }

        [Test]
        public void RawDecodingMatchesWindowsForDeterministicQuoteAndSlashCombinations()
        {
            var random = new Random(71237);
            var alphabet = new[] { 'a', 'b', 'c', ' ', (char)92, (char)34, (char)9 };
            for (int example = 0; example < 300; example++)
            {
                string raw = new string(Enumerable.Range(0, random.Next(0, 80))
                    .Select(index => alphabet[random.Next(alphabet.Length)]).ToArray());
                CollectionAssert.AreEqual(DecodeWithWindows(raw), WindowsCommandLine.Parse(raw).Select(token => token.Value),
                    "Windows tokenization mismatch in deterministic example " + example);
            }
        }

        [Test]
        public void QuotedArgumentsRoundTripThroughBothDecoders()
        {
            var random = new Random(98321);
            string[] atoms = { "a", " ", "\t", "\\", "\"", "\u03bb", "\u6587", "\n", "'", "-pw" };
            for (int example = 0; example < 100; example++)
            {
                var expected = Enumerable.Range(0, random.Next(1, 8)).Select(argument =>
                    String.Concat(Enumerable.Range(0, random.Next(0, 20)).Select(index => atoms[random.Next(atoms.Length)]))).ToArray();
                string raw = String.Join(" ", expected.Select(CommandLineOptions.QuoteArgument));
                CollectionAssert.AreEqual(expected, DecodeWithWindows(raw));
                CollectionAssert.AreEqual(expected, WindowsCommandLine.Parse(raw).Select(token => token.Value));
                Assert.That(WindowsCommandLine.Parse(raw).All(token => token.Complete), Is.True);
            }
        }
    }
}
