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
        private Label lblMaxFileContentLength;
        private TextBox txtMaxFileContentLength;
        private Label lblMaxOpenFilesInContext;
        private TextBox txtMaxOpenFilesInContext;
        private Label lblChatHistoryLimit;
        private TextBox txtChatHistoryLimit;
        private CheckBox chkChatTitlesByModel;

        public ContextOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(500);

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

            this.lblMaxFileContentLength = new Label();
            this.lblMaxFileContentLength.AutoSize = true;
            this.lblMaxFileContentLength.Text = "Maximum Processing Length for File Content:";

            this.txtMaxFileContentLength = new TextBox();
            this.txtMaxFileContentLength.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblMaxOpenFilesInContext = new Label();
            this.lblMaxOpenFilesInContext.AutoSize = true;
            this.lblMaxOpenFilesInContext.Text = "Max Open Files in Context:";

            this.txtMaxOpenFilesInContext = new TextBox();
            this.txtMaxOpenFilesInContext.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblChatHistoryLimit = new Label();
            this.lblChatHistoryLimit.AutoSize = true;
            this.lblChatHistoryLimit.Text = "Number of Recent Chat Sessions to Keep (per solution, 0 to disable):";

            this.txtChatHistoryLimit = new TextBox();
            this.txtChatHistoryLimit.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.chkChatTitlesByModel = new CheckBox();
            this.chkChatTitlesByModel.AutoSize = true;
            this.chkChatTitlesByModel.Text = "Generate chat titles from model";

            AddRow(MakeSectionTitle("Context"), new Padding(0, 0, 0, 12), false);
            AddRow(this.lblContextWindowSize, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtContextWindowSize, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxReadLines, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxReadLines, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxFileContentLength, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxFileContentLength, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxOpenFilesInContext, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxOpenFilesInContext, new Padding(0, 0, 0, 12), false);
            AddRow(this.chkAutoRag, new Padding(0, 0, 0, 12), true);
            AddRow(this.lblChatHistoryLimit, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtChatHistoryLimit, new Padding(0, 0, 0, 12), false);
            AddRow(this.chkChatTitlesByModel, new Padding(0, 0, 0, 0), true);

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

        public int MaxFileContentLength
        {
            get
            {
                int result;
                if (int.TryParse(txtMaxFileContentLength.Text, out result) && result > 0)
                    return result;
                return 10000;
            }
            set { txtMaxFileContentLength.Text = value.ToString(); }
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

        public int ChatHistoryLimit
        {
            get
            {
                int result;
                if (int.TryParse(txtChatHistoryLimit.Text, out result) && result >= 0)
                    return result;
                return 50;
            }
            set { txtChatHistoryLimit.Text = value.ToString(); }
        }

        public bool ChatTitlesByModel
        {
            get { return chkChatTitlesByModel.Checked; }
            set { chkChatTitlesByModel.Checked = value; }
        }

        public override void LoadFromConfig()
        {
            AutoRagEnabled = ConfigHandler.GetAutoRagEnabled();
            ContextWindowSize = ConfigHandler.ContextWindowSize;
            MaxReadLines = ConfigHandler.MaxReadLines;
            MaxFileContentLength = ConfigHandler.MaxFileContentLength;
            MaxOpenFilesInContext = ConfigHandler.MaxOpenFilesInContext;
            ChatHistoryLimit = ConfigHandler.GetChatHistoryLimit();
            ChatTitlesByModel = ConfigHandler.GetChatTitlesByModel();
        }

        public override void SaveToConfig()
        {
            ConfigHandler.SetAutoRagEnabled(AutoRagEnabled);
            ConfigHandler.SetContextWindowSize(ContextWindowSize);
            ConfigHandler.SetMaxReadLines(MaxReadLines);
            ConfigHandler.SetMaxFileContentLength(MaxFileContentLength);
            ConfigHandler.SetMaxOpenFilesInContext(MaxOpenFilesInContext);
            ConfigHandler.SetChatHistoryLimit(ChatHistoryLimit);
            ConfigHandler.SetChatTitlesByModel(ChatTitlesByModel);
        }
    }
}
