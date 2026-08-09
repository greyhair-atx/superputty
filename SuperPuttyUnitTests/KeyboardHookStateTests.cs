using System;
using System.Collections.Generic;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class KeyboardHookStateTests
    {
        [Test]
        public void TracksLeftAndRightModifiersIndependently()
        {
            KeyboardHookState state = new KeyboardHookState();

            state.Update(Keys.LControlKey, true);
            state.Update(Keys.RControlKey, true);
            state.Update(Keys.LControlKey, false);

            Assert.True(state.IsControlDown);

            state.Update(Keys.RControlKey, false);

            Assert.False(state.IsControlDown);
        }

        [Test]
        public void ResetClearsAllModifiers()
        {
            KeyboardHookState state = new KeyboardHookState();
            state.Update(Keys.LControlKey, true);
            state.Update(Keys.RShiftKey, true);
            state.Update(Keys.LMenu, true);
            state.Update(Keys.RWin, true);

            state.Reset();

            Assert.False(state.IsControlDown);
            Assert.False(state.IsShiftDown);
            Assert.False(state.IsAltDown);
            Assert.False(state.IsWinDown);
        }

        [Test]
        public void SynchronizeRestoresPhysicallyHeldModifiers()
        {
            KeyboardHookState state = new KeyboardHookState();
            HashSet<Keys> downKeys = new HashSet<Keys> { Keys.LShiftKey, Keys.RWin };

            state.Synchronize(downKeys.Contains);

            Assert.False(state.IsControlDown);
            Assert.True(state.IsShiftDown);
            Assert.False(state.IsAltDown);
            Assert.True(state.IsWinDown);
        }

        [Test]
        public void IdentifiesOnlyMarkedSuperPuttyInput()
        {
            NativeMethods.KBDLLHOOKSTRUCT keyboardData = new NativeMethods.KBDLLHOOKSTRUCT
            {
                flags = NativeMethods.LowLevelKeyboardFlags.Injected,
                dwExtraInfo = KeyboardHookState.SuperPuttyInputMarker
            };

            Assert.True(KeyboardHookState.IsSuperPuttyInjectedInput(keyboardData));

            keyboardData.dwExtraInfo = UIntPtr.Zero;
            Assert.False(KeyboardHookState.IsSuperPuttyInjectedInput(keyboardData));

            keyboardData.flags = 0;
            keyboardData.dwExtraInfo = KeyboardHookState.SuperPuttyInputMarker;
            Assert.False(KeyboardHookState.IsSuperPuttyInjectedInput(keyboardData));
        }
    }
}
