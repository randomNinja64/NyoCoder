using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class ToolsOptionsPageHost : UserControl
    {
        private ToolsOptionsPage optionsPage;

        private CheckedListBox _toolList;
        private Dictionary<string, Control> _optionControls = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        public ToolsOptionsPageHost(ToolsOptionsPage page)
        {
            this.optionsPage = page;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.AutoScroll = true;
            this.BackColor = SystemColors.Control;
            this.Size = new Size(400, 460);

            BuildContent();
        }

        private Label MakeSectionTitle(string text, int y)
        {
            Label lbl = new Label();
            lbl.AutoSize = true;
            lbl.Font = new Font(this.Font, FontStyle.Bold);
            lbl.Location = new Point(20, y);
            lbl.Text = text;
            return lbl;
        }

        private Label MakeFieldLabel(string text, int y)
        {
            Label lbl = new Label();
            lbl.AutoSize = true;
            lbl.Location = new Point(20, y);
            lbl.Text = text;
            return lbl;
        }

        private CheckBox MakeCheckBox(string text, int y)
        {
            CheckBox cb = new CheckBox();
            cb.AutoSize = true;
            cb.Location = new Point(20, y);
            cb.Text = text;
            return cb;
        }

        private TextBox MakeTextBox(int y)
        {
            TextBox tb = new TextBox();
            tb.Location = new Point(20, y);
            tb.Size = new Size(340, 23);
            return tb;
        }

        private void BuildContent()
        {
            this.Controls.Clear();
            _optionControls.Clear();

            ExternalToolRegistry.EnsureLoaded();
            List<ExternalToolRegistry.PackageInfo> packages = ExternalToolRegistry.GetPackages();

            int y = 10;

            // Tools section
            this.Controls.Add(MakeSectionTitle("Tools:", y));
            y += 25;

            var allTools = new List<string>();
            foreach (string toolName in ToolDefinitions.BuiltInToolNames)
                allTools.Add(toolName);
            foreach (ExternalToolRegistry.PackageInfo pkg in packages)
                foreach (string toolName in pkg.ToolNames)
                    allTools.Add(toolName);

            _toolList = new CheckedListBox();
            _toolList.Location = new Point(20, y);
            _toolList.Size = new Size(340, 104);
            _toolList.CheckOnClick = true;
            _toolList.BorderStyle = BorderStyle.FixedSingle;
            foreach (string toolName in allTools)
                _toolList.Items.Add(toolName, true);
            this.Controls.Add(_toolList);
            y += 104 + 10;

            // Per-package config sections
            foreach (ExternalToolRegistry.PackageInfo pkg in packages)
            {
                if (pkg.Options.Count == 0)
                    continue;

                y += 5;
                this.Controls.Add(MakeSectionTitle(pkg.DisplayName + ":", y));
                y += 25;

                foreach (ExternalToolRegistry.OptionDefinition opt in pkg.Options)
                {
                    if (opt.Type == "bool")
                    {
                        CheckBox cb = MakeCheckBox(opt.Label, y);
                        this.Controls.Add(cb);
                        _optionControls[opt.Name] = cb;
                        y += 22;
                    }
                    else
                    {
                        this.Controls.Add(MakeFieldLabel(opt.Label + ":", y));
                        y += 17;
                        TextBox tb = MakeTextBox(y);
                        this.Controls.Add(tb);
                        _optionControls[opt.Name] = tb;
                        y += 30;
                    }
                }
            }

            if (packages.Count == 0)
            {
                y += 10;
                Label lblHint = new Label();
                lblHint.AutoSize = false;
                lblHint.Size = new Size(340, 34);
                lblHint.Location = new Point(20, y);
                lblHint.ForeColor = SystemColors.GrayText;
                lblHint.Text = "Drop external tool packages into: " + ExternalToolRegistry.ToolsDirectory;
                this.Controls.Add(lblHint);
            }
        }

        public List<string> GetDisabledTools()
        {
            var result = new List<string>();
            if (_toolList == null) return result;
            for (int i = 0; i < _toolList.Items.Count; i++)
            {
                if (!_toolList.GetItemChecked(i))
                    result.Add((string)_toolList.Items[i]);
            }
            return result;
        }

        public void SetDisabledTools(List<string> disabled)
        {
            if (_toolList == null) return;
            for (int i = 0; i < _toolList.Items.Count; i++)
            {
                bool isDisabled = false;
                if (disabled != null)
                {
                    string name = (string)_toolList.Items[i];
                    foreach (string t in disabled)
                    {
                        if (string.Compare(t, name, true) == 0)
                        {
                            isDisabled = true;
                            break;
                        }
                    }
                }
                _toolList.SetItemChecked(i, !isDisabled);
            }
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
    }
}
