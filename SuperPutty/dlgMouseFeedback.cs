using System.Windows.Forms;

namespace SuperPutty
{
    public partial class dlgMouseFeedback : Form
    {
        public dlgMouseFeedback()
        {
            InitializeComponent();
            SuperPutty.Gui.ThemeManager.Apply(this);
        }

        // Remove in a future cleanup. No MouseMove event is wired to this handler.
        private void dlgMouseFeedback_MouseMove(object sender, MouseEventArgs e)
        {

        }
    }
}
