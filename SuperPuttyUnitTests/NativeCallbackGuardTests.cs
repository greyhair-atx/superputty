using System;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class NativeCallbackGuardTests
    {
        [TestCase(-1, true)]
        [TestCase(0, false)]
        public void NegativeOrDisabledKeyboardCallbacksForwardWithoutReadingData(int code, bool enabled)
        {
            int forwards = 0;
            IntPtr result = NativeCallbackGuard.Keyboard(code, new IntPtr(12), new IntPtr(34), () => enabled,
                (c, w, l) => { Assert.Fail("Unavailable keyboard data was accessed."); return null; },
                (c, w, l) =>
                {
                    Assert.AreEqual(code, c);
                    Assert.AreEqual(new IntPtr(12), w);
                    Assert.AreEqual(new IntPtr(34), l);
                    forwards++;
                    return new IntPtr(56);
                }, failure => Assert.Fail("Forwarding reported an unexpected failure."));
            Assert.AreEqual(new IntPtr(56), result);
            Assert.AreEqual(1, forwards);
        }

        [TestCase(0)]
        [TestCase(99)]
        public void HandledKeyboardCallbacksReturnTheirResultWithoutForwarding(int value)
        {
            Assert.AreEqual(new IntPtr(value), NativeCallbackGuard.Keyboard(0, IntPtr.Zero, IntPtr.Zero,
                () => true, (code, w, l) => new IntPtr(value),
                (code, w, l) => { Assert.Fail("A handled key was forwarded."); return IntPtr.Zero; }, null));
        }

        [Test]
        public void UnhandledKeyboardCallbackForwardsExactlyOnce()
        {
            int forwards = 0;
            var result = NativeCallbackGuard.Keyboard(0, IntPtr.Zero, IntPtr.Zero, () => true,
                (code, w, l) => null, (code, w, l) => { forwards++; return new IntPtr(7); }, null);
            Assert.AreEqual(new IntPtr(7), result);
            Assert.AreEqual(1, forwards);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KeyboardFailuresAreReportedAndForwardedEvenWhenLoggingFails(bool failureInAvailability)
        {
            int reports = 0, forwards = 0;
            var result = NativeCallbackGuard.Keyboard(0, IntPtr.Zero, IntPtr.Zero,
                () => { if (failureInAvailability) throw new InvalidOperationException(); return true; },
                (code, w, l) => { throw new InvalidOperationException(); },
                (code, w, l) => { forwards++; return new IntPtr(8); },
                failure => { reports++; throw new InvalidOperationException("Logging failed"); });
            Assert.AreEqual(new IntPtr(8), result);
            Assert.AreEqual(1, reports);
            Assert.AreEqual(1, forwards);
        }

        [Test]
        public void FailingNextHookCannotThrowAcrossNativeBoundary()
        {
            int reports = 0;
            IntPtr result = NativeCallbackGuard.Keyboard(-1, IntPtr.Zero, IntPtr.Zero, () => true,
                (code, w, l) => null, (code, w, l) => { throw new InvalidOperationException(); },
                failure => { reports++; throw new InvalidOperationException("Logging failed"); });
            Assert.AreEqual(IntPtr.Zero, result);
            Assert.AreEqual(1, reports);
        }

        [Test]
        public void DisabledWindowCallbackDoesNotTouchItsOwner()
        {
            Assert.DoesNotThrow(() => NativeCallbackGuard.WindowEvent(() => false,
                () => Assert.Fail("A disposed owner was accessed."), failure => Assert.Fail("Unexpected failure.")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WindowFailuresCannotEscapeEvenWhenLoggingFails(bool failureInAvailability)
        {
            int reports = 0;
            Assert.DoesNotThrow(() => NativeCallbackGuard.WindowEvent(
                () => { if (failureInAvailability) throw new InvalidOperationException(); return true; },
                () => { throw new InvalidOperationException(); },
                failure => { reports++; throw new InvalidOperationException("Logging failed"); }));
            Assert.AreEqual(1, reports);
        }
    }
}
