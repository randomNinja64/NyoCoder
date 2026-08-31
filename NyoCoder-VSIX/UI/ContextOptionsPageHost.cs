using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class ContextOptionsPageHost : OptionsPageHostBase
    {
        private CheckBox chkAutoRag;
        private Label lblContextWindowSize;
        private TextBox txtContextWindowSize;
        private Label lblMaxReadLines;
        private TextBox txtMaxReadLines;
        private Label lblMaxOpenFilesInContext;
        private TextBox txtMaxOpenFilesInContext;

        public ContextOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(380);

            this.chkAutoRag = new CheckBox();
            this.chkAutoRag.AutoSize = true;
            this.chkAutoRag.Text = "Automatically Retrieve Context from Codebase (Auto-RAG)";

            this.lblContextWindowSize = new Label();
            this.lblContextWindowSize.AutoSize = true;
            this.lblContextWindowSize.Text = "Context Window Size (tokens):";

            this.txtContextWindowSize = new TextBox();
            this.txtContextWindowSize.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblMaxReadLines = new Label();
            this.lblMaxReadLines.AutoSize = true;
            this.lblMaxReadLines.Text = "Max File Lines to Read:";

            this.txtMaxReadLines = new TextBox();
            this.txtMaxReadLines.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblMaxOpenFilesInContext = new Label();
            this.lblMaxOpenFilesInContext.AutoSize = true;
            this.lblMaxOpenFilesInContext.Text = "Max Open Files in Context:";

            this.txtMaxOpenFilesInContext = new TextBox();
            this.txtMaxOpenFilesInContext.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            AddRow(MakeSectionTitle("Context:"), new Padding(0, 0, 0, 12), false);
            AddRow(this.lblContextWindowSize, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtContextWindowSize, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxReadLines, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxReadLines, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxOpenFilesInContext, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxOpenFilesInContext, new Padding(0, 0, 0, 12), false);
            AddRow(this.chkAutoRag, new Padding(0, 0, 0, 0), true);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
        }

        public bool AutoRagEnabled
        {
            get { return chkAutoRag.Checked; }
            set { chkAutoRag.Checked = value; }
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

        public int MaxOpenFilesInContext
        {
            get
            {
                int result;
                if (int.TryParse(txtMaxOpenFilesInContext.Text, out result) && result > 0)
                    return result;
                return 20;
            }
            set { txtMaxOpenFilesInContext.Text = value.ToString(); }
        }

        public override void LoadFromConfig()
        {
            AutoRagEnabled = ConfigHandler.GetAutoRagEnabled();
            ContextWindowSize = ConfigHandler.ContextWindowSize;
            MaxReadLines = ConfigHandler.MaxReadLines;
            MaxOpenFilesInContext = ConfigHandler.MaxOpenFilesInContext;
        }

        public override void SaveToConfig()
        {
            ConfigHandler.SetAutoRagEnabled(AutoRagEnabled);
            ConfigHandler.SetContextWindowSize(ContextWindowSize);
            ConfigHandler.SetMaxReadLines(MaxReadLines);
            ConfigHandler.SetMaxOpenFilesInContext(MaxOpenFilesInContext);
        }
    }
}
