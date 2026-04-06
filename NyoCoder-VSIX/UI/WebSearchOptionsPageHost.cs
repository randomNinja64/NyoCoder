using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class WebSearchOptionsPageHost : UserControl
    {
        private WebSearchOptionsPage optionsPage;

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

            this.lblTitle = new Label();
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new Font(this.Font, FontStyle.Bold);
            this.lblTitle.Location = new Point(20, 10);
            this.lblTitle.Text = "Web Search Options:";

            this.lblSearXNG = new Label();
            this.lblSearXNG.AutoSize = true;
            this.lblSearXNG.Location = new Point(20, 35);
            this.lblSearXNG.Text = "SearXNG Instance (optional, must support JSON API):";

            this.txtSearXNG = new TextBox();
            this.txtSearXNG.Location = new Point(20, 52);
            this.txtSearXNG.Size = new Size(360, 23);

            this.lblSearXNGHint = new Label();
            this.lblSearXNGHint.AutoSize = true;
            this.lblSearXNGHint.ForeColor = SystemColors.GrayText;
            this.lblSearXNGHint.Location = new Point(20, 78);
            this.lblSearXNGHint.Text = "If not set, DuckDuckGo and Wiby are used as fallbacks.";

            this.lblUserAgent = new Label();
            this.lblUserAgent.AutoSize = true;
            this.lblUserAgent.Location = new Point(20, 105);
            this.lblUserAgent.Text = "User Agent:";

            this.txtUserAgent = new TextBox();
            this.txtUserAgent.Location = new Point(20, 122);
            this.txtUserAgent.Size = new Size(360, 23);

            this.lblMaxSearchResults = new Label();
            this.lblMaxSearchResults.AutoSize = true;
            this.lblMaxSearchResults.Location = new Point(20, 152);
            this.lblMaxSearchResults.Text = "Maximum Search Results:";

            this.txtMaxSearchResults = new TextBox();
            this.txtMaxSearchResults.Location = new Point(20, 169);
            this.txtMaxSearchResults.Size = new Size(360, 23);

            this.lblMaxWebContentLength = new Label();
            this.lblMaxWebContentLength.AutoSize = true;
            this.lblMaxWebContentLength.Location = new Point(20, 199);
            this.lblMaxWebContentLength.Text = "Maximum Web Content Length (characters):";

            this.txtMaxWebContentLength = new TextBox();
            this.txtMaxWebContentLength.Location = new Point(20, 216);
            this.txtMaxWebContentLength.Size = new Size(360, 23);

            this.BackColor = SystemColors.Control;
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblSearXNG);
            this.Controls.Add(this.txtSearXNG);
            this.Controls.Add(this.lblSearXNGHint);
            this.Controls.Add(this.lblUserAgent);
            this.Controls.Add(this.txtUserAgent);
            this.Controls.Add(this.lblMaxSearchResults);
            this.Controls.Add(this.txtMaxSearchResults);
            this.Controls.Add(this.lblMaxWebContentLength);
            this.Controls.Add(this.txtMaxWebContentLength);
            this.Size = new Size(400, 260);
            this.ResumeLayout(false);
            this.PerformLayout();
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
