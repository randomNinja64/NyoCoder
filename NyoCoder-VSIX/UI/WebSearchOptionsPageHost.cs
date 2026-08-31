using System.Windows.Forms;

namespace NyoCoder
{
    public class WebSearchOptionsPageHost : OptionsPageHostBase
    {
        private Label lblMaxLinks;
        private TextBox txtMaxLinks;
        private Label lblMaxSearchResults;
        private TextBox txtMaxSearchResults;
        private Label lblMaxWebContentLength;
        private TextBox txtMaxWebContentLength;
        private Label lblSearXNG;
        private TextBox txtSearXNG;
        private Label lblFirecrawlEndpoint;
        private TextBox txtFirecrawlEndpoint;
        private Label lblFirecrawlApiKey;
        private TextBox txtFirecrawlApiKey;
        private Label lblUserAgent;
        private TextBox txtUserAgent;

        public WebSearchOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(380);

            this.lblMaxLinks = new Label();
            this.lblMaxLinks.AutoSize = true;
            this.lblMaxLinks.Text = "Maximum Links from Webpage:";

            this.txtMaxLinks = new TextBox();
            this.txtMaxLinks.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblMaxSearchResults = new Label();
            this.lblMaxSearchResults.AutoSize = true;
            this.lblMaxSearchResults.Text = "Maximum Search Results:";

            this.txtMaxSearchResults = new TextBox();
            this.txtMaxSearchResults.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblMaxWebContentLength = new Label();
            this.lblMaxWebContentLength.AutoSize = true;
            this.lblMaxWebContentLength.Text = "Maximum Web Content Length (0 = no limit):";

            this.txtMaxWebContentLength = new TextBox();
            this.txtMaxWebContentLength.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblSearXNG = new Label();
            this.lblSearXNG.AutoSize = true;
            this.lblSearXNG.Text = "SearXNG Instance (optional, must support JSON API):";

            this.txtSearXNG = new TextBox();
            this.txtSearXNG.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblFirecrawlEndpoint = new Label();
            this.lblFirecrawlEndpoint.AutoSize = true;
            this.lblFirecrawlEndpoint.Text = "Firecrawl API base URL (optional):";

            this.txtFirecrawlEndpoint = new TextBox();
            this.txtFirecrawlEndpoint.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblFirecrawlApiKey = new Label();
            this.lblFirecrawlApiKey.AutoSize = true;
            this.lblFirecrawlApiKey.Text = "Firecrawl API key (optional):";

            this.txtFirecrawlApiKey = new TextBox();
            this.txtFirecrawlApiKey.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblUserAgent = new Label();
            this.lblUserAgent.AutoSize = true;
            this.lblUserAgent.Text = "User Agent:";

            this.txtUserAgent = new TextBox();
            this.txtUserAgent.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            AddRow(MakeSectionTitle("Web Search:"), new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxLinks, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxLinks, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxSearchResults, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxSearchResults, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxWebContentLength, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxWebContentLength, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblSearXNG, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtSearXNG, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblFirecrawlEndpoint, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtFirecrawlEndpoint, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblFirecrawlApiKey, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtFirecrawlApiKey, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblUserAgent, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtUserAgent, new Padding(0, 0, 0, 0), false);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
        }

        public int MaxLinks
        {
            get
            {
                int result;
                if (int.TryParse(txtMaxLinks.Text, out result) && result >= 0)
                    return result;
                return 40;
            }
            set { txtMaxLinks.Text = value.ToString(); }
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
                if (int.TryParse(txtMaxWebContentLength.Text, out result) && result >= 0)
                    return result;
                return 10000;
            }
            set { txtMaxWebContentLength.Text = value.ToString(); }
        }

        public string SearXNGInstance
        {
            get { return txtSearXNG.Text != null ? txtSearXNG.Text.Trim() : string.Empty; }
            set { txtSearXNG.Text = value ?? string.Empty; }
        }

        public string FirecrawlEndpoint
        {
            get { return txtFirecrawlEndpoint.Text != null ? txtFirecrawlEndpoint.Text.Trim() : string.Empty; }
            set { txtFirecrawlEndpoint.Text = value ?? string.Empty; }
        }

        public string FirecrawlApiKey
        {
            get { return txtFirecrawlApiKey.Text != null ? txtFirecrawlApiKey.Text.Trim() : string.Empty; }
            set { txtFirecrawlApiKey.Text = value ?? string.Empty; }
        }

        public string UserAgent
        {
            get { return txtUserAgent.Text != null ? txtUserAgent.Text.Trim() : WebSearchTool.DefaultUserAgent; }
            set { txtUserAgent.Text = value ?? WebSearchTool.DefaultUserAgent; }
        }

        public override void LoadFromConfig()
        {
            MaxLinks = ConfigHandler.GetConfigInt("maxLinks", 40);
            MaxSearchResults = ConfigHandler.GetConfigInt("maxSearchResults", 20);
            MaxWebContentLength = ConfigHandler.GetConfigInt("maxWebContentLength", 10000);
            SearXNGInstance = ConfigHandler.GetConfigValue("searxngInstance", "");
            FirecrawlEndpoint = ConfigHandler.GetConfigValue("firecrawlEndpoint", "");
            FirecrawlApiKey = ConfigHandler.GetConfigValue("firecrawlApiKey", "");
            UserAgent = ConfigHandler.GetConfigValue("webUserAgent", WebSearchTool.DefaultUserAgent);
        }

        public override void SaveToConfig()
        {
            ConfigHandler.SetConfigValue("maxLinks", MaxLinks >= 0 ? MaxLinks.ToString() : null);
            ConfigHandler.SetConfigValue("maxSearchResults", MaxSearchResults > 0 ? MaxSearchResults.ToString() : null);
            ConfigHandler.SetConfigValue("maxWebContentLength", MaxWebContentLength >= 0 ? MaxWebContentLength.ToString() : null);
            ConfigHandler.SetConfigValue("searxngInstance", SearXNGInstance);
            ConfigHandler.SetConfigValue("firecrawlEndpoint", FirecrawlEndpoint);
            ConfigHandler.SetConfigValue("firecrawlApiKey", FirecrawlApiKey);
            ConfigHandler.SetConfigValue("webUserAgent", UserAgent);
        }
    }
}
