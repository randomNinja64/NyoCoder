using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class ToolsOptionsPageHost : OptionsPageHostBase
    {
        /// <summary>Per-tool setting: whether the tool is enabled and, if so, how calls are approved.</summary>
        private enum ToolModeChoice
        {
            AlwaysRun,
            Auto,
            Manual,
            Off
        }

        private sealed class ToolModeRow
        {
            public string Name;
            public RadioButton AlwaysRun;
            public RadioButton Auto;
            public RadioButton Manual;
            public RadioButton Off;

            // Approval mode from config, kept for tools set to Off so re-enabling
            // a tool later does not silently drop its approval requirement.
            public ToolModeChoice StoredApproval;
        }

        /// <summary>
        /// Draws the name with GDI text rendering in every case. Label.AutoEllipsis switches to a different
        /// renderer only when the text is truncated, which shifts truncated names vertically.
        /// </summary>
        private sealed class ToolNameLabel : Label
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private NumericUpDown _maxConsecutiveFailures;
        private CheckBox _selfImprovement;
        private readonly List<ToolModeRow> _modes = new List<ToolModeRow>();
        private readonly ToolTip _toolNameTips = new ToolTip();
        private TextBox _jevBaseUrl;
        private TextBox _jevApiKey;
        private TextBox _jevModel;
        private Dictionary<string, Control> _optionControls = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        public ToolsOptionsPageHost()
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
            _optionControls.Clear();
            _modes.Clear();

            ExternalToolRegistry.EnsureLoaded();
            List<ExternalToolRegistry.PackageInfo> packages = ExternalToolRegistry.GetPackages();

            List<string> allTools = BuildAllToolNames(packages);

            AddRow(MakeSectionTitle("Tools"), new Padding(0, 0, 0, 12), false);
            AddRow(MakeFieldLabel("Max consecutive tool failures (0 = off):"), new Padding(0, 0, 0, 4), true);
            _maxConsecutiveFailures = new NumericUpDown();
            _maxConsecutiveFailures.Minimum = 0;
            _maxConsecutiveFailures.Maximum = 999;
            _maxConsecutiveFailures.Value = 10;
            _maxConsecutiveFailures.Width = 100;
            AddRow(_maxConsecutiveFailures, new Padding(0, 0, 0, 12), false);
            _selfImprovement = MakeCheckBox("Self-improvement (creates and updates skills)");
            _selfImprovement.Checked = true;
            AddRow(_selfImprovement, new Padding(0, 0, 0, 12), true);

            BuildJevSection();

            AddRow(MakeFieldLabel("Tools:"), new Padding(0, 0, 0, 4), true);
            AddRow(MakeModeButtons(), new Padding(0, 0, 0, 8), false);
            AddRow(BuildModeList(allTools), new Padding(0, 0, 0, 12), false);

            // Per-package config sections
            foreach (ExternalToolRegistry.PackageInfo pkg in packages)
            {
                if (pkg.Options.Count == 0)
                    continue;

                AddRow(MakeSectionTitle(pkg.DisplayName + ":"), new Padding(0, 0, 0, 12), false);

                foreach (ExternalToolRegistry.OptionDefinition opt in pkg.Options)
                {
                    if (opt.Type == "bool")
                    {
                        CheckBox cb = MakeCheckBox(opt.Label);
                        AddRow(cb, new Padding(0, 0, 0, 12), true);
                        _optionControls[opt.Name] = cb;
                    }
                    else
                    {
                        AddRow(MakeFieldLabel(opt.Label + ":"), new Padding(0, 0, 0, 4), true);
                        TextBox tb = MakeTextBox();
                        AddRow(tb, new Padding(0, 0, 0, 12), false);
                        _optionControls[opt.Name] = tb;
                    }
                }
            }

            if (packages.Count == 0)
            {
                Label lblHint = new Label();
                lblHint.AutoSize = true;
                lblHint.ForeColor = SystemColors.GrayText;
                lblHint.Text = "Drop external tool packages into: " + ExternalToolRegistry.ToolsDirectory;
                AddRow(lblHint, new Padding(0, 0, 0, 0), true);
            }

            this.layout.ResumeLayout(true);
            UpdateWrappingWidths();
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

        /// <summary>Jev endpoint used for tools set to Auto.</summary>
        private void BuildJevSection()
        {
            AddRow(MakeFieldLabel("Jev-Compatible URL (for Auto):"), new Padding(0, 0, 0, 4), true);
            _jevBaseUrl = MakeTextBox();
            AddRow(_jevBaseUrl, new Padding(0, 0, 0, 12), false);

            AddRow(MakeFieldLabel("Jev-Compatible API Key:"), new Padding(0, 0, 0, 4), true);
            _jevApiKey = MakeTextBox();
            _jevApiKey.UseSystemPasswordChar = true;
            AddRow(_jevApiKey, new Padding(0, 0, 0, 12), false);

            AddRow(MakeFieldLabel("Jev Model:"), new Padding(0, 0, 0, 4), true);
            _jevModel = MakeTextBox();
            _jevModel.Margin = new Padding(0, 0, 6, 0);
            Button modelList = new Button();
            modelList.Text = "Model List";
            modelList.AutoSize = true;
            modelList.Anchor = AnchorStyles.Left;
            modelList.Margin = new Padding(0);
            modelList.FlatStyle = FlatStyle.System;
            modelList.Click += OnJevModelListClick;

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
            modelRow.Controls.Add(modelList, 1, 0);
            AddRow(modelRow, new Padding(0, 0, 0, 12), false);
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

        private Control MakeModeButtons()
        {
            FlowLayoutPanel bulkRow = new FlowLayoutPanel();
            bulkRow.AutoSize = true;
            bulkRow.WrapContents = false;
            bulkRow.Margin = new Padding(0);
            bulkRow.Controls.Add(MakeModeButton("All always run", ToolModeChoice.AlwaysRun));
            bulkRow.Controls.Add(MakeModeButton("All auto", ToolModeChoice.Auto));
            bulkRow.Controls.Add(MakeModeButton("All ask me", ToolModeChoice.Manual));
            bulkRow.Controls.Add(MakeModeButton("All off", ToolModeChoice.Off));
            return bulkRow;
        }

        private Button MakeModeButton(string text, ToolModeChoice choice)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.FlatStyle = FlatStyle.System;
            button.Margin = choice == ToolModeChoice.Off
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
                line.ColumnCount = 5;
                line.RowCount = 1;
                line.Height = rowHeight;
                line.Dock = DockStyle.Fill;
                line.Margin = new Padding(4, 0, 4, 0);
                line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                line.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                Label name = new ToolNameLabel();
                name.Text = toolName;
                name.AutoSize = false;
                name.Dock = DockStyle.Fill;
                name.Margin = new Padding(0, 0, 2, 0);
                _toolNameTips.SetToolTip(name, toolName);

                ToolModeRow row = new ToolModeRow();
                row.Name = toolName;
                row.AlwaysRun = MakeChoice("Always Run");
                row.Auto = MakeChoice("Auto");
                row.Manual = MakeChoice("Ask Me");
                row.Off = MakeChoice("Off");
                row.AlwaysRun.Checked = true;
                row.StoredApproval = ToolModeChoice.AlwaysRun;

                line.Controls.Add(name, 0, 0);
                line.Controls.Add(row.AlwaysRun, 1, 0);
                line.Controls.Add(row.Auto, 2, 0);
                line.Controls.Add(row.Manual, 3, 0);
                line.Controls.Add(row.Off, 4, 0);

                stack.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
                stack.Controls.Add(line, 0, stack.RowCount);
                stack.RowCount++;

                _modes.Add(row);
            }

            int visibleRows = Math.Max(1, Math.Min(allTools.Count, 10));
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
            choice.Anchor = AnchorStyles.Left;
            choice.Margin = new Padding(0);
            choice.Padding = new Padding(0);
            return choice;
        }

        private void SetAllModes(ToolModeChoice choice)
        {
            foreach (ToolModeRow row in _modes)
                SelectMode(row, choice);
        }

        private static void SelectMode(ToolModeRow row, ToolModeChoice choice)
        {
            if (choice == ToolModeChoice.AlwaysRun)
                row.AlwaysRun.Checked = true;
            else if (choice == ToolModeChoice.Auto)
                row.Auto.Checked = true;
            else if (choice == ToolModeChoice.Manual)
                row.Manual.Checked = true;
            else
                row.Off.Checked = true;
        }

        private static ToolModeChoice GetMode(ToolModeRow row)
        {
            if (row.Off.Checked) return ToolModeChoice.Off;
            if (row.Auto.Checked) return ToolModeChoice.Auto;
            if (row.Manual.Checked) return ToolModeChoice.Manual;
            return ToolModeChoice.AlwaysRun;
        }

        /// <summary>Approval mode to save: the selected one, or for Off tools the one loaded from config.</summary>
        private static ToolModeChoice GetApprovalToSave(ToolModeRow row)
        {
            ToolModeChoice mode = GetMode(row);
            return mode == ToolModeChoice.Off ? row.StoredApproval : mode;
        }

        public Dictionary<string, string> GetToolOptions()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _optionControls)
            {
                TextBox tb = kvp.Value as TextBox;
                if (tb != null) { result[kvp.Key] = tb.Text; continue; }
                CheckBox cb = kvp.Value as CheckBox;
                if (cb != null) result[kvp.Key] = cb.Checked ? "1" : "0";
            }
            return result;
        }

        public void SetToolOptions(Dictionary<string, string> options)
        {
            if (options == null) return;
            foreach (var kvp in _optionControls)
            {
                string value;
                if (!options.TryGetValue(kvp.Key, out value)) continue;
                TextBox tb = kvp.Value as TextBox;
                if (tb != null) { tb.Text = value; continue; }
                CheckBox cb = kvp.Value as CheckBox;
                if (cb != null) cb.Checked = value == "1";
            }
        }

        public override void LoadFromConfig()
        {
            int limit = ConfigHandler.GetMaxConsecutiveFailedToolCalls();
            if (limit > (int)_maxConsecutiveFailures.Maximum)
                limit = (int)_maxConsecutiveFailures.Maximum;
            _maxConsecutiveFailures.Value = limit;
            _selfImprovement.Checked = ConfigHandler.GetSelfImprovementEnabled();
            _jevBaseUrl.Text = ConfigHandler.GetJevBaseUrl() ?? string.Empty;
            _jevApiKey.Text = ConfigHandler.GetJevApiKey() ?? string.Empty;
            _jevModel.Text = ConfigHandler.GetJevModel() ?? string.Empty;

            var disabled = new HashSet<string>(ConfigHandler.GetDisabledTools() ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (ToolModeRow row in _modes)
            {
                if (ConfigHandler.IsAutoToolApproval(row.Name))
                    row.StoredApproval = ToolModeChoice.Auto;
                else if (ConfigHandler.ToolRequiresApproval(row.Name))
                    row.StoredApproval = ToolModeChoice.Manual;
                else
                    row.StoredApproval = ToolModeChoice.AlwaysRun;

                SelectMode(row, disabled.Contains(row.Name) ? ToolModeChoice.Off : row.StoredApproval);
            }

            var toolOpts = new Dictionary<string, string>(ExternalToolRegistry.GetOptionDefaults(), StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in ConfigHandler.GetAllValues())
                toolOpts[kvp.Key] = kvp.Value;
            SetToolOptions(toolOpts);
        }

        public override void SaveToConfig()
        {
            var disabled = new List<string>();
            var auto = new List<string>();
            var manual = new List<string>();
            foreach (ToolModeRow row in _modes)
            {
                if (GetMode(row) == ToolModeChoice.Off)
                    disabled.Add(row.Name);
                ToolModeChoice approval = GetApprovalToSave(row);
                if (approval == ToolModeChoice.Auto)
                    auto.Add(row.Name);
                else if (approval == ToolModeChoice.Manual)
                    manual.Add(row.Name);
            }

            ConfigHandler.SetMaxConsecutiveFailedToolCalls((int)_maxConsecutiveFailures.Value);
            ConfigHandler.SetSelfImprovementEnabled(_selfImprovement.Checked);
            ConfigHandler.SetDisabledTools(disabled);
            ConfigHandler.SetAutoApprovalTools(auto);
            ConfigHandler.SetToolsRequiringApproval(manual);
            ConfigHandler.SetJevBaseUrl(_jevBaseUrl.Text);
            ConfigHandler.SetJevApiKey(_jevApiKey.Text);
            ConfigHandler.SetJevModel(_jevModel.Text);
            foreach (var kvp in GetToolOptions())
                ConfigHandler.SetConfigValue(kvp.Key, kvp.Value);
        }
    }
}
