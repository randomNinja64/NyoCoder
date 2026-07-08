using System;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class OptionsPageHost : OptionsPageHostBase
    {
        private static readonly string[] ReasoningEffortOptions =
        {
            "", "none", "minimal", "low", "medium", "high", "xhigh"
        };

        private Label lblApiKey;
        private TextBox txtApiKey;
        private Label lblLlmServer;
        private TextBox txtLlmServer;
        private Label lblModel;
        private TextBox txtModel;
        private Label lblReasoningEffort;
        private ComboBox cboReasoningEffort;

        public OptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(320);

            this.lblApiKey = new Label();
            this.lblApiKey.AutoSize = true;
            this.lblApiKey.Text = "API Key:";

            this.txtApiKey = new TextBox();
            this.txtApiKey.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.txtApiKey.UseSystemPasswordChar = true;

            this.lblLlmServer = new Label();
            this.lblLlmServer.AutoSize = true;
            this.lblLlmServer.Text = "LLM Server (OpenAI Compatible):";

            this.txtLlmServer = new TextBox();
            this.txtLlmServer.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblModel = new Label();
            this.lblModel.AutoSize = true;
            this.lblModel.Text = "Model:";

            this.txtModel = new TextBox();
            this.txtModel.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblReasoningEffort = new Label();
            this.lblReasoningEffort.AutoSize = true;
            this.lblReasoningEffort.Text = "Reasoning Effort:";

            this.cboReasoningEffort = new ComboBox();
            this.cboReasoningEffort.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.cboReasoningEffort.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string option in ReasoningEffortOptions)
                this.cboReasoningEffort.Items.Add(option);

            AddRow(MakeSectionTitle("Options:"), new Padding(0, 0, 0, 12), false);
            AddRow(this.lblApiKey, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtApiKey, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblLlmServer, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtLlmServer, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblModel, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtModel, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblReasoningEffort, new Padding(0, 0, 0, 4), true);
            AddRow(this.cboReasoningEffort, new Padding(0, 0, 0, 0), false);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
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

        public string ReasoningEffort
        {
            get { return cboReasoningEffort.Text; }
            set { cboReasoningEffort.Text = value ?? string.Empty; }
        }
    }
}
