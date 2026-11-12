using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable]
    public class NativeCallbackSafetyTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void FailedRemovalKeepsTheCallbackAliveAndItsHandleAvailableForRetry(bool throwOnRemoval)
        {
            bool allowRemoval = false;
            WeakReference callbackTarget;
            var registration = CreateRegistration(out callbackTarget, () =>
            {
                if (!allowRemoval && throwOnRemoval) throw new InvalidOperationException("Removal failed");
                return allowRemoval;
            });
            try
            {
                Assert.True(registration.IsEnabled);
                Assert.False(registration.TryRemove());
                Assert.False(registration.IsEnabled);
                Assert.AreEqual(new IntPtr(12), registration.Handle);
                Collect();
                Assert.True(callbackTarget.IsAlive, "Windows could still call the delegate after a failed removal.");
                allowRemoval = true;
                Assert.True(registration.TryRemove());
                Assert.AreEqual(IntPtr.Zero, registration.Handle);
                Collect();
                Assert.False(callbackTarget.IsAlive, "Confirmed removal must release the native callback root.");
            }
            finally
            {
                allowRemoval = true;
                registration.Dispose();
            }
        }

        [Test]
        public void ComponentOwnerDisposalDisablesCallbackAndRetainsFailedRemovalForOwnerRetry()
        {
            bool allowRemoval = false;
            int removals = 0;
            Action callback = () => { };
            var registration = new NativeHookRegistration(callback, () => new IntPtr(12), handle =>
            {
                removals++;
                return allowRemoval;
            }, "test hook");
            try
            {
                using (var owner = new Container()) owner.Add(registration);
                Assert.AreEqual(1, removals);
                Assert.False(registration.IsEnabled);
                Assert.True(registration.IsRegistered);
                allowRemoval = true;
                registration.Dispose();
                registration.Dispose();
                Assert.AreEqual(2, removals, "Repeated cleanup after success must not remove a recycled handle.");
                Assert.False(registration.IsRegistered);
            }
            finally
            {
                allowRemoval = true;
                registration.TryRemove();
            }
        }

        [Test]
        public void RegistrationFailureReleasesItsCallbackWithoutAttemptingRemoval()
        {
            int removals = 0;
            WeakReference callbackTarget;
            using (var registration = CreateRegistration(out callbackTarget, () => { removals++; return true; }, IntPtr.Zero))
            {
                Assert.False(registration.IsEnabled);
                Assert.False(registration.IsRegistered);
                Collect();
                Assert.False(callbackTarget.IsAlive);
            }
            Assert.AreEqual(0, removals);
        }

        [Test]
        public void CrossThreadCleanupDisablesCallbacksButLeavesRemovalToTheInstallingThread()
        {
            int removals = 0;
            Action callback = () => { };
            using (var registration = new NativeHookRegistration(callback, () => new IntPtr(12), handle =>
            {
                removals++;
                return true;
            }, "test hook"))
            {
                bool removed = true;
                var worker = new Thread(() => removed = registration.TryRemove());
                worker.Start();
                Assert.True(worker.Join(5000));
                Assert.False(removed);
                Assert.AreEqual(0, removals);
                Assert.False(registration.IsEnabled);
                Assert.True(registration.IsRegistered);
                Assert.True(registration.TryRemove());
            }
            Assert.AreEqual(1, removals);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void DisposedSessionPanelRetainsFailedHookRegistrationAndCanRetryRemoval()
        {
            bool allowRemoval = false;
            int removals = 0;
            var panel = new ApplicationPanel(ConnectionProtocol.SSH);
            var callback = (NativeMethods.WinEventDelegate)Delegate.CreateDelegate(typeof(NativeMethods.WinEventDelegate), panel,
                typeof(ApplicationPanel).GetMethod("WinEventProc", BindingFlags.Instance | BindingFlags.NonPublic));
            var registration = new NativeHookRegistration(callback, () => new IntPtr(12), handle =>
            {
                removals++;
                return allowRemoval;
            }, "test session hook");
            var registrations = (List<NativeHookRegistration>)typeof(ApplicationPanel)
                .GetField("windowEventHooks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
            registrations.Add(registration);
            try
            {
                panel.Dispose();
                Assert.AreEqual(1, removals);
                Assert.AreEqual(1, registrations.Count);
                Assert.False(registration.IsEnabled);
                Assert.DoesNotThrow(() => callback(new IntPtr(12),
                    (uint)NativeMethods.WinEvents.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, 0, 0, 0, 0));
                allowRemoval = true;
                panel.ReleaseWindowEventHooks();
                Assert.AreEqual(2, removals);
                Assert.IsEmpty(registrations);
            }
            finally
            {
                allowRemoval = true;
                panel.ReleaseWindowEventHooks();
                panel.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static NativeHookRegistration CreateRegistration(out WeakReference callbackTarget,
            Func<bool> remove, IntPtr? handle = null)
        {
            var target = new CallbackTarget();
            callbackTarget = new WeakReference(target);
            Action callback = target.Invoke;
            return new NativeHookRegistration(callback, () => handle ?? new IntPtr(12), pointer => remove(), "test hook");
        }

        private sealed class CallbackTarget { internal void Invoke() { } }

        private static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
