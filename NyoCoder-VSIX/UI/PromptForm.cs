using System;
using System.Windows.Forms;

namespace NyoCoder
{
	public partial class PromptForm : Form
	{
		public PromptForm()
		{
			InitializeComponent();
			this.Load += PromptForm_Load;
		}

		private void PromptForm_Load(object sender, EventArgs e)
		{
			txtPrompt.Focus();
		}

		public string Prompt
		{
			get { return txtPrompt.Text; }
			set { txtPrompt.Text = value; }
		}

		private void btnOK_Click(object sender, EventArgs e)
		{
			if (string.IsNullOrWhiteSpace(txtPrompt.Text))
			{
				MessageBox.Show("Please enter a prompt.", "NyoCoder", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			this.DialogResult = DialogResult.OK;
			this.Close();
		}

		private void txtPrompt_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control)
			{
				// Enter key sends the prompt
				btnOK_Click(sender, e);
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			else if (e.KeyCode == Keys.Enter && e.Shift)
			{
				// Shift+Enter creates a new line in multiline mode
				// Allow default behavior
			}
		}
	}
}
