using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class ToolsOptionsPageHost : OptionsPageHostBase
    {
        private NumericUpDown _maxConsecutiveFailures;
        private CheckBox _selfImprovement;
        private CheckedListBox _toolList;
        private Dictionary<string, Control> _optionControls = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        public ToolsOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(520);
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
            _toolList = MakeToolCheckList(allTools, defaultChecked: true);
            AddRow(_toolList, new Padding(0, 0, 0, 12), false);

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

        private CheckedListBox MakeToolCheckList(List<string> allTools, bool defaultChecked)
        {
            var list = new CheckedListBox();
            list.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            list.CheckOnClick = true;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.IntegralHeight = false;
            foreach (string toolName in allTools)
                list.Items.Add(toolName, defaultChecked);
            list.Height = Math.Max(100, (list.ItemHeight * Math.Max(1, Math.Min(allTools.Count, 8))) + 2);
            return list;
        }

        public void ApplyFromConfig(List<string> disabled)
        {
            ApplyToolList(_toolList, disabled, checkedWhenListed: false);
        }

        public void ReadToConfig(out List<string> disabled)
        {
            disabled = ReadToolList(_toolList, wantChecked: false);
        }

        private static List<string> ReadToolList(CheckedListBox list, bool wantChecked)
        {
            var result = new List<string>();
            for (int i = 0; i < list.Items.Count; i++)
                if (list.GetItemChecked(i) == wantChecked)
                    result.Add((string)list.Items[i]);
            return result;
        }

        private static void ApplyToolList(CheckedListBox list, List<string> selected, bool checkedWhenListed)
        {
            var set = new HashSet<string>(selected ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Items.Count; i++)
                list.SetItemChecked(i, set.Contains((string)list.Items[i]) == checkedWhenListed);
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
            ApplyFromConfig(ConfigHandler.GetDisabledTools());
            var toolOpts = new Dictionary<string, string>(ExternalToolRegistry.GetOptionDefaults(), StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in ConfigHandler.GetAllValues())
                toolOpts[kvp.Key] = kvp.Value;
            SetToolOptions(toolOpts);
        }

        public override void SaveToConfig()
        {
            List<string> disabled;
            ReadToConfig(out disabled);
            ConfigHandler.SetMaxConsecutiveFailedToolCalls((int)_maxConsecutiveFailures.Value);
            ConfigHandler.SetSelfImprovementEnabled(_selfImprovement.Checked);
            ConfigHandler.SetDisabledTools(disabled);
            foreach (var kvp in GetToolOptions())
                ConfigHandler.SetConfigValue(kvp.Key, kvp.Value);
        }
    }
}
