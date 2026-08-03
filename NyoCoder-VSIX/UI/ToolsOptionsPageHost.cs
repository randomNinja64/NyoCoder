using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class ToolsOptionsPageHost : OptionsPageHostBase
    {
        private CheckedListBox _toolList;
        private CheckedListBox _approvalList;
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

            AddRow(MakeSectionTitle("Tools:"), new Padding(0, 0, 0, 8), false);
            _toolList = MakeToolCheckList(allTools, defaultChecked: true);
            AddRow(_toolList, new Padding(0, 0, 0, 12), false);

            AddRow(MakeSectionTitle("Tools Requiring Approval:"), new Padding(0, 0, 0, 8), false);
            _approvalList = MakeToolCheckList(allTools, defaultChecked: false);
            AddRow(_approvalList, new Padding(0, 0, 0, 12), false);

            // Per-package config sections
            foreach (ExternalToolRegistry.PackageInfo pkg in packages)
            {
                if (pkg.Options.Count == 0)
                    continue;

                AddRow(MakeSectionTitle(pkg.DisplayName + ":"), new Padding(0, 4, 0, 8), false);

                foreach (ExternalToolRegistry.OptionDefinition opt in pkg.Options)
                {
                    if (opt.Type == "bool")
                    {
                        CheckBox cb = MakeCheckBox(opt.Label);
                        AddRow(cb, new Padding(0, 0, 0, 8), true);
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
                AddRow(lblHint, new Padding(0, 8, 0, 0), true);
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
            list.Height = Math.Max(104, (list.ItemHeight * Math.Max(1, Math.Min(allTools.Count, 8))) + 8);
            return list;
        }

        public void ApplyFromConfig(List<string> disabled, List<string> approval)
        {
            ApplyToolList(_toolList,    disabled, checkedWhenListed: false);
            ApplyToolList(_approvalList, approval, checkedWhenListed: true);
        }

        public void ReadToConfig(out List<string> disabled, out List<string> approval)
        {
            disabled = ReadToolList(_toolList,    wantChecked: false);
            approval = ReadToolList(_approvalList, wantChecked: true);
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

        public void LoadFromConfig()
        {
            ApplyFromConfig(ConfigHandler.GetDisabledTools(), ConfigHandler.GetToolsRequiringApproval());
            var toolOpts = new Dictionary<string, string>(ExternalToolRegistry.GetOptionDefaults(), StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in ConfigHandler.GetAllValues())
                toolOpts[kvp.Key] = kvp.Value;
            SetToolOptions(toolOpts);
        }

        public void SaveToConfig()
        {
            List<string> disabled, approval;
            ReadToConfig(out disabled, out approval);
            ConfigHandler.SetDisabledTools(disabled);
            ConfigHandler.SetToolsRequiringApproval(approval);
            foreach (var kvp in GetToolOptions())
                ConfigHandler.SetConfigValue(kvp.Key, kvp.Value);
        }
    }
}
