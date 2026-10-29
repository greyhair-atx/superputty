using System.Drawing;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Gui.Docking
{
    /// <summary>VS Code Dark Modern inspired surfaces, with the existing docking behavior.</summary>
    internal sealed class DarkModernTheme : VS2015DarkTheme
    {
        internal static readonly Color Surface = Color.FromArgb(24, 24, 24);
        internal static readonly Color Content = Color.FromArgb(31, 31, 31);
        internal static readonly Color Border = Color.FromArgb(43, 43, 43);
        internal static readonly Color InputBorder = Color.FromArgb(45, 45, 48);
        internal static readonly Color Foreground = Color.FromArgb(204, 204, 204);
        internal static readonly Color Muted = Color.FromArgb(157, 157, 157);
        internal static readonly Color Accent = Color.FromArgb(0, 120, 212);

        public DarkModernTheme()
        {
            var p = ColorPalette;
            p.MainWindowActive.Background = Surface;
            p.TabSelectedActive.Background = Content;
            p.TabSelectedActive.Text = Color.White;
            p.TabSelectedInactive.Background = Content;
            p.TabSelectedInactive.Text = Foreground;
            p.TabUnselected.Background = Surface;
            p.TabUnselected.Text = Muted;
            p.TabUnselectedHovered.Background = Content;
            p.TabUnselectedHovered.Text = Foreground;
            p.ToolWindowCaptionActive.Background = Surface;
            p.ToolWindowCaptionActive.Text = Foreground;
            p.ToolWindowCaptionInactive.Background = Surface;
            p.ToolWindowCaptionInactive.Text = Muted;
            p.ToolWindowBorder = Border;
            p.ToolWindowSeparator = Border;
            p.ToolWindowTabSelectedActive.Background = Content;
            p.ToolWindowTabSelectedActive.Text = Color.White;
            p.ToolWindowTabSelectedInactive.Background = Content;
            p.ToolWindowTabSelectedInactive.Text = Foreground;
            p.ToolWindowTabUnselected.Background = Surface;
            p.ToolWindowTabUnselected.Text = Muted;
            p.ToolWindowTabUnselectedHovered.Background = Content;
            p.ToolWindowTabUnselectedHovered.Text = Foreground;
            p.AutoHideStripDefault.Background = Surface;
            p.AutoHideStripDefault.Border = Border;
            p.AutoHideStripDefault.Text = Muted;
            p.AutoHideStripHovered.Background = Content;
            p.AutoHideStripHovered.Border = Accent;
            p.AutoHideStripHovered.Text = Color.White;
            p.MainWindowStatusBarDefault.Background = Surface;
            p.MainWindowStatusBarDefault.Text = Foreground;
            p.CommandBarMenuDefault.Background = Surface;
            p.CommandBarMenuDefault.Text = Foreground;
            p.CommandBarMenuPopupDefault.BackgroundTop = Content;
            p.CommandBarMenuPopupDefault.BackgroundBottom = Content;
            p.CommandBarMenuPopupDefault.Border = Border;
            p.CommandBarMenuPopupDefault.IconBackground = Content;
            p.CommandBarMenuPopupDefault.Separator = Border;
            p.CommandBarMenuPopupHovered.ItemBackground = Accent;
            p.CommandBarMenuPopupHovered.Text = Color.White;
            p.CommandBarMenuTopLevelHeaderHovered.Background = Border;
            p.CommandBarMenuTopLevelHeaderHovered.Border = Border;
            p.CommandBarMenuTopLevelHeaderHovered.Text = Color.White;
            p.CommandBarToolbarDefault.Background = Surface;
            p.CommandBarToolbarDefault.Tray = Surface;
            p.CommandBarToolbarDefault.Border = Border;
            p.CommandBarToolbarDefault.Separator = Border;
            Extender.DockPaneStripFactory = new OutlinedVS2015DockPaneStripFactory();
        }
    }
}
