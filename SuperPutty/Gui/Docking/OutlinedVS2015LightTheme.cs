/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
 * Licensed under the MIT License; see the repository License.txt.
 */

using System.Drawing;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Gui.Docking
{
    /// <summary>
    /// VS2015 light theme with distinct, outlined document tabs.
    /// </summary>
    internal sealed class OutlinedVS2015LightTheme : VS2015LightTheme
    {
        internal static readonly Color ActiveTabBackground = Color.FromArgb(89, 89, 89);
        internal static readonly Color ActiveTabOutline = Color.White;
        internal static readonly Color InactiveTabBackground = Color.FromArgb(230, 230, 230);
        internal static readonly Color InactiveTabOutline = Color.FromArgb(160, 160, 160);
        internal static readonly Color HoveredTabBackground = Color.FromArgb(160, 160, 160);
        internal static readonly Color HoveredTabOutline = Color.White;

        public OutlinedVS2015LightTheme()
        {
            ColorPalette.TabSelectedActive.Background = ActiveTabBackground;
            ColorPalette.TabSelectedActive.Text = Color.White;
            ColorPalette.TabSelectedInactive.Background = ActiveTabBackground;
            ColorPalette.TabSelectedInactive.Text = Color.White;
            ColorPalette.TabUnselected.Background = InactiveTabBackground;
            ColorPalette.TabUnselected.Text = Color.Black;
            ColorPalette.TabUnselectedHovered.Background = HoveredTabBackground;
            ColorPalette.TabUnselectedHovered.Text = Color.Black;

            Extender.DockPaneStripFactory = new OutlinedVS2015DockPaneStripFactory();
        }
    }

    internal sealed class OutlinedVS2015DockPaneStripFactory : DockPanelExtender.IDockPaneStripFactory
    {
        public DockPaneStripBase CreateDockPaneStrip(DockPane pane)
        {
            return new OutlinedVS2015DockPaneStrip(pane);
        }
    }
}
