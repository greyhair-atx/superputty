using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class ChildFocusRetryTests
    {
        private static void PumpUntil(Func<bool> finished)
        {
            var elapsed = Stopwatch.StartNew();
            while (!finished() && elapsed.ElapsedMilliseconds < 2000)
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.That(finished(), Is.True, "The UI timer did not complete.");
        }

        [Test]
        public void FirstAttemptReturnsImmediatelyAndRetriesOnTheMessageLoop()
        {
            using (var retry = new ChildFocusRetry())
            {
                int attempts = 0;
                bool failed = false;
                retry.Request(() => true, () => { attempts++; return false; }, () => failed = true, 3, 1);
                Assert.AreEqual(1, attempts);
                Assert.IsFalse(failed);
                PumpUntil(() => failed);
                Assert.AreEqual(3, attempts);
            }
        }

        [Test]
        public void InactiveTabCancelsPendingAttemptsWithoutReportingFailure()
        {
            using (var retry = new ChildFocusRetry())
            {
                bool active = true, checkedInactive = false, failed = false;
                int attempts = 0;
                retry.Request(() => { if (!active) checkedInactive = true; return active; },
                    () => { attempts++; return false; }, () => failed = true, 3, 1);
                active = false;
                PumpUntil(() => checkedInactive);
                Assert.AreEqual(1, attempts);
                Assert.IsFalse(failed);
            }
        }

        [Test]
        public void NewRequestReplacesPendingRequest()
        {
            using (var retry = new ChildFocusRetry())
            {
                int oldAttempts = 0, newAttempts = 0;
                bool finished = false;
                retry.Request(() => true, () => { oldAttempts++; return false; }, () => Assert.Fail("Stale request failed"), 3, 1);
                retry.Request(() => true, () => { newAttempts++; return false; }, () => finished = true, 2, 1);
                PumpUntil(() => finished);
                Assert.AreEqual(1, oldAttempts);
                Assert.AreEqual(2, newAttempts);
            }
        }

        [Test]
        public void DisposingDuringFocusDoesNotScheduleAnotherAttempt()
        {
            var retry = new ChildFocusRetry();
            Assert.DoesNotThrow(() => retry.Request(() => true, () => { retry.Dispose(); return false; },
                () => Assert.Fail("Disposed request failed"), 3, 1));
        }
    }
}
