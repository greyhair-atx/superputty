using System;
using SuperPutty.Data;
using System.Runtime.Remoting.Channels.Ipc;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting;
using log4net;
using System.Runtime.Remoting.Messaging;

namespace SuperPutty.Utils
{
    public class SingleInstanceHelper : MarshalByRefObject
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SingleInstanceHelper));

        public const string ChannelName = "SuperPuTTY";
        public const string SingleInstanceServiceName = "SingleInstance";

        public static void RegisterRemotingService()
        {
            try
            {
                IpcChannel ipcCh = new IpcChannel(ChannelName);
                ChannelServices.RegisterChannel(ipcCh, false);
                RemotingConfiguration.RegisterWellKnownServiceType(
                    typeof(SingleInstanceHelper), SingleInstanceServiceName, WellKnownObjectMode.SingleCall);

                Log.InfoFormat("Registered IpcChannel and Service: [ipc://{0}/{1}]", ChannelName, SingleInstanceServiceName);
            }
            catch (RemotingException ex)
            {
                Log.Warn("Unable to register ipcchannel for single instance support, feature will be unavailable (or another instance of SuperPuTTY is already running)", ex);
            }            
        }

        internal static bool TryLaunchInExistingInstance(string[] args, Action<string[]> launch = null)
        {
            try
            {
                (launch ?? LaunchInExistingInstance)(args);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Unable to forward command to the running instance", ex);
                return false;
            }
        }

        public static void LaunchInExistingInstance(string[] args)
        {
            IpcChannel channel = new IpcChannel();
            ChannelServices.RegisterChannel(channel, false);
            string url = String.Format("ipc://{0}/{1}", ChannelName, SingleInstanceServiceName);

            // Register as client for remote object.
            WellKnownClientTypeEntry remoteType = new WellKnownClientTypeEntry(typeof(SingleInstanceHelper), url);
            RemotingConfiguration.RegisterWellKnownClientType(remoteType);

            // Create a message sink.
            string objectUri;
            IMessageSink messageSink = channel.CreateMessageSink(url, null, out objectUri);

            /*
            Console.WriteLine("The URI of the message sink is {0}.", objectUri);
            if (messageSink != null)
            {
                Console.WriteLine("The type of the message sink is {0}.", messageSink.GetType().ToString());
            }
            */

            SingleInstanceHelper helper = new SingleInstanceHelper();
            helper.Run(args);
        }


        public void Run(String[] args)
        {
            Log.InfoFormat("Received remote Run command: [{0}]", CommandLineOptions.RedactSensitiveArguments(args));
            var form = SuperPuTTY.MainForm;
            if (form == null || form.IsDisposed || form.Disposing || !form.IsHandleCreated)
                throw new InvalidOperationException("The running instance is not ready to accept commands.");
            // Parse and resolve layouts/sessions on the UI thread as both use UI-owned collections.
            form.Invoke(new Action(() => DispatchCommand(args,
                name =>
                {
                    var layout = SuperPuTTY.FindLayout(name);
                    if (layout == null) throw new ArgumentException("Layout not found: " + name);
                    SuperPuTTY.LoadLayout(layout);
                }, SuperPuTTY.OpenSession)));
        }

        internal static void DispatchCommand(string[] args, Action<string> loadLayout, Action<SessionDataStartInfo> openSession)
        {
            var cmd = new CommandLineOptions(args);
            if (!cmd.IsValid) return;
            if (cmd.Layout != null)
                loadLayout(cmd.Layout);
            else
            {
                var session = cmd.ToSessionStartInfo();
                if (session != null) openSession(session);
            }
        }
    }
}
