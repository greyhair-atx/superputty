using System;
using System.Windows.Forms;

namespace SuperPutty.Utils
{
    internal sealed class KeyboardHookState
    {
        internal static readonly UIntPtr SuperPuttyInputMarker = new UIntPtr(0x53505459u);

        private bool isLeftControlDown;
        private bool isRightControlDown;
        private bool isLeftShiftDown;
        private bool isRightShiftDown;
        private bool isLeftAltDown;
        private bool isRightAltDown;
        private bool isLeftWinDown;
        private bool isRightWinDown;

        internal long Version { get; private set; }

        internal bool IsControlDown { get { return isLeftControlDown || isRightControlDown; } }
        internal bool IsShiftDown { get { return isLeftShiftDown || isRightShiftDown; } }
        internal bool IsAltDown { get { return isLeftAltDown || isRightAltDown; } }
        internal bool IsWinDown { get { return isLeftWinDown || isRightWinDown; } }

        internal void Update(Keys keyCode, bool isKeyDown)
        {
            switch (keyCode)
            {
                case Keys.LControlKey:
                    SetState(ref isLeftControlDown, isKeyDown);
                    break;
                case Keys.RControlKey:
                    SetState(ref isRightControlDown, isKeyDown);
                    break;
                case Keys.LShiftKey:
                    SetState(ref isLeftShiftDown, isKeyDown);
                    break;
                case Keys.RShiftKey:
                    SetState(ref isRightShiftDown, isKeyDown);
                    break;
                case Keys.LMenu:
                    SetState(ref isLeftAltDown, isKeyDown);
                    break;
                case Keys.RMenu:
                    SetState(ref isRightAltDown, isKeyDown);
                    break;
                case Keys.LWin:
                    SetState(ref isLeftWinDown, isKeyDown);
                    break;
                case Keys.RWin:
                    SetState(ref isRightWinDown, isKeyDown);
                    break;
            }
        }

        internal void Synchronize(Func<Keys, bool> isKeyDown)
        {
            if (isKeyDown == null)
            {
                throw new ArgumentNullException("isKeyDown");
            }

            bool leftControlDown = isKeyDown(Keys.LControlKey);
            bool rightControlDown = isKeyDown(Keys.RControlKey);
            bool leftShiftDown = isKeyDown(Keys.LShiftKey);
            bool rightShiftDown = isKeyDown(Keys.RShiftKey);
            bool leftAltDown = isKeyDown(Keys.LMenu);
            bool rightAltDown = isKeyDown(Keys.RMenu);
            bool leftWinDown = isKeyDown(Keys.LWin);
            bool rightWinDown = isKeyDown(Keys.RWin);

            bool changed =
                isLeftControlDown != leftControlDown ||
                isRightControlDown != rightControlDown ||
                isLeftShiftDown != leftShiftDown ||
                isRightShiftDown != rightShiftDown ||
                isLeftAltDown != leftAltDown ||
                isRightAltDown != rightAltDown ||
                isLeftWinDown != leftWinDown ||
                isRightWinDown != rightWinDown;

            isLeftControlDown = leftControlDown;
            isRightControlDown = rightControlDown;
            isLeftShiftDown = leftShiftDown;
            isRightShiftDown = rightShiftDown;
            isLeftAltDown = leftAltDown;
            isRightAltDown = rightAltDown;
            isLeftWinDown = leftWinDown;
            isRightWinDown = rightWinDown;

            if (changed)
            {
                Version++;
            }
        }

        internal void Reset()
        {
            if (IsControlDown || IsShiftDown || IsAltDown || IsWinDown)
            {
                Version++;
            }

            isLeftControlDown = false;
            isRightControlDown = false;
            isLeftShiftDown = false;
            isRightShiftDown = false;
            isLeftAltDown = false;
            isRightAltDown = false;
            isLeftWinDown = false;
            isRightWinDown = false;
        }

        internal static bool IsSuperPuttyInjectedInput(NativeMethods.KBDLLHOOKSTRUCT keyboardData)
        {
            return (keyboardData.flags & NativeMethods.LowLevelKeyboardFlags.Injected) != 0 &&
                keyboardData.dwExtraInfo.Equals(SuperPuttyInputMarker);
        }

        private void SetState(ref bool currentState, bool newState)
        {
            if (currentState != newState)
            {
                currentState = newState;
                Version++;
            }
        }
    }
}
