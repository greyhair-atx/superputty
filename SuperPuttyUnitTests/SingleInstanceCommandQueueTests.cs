using System;
using System.Collections.Generic;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SingleInstanceCommandQueueTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void FailureReporterCannotBreakRejectionOrDispatchCleanup(bool failToPost)
        {
            Action pending = null;
            int failures = 0;
            using (var queue = new SingleInstanceCommandQueue(action =>
                { if (failToPost) throw new InvalidOperationException("UI closed"); pending = action; },
                args => { throw new InvalidOperationException("Dispatch failed"); }, () => true, 1,
                error => { failures++; throw new InvalidOperationException("Reporter failed"); }))
            {
                Assert.AreEqual(failToPost ? SingleInstanceReply.NotReady : SingleInstanceReply.Accepted,
                    queue.TryEnqueue(new[] { "request" }));
                if (!failToPost) Assert.DoesNotThrow(() => pending());
                Assert.AreEqual(1, failures);
                Assert.AreEqual(0, queue.PendingCount);
            }
        }

        [Test]
        public void NotReadyAndFullQueuesDoNotPostUnboundedWork()
        {
            var pending = new Queue<Action>();
            var dispatched = new List<string>();
            bool ready = false;
            using (var queue = new SingleInstanceCommandQueue(pending.Enqueue,
                args => dispatched.Add(args[0]), () => ready, 2))
            {
                Assert.AreEqual(SingleInstanceReply.NotReady, queue.TryEnqueue(new[] { "early" }));
                Assert.AreEqual(0, pending.Count);
                ready = true;
                Assert.AreEqual(SingleInstanceReply.Accepted, queue.TryEnqueue(new[] { "first" }));
                Assert.AreEqual(SingleInstanceReply.Accepted, queue.TryEnqueue(new[] { "second" }));
                Assert.AreEqual(SingleInstanceReply.Busy, queue.TryEnqueue(new[] { "excess" }));
                Assert.AreEqual(2, pending.Count);
                Assert.AreEqual(2, queue.PendingCount);
                pending.Dequeue()();
                Assert.AreEqual(1, queue.PendingCount);
                Assert.AreEqual(SingleInstanceReply.Accepted, queue.TryEnqueue(new[] { "third" }));
                while (pending.Count > 0) pending.Dequeue()();
                CollectionAssert.AreEqual(new[] { "first", "second", "third" }, dispatched);
                Assert.AreEqual(0, queue.PendingCount);
            }
        }

        [Test]
        public void AcceptedCommandHasItsOwnArgumentSnapshot()
        {
            Action pending = null;
            string observed = null;
            using (var queue = new SingleInstanceCommandQueue(action => pending = action,
                args => observed = args[1], () => true))
            {
                var arguments = new[] { "-session", "original" };
                Assert.AreEqual(SingleInstanceReply.Accepted, queue.TryEnqueue(arguments));
                arguments[1] = "changed by caller";
                pending();
                Assert.AreEqual("original", observed);
                Assert.AreEqual(0, queue.PendingCount);
            }
        }

        [Test]
        public void StoppingDropsAcceptedWorkAndRejectsFurtherRequests()
        {
            var pending = new Queue<Action>();
            using (var queue = new SingleInstanceCommandQueue(pending.Enqueue,
                args => Assert.Fail("Shutdown executed a queued command."), () => true))
            {
                Assert.AreEqual(SingleInstanceReply.Accepted, queue.TryEnqueue(new[] { "before stop" }));
                queue.Stop();
                Assert.AreEqual(SingleInstanceReply.NotReady, queue.TryEnqueue(new[] { "after stop" }));
                while (pending.Count > 0) pending.Dequeue()();
                Assert.AreEqual(0, queue.PendingCount);
            }
        }

        [Test]
        public void FailedUiPostDoesNotAcknowledgeOrConsumeCapacity()
        {
            int failures = 0;
            using (var queue = new SingleInstanceCommandQueue(action => { throw new InvalidOperationException("UI closed"); },
                args => Assert.Fail("Unposted command executed"), () => true, 1, error => failures++))
            {
                Assert.AreNotEqual(SingleInstanceReply.Accepted, queue.TryEnqueue(new[] { "request" }));
                Assert.AreEqual(0, queue.PendingCount);
                Assert.AreEqual(1, failures);
            }
        }

        [Test]
        public void DispatchFailureIsReportedAndReleasesItsQueueSlot()
        {
            Action pending = null;
            int failures = 0;
            using (var queue = new SingleInstanceCommandQueue(action => pending = action,
                args => { throw new InvalidOperationException("Dispatch failed"); }, () => true, 1, error => failures++))
            {
                Assert.AreEqual(SingleInstanceReply.Accepted, queue.TryEnqueue(new[] { "request" }));
                Assert.DoesNotThrow(() => pending());
                Assert.AreEqual(1, failures);
                Assert.AreEqual(0, queue.PendingCount);
                Assert.AreEqual(SingleInstanceReply.Accepted, queue.TryEnqueue(new[] { "retry" }));
            }
        }
    }
}
