using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Relays command-line arguments from a secondary process to the running
    /// SuperPuTTY process. Named pipes replace the .NET Framework Remoting IPC
    /// channel, which isn't available on modern .NET.
    /// </summary>
    public static class SingleInstanceHelper
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SingleInstanceHelper));
        private static readonly CancellationTokenSource ServerCancellation = new CancellationTokenSource();
        private static Task serverTask;

        private const string PipeName = "SuperPuTTY.SingleInstance";
        private const int ConnectTimeoutMilliseconds = 3000;
        private const int MaximumArgumentCount = 256;
        private const int MaximumArgumentLength = 32768;

        public static void StartServer()
        {
            if (serverTask != null)
            {
                return;
            }

            serverTask = Task.Run(() => ListenAsync(ServerCancellation.Token));
            Log.InfoFormat("Started single-instance named pipe server: {0}", PipeName);
        }

        public static void StopServer()
        {
            ServerCancellation.Cancel();
        }

        public static bool LaunchInExistingInstance(string[] args)
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly))
                {
                    client.Connect(ConnectTimeoutMilliseconds);
                    using (BinaryWriter writer = new BinaryWriter(client, Encoding.UTF8, true))
                    {
                        writer.Write(args.Length);
                        foreach (string arg in args)
                        {
                            writer.Write(arg ?? String.Empty);
                        }

                        writer.Flush();
                    }
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException)
            {
                Log.Warn("Unable to contact the running SuperPuTTY instance", ex);
                return false;
            }
        }

        private static async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (NamedPipeServerStream server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
                    {
                        await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                        string[] args = ReadArguments(server);
                        Run(args);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error("Error receiving a single-instance command", ex);
                }
            }
        }

        private static string[] ReadArguments(Stream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > MaximumArgumentCount)
                {
                    throw new InvalidDataException("Invalid command-line argument count.");
                }

                string[] args = new string[count];
                for (int index = 0; index < count; index++)
                {
                    args[index] = reader.ReadString();
                    if (args[index].Length > MaximumArgumentLength)
                    {
                        throw new InvalidDataException("Command-line argument exceeds the allowed length.");
                    }
                }

                return args;
            }
        }

        private static void Run(string[] args)
        {
            Log.InfoFormat("Received remote Run command: [{0}]", String.Join(" ", args));
            CommandLineOptions commandLine = new CommandLineOptions(args);
            SessionDataStartInfo sessionStartInfo = commandLine.ToSessionStartInfo();
            SuperPuTTY.OpenSession(sessionStartInfo);
        }
    }
}
