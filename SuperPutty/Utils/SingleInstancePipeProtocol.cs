using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuperPutty.Utils
{
    internal enum SingleInstanceReply : byte
    {
        Accepted, InvalidRequest, UnsupportedVersion, NotReady, Busy
    }

    internal static class SingleInstancePipeProtocol
    {
        internal const int MaximumPayloadBytes = 65536;
        internal const int MaximumArguments = 64;
        internal const int MaximumArgumentBytes = 8192;
        internal const int HeaderBytes = 12;
        private const uint RequestMagic = 0x45435053; // SPCE
        private const uint ReplyMagic = 0x41435053; // SPCA
        private const ushort Version = 1;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        internal static byte[] EncodeRequest(string[] args)
        {
            if (args == null || args.Length > MaximumArguments) throw new InvalidDataException("Invalid IPC argument count.");
            int payloadLength = sizeof(int);
            foreach (string arg in args)
            {
                if (arg == null || arg.IndexOf('\0') >= 0) throw new InvalidDataException("Invalid IPC argument.");
                int length = Utf8.GetByteCount(arg);
                if (length > MaximumArgumentBytes) throw new InvalidDataException("An IPC argument exceeds the supported limit.");
                payloadLength += sizeof(int) + length;
                if (payloadLength > MaximumPayloadBytes) throw new InvalidDataException("The IPC request exceeds the supported limit.");
            }
            using (var output = new MemoryStream(HeaderBytes + payloadLength))
            using (var writer = new BinaryWriter(output, Utf8, true))
            {
                writer.Write(RequestMagic);
                writer.Write(Version);
                writer.Write((ushort)0);
                writer.Write(payloadLength);
                writer.Write(args.Length);
                foreach (string arg in args)
                {
                    byte[] bytes = Utf8.GetBytes(arg);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                }
                return output.ToArray();
            }
        }

        private static int ReadHeader(byte[] header)
        {
            using (var input = new MemoryStream(header, false))
            using (var reader = new BinaryReader(input))
            {
                if (reader.ReadUInt32() != RequestMagic) throw new InvalidDataException("Invalid IPC protocol header.");
                if (reader.ReadUInt16() != Version) throw new NotSupportedException("Unsupported IPC protocol version.");
                if (reader.ReadUInt16() != 0) throw new InvalidDataException("Invalid IPC header flags.");
                int length = reader.ReadInt32();
                if (length < sizeof(int) || length > MaximumPayloadBytes) throw new InvalidDataException("Invalid IPC payload length.");
                return length;
            }
        }

        private static string[] DecodePayload(byte[] payload)
        {
            using (var input = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(input, Utf8, true))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > MaximumArguments) throw new InvalidDataException("Invalid IPC argument count.");
                var args = new string[count];
                for (int index = 0; index < count; index++)
                {
                    if (input.Length - input.Position < sizeof(int)) throw new InvalidDataException("Truncated IPC argument.");
                    int length = reader.ReadInt32();
                    if (length < 0 || length > MaximumArgumentBytes || length > input.Length - input.Position)
                        throw new InvalidDataException("Invalid IPC argument length.");
                    try { args[index] = Utf8.GetString(reader.ReadBytes(length)); }
                    catch (DecoderFallbackException) { throw new InvalidDataException("Invalid UTF-8 in IPC argument."); }
                    if (args[index].IndexOf('\0') >= 0) throw new InvalidDataException("Invalid IPC argument.");
                }
                if (input.Position != input.Length) throw new InvalidDataException("Unexpected trailing IPC data.");
                return args;
            }
        }

        internal static string[] DecodeRequest(byte[] frame)
        {
            if (frame == null || frame.Length < HeaderBytes) throw new InvalidDataException("Truncated IPC header.");
            var header = new byte[HeaderBytes];
            Buffer.BlockCopy(frame, 0, header, 0, HeaderBytes);
            int length = ReadHeader(header);
            if (frame.Length != HeaderBytes + length) throw new InvalidDataException("Truncated or trailing IPC frame data.");
            var payload = new byte[length];
            Buffer.BlockCopy(frame, HeaderBytes, payload, 0, length);
            return DecodePayload(payload);
        }

        internal static async Task WriteRequestAsync(Stream stream, string[] args, CancellationToken cancellation)
        {
            byte[] frame = EncodeRequest(args);
            await stream.WriteAsync(frame, 0, frame.Length, cancellation).ConfigureAwait(false);
            await stream.FlushAsync(cancellation).ConfigureAwait(false);
        }

        internal static async Task<string[]> ReadRequestAsync(Stream stream, CancellationToken cancellation)
        {
            var header = new byte[HeaderBytes];
            await ReadExactlyAsync(stream, header, cancellation).ConfigureAwait(false);
            int length = ReadHeader(header);
            var payload = new byte[length];
            await ReadExactlyAsync(stream, payload, cancellation).ConfigureAwait(false);
            return DecodePayload(payload);
        }

        internal static async Task WriteReplyAsync(Stream stream, SingleInstanceReply reply, CancellationToken cancellation)
        {
            byte[] response;
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(ReplyMagic);
                writer.Write(Version);
                writer.Write((byte)reply);
                writer.Write((byte)0);
                response = output.ToArray();
            }
            await stream.WriteAsync(response, 0, response.Length, cancellation).ConfigureAwait(false);
            await stream.FlushAsync(cancellation).ConfigureAwait(false);
        }

        internal static async Task<SingleInstanceReply> ReadReplyAsync(Stream stream, CancellationToken cancellation)
        {
            var response = new byte[8];
            await ReadExactlyAsync(stream, response, cancellation).ConfigureAwait(false);
            using (var reader = new BinaryReader(new MemoryStream(response, false)))
            {
                if (reader.ReadUInt32() != ReplyMagic || reader.ReadUInt16() != Version)
                    throw new InvalidDataException("The running instance uses an incompatible IPC protocol.");
                byte result = reader.ReadByte();
                if (result > (byte)SingleInstanceReply.Busy || reader.ReadByte() != 0)
                    throw new InvalidDataException("Invalid IPC response.");
                return (SingleInstanceReply)result;
            }
        }

        private static async Task ReadExactlyAsync(Stream stream, byte[] destination, CancellationToken cancellation)
        {
            int offset = 0;
            while (offset < destination.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                int read = await stream.ReadAsync(destination, offset, destination.Length - offset, cancellation).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("The IPC frame was truncated.");
                offset += read;
            }
        }
    }
}
