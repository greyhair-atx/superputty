namespace SuperPutty
{
    public enum SessionConnectionState
    {
        Unknown,
        Disconnected,
        Connecting,
        Connected,
        Reconnecting
    }

    // Events cover the interval before the ActiveX Connected property changes.
    // A readable native snapshot otherwise supersedes a stale event state.
    internal sealed class RdpConnectionStateTracker
    {
        private SessionConnectionState state = SessionConnectionState.Disconnected;

        internal void Connecting()
        {
            if (state != SessionConnectionState.Reconnecting) state = SessionConnectionState.Connecting;
        }
        internal void Connected() { state = SessionConnectionState.Connected; }
        internal void Reconnecting() { state = SessionConnectionState.Reconnecting; }
        internal void Disconnected() { state = SessionConnectionState.Disconnected; }

        internal SessionConnectionState Resolve(short? nativeState)
        {
            if (!nativeState.HasValue) return SessionConnectionState.Unknown;
            switch (nativeState.Value)
            {
                case 0:
                    return state == SessionConnectionState.Connecting || state == SessionConnectionState.Reconnecting
                        ? state : SessionConnectionState.Disconnected;
                case 1:
                    return state == SessionConnectionState.Reconnecting ? state : SessionConnectionState.Connected;
                case 2:
                    return state == SessionConnectionState.Connected || state == SessionConnectionState.Reconnecting
                        ? SessionConnectionState.Reconnecting : SessionConnectionState.Connecting;
                default:
                    return SessionConnectionState.Unknown;
            }
        }
    }
}
