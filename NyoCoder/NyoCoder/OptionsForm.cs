using System;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
	public partial class OptionsForm : Form
	{
		private Label lblTitle;
		private Label lblApiKey;
		private TextBox txtApiKey;
		private Label lblLlmServer;
		private TextBox txtLlmServer;
		private Label lblModel;
		private TextBox txtModel;
		private Button btnOK;
		private Button btnCancel;

		public OptionsForm()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			this.lblTitle = new Label();
			this.lblApiKey = new Label();
			this.txtApiKey = new TextBox();
			this.lblLlmServer = new Label();
			this.txtLlmServer = new TextBox();
			this.lblModel = new Label();
			this.txtModel = new TextBox();
			this.btnOK = new Button();
			this.btnCancel = new Button();
			this.SuspendLayout();

			// lblTitle
			this.lblTitle.AutoSize = true;
			this.lblTitle.Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Bold);
			this.lblTitle.Location = new Point(12, 9);
			this.lblTitle.Name = "lblTitle";
			this.lblTitle.Size = new Size(59, 17);
			this.lblTitle.TabIndex = 0;
			this.lblTitle.Text = "Options:";

			// lblApiKey
			this.lblApiKey.AutoSize = true;
			this.lblApiKey.Location = new Point(12, 40);
			this.lblApiKey.Name = "lblApiKey";
			this.lblApiKey.Size = new Size(52, 13);
			this.lblApiKey.TabIndex = 1;
			this.lblApiKey.Text = "API Key:";

			// txtApiKey
			this.txtApiKey.Location = new Point(12, 56);
			this.txtApiKey.Name = "txtApiKey";
			this.txtApiKey.Size = new Size(360, 20);
			this.txtApiKey.TabIndex = 2;
			this.txtApiKey.UseSystemPasswordChar = true;

			// lblLlmServer
			this.lblLlmServer.AutoSize = true;
			this.lblLlmServer.Location = new Point(12, 90);
			this.lblLlmServer.Name = "lblLlmServer";
			this.lblLlmServer.Size = new Size(178, 13);
			this.lblLlmServer.TabIndex = 3;
			this.lblLlmServer.Text = "LLM Server (OpenAI Compatible):";

			// txtLlmServer
			this.txtLlmServer.Location = new Point(12, 106);
			this.txtLlmServer.Name = "txtLlmServer";
			this.txtLlmServer.Size = new Size(360, 20);
			this.txtLlmServer.TabIndex = 4;

			// lblModel
			this.lblModel.AutoSize = true;
			this.lblModel.Location = new Point(12, 140);
			this.lblModel.Name = "lblModel";
			this.lblModel.Size = new Size(39, 13);
			this.lblModel.TabIndex = 5;
			this.lblModel.Text = "Model:";

			// txtModel
			this.txtModel.Location = new Point(12, 156);
			this.txtModel.Name = "txtModel";
			this.txtModel.Size = new Size(360, 20);
			this.txtModel.TabIndex = 6;

			// btnOK
			this.btnOK.DialogResult = DialogResult.OK;
			this.btnOK.Location = new Point(216, 190);
			this.btnOK.Name = "btnOK";
			this.btnOK.Size = new Size(75, 23);
			this.btnOK.TabIndex = 7;
			this.btnOK.Text = "OK";
			this.btnOK.UseVisualStyleBackColor = true;

			// btnCancel
			this.btnCancel.DialogResult = DialogResult.Cancel;
			this.btnCancel.Location = new Point(297, 190);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new Size(75, 23);
			this.btnCancel.TabIndex = 8;
			this.btnCancel.Text = "Cancel";
			this.btnCancel.UseVisualStyleBackColor = true;

			// OptionsForm
			this.AcceptButton = this.btnOK;
			this.CancelButton = this.btnCancel;
			this.ClientSize = new Size(384, 225);
			this.Controls.Add(this.btnCancel);
			this.Controls.Add(this.btnOK);
			this.Controls.Add(this.txtModel);
			this.Controls.Add(this.lblModel);
			this.Controls.Add(this.txtLlmServer);
			this.Controls.Add(this.lblLlmServer);
			this.Controls.Add(this.txtApiKey);
			this.Controls.Add(this.lblApiKey);
			this.Controls.Add(this.lblTitle);
			this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "OptionsForm";
			this.StartPosition = FormStartPosition.CenterScreen;
			this.Text = "NyoCoder Options";
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		public string ApiKey
		{
			get { return txtApiKey.Text; }
			set { txtApiKey.Text = value; }
		}

		public string LlmServer
		{
			get { return txtLlmServer.Text; }
			set { txtLlmServer.Text = value; }
		}

		public string Model
		{
			get { return txtModel.Text; }
			set { txtModel.Text = value; }
		}
	}
}

