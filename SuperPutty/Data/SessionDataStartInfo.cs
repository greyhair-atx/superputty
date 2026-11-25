namespace SuperPutty.Data
{
    public class SessionDataStartInfo
    {
        public SessionData Session { get; set; }
        public bool UseScp { get; set; }
    }

    internal enum SessionLaunchStatus
    {
        Started,
        Failed,
        WaitingForSignIn,
        Starting
    }

    internal struct SessionLaunchResult
    {
        internal SessionLaunchStatus Status { get; }
        internal string Error { get; }

        internal SessionLaunchResult(SessionLaunchStatus status, string error = null)
        {
            Status = status;
            Error = error;
        }

        internal static SessionLaunchResult Completed(string error)
        {
            return error == null ? new SessionLaunchResult(SessionLaunchStatus.Started) :
                new SessionLaunchResult(SessionLaunchStatus.Failed, error);
        }
    }
}
