using System.Windows.Forms;

namespace NyoCoder
{
    public class AppearanceOptionsPageHost : OptionsPageHostBase
    {
        private static readonly string[] DisplayModeOptions = { "Shown", "Collapsed", "Hidden" };

        private CheckBox chkMarkdownParsing;
        private Label lblThinkingDisplay;
        private ComboBox cboThinkingDisplay;
        private Label lblToolCallDisplay;
        private ComboBox cboToolCallDisplay;
        private Label lblToolOutputDisplay;
        private ComboBox cboToolOutputDisplay;

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

            lblThinkingDisplay = new Label
            {
                AutoSize = true,
                Text = "Thinking display:"
            };
            cboThinkingDisplay = MakeDisplayModeCombo();

            lblToolCallDisplay = new Label
            {
                AutoSize = true,
                Text = "Tool call display:"
            };
            cboToolCallDisplay = MakeDisplayModeCombo();

            lblToolOutputDisplay = new Label
            {
                AutoSize = true,
                Text = "Tool output display:"
            };
            cboToolOutputDisplay = MakeDisplayModeCombo();

            AddRow(MakeSectionTitle("Chat:"), new Padding(0, 0, 0, 8), false);
            AddRow(chkMarkdownParsing, new Padding(0, 0, 0, 12), true);

            AddRow(MakeSectionTitle("Thinking:"), new Padding(0, 0, 0, 8), false);
            AddRow(lblThinkingDisplay, new Padding(0, 0, 0, 4), true);
            AddRow(cboThinkingDisplay, new Padding(0, 0, 0, 12), false);

            AddRow(MakeSectionTitle("Tools:"), new Padding(0, 0, 0, 8), false);
            AddRow(lblToolCallDisplay, new Padding(0, 0, 0, 4), true);
            AddRow(cboToolCallDisplay, new Padding(0, 0, 0, 8), false);
            AddRow(lblToolOutputDisplay, new Padding(0, 0, 0, 4), true);
            AddRow(cboToolOutputDisplay, new Padding(0, 0, 0, 0), false);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
        }

        private static ComboBox MakeDisplayModeCombo()
        {
            var combo = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (string option in DisplayModeOptions)
                combo.Items.Add(option);
            return combo;
        }

        public bool MarkdownParsing
        {
            get { return chkMarkdownParsing.Checked; }
            set { chkMarkdownParsing.Checked = value; }
        }

        public ChatBlockDisplayMode ThinkingDisplayMode
        {
            get { return DisplayModeFromCombo(cboThinkingDisplay); }
            set { SelectDisplayMode(cboThinkingDisplay, value); }
        }

        public ChatBlockDisplayMode ToolCallDisplayMode
        {
            get { return DisplayModeFromCombo(cboToolCallDisplay); }
            set { SelectDisplayMode(cboToolCallDisplay, value); }
        }

        public ChatBlockDisplayMode ToolOutputDisplayMode
        {
            get { return DisplayModeFromCombo(cboToolOutputDisplay); }
            set { SelectDisplayMode(cboToolOutputDisplay, value); }
        }

        public void LoadFromConfig()
        {
            MarkdownParsing = ConfigHandler.GetMarkdownParsing();
            ThinkingDisplayMode = ConfigHandler.GetThinkingDisplayMode();
            ToolCallDisplayMode = ConfigHandler.GetToolCallDisplayMode();
            ToolOutputDisplayMode = ConfigHandler.GetToolOutputDisplayMode();
        }

        public void SaveToConfig()
        {
            ConfigHandler.SetMarkdownParsing(MarkdownParsing);
            ConfigHandler.SetThinkingDisplayMode(ThinkingDisplayMode);
            ConfigHandler.SetToolCallDisplayMode(ToolCallDisplayMode);
            ConfigHandler.SetToolOutputDisplayMode(ToolOutputDisplayMode);
        }

        private static ChatBlockDisplayMode DisplayModeFromCombo(ComboBox combo)
        {
            switch ((combo.SelectedItem as string ?? "").ToLowerInvariant())
            {
                case "shown": return ChatBlockDisplayMode.Shown;
                case "hidden": return ChatBlockDisplayMode.Hidden;
                default: return ChatBlockDisplayMode.Collapsed;
            }
        }

        private static void SelectDisplayMode(ComboBox combo, ChatBlockDisplayMode mode)
        {
            string text;
            switch (mode)
            {
                case ChatBlockDisplayMode.Shown: text = "Shown"; break;
                case ChatBlockDisplayMode.Hidden: text = "Hidden"; break;
                default: text = "Collapsed"; break;
            }
            combo.SelectedItem = text;
            if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }
    }
}
