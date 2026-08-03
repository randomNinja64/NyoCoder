using System.Windows.Forms;

namespace NyoCoder
{
    public class AppearanceOptionsPageHost : OptionsPageHostBase
    {
        private CheckBox chkMarkdownParsing;
        private CheckBox chkShowReasoning;
        private CheckBox chkCollapseThinking;
        private CheckBox chkShowToolOutput;
        private CheckBox chkCollapseToolCalls;

        public AppearanceOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(300);

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

            chkCollapseThinking = new CheckBox
            {
                AutoSize = true,
                Text = "Collapse thinking blocks by default"
            };

            chkShowToolOutput = new CheckBox
            {
                AutoSize = true,
                Text = "Show full tool execution output"
            };

            chkCollapseToolCalls = new CheckBox
            {
                AutoSize = true,
                Text = "Collapse tool calls by default"
            };

            AddRow(MakeSectionTitle("Chat:"), new Padding(0, 0, 0, 8), false);
            AddRow(chkMarkdownParsing, new Padding(0, 0, 0, 12), true);

            AddRow(MakeSectionTitle("Thinking:"), new Padding(0, 0, 0, 8), false);
            AddRow(chkShowReasoning, new Padding(0, 0, 0, 8), true);
            AddRow(chkCollapseThinking, new Padding(0, 0, 0, 12), true);

            AddRow(MakeSectionTitle("Tools:"), new Padding(0, 0, 0, 8), false);
            AddRow(chkShowToolOutput, new Padding(0, 0, 0, 8), true);
            AddRow(chkCollapseToolCalls, new Padding(0, 0, 0, 0), true);

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

        public bool CollapseThinkingBlocks
        {
            get { return chkCollapseThinking.Checked; }
            set { chkCollapseThinking.Checked = value; }
        }

        public bool ShowToolOutput
        {
            get { return chkShowToolOutput.Checked; }
            set { chkShowToolOutput.Checked = value; }
        }

        public bool CollapseToolCalls
        {
            get { return chkCollapseToolCalls.Checked; }
            set { chkCollapseToolCalls.Checked = value; }
        }

        public void LoadFromConfig()
        {
            MarkdownParsing = ConfigHandler.GetMarkdownParsing();
            ShowReasoningOutput = ConfigHandler.GetShowReasoningOutput();
            CollapseThinkingBlocks = ConfigHandler.GetCollapseThinkingBlocks();
            ShowToolOutput = ConfigHandler.GetShowToolOutput();
            CollapseToolCalls = ConfigHandler.GetCollapseToolCalls();
        }

        public void SaveToConfig()
        {
            ConfigHandler.SetMarkdownParsing(MarkdownParsing);
            ConfigHandler.SetShowReasoningOutput(ShowReasoningOutput);
            ConfigHandler.SetCollapseThinkingBlocks(CollapseThinkingBlocks);
            ConfigHandler.SetShowToolOutput(ShowToolOutput);
            ConfigHandler.SetCollapseToolCalls(CollapseToolCalls);
        }
    }
}
