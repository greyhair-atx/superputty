using System;
using System.Data;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Gui;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPuttyUnitTests
{
    [TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
    public class FolderAndSelectorRegressionTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void MovingFolderUpdatesAllDescendantsAndPersistsTheirNewPaths()
        {
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                var root = new TreeNode("Root");
                var oldParent = root.Nodes.Add("OldParent");
                var moved = oldParent.Nodes.Add("Moved");
                var direct = moved.Nodes.Add("Direct");
                var nested = moved.Nodes.Add("Nested").Nodes.Add("Deeper").Nodes.Add("NestedSession");
                var directSession = new SessionData { SessionName = "Direct", SessionId = "OldParent/Moved/Direct" };
                var nestedSession = new SessionData { SessionName = "NestedSession", SessionId = "OldParent/Moved/Nested/Deeper/NestedSession" };
                direct.Tag = directSession;
                nested.Tag = nestedSession;
                var destination = root.Nodes.Add("Destination");
                moved.Remove();
                destination.Nodes.Add(moved);
                view.resetFoldersChildrenPaths(moved);
                Assert.AreEqual("Destination/Moved/Direct", directSession.SessionId);
                Assert.AreEqual("Destination/Moved/Nested/Deeper/NestedSession", nestedSession.SessionId);
                string path = Path.GetTempFileName();
                try
                {
                    File.Delete(path);
                    SessionData.SaveSessionsToFile(new System.Collections.Generic.List<SessionData> { directSession, nestedSession }, path);
                    var restored = SessionData.LoadSessionsFromFile(path);
                    Assert.That(restored.ConvertAll(s => s.SessionId), Is.EquivalentTo(new[] {
                        "Destination/Moved/Direct", "Destination/Moved/Nested/Deeper/NestedSession" }));
                }
                finally { File.Delete(path); }
            }
        }

        [TestCase("O'Brien")]
        [TestCase("a'b'c")]
        public void ApostropheSearchMatchesLiteralTextWithoutBreakingFilter(string query)
        {
            using (var selector = new QuickSelector())
            using (var table = new DataTable())
            {
                table.Columns.Add("Name"); table.Columns.Add("Detail");
                table.Rows.Add(query, "first");
                table.Rows.Add("other", query);
                table.Rows.Add("unrelated", "unrelated");
                string filter = (string)typeof(QuickSelector).GetMethod("FormatFilterString", PrivateInstance)
                    .Invoke(selector, new object[] { query });
                Assert.DoesNotThrow(() => table.DefaultView.RowFilter = filter);
                Assert.AreEqual(2, table.DefaultView.Count);
            }
        }

        [TestCase("textBoxData_KeyDown", Keys.Down)]
        [TestCase("dataGridViewData_KeyDown", Keys.Up)]
        public void ArrowNavigationWithNoResultsDoesNotThrow(string handler, Keys key)
        {
            using (var selector = new QuickSelector())
            {
                var grid = (DataGridView)typeof(QuickSelector).GetField("dataGridViewData", PrivateInstance).GetValue(selector);
                grid.DataSource = null;
                var args = new KeyEventArgs(key);
                Assert.DoesNotThrow(() => typeof(QuickSelector).GetMethod(handler, PrivateInstance)
                    .Invoke(selector, new object[] { grid, args }));
                Assert.IsTrue(args.Handled);
            }
        }

        [Test]
        public void DownNavigationUsesVisibleColumnWhenNameColumnIsHidden()
        {
            using (var selector = new QuickSelector())
            {
                var grid = (DataGridView)typeof(QuickSelector).GetField("dataGridViewData", PrivateInstance).GetValue(selector);
                var data = new QuickSelectorData();
                data.ItemData.AddItemDataRow("first", "detail", System.Drawing.Color.Black, null);
                data.ItemData.AddItemDataRow("second", "detail", System.Drawing.Color.Black, null);
                grid.BindingContext = new BindingContext();
                grid.DataSource = data.ItemData.DefaultView;
                grid.Columns[0].Visible = false;
                grid.CurrentCell = grid.Rows[0].Cells[1];
                grid.Rows[0].Selected = true;
                Assert.DoesNotThrow(() => typeof(QuickSelector).GetMethod("textBoxData_KeyDown", PrivateInstance)
                    .Invoke(selector, new object[] { grid, new KeyEventArgs(Keys.Down) }));
                Assert.AreEqual(1, grid.CurrentCell.ColumnIndex);
                Assert.AreEqual(1, grid.CurrentCell.RowIndex);
            }
        }
    }
}
