using System;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using SuperPutty.Scp;

namespace SuperPuttyUnitTests.Scp
{
    [TestFixture]
    public class BufferedOutputWriterTests
    {
        private sealed class RecordingStream : MemoryStream
        {
            private readonly object gate = new object();
            internal readonly ManualResetEventSlim Written = new ManualResetEventSlim();
            internal int Writes;
            internal bool FailWrites;

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (gate)
                {
                    Writes++;
                    if (FailWrites) { Written.Set(); throw new IOException("Disconnected test pipe"); }
                    base.Write(buffer, offset, count);
                    Written.Set();
                }
            }

            internal byte[] Snapshot() { lock (gate) return ToArray(); }

            protected override void Dispose(bool disposing)
            {
                if (disposing) Written.Dispose();
                base.Dispose(disposing);
            }
        }

        [Test]
        public void BatchingPreservesEveryEmptyAndUnicodeFrameAndFinalExitCode()
        {
            using (var stream = new RecordingStream())
            {
                using (var writer = new InteractivePscpDialog.BufferedOutputWriter(stream))
                {
                    writer.WriteLine("pid-123");
                    for (int i = 0; i < 10000; i++) writer.WriteLine(String.Empty);
                    writer.WriteLine("\u03bb\u65e5\ud83d\ude80");
                    writer.WriteExit(7);
                }
                Assert.Less(stream.Writes, 100, "Tiny lines should share pipe writes.");
                using (var reader = new BinaryReader(new MemoryStream(stream.Snapshot()), Encoding.UTF8))
                {
                    Assert.AreEqual(0, reader.ReadByte());
                    Assert.AreEqual("pid-123", ScpOutputCapture.ReadString(reader));
                    for (int i = 0; i < 10000; i++)
                    {
                        Assert.AreEqual(0, reader.ReadByte());
                        Assert.AreEqual(String.Empty, ScpOutputCapture.ReadString(reader));
                    }
                    Assert.AreEqual(0, reader.ReadByte());
                    Assert.AreEqual("\u03bb\u65e5\ud83d\ude80", ScpOutputCapture.ReadString(reader));
                    Assert.AreEqual(1, reader.ReadByte());
                    Assert.AreEqual(7, reader.ReadInt32());
                    Assert.AreEqual(reader.BaseStream.Length, reader.BaseStream.Position);
                }
            }
        }

        [Test]
        public void FirstLineIsImmediateAndQuietSecondLineFlushesWhileProducerIsBlocked()
        {
            using (var stream = new RecordingStream())
            using (var writer = new InteractivePscpDialog.BufferedOutputWriter(stream))
            {
                writer.WriteLine("pid-123");
                Assert.AreEqual(1, stream.Writes, "Cancellation needs the PID before the next ReadLine returns.");
                stream.Written.Reset();
                writer.WriteLine("progress");
                Assert.IsTrue(stream.Written.Wait(2000), "A quiet producer must not wait for another frame to flush.");
                using (var reader = new BinaryReader(new MemoryStream(stream.Snapshot()), Encoding.UTF8))
                {
                    Assert.AreEqual(0, reader.ReadByte());
                    Assert.AreEqual("pid-123", ScpOutputCapture.ReadString(reader));
                    Assert.AreEqual(0, reader.ReadByte());
                    Assert.AreEqual("progress", ScpOutputCapture.ReadString(reader));
                }
            }
        }

        [Test]
        public void LimitStatusFlushesEarlierOutputAndRejectsOversizedFrames()
        {
            using (var stream = new RecordingStream())
            {
                using (var writer = new InteractivePscpDialog.BufferedOutputWriter(stream))
                {
                    writer.WriteLine("first");
                    writer.WriteLine("last");
                    Assert.Throws<ScpOutputLimitException>(() => writer.WriteLine(new string('x', ScpOutputCapture.MaximumLineCharacters + 1)));
                    writer.WriteLimit();
                }
                using (var reader = new BinaryReader(new MemoryStream(stream.Snapshot()), Encoding.UTF8))
                {
                    Assert.AreEqual(0, reader.ReadByte());
                    Assert.AreEqual("first", ScpOutputCapture.ReadString(reader));
                    Assert.AreEqual(0, reader.ReadByte());
                    Assert.AreEqual("last", ScpOutputCapture.ReadString(reader));
                    Assert.AreEqual(2, reader.ReadByte());
                    Assert.AreEqual(reader.BaseStream.Length, reader.BaseStream.Position);
                }
            }
        }

        [Test]
        public void TimerWriteFailureIsReportedToProducerWithoutEscapingCallback()
        {
            using (var stream = new RecordingStream())
            using (var writer = new InteractivePscpDialog.BufferedOutputWriter(stream))
            {
                writer.WriteLine("first");
                stream.Written.Reset();
                stream.FailWrites = true;
                writer.WriteLine("pending");
                Assert.IsTrue(stream.Written.Wait(2000));
                Assert.Throws<IOException>(() => writer.WriteExit(0));
            }
        }
    }
}
