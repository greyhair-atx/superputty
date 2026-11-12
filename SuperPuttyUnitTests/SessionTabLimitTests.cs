using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Scp;
using WeifenLuo.WinFormsUI.Docking;
using App = SuperPutty.SuperPuTTY;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class SessionTabLimitTests
    {
        [Test]
        public void TerminalAndScpTabsShareTwoHundredSlotsAndToolsDoNotCount()
        {
            Assert.AreEqual(200, App.MaximumOpenSessions);
            using (var theme = new VS2015LightTheme())
            using (var dock = new DockPanel { Theme = theme })
            using (var tool = new ToolWindow { DockPanel = dock })
            {
                var documents = new List<ToolWindowDocument>();
                try
                {
                    for (int i = 0; i < 199; i++)
                        documents.Add(new ToolWindowDocument { DockPanel = dock });
                    Assert.IsTrue(App.HasSessionCapacity(dock), "Tool windows must not consume session slots.");
                    var scp = new PscpBrowserPanel { DockPanel = dock };
                    documents.Add(scp);
                    Assert.IsFalse(App.HasSessionCapacity(dock));
                    // Include docked sessions, not just DockPanel.Documents.
                    scp.DockState = DockState.DockRight;
                    Assert.IsFalse(App.HasSessionCapacity(dock));
                    documents[0].Dispose();
                    Assert.IsTrue(App.HasSessionCapacity(dock));
                    documents.Add(new ToolWindowDocument { DockPanel = dock });
                    Assert.IsFalse(App.HasSessionCapacity(dock));
                }
                finally { foreach (var document in documents) document.Dispose(); }
            }
        }

        [TestCase(200, false)]
        [TestCase(201, true)]
        public void LayoutsRejectExcessSessionTabsBeforeApplying(int count, bool rejected)
        {
            string file = Path.Combine(Path.GetTempPath(), "SuperPutty-TabLimit-" + Guid.NewGuid() + ".xml");
            var xml = new StringBuilder("<DockPanel><Contents Count='" + (count + 1) + "'>");
            xml.Append("<Content PersistString='SuperPutty.SessionTreeview'/>");
            for (int i = 0; i < count; i++)
                xml.Append("<Content PersistString='SuperPutty.ctlPuttyPanel?SessionId=Session").Append(i).Append("'/>");
            xml.Append("</Contents></DockPanel>");
            File.WriteAllText(file, xml.ToString());
            bool validated = false;
            try
            {
                if (rejected)
                {
                    Assert.Throws<InvalidDataException>(() => LayoutStorage.LoadValidated(file, bytes => validated = true));
                    Assert.IsFalse(validated, "Reject oversized session counts before constructing docking controls.");
                }
                else
                {
                    Assert.DoesNotThrow(() => LayoutStorage.LoadValidated(file, bytes => validated = true));
                    Assert.IsTrue(validated);
                }
            }
            finally { File.Delete(file); }
        }
    }
}
