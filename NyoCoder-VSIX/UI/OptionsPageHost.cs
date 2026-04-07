using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class OptionsPageHost : UserControl
    {
        private OptionsPage optionsPage;
        private TableLayoutPanel layout;
        private readonly List<Label> wrappingLabels = new List<Label>();

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
            this.SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.Font;
            this.AutoScroll = true;
            this.BackColor = SystemColors.Control;

            this.layout = new TableLayoutPanel();
            this.layout.AutoSize = true;
            this.layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.layout.ColumnCount = 1;
            this.layout.Dock = DockStyle.Top;
            this.layout.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            this.layout.Padding = new Padding(20, 10, 20, 10);
            this.layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            this.lblTitle = new Label();
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new Font(this.Font, FontStyle.Bold);
            this.lblTitle.Text = "Options:";

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

            this.lblMaxReadLines = new Label();
            this.lblMaxReadLines.AutoSize = true;
            this.lblMaxReadLines.Text = "Max Read Lines:";

            this.txtMaxReadLines = new TextBox();
            this.txtMaxReadLines.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblContextWindowSize = new Label();
            this.lblContextWindowSize.AutoSize = true;
            this.lblContextWindowSize.Text = "Context Window Size (tokens):";

            this.txtContextWindowSize = new TextBox();
            this.txtContextWindowSize.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            AddRow(this.lblTitle, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblApiKey, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtApiKey, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblLlmServer, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtLlmServer, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblModel, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtModel, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxReadLines, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxReadLines, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblContextWindowSize, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtContextWindowSize, new Padding(0, 0, 0, 0), false);

            this.Controls.Add(this.layout);
            this.MinimumSize = new Size(420, 0);
            this.Size = new Size(420, 320);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingLabelWidths();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateWrappingLabelWidths();
        }

        private void AddRow(Control control, Padding margin, bool wrapLabel)
        {
            control.Margin = margin;
            this.layout.RowStyles.Add(new RowStyle());
            this.layout.Controls.Add(control, 0, this.layout.RowCount);
            this.layout.RowCount++;

            if (wrapLabel)
            {
                Label label = control as Label;
                if (label != null)
                    wrappingLabels.Add(label);
            }
        }

        private void UpdateWrappingLabelWidths()
        {
            int availableWidth = Math.Max(120, this.ClientSize.Width - this.layout.Padding.Left - this.layout.Padding.Right - 8);
            foreach (Label label in wrappingLabels)
                label.MaximumSize = new Size(availableWidth, 0);
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
                return 500;
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
