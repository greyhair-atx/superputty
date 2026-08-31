using System;
using System.Collections.Generic;
using SuperPutty.Data;
using log4net;
using System.Threading;

namespace SuperPutty.Scp
{
    #region FileTransfer
    public class FileTransfer
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(FileTransfer));
        private static int idSeed = 0;

        public event EventHandler Update;

        private Thread thread = null;
        private CancellationTokenSource cancellation;
        private readonly object syncRoot = new object();
        private int generation;
        Status status = Status.Initializing;
        private int percentComplete;
        private string transferStatusMsg;
        private DateTime? startTime;
        private DateTime? endTime;

        public FileTransfer(PscpOptions options, FileTransferRequest request)
        {
            this.Options = options;
            this.Request = request;

            this.Id = Interlocked.Increment(ref idSeed);
        }

        public void Start()
        {
            EventHandler handler = null;
            lock (syncRoot)
            {
                if ((this.status == Status.Initializing || CanRestart(this.status)) &&
                    (this.thread == null || !this.thread.IsAlive))
                {
                    Log.InfoFormat("Starting transfer, id={0}", this.Id);

                    this.cancellation?.Dispose();
                    this.cancellation = new CancellationTokenSource();
                    int operationGeneration = ++this.generation;
                    this.startTime = DateTime.Now;
                    this.endTime = null;
                    this.percentComplete = 0;
                    this.status = Status.Running;
                    this.transferStatusMsg = "Started transfer";

                    CancellationToken cancellationToken = this.cancellation.Token;
                    this.thread = new Thread(() => this.DoTransfer(operationGeneration, cancellationToken))
                    {
                        IsBackground = true,
                        Name = "SCP file transfer " + this.Id
                    };
                    this.thread.Start();
                    handler = this.Update;
                }
                else
                {
                    Log.WarnFormat("Attempted to start active transfer, id={0}", this.Id);
                }
            }
            handler?.Invoke(this, EventArgs.Empty);
        }

        public void Cancel()
        {
            CancellationTokenSource activeCancellation = null;
            EventHandler handler = null;
            bool canceled = false;
            lock (syncRoot)
            {
                if (CanCancel(this.status))
                {
                    Log.InfoFormat("Canceling active transfer, id={0}", this.Id);
                    this.status = Status.Canceling;
                    this.transferStatusMsg = "Canceling";
                    activeCancellation = this.cancellation;
                    handler = this.Update;
                    canceled = true;
                }
                else
                {
                    Log.WarnFormat("Attempted to cancel inactive transfer, id={0}", this.Id);
                }
            }
            try
            {
                activeCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A completed worker may have been restarted after the state changed to Canceled.
            }
            handler?.Invoke(this, EventArgs.Empty);
            if (canceled)
                Log.InfoFormat("Cancellation requested for active transfer, id={0}", this.Id);
        }

        void DoTransfer(int operationGeneration, CancellationToken cancellationToken)
        {
            try
            {
                PscpClient client = new PscpClient(this.Options, this.Request.Session);

                int estSizeKB = Int32.MaxValue;
                FileTransferResult res = client.CopyFiles(
                    this.Request.SourceFiles,
                    this.Request.TargetFile,
                    (complete, cancelAll, s) =>
                    {
                        string msg;
                        if (s.PercentComplete > 0)
                        {
                            estSizeKB = Math.Min(estSizeKB, s.BytesTransferred * 100 / s.PercentComplete);
                            string units = estSizeKB > 1024 * 10 ? "MB" : "KB";
                            int divisor = units == "MB" ? 1024 : 1;
                            msg = string.Format(
                                "{0}, ({1} of {2} {3}, {4})",
                                s.Filename,
                                s.BytesTransferred / divisor,
                                estSizeKB / divisor,
                                units,
                                s.TimeLeft);
                        }
                        else
                        {
                            // < 1% completed
                            msg = string.Format("{0}, ({1} KB, {2})", s.Filename, s.BytesTransferred, s.TimeLeft);
                        }
                        this.UpdateStatus(operationGeneration, s.PercentComplete, Status.Running, msg);
                    },
                    cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    this.CompleteCancellation(operationGeneration);
                    return;
                }

                lock (syncRoot)
                {
                    if (operationGeneration == this.generation)
                        this.endTime = DateTime.Now;
                }
                switch (res.StatusCode)
                {
                    case ResultStatusCode.Success:
                        double duration = (EndTime.Value - StartTime.Value).TotalSeconds;
                        this.UpdateStatus(operationGeneration, 100, Status.Complete, String.Format("Duration {0:#,###} s", duration));
                        break;
                    case ResultStatusCode.Canceled:
                        this.CompleteCancellation(operationGeneration);
                        break;
                    case ResultStatusCode.RetryAuthentication:
                    case ResultStatusCode.Error:
                        this.UpdateStatus(operationGeneration, this.PercentComplete, Status.Error, res.ErrorMsg);
                        break;
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    this.CompleteCancellation(operationGeneration);
                }
                else
                {
                    Log.Error("Error running transfer, id=" + this.Id, ex);
                    this.UpdateStatus(operationGeneration, 0, Status.Error, ex.Message);
                }
            }
        }

        void CompleteCancellation(int operationGeneration)
        {
            EventHandler handler;
            lock (syncRoot)
            {
                if (operationGeneration != this.generation)
                    return;
                this.status = Status.Canceled;
                this.transferStatusMsg = "Canceled";
                this.endTime = DateTime.Now;
                handler = this.Update;
            }
            handler?.Invoke(this, EventArgs.Empty);
        }

        void UpdateStatus(int operationGeneration, int percentageComplete, Status newStatus, string message)
        {
            EventHandler handler;
            lock (syncRoot)
            {
                if (operationGeneration != this.generation)
                    return;
                if (this.status == Status.Canceling && newStatus == Status.Running)
                    return;
                this.percentComplete = percentageComplete;
                this.status = newStatus;
                this.transferStatusMsg = message;
                handler = this.Update;
            }
            handler?.Invoke(this, EventArgs.Empty);
        }

        public static bool CanRestart(Status status)
        {
            return status == Status.Complete || status == Status.Canceled || status == Status.Error;
        }

        public static bool CanCancel(Status status)
        {
            return status == Status.Running;
        }

        public PscpOptions Options { get; private set; }
        public FileTransferRequest Request { get; private set; }
        public int Id { get; private set; }

        public Status TransferStatus
        {
            get { lock (syncRoot) { return this.status; } }
        }

        public int PercentComplete { get { lock (syncRoot) { return this.percentComplete; } } }
        public string TransferStatusMsg { get { lock (syncRoot) { return this.transferStatusMsg; } } }
        public DateTime? StartTime { get { lock (syncRoot) { return this.startTime; } } }
        public DateTime? EndTime { get { lock (syncRoot) { return this.endTime; } } }

        public enum Status
        {
            Initializing,
            Running,
            Complete,
            Error,
            Canceling,
            Canceled
        }
    } 
    #endregion

    #region FileTransferRequest
    public class FileTransferRequest
    {
        public FileTransferRequest()
        {
            this.SourceFiles = new List<BrowserFileInfo>();
        }
        public SessionData Session { get; set; }
        public List<BrowserFileInfo> SourceFiles { get; set; }
        public BrowserFileInfo TargetFile { get; set; }
    } 
    #endregion
}
