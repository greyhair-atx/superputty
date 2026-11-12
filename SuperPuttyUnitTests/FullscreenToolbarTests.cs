using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;

namespace SuperPuttyUnitTests
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class FullscreenToolbarTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void EachFullscreenEntryPreservesBothCurrentToolbarPositions(bool commandsVisible)
        {
            using (var connection = new ToolStrip())
            using (var commands = new ToolStrip { Visible = commandsVisible })
            {
                var state = new frmSuperPutty.ToolbarPositions(connection, commands);
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    var expectedConnection = new Point(10 + cycle * 20, 24);
                    var expectedCommands = new Point(50 + cycle * 20, 49);
                    connection.Location = expectedConnection;
                    commands.Location = expectedCommands;
                    state.Capture();
                    connection.Location = Point.Empty;
                    commands.Location = Point.Empty;
                    state.Restore();
                    Assert.AreEqual(expectedConnection, connection.Location);
                    Assert.AreEqual(expectedCommands, commands.Location);
                    Assert.AreEqual(commandsVisible, commands.Visible);
                }
            }
        }
    }
}
