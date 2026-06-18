using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class WebSearchOptionsPageHost : OptionsPageHostBase
    {
        private Label lblSearXNG;
        private TextBox txtSearXNG;
        private Label lblSearXNGHint;
        private Label lblUserAgent;
        private TextBox txtUserAgent;
        private Label lblMaxSearchResults;
        private TextBox txtMaxSearchResults;
        private Label lblMaxWebContentLength;
        private TextBox txtMaxWebContentLength;

        public WebSearchOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(320);

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

            AddRow(MakeSectionTitle("Web Search Options:"), new Padding(0, 0, 0, 12), false);
            AddRow(this.lblSearXNG, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtSearXNG, new Padding(0, 0, 0, 4), false);
            AddRow(this.lblSearXNGHint, new Padding(0, 0, 0, 12), true);
            AddRow(this.lblUserAgent, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtUserAgent, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxSearchResults, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxSearchResults, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxWebContentLength, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxWebContentLength, new Padding(0, 0, 0, 0), false);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
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
