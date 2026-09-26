/*
 * https://github.com/jimradford/superputty/blob/master/License.txt
 */

using System;
using System.Windows.Forms;
using SuperPutty;
using SuperPutty.Utils;

namespace SuperPuTTY.Scripting
{
    public static partial class Commands
    {
        
        /// <summary>prompt a user for hidden input</summary>
        /// <param name="arg">The pre-parsed string to send</param>
        /// <returns>A string containing commands to send with variables replaced with a carriage return sent at the end</returns>
        internal static CommandData PrivatePromptHandler(string arg)
        {
            return ShowScriptPrompt("Insert your password", true);
        }

        internal static CommandData ShowScriptPrompt(string message, bool password,
            Action<frmPrivatePrompt> onShown = null)
        {
            SPSL.CheckCancellation();
            using (var dialog = new frmPrivatePrompt(message, password))
            using (var timer = new Timer { Interval = 50 })
            {
                timer.Tick += (sender, e) =>
                {
                    try { SPSL.CheckCancellation(); }
                    catch (OperationCanceledException) { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
                };
                dialog.Shown += (sender, e) =>
                {
                    timer.Start();
                    if (onShown != null) onShown(dialog);
                };
                if (dialog.ShowDialog() != DialogResult.OK)
                    throw new OperationCanceledException();
                SPSL.CheckCancellation();
                return new CommandData(dialog.GetResult(), new KeyEventArgs(Keys.Enter), TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
