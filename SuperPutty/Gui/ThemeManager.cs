/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
 * Copyright (c) 2012 John Peterson
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using log4net;
using Microsoft.Win32;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Applies the Light/Dark/System colour theme to the dock panel and to the WinForms controls
    /// of every SuperPuTTY window.  The theme is fixed when the process starts because
    /// DockPanelSuite can only change a DockPanel's theme while no panes are open; changing the
    /// setting therefore takes effect on the next start.  Light leaves every control untouched.
    /// </summary>
    public static class ThemeManager
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ThemeManager));

        public const string Light = "Light";
        public const string Dark = "Dark";
        public const string DarkModern = "Dark Modern";
        public const string FollowSystem = "System";

        public static readonly string[] ThemeNames = { Light, Dark, DarkModern, FollowSystem };

        // VS2015 dark palette
        private static Color DarkBack { get { return IsModern ? Color.FromArgb(24, 24, 24) : Color.FromArgb(45, 45, 48); } }
        private static Color DarkInputBack { get { return IsModern ? Color.FromArgb(49, 49, 49) : Color.FromArgb(37, 37, 38); } }
        private static Color DarkFore { get { return IsModern ? Color.FromArgb(204, 204, 204) : Color.FromArgb(241, 241, 241); } }
        private static Color DarkBorder { get { return IsModern ? Color.FromArgb(43, 43, 43) : Color.FromArgb(63, 63, 70); } }
        private static Color DarkSelection { get { return IsModern ? Color.FromArgb(0, 120, 212) : Color.FromArgb(0, 122, 204); } }
        private static Color ContentBack { get { return IsModern ? Color.FromArgb(31, 31, 31) : DarkInputBack; } }
        private static string startupTheme;
        public static bool IsModern { get { Initialize(); return startupTheme == DarkModern; } }
        public static bool RequiresRestart(string name)
        {
            Initialize();
            return Normalize(name) != startupTheme || Resolve(name) != isDark;
        }

        private static bool initialized;
        private static bool toolStripThemeReady;
        private static bool isDark;
        private static ThemeBase darkTheme;
        private static ToolStripRenderer darkRenderer;

        /// <summary>True when the theme chosen at startup is the dark one</summary>
        public static bool IsDark
        {
            get { Initialize(); return isDark; }
        }

        /// <summary>
        /// Icons without an alpha channel carry a solid white background that looks like a box on a dark
        /// theme.  Returns a copy where the near-white area connected to the image edge is transparent;
        /// images that already have alpha are returned unchanged.
        /// </summary>
        public static Image WithTransparentBackground(Image source)
        {
            if (source == null || Image.IsAlphaPixelFormat(source.PixelFormat)) return source;

            Bitmap bmp = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            int w = bmp.Width, h = bmp.Height;
            bool[,] seen = new bool[w, h];
            System.Collections.Generic.Stack<Point> todo = new System.Collections.Generic.Stack<Point>();
            for (int x = 0; x < w; x++) { todo.Push(new Point(x, 0)); todo.Push(new Point(x, h - 1)); }
            for (int y = 0; y < h; y++) { todo.Push(new Point(0, y)); todo.Push(new Point(w - 1, y)); }

            while (todo.Count > 0)
            {
                Point p = todo.Pop();
                if (p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h || seen[p.X, p.Y]) continue;
                seen[p.X, p.Y] = true;

                Color c = bmp.GetPixel(p.X, p.Y);
                if (c.A == 0 || (c.R >= 250 && c.G >= 250 && c.B >= 250))
                {
                    bmp.SetPixel(p.X, p.Y, Color.Transparent);
                    todo.Push(new Point(p.X + 1, p.Y)); todo.Push(new Point(p.X - 1, p.Y));
                    todo.Push(new Point(p.X, p.Y + 1)); todo.Push(new Point(p.X, p.Y - 1));
                }
            }
            return bmp;
        }

        /// <summary>Normalise a stored setting value to one of <see cref="ThemeNames"/></summary>
        public static string Normalize(string name)
        {
            foreach (string known in ThemeNames)
            {
                if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }
            return Light;
        }

        /// <summary>Resolve a Theme setting value (following Windows for "System") to dark or light</summary>
        public static bool Resolve(string name)
        {
            switch (Normalize(name))
            {
                case Dark: return true;
                case DarkModern: return true;
                case FollowSystem: return WindowsUsesDarkApps();
                default: return false;
            }
        }

        private static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            // only resolve the setting here: IsDark is read while SuperPuTTY initializes, before
            // Application.SetCompatibleTextRenderingDefault, so no controls may be created yet
            startupTheme = Normalize(SuperPuTTY.Settings.Theme);
            isDark = Resolve(startupTheme);
        }

        private static ThemeBase CreateDockTheme()
        {
            return IsModern ? (ThemeBase)new Docking.DarkModernTheme() : new VS2015DarkTheme();
        }

        /// <summary>Create the dark tool strip theme; creates controls, so only call once the UI is starting</summary>
        private static void EnsureToolStripTheme()
        {
            if (toolStripThemeReady || !IsDark) return;
            toolStripThemeReady = true;

            {
                darkTheme = CreateDockTheme();
                // also themes menus and context menus that are not part of any control tree
                darkTheme.ApplyToToolStripManager();

                // ApplyToToolStripManager doesn't reach drop-down and context menus, so also make the
                // theme's renderer the global one
                using (ToolStrip probe = new ToolStrip())
                {
                    darkTheme.ApplyTo(probe);
                    darkRenderer = probe.Renderer;
                }
                if (IsModern)
                    darkRenderer = new ModernToolbarRenderer(darkTheme.ColorPalette);
                if (darkRenderer != null)
                {
                    ToolStripManager.Renderer = darkRenderer;
                }
            }
        }

        /// <summary>Apply the dock theme to a dock panel; must be called before any content is added</summary>
        public static void ApplyToDockPanel(DockPanel panel)
        {
            if (panel == null || !IsDark) return;
            EnsureToolStripTheme();

            try
            {
                // each DockPanel needs its own theme instance
                panel.Theme = CreateDockTheme();
                panel.DockBackColor = DarkBack;
            }
            catch (Exception ex)
            {
                Log.Warn("Unable to apply dock panel theme", ex);
            }
        }

        /// <summary>
        /// Theme a form or control tree.  Controls added later are themed as they appear.
        /// Does nothing for the light theme.
        /// </summary>
        public static void Apply(Control root)
        {
            if (root == null || !IsDark) return;
            EnsureToolStripTheme();

            Form form = root as Form;
            if (form != null)
            {
                form.HandleCreated -= Form_HandleCreated;
                form.HandleCreated += Form_HandleCreated;

                // controls created by a derived form's InitializeComponent appear after this call
                // and may have properties set after they were added, so theme everything again on load
                form.Load -= Form_Load;
                form.Load += Form_Load;
                if (form.IsHandleCreated)
                {
                    SetTitleBarDark(form.Handle);
                }
            }

            ApplyRecursive(root);
        }

        private static void Form_HandleCreated(object sender, EventArgs e)
        {
            SetTitleBarDark(((Form) sender).Handle);
        }

        private static void Form_Load(object sender, EventArgs e)
        {
            ApplyRecursive((Form) sender);
        }

        /// <summary>Theme the drop-down menus (recursively) hanging off a tool strip's items</summary>
        private static void ApplyToDropDowns(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                ToolStripDropDownItem dropDownItem = item as ToolStripDropDownItem;
                if (dropDownItem == null || !dropDownItem.HasDropDownItems) continue;

                ToolStripDropDown dropDown = dropDownItem.DropDown;
                if (darkRenderer != null) dropDown.Renderer = darkRenderer;
                dropDown.BackColor = DarkBack;
                dropDown.ForeColor = DarkFore;
                ApplyToDropDowns(dropDownItem.DropDownItems);
            }
        }

        /// <summary>Use the Windows dark variants of native control themes (scroll bars, selection, borders)</summary>
        private static void SetNativeTheme(Control c)
        {
            c.HandleCreated -= NativeControl_HandleCreated;
            c.HandleCreated += NativeControl_HandleCreated;
            if (c.IsHandleCreated) NativeControl_HandleCreated(c, EventArgs.Empty);
        }

        private static void NativeControl_HandleCreated(object sender, EventArgs e)
        {
            try { SetWindowTheme(((Control)sender).Handle, "DarkMode_Explorer", null); }
            catch (Exception ex) { Log.Debug("Unable to set native dark theme", ex); }
        }

        private static void ApplyRecursive(Control control)
        {
            ApplyToControl(control);

            control.ControlAdded -= Control_ControlAdded;
            control.ControlAdded += Control_ControlAdded;

            foreach (Control child in control.Controls)
            {
                ApplyRecursive(child);
            }
        }

        private static void Control_ControlAdded(object sender, ControlEventArgs e)
        {
            ApplyRecursive(e.Control);
        }

        private static void ApplyToControl(Control c)
        {
            if (c is DockPanel)
            {
                return; // handled by ApplyToDockPanel
            }

            ToolStrip strip = c as ToolStrip;
            if (strip != null)
            {
                strip.RenderMode = ToolStripRenderMode.ManagerRenderMode;
                darkTheme.ApplyTo(strip);
                if (IsModern) strip.Renderer = darkRenderer;
                strip.BackColor = DarkBack;
                strip.ForeColor = DarkFore;
                ApplyToDropDowns(strip.Items);
                if (IsModern)
                    foreach (ToolStripItem item in strip.Items)
                    {
                        ToolStripControlHost host = item as ToolStripControlHost;
                        if (host != null && (host.Control is ComboBox || host.Control is TextBox))
                        {
                            ApplyToControl(host.Control);
                            ToolbarInputBorder.Attach(host.Control);
                        }
                    }
                return;
            }

            if (c is TextBox || c is RichTextBox || c is ListBox || c is TreeView || c is ListView ||
                c is NumericUpDown || c is ComboBox)
            {
                c.BackColor = c is TreeView || c is ListView || c is ListBox || c is RichTextBox
                    ? ContentBack : DarkInputBack;
                c.ForeColor = DarkFore;

                ComboBox combo = c as ComboBox;
                if (combo != null) combo.FlatStyle = FlatStyle.Flat;
                if (IsModern)
                {
                    TextBoxBase input = c as TextBoxBase;
                    if (input != null) input.BorderStyle = BorderStyle.FixedSingle;
                }

                if (c is TreeView || c is ListView || c is ListBox || c is RichTextBox)
                {
                    SetNativeTheme(c); // dark scroll bars and selection highlight
                }
                return;
            }

            Button button = c as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = DarkBorder;
                if (IsModern)
                {
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 43, 43);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(49, 49, 49);
                }
                button.BackColor = DarkBack;
                button.ForeColor = DarkFore;
                return;
            }

            CheckBox check = c as CheckBox;
            if (check != null)
            {
                check.ForeColor = DarkFore;
                return;
            }

            DataGridView grid = c as DataGridView;
            if (grid != null)
            {
                grid.EnableHeadersVisualStyles = false;
                grid.BackgroundColor = ContentBack;
                grid.GridColor = DarkBorder;
                grid.DefaultCellStyle.BackColor = ContentBack;
                grid.DefaultCellStyle.ForeColor = DarkFore;
                grid.DefaultCellStyle.SelectionBackColor = DarkSelection;
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.ColumnHeadersDefaultCellStyle.BackColor = DarkBack;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = DarkFore;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = DarkBack;
                grid.RowHeadersDefaultCellStyle.BackColor = DarkBack;
                grid.RowHeadersDefaultCellStyle.ForeColor = DarkFore;
                return;
            }

            TabControl tabs = c as TabControl;
            if (tabs != null)
            {
                tabs.BackColor = DarkBack;
                tabs.ForeColor = DarkFore;
                TabPainter.Attach(tabs);
                return;
            }

            PropertyGrid pg = c as PropertyGrid;
            if (pg != null)
            {
                pg.BackColor = DarkBack;
                pg.ForeColor = DarkFore;
                pg.ViewBackColor = ContentBack;
                pg.ViewForeColor = DarkFore;
                pg.HelpBackColor = DarkBack;
                pg.HelpForeColor = DarkFore;
                pg.CategoryForeColor = DarkFore;
                pg.LineColor = DarkBorder;
                return;
            }

            LinkLabel link = c as LinkLabel;
            if (link != null)
            {
                link.LinkColor = Color.FromArgb(86, 156, 214);
                link.ActiveLinkColor = Color.FromArgb(120, 180, 230);
                link.VisitedLinkColor = Color.FromArgb(86, 156, 214);
            }

            if (c is ProgressBar || c is PictureBox)
            {
                return;
            }

            // Form, Panel, GroupBox, TabPage, UserControl, Label, SplitContainer, ...
            c.BackColor = DarkBack;
            c.ForeColor = DarkFore;
        }

        private sealed class ModernToolbarRenderer : VisualStudioToolStripRenderer
        {
            public ModernToolbarRenderer(DockPanelColorPalette palette) : base(palette) { }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip is ToolStripDropDown)
                    base.OnRenderToolStripBackground(e);
                else
                    using (var brush = new SolidBrush(DarkBack))
                        e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
            {
                using (var brush = new SolidBrush(DarkBack))
                    e.Graphics.FillRectangle(brush, e.ToolStripPanel.ClientRectangle);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e);
            }
        }

        /// <summary>
        /// Gives hosted toolbar inputs the same border despite their different native renderers.
        /// </summary>
        private sealed class ToolbarInputBorder : NativeWindow
        {
            private readonly Control control;
            private static readonly ConditionalWeakTable<Control, ToolbarInputBorder> Borders =
                new ConditionalWeakTable<Control, ToolbarInputBorder>();

            private ToolbarInputBorder(Control control)
            {
                this.control = control;
                control.HandleCreated += delegate { AssignHandle(control.Handle); };
                control.HandleDestroyed += delegate { ReleaseHandle(); };
                if (control.IsHandleCreated) AssignHandle(control.Handle);
            }

            public static void Attach(Control control)
            {
                Borders.GetValue(control, c => new ToolbarInputBorder(c));
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                // TextBox borders are non-client; ComboBox borders are client painted.
                // Redraw after native painting, including focus and hover transitions.
                if (m.Msg != 0x000F && m.Msg != 0x0085 && m.Msg != 0x0007 && m.Msg != 0x0008)
                    return;
                IntPtr dc = GetWindowDC(Handle);
                if (dc == IntPtr.Zero) return;
                try
                {
                    using (Graphics graphics = Graphics.FromHdc(dc))
                    using (Pen pen = new Pen(Docking.DarkModernTheme.InputBorder))
                        graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
                }
                finally { ReleaseDC(Handle, dc); }
            }

            [DllImport("user32.dll")]
            private static extern IntPtr GetWindowDC(IntPtr window);

            [DllImport("user32.dll")]
            private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        }

        /// <summary>Paints dark tabs and frames over the native tab control.</summary>
        private class TabPainter : NativeWindow
        {
            private const int WM_PAINT = 0x000F;
            private const int WM_ERASEBKGND = 0x0014;

            private readonly TabControl tabs;

            private TabPainter(TabControl tabs)
            {
                this.tabs = tabs;
                tabs.HandleCreated += delegate { this.AssignHandle(tabs.Handle); };
                tabs.HandleDestroyed += delegate { this.ReleaseHandle(); };
                if (tabs.IsHandleCreated)
                {
                    this.AssignHandle(tabs.Handle);
                }
            }

            private static readonly ConditionalWeakTable<TabControl, TabPainter> Painters =
                new ConditionalWeakTable<TabControl, TabPainter>();

            public static void Attach(TabControl tabs)
            {
                TabPainter existing;
                if (!Painters.TryGetValue(tabs, out existing))
                {
                    Painters.Add(tabs, new TabPainter(tabs));
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_ERASEBKGND)
                {
                    using (Graphics g = Graphics.FromHdc(m.WParam))
                    {
                        g.Clear(DarkBack);
                    }
                    m.Result = (IntPtr) 1;
                    return;
                }

                base.WndProc(ref m);

                if (m.Msg == WM_PAINT)
                {
                    try { PaintOver(); }
                    catch (Exception ex) { Log.Debug("Unable to paint dark tab strip", ex); }
                }
            }

            private void PaintOver()
            {
                using (Graphics g = Graphics.FromHwnd(this.Handle))
                {
                    Rectangle page = tabs.DisplayRectangle;

                    // everything except the page area: tab strip background and the frame around the pages
                    g.SetClip(page, System.Drawing.Drawing2D.CombineMode.Exclude);
                    g.Clear(DarkBack);

                    using (Pen border = new Pen(DarkBorder))
                    {
                        Rectangle frame = Rectangle.Inflate(page, 2, 2);
                        g.DrawRectangle(border, frame.X, frame.Y, frame.Width - 1, frame.Height - 1);

                        for (int i = 0; i < tabs.TabCount; i++)
                        {
                            Rectangle r = tabs.GetTabRect(i);
                            bool selected = i == tabs.SelectedIndex;
                            using (Brush back = new SolidBrush(selected ? (IsModern ? ContentBack : DarkSelection) : DarkBack))
                            {
                                g.FillRectangle(back, r);
                            }
                            g.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - 1);
                            if (selected && IsModern)
                            {
                                using (Brush accent = new SolidBrush(DarkSelection))
                                    g.FillRectangle(accent, r.X, r.Y, r.Width, 2);
                            }
                            TextRenderer.DrawText(g, tabs.TabPages[i].Text, tabs.Font, r,
                                selected ? Color.White : DarkFore,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                        }
                    }
                }
            }
        }

        private static bool WindowsUsesDarkApps()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key != null ? key.GetValue("AppsUseLightTheme") : null;
                    return value is int && (int) value == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Unable to read Windows app theme", ex);
                return false;
            }
        }

        // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 10 20H1+/11, 19 on earlier Windows 10 builds
        private static void SetTitleBarDark(IntPtr hwnd)
        {
            try
            {
                int value = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                // dwmapi attribute unsupported on this version of Windows; title bar stays light
                Log.Debug("Unable to set immersive dark mode", ex);
            }
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
