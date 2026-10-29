using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ProtocolVisibilityTests
    {
        [Test]
        public void DefaultsShowAllAndHiddenEntriesKeepDisplayOrder()
        {
            var all = SessionData.GetProtocolDisplayOrder();
            CollectionAssert.AreEqual(all, SessionData.GetVisibleProtocols(null));
            CollectionAssert.AreEqual(all.Where(p => p != ConnectionProtocol.Raw && p != ConnectionProtocol.Telnet),
                SessionData.GetVisibleProtocols("raw,TELNET,Unknown"));
            CollectionAssert.AreEqual(new[] { ConnectionProtocol.SSH },
                SessionData.GetVisibleProtocols(String.Join(",", all)));
        }

        [TestCase(ConnectionProtocol.Raw, "Raw")]
        [TestCase(ConnectionProtocol.SSH2, "SSH")]
        [TestCase(ConnectionProtocol.SSHNet, "SSH")]
        public void EditingExistingSessionPreservesHiddenProtocol(ConnectionProtocol protocol, string label)
        {
            WithHiddenProtocols("Raw,SSH", delegate
            {
                using (var dialog = new dlgEditSession(new SessionData { SessionName = "Saved", Proto = protocol }, null))
                {
                    var combo = (ComboBox)dialog.Controls.Find("comboBoxProto", true).Single();
                    Assert.AreEqual(label, combo.SelectedItem);
                    Assert.IsFalse(combo.Items.Contains(label == "Raw" ? "SSH" : "Raw"));
                }
            });
        }

        [Test]
        public void NewSessionFallsBackWhenSshIsHidden()
        {
            WithHiddenProtocols("SSH,Raw", delegate
            {
                using (var dialog = new dlgEditSession(new SessionData(), null))
                {
                    var combo = (ComboBox)dialog.Controls.Find("comboBoxProto", true).Single();
                    Assert.AreEqual("Serial", combo.SelectedItem);
                    Assert.IsFalse(combo.Items.Contains("SSH"));
                    Assert.IsFalse(combo.Items.Contains("Raw"));
                }
            });
        }

        private static void WithHiddenProtocols(string hidden, Action action)
        {
            string original = SuperPutty.SuperPuTTY.Settings.HiddenProtocols;
            try
            {
                SuperPutty.SuperPuTTY.Settings.HiddenProtocols = hidden;
                action();
            }
            finally { SuperPutty.SuperPuTTY.Settings.HiddenProtocols = original; }
        }
    }
}
