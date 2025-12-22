using System;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
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
        private Label lblMaxReadLines;
        private TextBox txtMaxReadLines;
        private Label lblContextWindowSize;
        private TextBox txtContextWindowSize;

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
            this.lblMaxReadLines = new Label();
            this.txtMaxReadLines = new TextBox();
            this.lblContextWindowSize = new Label();
            this.txtContextWindowSize = new TextBox();
            this.SuspendLayout();

            // lblTitle
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new Font(this.Font, FontStyle.Bold);
            this.lblTitle.Location = new Point(20, 10);
            this.lblTitle.Text = "Options:";

            // lblApiKey
            this.lblApiKey.AutoSize = true;
            this.lblApiKey.Location = new Point(20, 35);
            this.lblApiKey.Text = "API Key:";

            // txtApiKey
            this.txtApiKey.Location = new Point(20, 52);
            this.txtApiKey.Size = new Size(360, 23);
            this.txtApiKey.UseSystemPasswordChar = true;

            // lblLlmServer
            this.lblLlmServer.AutoSize = true;
            this.lblLlmServer.Location = new Point(20, 82);
            this.lblLlmServer.Text = "LLM Server (OpenAI Compatible):";

            // txtLlmServer
            this.txtLlmServer.Location = new Point(20, 99);
            this.txtLlmServer.Size = new Size(360, 23);

            // lblModel
            this.lblModel.AutoSize = true;
            this.lblModel.Location = new Point(20, 129);
            this.lblModel.Text = "Model:";

            // txtModel
            this.txtModel.Location = new Point(20, 146);
            this.txtModel.Size = new Size(360, 23);

            // lblMaxReadLines
            this.lblMaxReadLines.AutoSize = true;
            this.lblMaxReadLines.Location = new Point(20, 176);
            this.lblMaxReadLines.Text = "Max Read Lines:";

            // txtMaxReadLines
            this.txtMaxReadLines.Location = new Point(20, 193);
            this.txtMaxReadLines.Size = new Size(360, 23);

            // lblContextWindowSize
            this.lblContextWindowSize.AutoSize = true;
            this.lblContextWindowSize.Location = new Point(20, 223);
            this.lblContextWindowSize.Text = "Context Window Size (tokens)";

            // txtContextWindowSize
            this.txtContextWindowSize.Location = new Point(20, 240);
            this.txtContextWindowSize.Size = new Size(360, 23);

            // OptionsPageHost
            this.BackColor = SystemColors.Control;
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblApiKey);
            this.Controls.Add(this.txtApiKey);
            this.Controls.Add(this.lblLlmServer);
            this.Controls.Add(this.txtLlmServer);
            this.Controls.Add(this.lblModel);
            this.Controls.Add(this.txtModel);
            this.Controls.Add(this.lblMaxReadLines);
            this.Controls.Add(this.txtMaxReadLines);
            this.Controls.Add(this.lblContextWindowSize);
            this.Controls.Add(this.txtContextWindowSize);
            this.Size = new Size(400, 280);
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

        public int MaxReadLines
        {
            get 
            {
                int result;
                if (int.TryParse(txtMaxReadLines.Text, out result) && result > 0)
                    return result;
                return 500; // Default value
            }
            set { txtMaxReadLines.Text = value.ToString(); }
        }

        public int? ContextWindowSize
        {
            get 
            {
                string text = txtContextWindowSize.Text != null ? txtContextWindowSize.Text.Trim() : null;
                if (string.IsNullOrEmpty(text))
                    return null;
                
                int result;
                return (int.TryParse(text, out result) && result > 0) ? (int?)result : null;
            }
            set 
            { 
                txtContextWindowSize.Text = value.HasValue ? value.Value.ToString() : string.Empty; 
            }
        }

    }
}
