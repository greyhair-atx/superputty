using System;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class NativeHookLifecycleTests
    {
        [Test]
        public void DisposeRemovesHookOnceOnInstallingThread()
        {
            int owner = Thread.CurrentThread.ManagedThreadId;
            int removals = 0;
            var events = new GlobalWindowEvents(callback => new IntPtr(12), handle =>
            {
                Assert.AreEqual(owner, Thread.CurrentThread.ManagedThreadId);
                Assert.AreEqual(new IntPtr(12), handle);
                removals++;
                return true;
            });
            Assert.True(events.IsRegistered);
            events.Dispose();
            events.Dispose();
            Assert.False(events.IsRegistered);
            Assert.AreEqual(1, removals);
        }

        [Test]
        public void WrongThreadCannotReleaseHookOrLoseItsOwner()
        {
            int removals = 0;
            using (var events = new GlobalWindowEvents(callback => new IntPtr(12), handle => { removals++; return true; }))
            {
                Exception failure = null;
                var worker = new Thread(() =>
                {
                    try { events.Dispose(); }
                    catch (Exception ex) { failure = ex; }
                });
                worker.Start();
                Assert.True(worker.Join(5000));
                Assert.IsInstanceOf<InvalidOperationException>(failure);
                Assert.AreEqual(0, removals);
                Assert.True(events.IsRegistered);
            }
            Assert.AreEqual(1, removals);
        }

        [Test]
        public void RegistrationFailureDoesNotAttemptToUnhookAnInvalidHandle()
        {
            int removals = 0;
            using (var events = new GlobalWindowEvents(callback => IntPtr.Zero, handle => { removals++; return true; }))
                Assert.False(events.IsRegistered);
            Assert.AreEqual(0, removals);
        }

        [Test]
        public void FailedUnhookKeepsCallbackUsableAndAllowsOwnerToRetry()
        {
            NativeMethods.WinEventDelegate callback = null;
            bool canRemove = false;
            int notifications = 0;
            var events = new GlobalWindowEvents(value => { callback = value; return new IntPtr(12); }, handle => canRemove);
            try
            {
                events.SystemSwitch += (sender, args) => notifications++;
                events.Dispose();
                Assert.True(events.IsRegistered);
                callback(new IntPtr(12), 1, new IntPtr(34), 0, 0, 0, 0);
                Assert.AreEqual(1, notifications);
                canRemove = true;
                events.Dispose();
                Assert.False(events.IsRegistered);
                callback(new IntPtr(12), 1, new IntPtr(34), 0, 0, 0, 0);
                Assert.AreEqual(1, notifications);
            }
            finally
            {
                canRemove = true;
                events.Dispose();
            }
        }

        [Test]
        public void SubscriberExceptionsCannotEscapeNativeCallbackOrSkipOtherSubscribers()
        {
            NativeMethods.WinEventDelegate callback = null;
            using (var events = new GlobalWindowEvents(value => { callback = value; return new IntPtr(12); }, handle => true))
            {
                int notifications = 0;
                events.SystemSwitch += (sender, args) => { throw new InvalidOperationException("Subscriber failed"); };
                events.SystemSwitch += (sender, args) =>
                {
                    Assert.AreEqual(new IntPtr(34), args.hwnd);
                    Assert.AreEqual(56u, args.eventType);
                    notifications++;
                };
                Assert.DoesNotThrow(() => callback(new IntPtr(12), 56, new IntPtr(34), 0, 0, 0, 0));
                Assert.AreEqual(1, notifications);
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void NativeWindowMessagesPreservePointerSizedArgumentsAndResult()
        {
            Assert.AreEqual(8, IntPtr.Size, "The supported application platform is x64.");
            var wParam = new IntPtr(0x12345678ABCDEF01L);
            var lParam = new IntPtr(0x23456789ABCDEF02L);
            using (var window = new MessageProbe())
            {
                Assert.AreEqual(lParam, NativeMethods.SendMessage(window.Handle, MessageProbe.ProbeMessage, wParam, lParam));
                Assert.AreEqual(wParam, window.LastWParam);
                Assert.AreEqual(lParam, NativeMethods.SendMessage(window.Handle, MessageProbe.ProbeMessage,
                    wParam.ToInt64(), lParam.ToInt64()));
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void DesktopEnumerationPreservesPointerSizedCallbackData()
        {
            var data = new IntPtr(0x12345678ABCDEF01L);
            using (var window = new MessageProbe())
            {
                bool found = false;
                IntPtr received = IntPtr.Zero;
                NativeMethods.EnumDesktopWindows(IntPtr.Zero, (handle, parameter) =>
                {
                    if (handle != window.Handle) return true;
                    received = parameter;
                    found = true;
                    return false;
                }, data);
                Assert.True(found, "The hidden probe window should be enumerated on this desktop.");
                Assert.AreEqual(data, received);
            }
        }

        private sealed class MessageProbe : NativeWindow, IDisposable
        {
            internal const uint ProbeMessage = 0x8000 + 73;
            internal IntPtr LastWParam;

            internal MessageProbe() { CreateHandle(new CreateParams { Caption = "SuperPutty native message test" }); }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == ProbeMessage)
                {
                    this.LastWParam = message.WParam;
                    message.Result = message.LParam;
                }
                else base.WndProc(ref message);
            }

            public void Dispose() { DestroyHandle(); }
        }
    }
}
