using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class WebSearchOptionsPageHost : UserControl
    {
        private WebSearchOptionsPage optionsPage;
        private TableLayoutPanel layout;
        private readonly List<Label> wrappingLabels = new List<Label>();

        private Label lblTitle;
        private Label lblSearXNG;
        private TextBox txtSearXNG;
        private Label lblSearXNGHint;
        private Label lblUserAgent;
        private TextBox txtUserAgent;
        private Label lblMaxSearchResults;
        private TextBox txtMaxSearchResults;
        private Label lblMaxWebContentLength;
        private TextBox txtMaxWebContentLength;

        public WebSearchOptionsPageHost(WebSearchOptionsPage page)
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
            this.lblTitle.Text = "Web Search Options:";

            this.lblSearXNG = new Label();
            this.lblSearXNG.AutoSize = true;
            this.lblSearXNG.Text = "SearXNG Instance (optional, must support JSON API):";

            this.txtSearXNG = new TextBox();
            this.txtSearXNG.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblSearXNGHint = new Label();
            this.lblSearXNGHint.AutoSize = true;
            this.lblSearXNGHint.ForeColor = SystemColors.GrayText;
            this.lblSearXNGHint.Text = "If not set, DuckDuckGo and Wiby are used as fallbacks.";

            this.lblUserAgent = new Label();
            this.lblUserAgent.AutoSize = true;
            this.lblUserAgent.Text = "User Agent:";

            this.txtUserAgent = new TextBox();
            this.txtUserAgent.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblMaxSearchResults = new Label();
            this.lblMaxSearchResults.AutoSize = true;
            this.lblMaxSearchResults.Text = "Maximum Search Results:";

            this.txtMaxSearchResults = new TextBox();
            this.txtMaxSearchResults.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblMaxWebContentLength = new Label();
            this.lblMaxWebContentLength.AutoSize = true;
            this.lblMaxWebContentLength.Text = "Maximum Web Content Length (characters):";

            this.txtMaxWebContentLength = new TextBox();
            this.txtMaxWebContentLength.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            AddRow(this.lblTitle, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblSearXNG, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtSearXNG, new Padding(0, 0, 0, 4), false);
            AddRow(this.lblSearXNGHint, new Padding(0, 0, 0, 12), true);
            AddRow(this.lblUserAgent, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtUserAgent, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxSearchResults, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxSearchResults, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxWebContentLength, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxWebContentLength, new Padding(0, 0, 0, 0), false);

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

        public string SearXNGInstance
        {
            get { return txtSearXNG.Text != null ? txtSearXNG.Text.Trim() : string.Empty; }
            set { txtSearXNG.Text = value ?? string.Empty; }
        }

        public string UserAgent
        {
            get { return txtUserAgent.Text != null ? txtUserAgent.Text.Trim() : WebSearchTool.DefaultUserAgent; }
            set { txtUserAgent.Text = value ?? WebSearchTool.DefaultUserAgent; }
        }

        public int MaxSearchResults
        {
            get
            {
                int result;
                if (int.TryParse(txtMaxSearchResults.Text, out result) && result > 0)
                    return result;
                return 20;
            }
            set { txtMaxSearchResults.Text = value.ToString(); }
        }

        public int MaxWebContentLength
        {
            get
            {
                int result;
                if (int.TryParse(txtMaxWebContentLength.Text, out result) && result > 0)
                    return result;
                return 8000;
            }
            set { txtMaxWebContentLength.Text = value.ToString(); }
        }
    }
}
