using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class ApprovalsOptionsPageHost : OptionsPageHostBase
    {
        private enum ModeChoice
        {
            Auto,
            Manual,
            None
        }

        private sealed class ToolMode
        {
            public string Name;
            public RadioButton Auto;
            public RadioButton Manual;
            public RadioButton None;
        }

        private readonly List<ToolMode> _modes = new List<ToolMode>();
        private CheckBox _showJevConfidence;
        private TextBox _jevBaseUrl;
        private TextBox _jevApiKey;
        private TextBox _jevModel;
        private Button _jevModelList;

        public ApprovalsOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(560);
            BuildContent();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private Label MakeFieldLabel(string text)
        {
            Label lbl = new Label();
            lbl.AutoSize = true;
            lbl.Text = text;
            return lbl;
        }

        private CheckBox MakeCheckBox(string text)
        {
            CheckBox cb = new CheckBox();
            cb.AutoSize = true;
            cb.Text = text;
            return cb;
        }

        private TextBox MakeTextBox()
        {
            TextBox tb = new TextBox();
            tb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            return tb;
        }

        private void BuildContent()
        {
            this.layout.SuspendLayout();
            this.layout.Controls.Clear();
            this.layout.RowStyles.Clear();
            this.layout.RowCount = 0;
            _modes.Clear();

            ExternalToolRegistry.EnsureLoaded();
            List<string> allTools = BuildAllToolNames(ExternalToolRegistry.GetPackages());

            AddRow(MakeSectionTitle("Jev"), new Padding(0, 0, 0, 12), false);
            _showJevConfidence = MakeCheckBox("Show confidence level for automatic approvals");
            _showJevConfidence.Checked = true;
            AddRow(_showJevConfidence, new Padding(0, 0, 0, 12), true);

            AddRow(MakeFieldLabel("Jev-Compatible URL:"), new Padding(0, 0, 0, 4), true);
            _jevBaseUrl = MakeTextBox();
            AddRow(_jevBaseUrl, new Padding(0, 0, 0, 12), false);

            AddRow(MakeFieldLabel("Jev-Compatible API Key:"), new Padding(0, 0, 0, 4), true);
            _jevApiKey = MakeTextBox();
            _jevApiKey.UseSystemPasswordChar = true;
            AddRow(_jevApiKey, new Padding(0, 0, 0, 12), false);

            AddRow(MakeFieldLabel("Model:"), new Padding(0, 0, 0, 4), true);
            _jevModel = MakeTextBox();
            _jevModel.Margin = new Padding(0, 0, 6, 0);
            _jevModelList = new Button();
            _jevModelList.Text = "Model List";
            _jevModelList.AutoSize = true;
            _jevModelList.Anchor = AnchorStyles.Left;
            _jevModelList.Margin = new Padding(0);
            _jevModelList.FlatStyle = FlatStyle.System;
            _jevModelList.Click += OnJevModelListClick;

            TableLayoutPanel modelRow = new TableLayoutPanel();
            modelRow.AutoSize = true;
            modelRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            modelRow.ColumnCount = 2;
            modelRow.RowCount = 1;
            modelRow.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            modelRow.Margin = new Padding(0);
            modelRow.Padding = new Padding(0);
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            modelRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            modelRow.Controls.Add(_jevModel, 0, 0);
            modelRow.Controls.Add(_jevModelList, 1, 0);
            AddRow(modelRow, new Padding(0, 0, 0, 12), false);

            AddRow(MakeSectionTitle("Approval"), new Padding(0, 0, 0, 12), false);
            AddRow(MakeModeButtons(), new Padding(0, 0, 0, 12), false);
            AddRow(BuildModeList(allTools), new Padding(0, 0, 0, 0), false);

            this.layout.ResumeLayout(true);
            UpdateWrappingWidths();
        }

        private Control MakeModeButtons()
        {
            FlowLayoutPanel bulkRow = new FlowLayoutPanel();
            bulkRow.AutoSize = true;
            bulkRow.WrapContents = false;
            bulkRow.Margin = new Padding(0);
            bulkRow.Controls.Add(MakeModeButton("All auto", ModeChoice.Auto));
            bulkRow.Controls.Add(MakeModeButton("All manual", ModeChoice.Manual));
            bulkRow.Controls.Add(MakeModeButton("All none", ModeChoice.None));
            return bulkRow;
        }

        private Button MakeModeButton(string text, ModeChoice choice)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.FlatStyle = FlatStyle.System;
            button.Margin = choice == ModeChoice.None
                ? new Padding(0)
                : new Padding(0, 0, 8, 0);
            button.Click += delegate { SetAllModes(choice); };
            return button;
        }

        private Control BuildModeList(List<string> allTools)
        {
            Panel list = new Panel();
            list.AutoScroll = true;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            TableLayoutPanel stack = new TableLayoutPanel();
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.ColumnCount = 1;
            stack.Dock = DockStyle.Top;
            stack.Margin = new Padding(0);
            stack.Padding = new Padding(0, 4, 0, 4);
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            const int rowHeight = 23;
            foreach (string toolName in allTools)
            {
                TableLayoutPanel line = new TableLayoutPanel();
                line.ColumnCount = 4;
                line.RowCount = 1;
                line.Height = rowHeight;
                line.Dock = DockStyle.Fill;
                line.Margin = new Padding(4, 0, 4, 0);
                line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                line.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                Label name = new Label();
                name.Text = toolName;
                name.AutoSize = true;
                name.Anchor = AnchorStyles.Left;
                name.Margin = new Padding(0, 0, 8, 0);

                RadioButton auto = MakeChoice("Auto");
                RadioButton manual = MakeChoice("Manual");
                RadioButton none = MakeChoice("None");
                none.Checked = true;

                line.Controls.Add(name, 0, 0);
                line.Controls.Add(auto, 1, 0);
                line.Controls.Add(manual, 2, 0);
                line.Controls.Add(none, 3, 0);

                stack.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
                stack.Controls.Add(line, 0, stack.RowCount);
                stack.RowCount++;

                _modes.Add(new ToolMode { Name = toolName, Auto = auto, Manual = manual, None = none });
            }

            int visibleRows = Math.Max(1, Math.Min(allTools.Count, 8));
            list.Height = (rowHeight * visibleRows) + stack.Padding.Vertical - 4;
            list.Controls.Add(stack);
            list.Resize += delegate
            {
                stack.Width = Math.Max(120, list.ClientSize.Width - stack.Padding.Horizontal);
            };
            return list;
        }

        private static RadioButton MakeChoice(string text)
        {
            RadioButton choice = new RadioButton();
            choice.Text = text;
            choice.AutoSize = true;
            choice.Margin = new Padding(8, 0, 0, 0);
            return choice;
        }

        private static List<string> BuildAllToolNames(List<ExternalToolRegistry.PackageInfo> packages)
        {
            var allTools = new List<string>();
            foreach (string toolName in ToolDefinitions.BuiltInToolNames)
                allTools.Add(toolName);
            foreach (ExternalToolRegistry.PackageInfo pkg in packages)
                foreach (string toolName in pkg.ToolNames)
                    allTools.Add(toolName);
            allTools.Sort(StringComparer.OrdinalIgnoreCase);
            return allTools;
        }

        private void SetAllModes(ModeChoice choice)
        {
            foreach (ToolMode mode in _modes)
                SelectMode(mode, choice);
        }

        private static void SelectMode(ToolMode mode, ModeChoice choice)
        {
            if (choice == ModeChoice.Auto)
                mode.Auto.Checked = true;
            else if (choice == ModeChoice.Manual)
                mode.Manual.Checked = true;
            else
                mode.None.Checked = true;
        }

        private List<string> ReadTools(ModeChoice choice)
        {
            var result = new List<string>();
            foreach (ToolMode mode in _modes)
            {
                bool selected = choice == ModeChoice.Auto
                    ? mode.Auto.Checked
                    : mode.Manual.Checked;
                if (selected)
                    result.Add(mode.Name);
            }
            return result;
        }

        public override void LoadFromConfig()
        {
            _showJevConfidence.Checked = ConfigHandler.GetShowJevConfidence();
            _jevBaseUrl.Text = ConfigHandler.GetJevBaseUrl() ?? string.Empty;
            _jevApiKey.Text = ConfigHandler.GetJevApiKey() ?? string.Empty;
            _jevModel.Text = ConfigHandler.GetJevModel() ?? string.Empty;
            foreach (ToolMode mode in _modes)
            {
                if (ConfigHandler.IsAutoToolApproval(mode.Name))
                    SelectMode(mode, ModeChoice.Auto);
                else if (ConfigHandler.ToolRequiresApproval(mode.Name))
                    SelectMode(mode, ModeChoice.Manual);
                else
                    SelectMode(mode, ModeChoice.None);
            }
        }

        public override void SaveToConfig()
        {
            ConfigHandler.SetShowJevConfidence(_showJevConfidence.Checked);
            ConfigHandler.SetJevBaseUrl(_jevBaseUrl.Text);
            ConfigHandler.SetJevApiKey(_jevApiKey.Text);
            ConfigHandler.SetJevModel(_jevModel.Text);
            ConfigHandler.SetAutoApprovalTools(ReadTools(ModeChoice.Auto));
            ConfigHandler.SetToolsRequiringApproval(ReadTools(ModeChoice.Manual));
        }

        private void OnJevModelListClick(object sender, EventArgs e)
        {
            string baseUrl = JevModelsBaseUrl(_jevBaseUrl.Text);
            if (string.IsNullOrEmpty(baseUrl))
            {
                MessageBox.Show(
                    this,
                    "Enter a Jev-compatible URL before listing models.",
                    "Model List",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using (ModelChooserDialog dialog = new ModelChooserDialog(baseUrl, _jevApiKey.Text, _jevModel.Text))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedModel))
                    _jevModel.Text = dialog.SelectedModel;
            }
        }

        private static string JevModelsBaseUrl(string decisionUrl)
        {
            string url = decisionUrl != null ? decisionUrl.Trim().TrimEnd('/') : string.Empty;
            const string suffix = "/systemone";
            if (url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                url = url.Substring(0, url.Length - suffix.Length).TrimEnd('/');
            return url;
        }
    }
}
