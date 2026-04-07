using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class ToolsOptionsPageHost : UserControl
    {
        private ToolsOptionsPage optionsPage;
        private TableLayoutPanel layout;
        private readonly List<Control> wrappingControls = new List<Control>();

        private CheckedListBox _toolList;
        private Dictionary<string, Control> _optionControls = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        public ToolsOptionsPageHost(ToolsOptionsPage page)
        {
            this.optionsPage = page;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.Font;
            this.AutoScroll = true;
            this.BackColor = SystemColors.Control;
            this.layout = new TableLayoutPanel();
            this.layout.AutoSize = true;
            this.layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.layout.ColumnCount = 1;
            this.layout.Dock = DockStyle.Top;
            this.layout.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            this.layout.Padding = new Padding(20, 10, 20, 10);
            this.layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            this.Controls.Add(this.layout);
            this.MinimumSize = new Size(420, 0);
            this.Size = new Size(420, 520);

            BuildContent();

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateWrappingWidths();
        }

        private Label MakeSectionTitle(string text)
        {
            Label lbl = new Label();
            lbl.AutoSize = true;
            lbl.Font = new Font(this.Font, FontStyle.Bold);
            lbl.Text = text;
            return lbl;
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
            wrappingControls.Clear();

            ExternalToolRegistry.EnsureLoaded();
            List<ExternalToolRegistry.PackageInfo> packages = ExternalToolRegistry.GetPackages();

            // Tools section
            AddRow(MakeSectionTitle("Tools:"), new Padding(0, 0, 0, 8), false);

            var allTools = new List<string>();
            foreach (string toolName in ToolDefinitions.BuiltInToolNames)
                allTools.Add(toolName);
            foreach (ExternalToolRegistry.PackageInfo pkg in packages)
                foreach (string toolName in pkg.ToolNames)
                    allTools.Add(toolName);

            _toolList = new CheckedListBox();
            _toolList.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _toolList.CheckOnClick = true;
            _toolList.BorderStyle = BorderStyle.FixedSingle;
            _toolList.IntegralHeight = false;
            foreach (string toolName in allTools)
                _toolList.Items.Add(toolName, true);
            _toolList.Height = Math.Max(104, (_toolList.ItemHeight * Math.Max(1, Math.Min(allTools.Count, 8))) + 8);
            AddRow(_toolList, new Padding(0, 0, 0, 12), false);

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

        private void AddRow(Control control, Padding margin, bool wrapControl)
        {
            control.Margin = margin;
            this.layout.RowStyles.Add(new RowStyle());
            this.layout.Controls.Add(control, 0, this.layout.RowCount);
            this.layout.RowCount++;

            if (wrapControl)
                wrappingControls.Add(control);
        }

        private void UpdateWrappingWidths()
        {
            int availableWidth = Math.Max(120, this.ClientSize.Width - this.layout.Padding.Left - this.layout.Padding.Right - 8);
            foreach (Control control in wrappingControls)
                control.MaximumSize = new Size(availableWidth, 0);
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
