using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Gui.Docking
{
    // Keep the docking library's hit testing, close restrictions and tab selection.
    // Scope the confirmation bypass to one synchronously dispatched middle click.
    internal sealed class TabCloseMouseFilter : Component, IMessageFilter
    {
        internal const int MiddleButtonDown = 0x0207;
        private readonly DockPanel panel;
        [ThreadStatic] private static DockPaneStripBase middleClickStrip;
        [ThreadStatic] private static IDockContent middleClickContent;

        internal TabCloseMouseFilter(DockPanel panel)
        {
            this.panel = panel;
            Application.AddMessageFilter(this);
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != MiddleButtonDown || !SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation || panel.IsDisposed)
                return false;
            var strip = Control.FromHandle(message.HWnd) as DockPaneStripBase;
            if (strip == null || strip.IsDisposed) return false;
            bool ownedStrip = false;
            foreach (DockPane pane in panel.Panes)
                if (pane.TabStripControl == strip)
                { ownedStrip = true; break; }
            if (!ownedStrip) return false;

            var previous = middleClickStrip;
            var previousContent = middleClickContent;
            try
            {
                middleClickStrip = strip;
                middleClickContent = null;
                // Sent messages enter the control's WndProc directly, rather than
                // returning through the queued-message filter a second time.
                message.Result = SendMessage(message.HWnd, message.Msg, message.WParam, message.LParam);
            }
            finally { middleClickStrip = previous; middleClickContent = previousContent; }
            return true;
        }

        internal static bool IsUnconfirmedMiddleClick(IDockContent content)
        {
            var pane = content.DockHandler.Pane;
            if (pane == null || middleClickStrip == null || pane.TabStripControl != middleClickStrip) return false;
            if (middleClickContent == null) middleClickContent = content;
            return middleClickContent == content;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Application.RemoveMessageFilter(this);
            base.Dispose(disposing);
        }
    }
}
