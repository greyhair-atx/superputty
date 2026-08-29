/*
 * Copyright (c) 2026 SuperPuTTY contributors
 * Licensed under the MIT license. See License.txt in the project root.
 */

using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AxMSTSCLib;
using log4net;
using MSTSCLib;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPutty
{
    /// <summary>
    /// Hosts Microsoft's RDP ActiveX control directly in a SuperPuTTY tab.
    /// This avoids launching, discovering, and reparenting an MSTSC window.
    /// </summary>
    public sealed class RdpClientPanel : ApplicationPanel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(RdpClientPanel));

        private readonly SessionData session;
        private AxMsRdpClient10NotSafeForScripting client;
        private bool configured;
        private bool connecting;
        private bool closing;
        private int closeNotified;
        private readonly System.Threading.Timer displayResizeTimer;
        private Size lastSessionDisplaySize = Size.Empty;

        private RdpClientPanel(SessionData session, PuttyClosedCallback closeCallback)
            : base(ConnectionProtocol.RDP)
        {
            this.session = session;
            this.m_CloseCallback = closeCallback;
            this.displayResizeTimer = new System.Threading.Timer(this.UpdateSessionDisplaySettings, null,
                Timeout.Infinite, Timeout.Infinite);
            this.InitializeClient();
        }

        protected override bool UsesManagedChildHost { get { return true; } }

        public override IntPtr AppWindowHandle
        {
            get
            {
                if (this.client != null && this.client.IsHandleCreated)
                {
                    return this.client.Handle;
                }

                return this.IsHandleCreated ? this.Handle : IntPtr.Zero;
            }
        }

        public override bool ExternalProcessCaptured
        {
            get { return this.client != null && this.client.IsHandleCreated; }
        }

        /// <summary>
        /// Uses ActiveX only for ordinary MSTSC sessions. FreeRDP remains an
        /// external client, and sessions with MSTSC command-line overrides retain
        /// the external path so existing arguments keep their exact behavior.
        /// </summary>
        public static bool ShouldUseActiveX(SessionData session, string executable)
        {
            return session != null &&
                session.Proto == ConnectionProtocol.RDP &&
                !RDPStartInfo.IsFreeRdpExecutable(executable) &&
                String.IsNullOrWhiteSpace(session.ExtraArgs);
        }

        public static bool TryCreate(
            SessionData session,
            PuttyClosedCallback closeCallback,
            out RdpClientPanel panel)
        {
            panel = null;
            RdpClientPanel candidate = null;
            try
            {
                candidate = new RdpClientPanel(session, closeCallback);

                // Force COM activation here so ctlPuttyPanel can fall back to the
                // external MSTSC host before the tab is displayed.
                candidate.CreateControl();
                candidate.client.CreateControl();
                candidate.ConfigureClient();
                panel = candidate;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Unable to initialize the Microsoft RDP ActiveX control.", ex);
                if (candidate != null)
                {
                    try
                    {
                        candidate.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        Log.Debug("Unable to completely dispose a partially initialized RDP ActiveX control.", disposeException);
                    }
                }
                return false;
            }
        }

        public override void RefreshAppWindow()
        {
            if (this.client == null || this.closing || this.IsDisposed || this.Disposing)
            {
                return;
            }

            try
            {
                this.client.AdvancedSettings2.SmartSizing = true;
                this.client.Invalidate();
                this.client.Update();
            }
            catch (InvalidComObjectException ex)
            {
                Log.Debug("Ignoring an RDP refresh after its ActiveX control was released.", ex);
            }
        }

        public override bool ReFocusPuTTY(string caller)
        {
            if (this.client == null ||
                this.closing ||
                this.IsDisposed ||
                this.Disposing ||
                !this.client.IsHandleCreated)
            {
                return false;
            }

            try
            {
                bool focused = this.client.Focus();
                Log.DebugFormat("RDP ActiveX focus requested by {0}; result={1}", caller, focused);
                return focused;
            }
            catch (InvalidComObjectException ex)
            {
                Log.Debug("Ignoring an RDP focus request after its ActiveX control was released.", ex);
                return false;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            if (!this.Visible ||
                !this.configured ||
                this.connecting ||
                this.closing ||
                this.IsDisposed ||
                this.Disposing ||
                this.client == null ||
                !this.client.IsHandleCreated)
            {
                return;
            }

            try
            {
                if (this.client.Connected != 0)
                {
                    return;
                }

                this.connecting = true;
                this.client.Connect();
            }
            catch (InvalidComObjectException ex)
            {
                this.connecting = false;
                Log.Debug("Ignoring a visibility change after the RDP ActiveX control was released.", ex);

                if (!this.closing && !this.IsDisposed && !this.Disposing)
                {
                    this.NotifyClosed(true);
                }
            }
            catch (COMException ex)
            {
                this.connecting = false;
                Log.Error("Unable to connect the RDP ActiveX control.", ex);
                this.NotifyClosed(true);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (this.configured && !this.closing && this.displayResizeTimer != null)
            {
                // Docking and splitter drags generate many intermediate sizes. Let
                // the final tab size settle before asking the server to resize.
                this.displayResizeTimer.Change(200, Timeout.Infinite);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            this.CloseClient();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // This must happen before base.Dispose tears down the AxHost and
                // separates its COM object from the runtime callable wrapper.
                this.CloseClient();
                this.displayResizeTimer.Dispose();
                this.DetachClientEvents();
            }

            base.Dispose(disposing);
        }

        private void InitializeClient()
        {
            this.client = new AxMsRdpClient10NotSafeForScripting();
            ((ISupportInitialize)this.client).BeginInit();
            this.client.Dock = DockStyle.Fill;
            this.client.Enabled = true;
            this.client.Name = "rdpActiveXClient";
            this.Controls.Add(this.client);
            ((ISupportInitialize)this.client).EndInit();

            this.client.OnConnected += this.Client_OnConnected;
            this.client.OnLoginComplete += this.Client_OnLoginComplete;
            this.client.OnDisconnected += this.Client_OnDisconnected;
            this.client.OnFatalError += this.Client_OnFatalError;
        }

        private void ConfigureClient()
        {
            if (this.configured)
            {
                return;
            }

            this.client.Server = this.session.Host;
            this.client.UserName = this.session.Username ?? String.Empty;
            this.client.ColorDepth = 32;
            this.client.DesktopWidth = 1920;
            this.client.DesktopHeight = 1080;
            this.client.AdvancedSettings2.RDPPort = this.session.Port == 0 ? 3389 : this.session.Port;
            this.client.AdvancedSettings2.SmartSizing = true;
            this.client.AdvancedSettings8.RedirectClipboard = true;
            this.client.AdvancedSettings8.EnableCredSspSupport = true;
            this.configured = true;
        }

        private void Client_OnConnected(object sender, EventArgs e)
        {
            this.connecting = false;
            Log.InfoFormat("RDP ActiveX connected to {0}:{1}",
                this.session.Host,
                this.session.Port == 0 ? 3389 : this.session.Port);
            this.displayResizeTimer.Change(0, Timeout.Infinite);
        }

        private void UpdateSessionDisplaySettings(object state)
        {
            if (this.IsDisposed || this.Disposing || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                this.BeginInvoke(new MethodInvoker(this.UpdateSessionDisplaySettings));
            }
            catch (InvalidOperationException)
            {
                // The panel can lose its handle while a delayed resize is queued.
            }
        }

        private void UpdateSessionDisplaySettings()
        {
            if (this.client == null || this.closing || this.client.Connected == 0)
            {
                return;
            }

            Size displaySize = this.client.ClientSize;
            int width = Math.Max(200, Math.Min(8192, displaySize.Width));
            int height = Math.Max(200, Math.Min(8192, displaySize.Height));
            width -= width % 2;
            displaySize = new Size(width, height);
            if (displaySize == this.lastSessionDisplaySize)
            {
                return;
            }

            try
            {
                IMsRdpClient9 rdpClient9 = (IMsRdpClient9)this.client.GetOcx();
                uint dpi = (uint)Math.Max(96, this.DeviceDpi);
                uint physicalWidth = (uint)Math.Max(10, Math.Round(width * 25.4 / dpi));
                uint physicalHeight = (uint)Math.Max(10, Math.Round(height * 25.4 / dpi));

                rdpClient9.UpdateSessionDisplaySettings(
                    (uint)width,
                    (uint)height,
                    physicalWidth,
                    physicalHeight,
                    0,
                    100,
                    100);
                this.lastSessionDisplaySize = displaySize;
                Log.DebugFormat("RDP session display resized to {0}x{1}", width, height);
            }
            catch (Exception ex)
            {
                // SmartSizing remains enabled throughout the connection, so older
                // clients and servers continue to scale the original desktop.
                Log.Debug("Dynamic RDP display resizing is unavailable; retaining smart sizing.", ex);
            }
        }

        private void Client_OnLoginComplete(object sender, EventArgs e)
        {
            Log.InfoFormat("RDP ActiveX login completed for {0}", this.session.Host);
        }

        private void Client_OnDisconnected(object sender, IMsTscAxEvents_OnDisconnectedEvent e)
        {
            this.connecting = false;
            if (this.closing)
            {
                return;
            }

            uint extendedReason = 0;
            string description = String.Empty;
            try
            {
                if (this.client != null)
                {
                    extendedReason = (uint)this.client.ExtendedDisconnectReason;
                    description = this.client.GetErrorDescription((uint)e.discReason, extendedReason);
                }
            }
            catch (COMException)
            {
                // Some older client builds don't return descriptions for every code.
            }

            Log.InfoFormat("RDP ActiveX disconnected. reason={0}, extended={1}, description={2}",
                e.discReason,
                extendedReason,
                description);
            this.NotifyClosed(e.discReason != 1 && e.discReason != 3);
        }

        private void Client_OnFatalError(object sender, IMsTscAxEvents_OnFatalErrorEvent e)
        {
            Log.ErrorFormat("RDP ActiveX fatal error {0}", e.errorCode);
            this.NotifyClosed(true);
        }

        private void CloseClient()
        {
            if (this.closing)
            {
                return;
            }

            this.closing = true;
            try
            {
                if (this.client != null && this.client.IsHandleCreated && this.client.Connected != 0)
                {
                    ControlCloseStatus closeStatus = this.client.RequestClose();
                    if (closeStatus == ControlCloseStatus.controlCloseCanProceed)
                    {
                        this.client.Disconnect();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("RDP ActiveX was already disconnected while closing.", ex);
            }
        }

        private void DetachClientEvents()
        {
            if (this.client == null)
            {
                return;
            }

            try
            {
                this.client.OnConnected -= this.Client_OnConnected;
                this.client.OnLoginComplete -= this.Client_OnLoginComplete;
                this.client.OnDisconnected -= this.Client_OnDisconnected;
                this.client.OnFatalError -= this.Client_OnFatalError;
            }
            catch (InvalidComObjectException ex)
            {
                Log.Debug("RDP ActiveX events were already detached during disposal.", ex);
            }
        }

        private void NotifyClosed(bool error)
        {
            if (Interlocked.Exchange(ref this.closeNotified, 1) != 0)
            {
                return;
            }

            PuttyClosedCallback callback = this.m_CloseCallback;
            if (callback != null)
            {
                callback(error);
            }
        }
    }
}
