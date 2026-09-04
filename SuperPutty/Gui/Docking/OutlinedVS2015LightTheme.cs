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
        public OutlinedVS2015LightTheme()
        {
            Color activeTabColor = Color.FromArgb(230, 230, 230);
            Color inactiveTabColor = Color.FromArgb(89, 89, 89);

            ColorPalette.TabSelectedActive.Background = activeTabColor;
            ColorPalette.TabSelectedActive.Text = Color.Black;
            ColorPalette.TabSelectedInactive.Background = activeTabColor;
            ColorPalette.TabSelectedInactive.Text = Color.Black;
            ColorPalette.TabUnselected.Background = inactiveTabColor;
            ColorPalette.TabUnselected.Text = Color.White;
            ColorPalette.TabUnselectedHovered.Background = Color.FromArgb(115, 115, 115);
            ColorPalette.TabUnselectedHovered.Text = Color.White;

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
