using System;

namespace SuperPutty.Utils
{
    internal static class NativeCallbackGuard
    {
        internal delegate IntPtr? KeyboardHandler(int code, IntPtr wParam, IntPtr lParam);

        internal static void WindowEvent(Func<bool> enabled, Action handler, Action<Exception> reportFailure)
        {
            try { if (enabled()) handler(); }
            catch (Exception ex) { Report(reportFailure, ex); }
        }

        internal static IntPtr Keyboard(int code, IntPtr wParam, IntPtr lParam, Func<bool> enabled,
            KeyboardHandler handler, NativeMethods.LowLevelKMProc next, Action<Exception> reportFailure)
        {
            try
            {
                // Windows requires negative codes to be forwarded without examining
                // the keyboard data. A nullable result distinguishes handled keys
                // from forwarding; the next hook is called at most once.
                if (code >= 0 && enabled())
                {
                    IntPtr? handled = handler(code, wParam, lParam);
                    if (handled.HasValue) return handled.Value;
                }
            }
            catch (Exception ex) { Report(reportFailure, ex); }
            try { return next(code, wParam, lParam); }
            catch (Exception ex)
            {
                Report(reportFailure, ex);
                return IntPtr.Zero;
            }
        }

        private static void Report(Action<Exception> reportFailure, Exception failure)
        {
            try { reportFailure?.Invoke(failure); }
            catch (Exception) { }
        }
    }
}
