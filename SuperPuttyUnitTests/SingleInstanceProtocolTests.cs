using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SingleInstanceProtocolTests
    {
        private static void Rejected(Action operation)
        {
            Exception failure = Assert.Catch(() => operation());
            Assert.That(failure, Is.InstanceOf<InvalidDataException>().Or.InstanceOf<ArgumentException>());
        }

        [Test]
        public async Task ArgumentsRoundTripWithoutReparsingDecodedValues()
        {
            string[] arguments = { "-session", "Folder/\u03bb session", "-pw", "a quoted \"value\"\\", "", "a\nb" };
            byte[] frame = SingleInstancePipeProtocol.EncodeRequest(arguments);
            CollectionAssert.AreEqual(arguments, SingleInstancePipeProtocol.DecodeRequest(frame));
            using (var stream = new MemoryStream())
            {
                await SingleInstancePipeProtocol.WriteRequestAsync(stream, arguments, CancellationToken.None);
                stream.Position = 0;
                CollectionAssert.AreEqual(arguments, await SingleInstancePipeProtocol.ReadRequestAsync(stream, CancellationToken.None));
            }
        }

        [Test]
        public void CountAndArgumentLimitsAreEnforcedBeforeSending()
        {
            Rejected(() => SingleInstancePipeProtocol.EncodeRequest(
                Enumerable.Repeat("x", SingleInstancePipeProtocol.MaximumArguments + 1).ToArray()));
            Rejected(() => SingleInstancePipeProtocol.EncodeRequest(
                new[] { new string('x', SingleInstancePipeProtocol.MaximumArgumentBytes + 1) }));
            Rejected(() => SingleInstancePipeProtocol.EncodeRequest(new[] { "embedded\0null" }));
            Rejected(() => SingleInstancePipeProtocol.EncodeRequest(new[] { "\ud800" }));
        }

        [Test]
        public void TotalFrameBudgetAppliesAcrossIndividuallyValidArguments()
        {
            Rejected(() => SingleInstancePipeProtocol.EncodeRequest(
                Enumerable.Repeat(new string('x', SingleInstancePipeProtocol.MaximumArgumentBytes), 9).ToArray()));
        }

        [Test]
        public void TruncatedOrTrailingFrameDataIsRejected()
        {
            byte[] frame = SingleInstancePipeProtocol.EncodeRequest(new[] { "-session", "saved" });
            Rejected(() => SingleInstancePipeProtocol.DecodeRequest(frame.Take(frame.Length - 1).ToArray()));
            Rejected(() => SingleInstancePipeProtocol.DecodeRequest(frame.Concat(new byte[] { 0 }).ToArray()));
            Rejected(() => SingleInstancePipeProtocol.DecodeRequest(new byte[SingleInstancePipeProtocol.HeaderBytes - 1]));
        }

        [Test]
        public async Task PeerIsVerifiedBeforeAnyCredentialBytesAreWritten()
        {
            using (var stream = new MemoryStream())
            {
                bool checkedPeer = false;
                try
                {
                    await SingleInstancePipeClient.ExchangeAuthenticatedAsync(stream, new[] { "-pw", "secret" },
                        () => { checkedPeer = true; throw new InvalidOperationException("Unexpected peer owner"); }, CancellationToken.None);
                    Assert.Fail("An unverified peer was accepted.");
                }
                catch (InvalidOperationException) { }
                Assert.True(checkedPeer);
                Assert.AreEqual(0, stream.Length);
            }
        }
    }
}
