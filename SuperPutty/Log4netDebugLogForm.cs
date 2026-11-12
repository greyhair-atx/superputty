using System;
using System.Windows.Forms;
using log4net.Appender;
using log4net;
using log4net.Repository.Hierarchy;
using log4net.Core;
using log4net.Layout;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace SuperPutty
{
    public partial class Log4netLogViewer : ToolWindow
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Log4netLogViewer));

        internal const int MaximumHistoryCharacters = 256 * 1024;
        internal const int MaximumPendingMessages = 2000;
        private ViewerAppender memoryAppender;
        private Hierarchy repository;
        private bool loggerDisposed;

        // Render on the logging thread so queued entries cannot retain arbitrary
        // application objects. Bound both the backlog and each rendered message.
        private sealed class ViewerAppender : AppenderSkeleton
        {
            private readonly Queue<string> messages = new Queue<string>();
            private readonly object gate = new object();

            protected override void Append(LoggingEvent loggingEvent)
            {
                string message;
                using (var writer = new StringWriter())
                {
                    Layout.Format(writer, loggingEvent);
                    message = writer.ToString();
                }
                const int maximumMessageCharacters = 8192;
                if (message.Length > maximumMessageCharacters)
                    message = message.Substring(0, maximumMessageCharacters - 16) + " [truncated]\r\n";
                lock (gate)
                {
                    if (messages.Count == MaximumPendingMessages) messages.Dequeue();
                    messages.Enqueue(message);
                }
            }

            internal string Drain()
            {
                string[] batch;
                lock (gate)
                {
                    batch = messages.ToArray();
                    messages.Clear();
                }
                int first = batch.Length;
                int length = 0;
                while (first > 0 && length < MaximumHistoryCharacters)
                    length += batch[--first].Length;
                var text = new StringBuilder(Math.Min(length, MaximumHistoryCharacters));
                int skip = Math.Max(0, length - MaximumHistoryCharacters);
                for (int i = first; i < batch.Length; i++)
                {
                    text.Append(batch[i], skip, batch[i].Length - skip);
                    skip = 0;
                }
                return text.ToString();
            }

            protected override void OnClose()
            {
                lock (gate) messages.Clear();
                base.OnClose();
            }
        }

        public Log4netLogViewer()
        {
            InitializeComponent();

            InitLogger();

            // start pulling log messages
            this.timerLogPull.Start();

            Log.Info("Viewer Ready");
        }

        /// <summary>
        /// Init log4net memoryAppender 
        /// http://dhvik.blogspot.com/2008/08/adding-appender-to-log4net-in-runtime.html
        /// </summary>
        void InitLogger()
        {
            //First create and configure the appender  
            this.memoryAppender = new ViewerAppender {Name = this.GetType().Name + "MemoryAppender"};

            PatternLayout layout = new PatternLayout
            {
                ConversionPattern = "%date %-5level %20.20logger{1} - %message%newline"
            };
            layout.ActivateOptions();
            this.memoryAppender.Layout = layout;

            //Notify the appender on the configuration changes  
            this.memoryAppender.ActivateOptions();

            //Get the logger repository hierarchy.  
            this.repository = (Hierarchy)LogManager.GetRepository();

            //and add the appender to the root level  
            //of the logging hierarchy  
            this.repository.Root.AddAppender(memoryAppender);

            //configure the logging at the root.  
            //this.repository.Root.Level = Level.Info;

            //mark repository as configured and notify that is has changed.  
            this.repository.Configured = true;
            this.repository.RaiseConfigurationChanged(EventArgs.Empty);

        }

        void DisposeLogger()
        {
            if (loggerDisposed) return;
            loggerDisposed = true;
            this.timerLogPull.Stop();
            // remove appender and drain events
            this.repository?.Root.RemoveAppender(this.memoryAppender);
            this.memoryAppender?.Close();

            //mark repository as configured and notify that is has changed.  
            if (this.repository != null)
            {
                this.repository.Configured = true;
                this.repository.RaiseConfigurationChanged(EventArgs.Empty);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            this.DisposeLogger();
            base.OnFormClosed(e);
        }

        private void timerLogPull_Tick(object sender, EventArgs e)
        {
            if (loggerDisposed || IsDisposed || Disposing) return;
            this.timerLogPull.Stop();
            try
            {
                string batch = this.memoryAppender.Drain();
                if (batch.Length > 0)
                {
                    int excess = this.richTextBoxLogMessages.TextLength + batch.Length - MaximumHistoryCharacters;
                    if (excess > 0)
                    {
                        this.richTextBoxLogMessages.Select(0, excess);
                        this.richTextBoxLogMessages.SelectedText = String.Empty;
                    }
                    this.richTextBoxLogMessages.AppendText(batch);
                    this.richTextBoxLogMessages.ScrollToCaret();
                }
            }
            finally
            {
                if (!loggerDisposed && !IsDisposed && !Disposing) this.timerLogPull.Start();
            }
        }

    }
}
