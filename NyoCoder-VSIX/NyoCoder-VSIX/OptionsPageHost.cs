using System;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder.NyoCoder_VSIX
{
    public class OptionsPageHost : UserControl
    {
        private OptionsPage optionsPage;
        
        private Label lblTitle;
        private Label lblApiKey;
        private TextBox txtApiKey;
        private Label lblLlmServer;
        private TextBox txtLlmServer;
        private Label lblModel;
        private TextBox txtModel;

        public OptionsPageHost(OptionsPage page)
        {
            this.optionsPage = page;
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
            this.SuspendLayout();

            // lblTitle
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new Font(this.Font, FontStyle.Bold);
            this.lblTitle.Location = new Point(20, 20);
            this.lblTitle.Text = "Options:";

            // lblApiKey
            this.lblApiKey.AutoSize = true;
            this.lblApiKey.Location = new Point(20, 50);
            this.lblApiKey.Text = "API Key:";

            // txtApiKey
            this.txtApiKey.Location = new Point(20, 70);
            this.txtApiKey.Size = new Size(360, 23);
            this.txtApiKey.UseSystemPasswordChar = true;
            this.txtApiKey.TextChanged += new EventHandler(this.txtApiKey_TextChanged);

            // lblLlmServer
            this.lblLlmServer.AutoSize = true;
            this.lblLlmServer.Location = new Point(20, 110);
            this.lblLlmServer.Text = "LLM Server (OpenAI Compatible):";

            // txtLlmServer
            this.txtLlmServer.Location = new Point(20, 130);
            this.txtLlmServer.Size = new Size(360, 23);
            this.txtLlmServer.TextChanged += new EventHandler(this.txtLlmServer_TextChanged);

            // lblModel
            this.lblModel.AutoSize = true;
            this.lblModel.Location = new Point(20, 170);
            this.lblModel.Text = "Model:";

            // txtModel
            this.txtModel.Location = new Point(20, 190);
            this.txtModel.Size = new Size(360, 23);
            this.txtModel.TextChanged += new EventHandler(this.txtModel_TextChanged);

            // OptionsPageHost
            this.BackColor = SystemColors.Control;
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblApiKey);
            this.Controls.Add(this.txtApiKey);
            this.Controls.Add(this.lblLlmServer);
            this.Controls.Add(this.txtLlmServer);
            this.Controls.Add(this.lblModel);
            this.Controls.Add(this.txtModel);
            this.Size = new Size(400, 250);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        public string ApiKey
        {
            get { return txtApiKey.Text; }
            set { txtApiKey.Text = value ?? string.Empty; }
        }

        public string LlmServer
        {
            get { return txtLlmServer.Text; }
            set { txtLlmServer.Text = value ?? string.Empty; }
        }

        public string Model
        {
            get { return txtModel.Text; }
            set { txtModel.Text = value ?? string.Empty; }
        }

        private void txtApiKey_TextChanged(object sender, EventArgs e)
        {
            if (optionsPage != null)
            {
                optionsPage.ApiKey = this.ApiKey;
            }
        }

        private void txtLlmServer_TextChanged(object sender, EventArgs e)
        {
            if (optionsPage != null)
            {
                optionsPage.LlmServer = this.LlmServer;
            }
        }

        private void txtModel_TextChanged(object sender, EventArgs e)
        {
            if (optionsPage != null)
            {
                optionsPage.Model = this.Model;
            }
        }
    }
}
