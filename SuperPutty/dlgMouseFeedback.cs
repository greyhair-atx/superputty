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

        private void dlgMouseFeedback_MouseMove(object sender, MouseEventArgs e)
        {

        }
    }
}
