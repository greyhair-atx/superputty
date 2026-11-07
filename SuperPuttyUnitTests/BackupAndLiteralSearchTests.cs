using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Gui;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class BackupAndLiteralSearchTests
    {
        [TestCase("load%prod")]
        [TestCase("100%")]
        [TestCase("[prod]")]
        [TestCase("name,detail")]
        [TestCase("O'Brien%[prod]")]
        [Apartment(ApartmentState.STA)]
        public void SelectorTreatsExpressionCharactersAsLiteralText(string query)
        {
            using (var selector = new QuickSelector())
            using (var table = new DataTable())
            {
                table.Columns.Add("Name"); table.Columns.Add("Detail");
                table.Rows.Add(query, "exact");
                table.Rows.Add("other", query);
                table.Rows.Add("load-other-prod", "100-other");
                string filter = (string)typeof(QuickSelector).GetMethod("FormatFilterString", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(selector, new object[] { query });
                table.DefaultView.RowFilter = filter;
                Assert.AreEqual(2, table.DefaultView.Count);
            }
        }

        [Test]
        public void RapidBackupsPreserveEverySnapshot()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPutty-BackupSnapshots-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "Sessions.XML");
                foreach (string snapshot in new[] { "first", "second", "third" })
                {
                    File.WriteAllText(path, "<Sessions><Item>" + snapshot + "</Item></Sessions>");
                    Backup(path, 20);
                }
                string[] backups = Directory.GetFiles(directory, "Sessions.*.XML");
                Assert.AreEqual(3, backups.Length);
                Assert.That(backups.Select(file => { var doc = new XmlDocument(); doc.Load(file); return doc.DocumentElement.InnerText; }),
                    Is.EquivalentTo(new[] { "first", "second", "third" }));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void RetentionOnlyDeletesRecognizedBackups()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPutty-BackupRetention-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "Sessions.XML");
                File.WriteAllText(path, "<Sessions/>");
                foreach (string unrelated in new[] { "Sessions.notes.XML", "Sessions.customer.XML", "Sessions.20260101_notes.XML" })
                    File.WriteAllText(Path.Combine(directory, unrelated), "unrelated data");
                File.WriteAllText(Path.Combine(directory, "Sessions.20000101_000000.XML"), "<Sessions/>");
                Backup(path, 1);
                Assert.IsFalse(File.Exists(Path.Combine(directory, "Sessions.20000101_000000.XML")));
                foreach (string unrelated in new[] { "Sessions.notes.XML", "Sessions.customer.XML", "Sessions.20260101_notes.XML" })
                    Assert.AreEqual("unrelated data", File.ReadAllText(Path.Combine(directory, unrelated)));
                Assert.AreEqual(4, Directory.GetFiles(directory, "Sessions.*.XML").Length);
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void Backup(string path, int count)
        {
            typeof(SessionData).GetMethod("BackUpFiles", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { path, count });
        }
    }
}
