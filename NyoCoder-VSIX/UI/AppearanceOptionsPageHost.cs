using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class AppearanceOptionsPageHost : OptionsPageHostBase
    {
        private CheckBox chkMarkdownParsing;
        private CheckBox chkShowReasoning;
        private CheckBox chkShowToolOutput;

        public AppearanceOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(220);

            chkMarkdownParsing = new CheckBox
            {
                AutoSize = true,
                Text = "Render Markdown in chat output"
            };

            chkShowReasoning = new CheckBox
            {
                AutoSize = true,
                Text = "Show model reasoning output"
            };

            chkShowToolOutput = new CheckBox
            {
                AutoSize = true,
                Text = "Show full tool execution output"
            };

            AddRow(MakeSectionTitle("Appearance:"), new Padding(0, 0, 0, 12), false);
            AddRow(chkMarkdownParsing, new Padding(0, 0, 0, 8), true);
            AddRow(chkShowToolOutput, new Padding(0, 0, 0, 8), true);
            AddRow(chkShowReasoning, new Padding(0, 0, 0, 0), true);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
        }

        public bool MarkdownParsing
        {
            get { return chkMarkdownParsing.Checked; }
            set { chkMarkdownParsing.Checked = value; }
        }

        public bool ShowReasoningOutput
        {
            get { return chkShowReasoning.Checked; }
            set { chkShowReasoning.Checked = value; }
        }

        public bool ShowToolOutput
        {
            get { return chkShowToolOutput.Checked; }
            set { chkShowToolOutput.Checked = value; }
        }

        public void LoadFromConfig()
        {
            MarkdownParsing = ConfigHandler.GetMarkdownParsing();
            ShowReasoningOutput = ConfigHandler.GetShowReasoningOutput();
            ShowToolOutput = ConfigHandler.GetShowToolOutput();
        }

        public void SaveToConfig()
        {
            ConfigHandler.SetMarkdownParsing(MarkdownParsing);
            ConfigHandler.SetShowReasoningOutput(ShowReasoningOutput);
            ConfigHandler.SetShowToolOutput(ShowToolOutput);
        }
    }
}
