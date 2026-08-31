using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class ModesOptionsPageHost : OptionsPageHostBase
    {
        private sealed class PromptTextBox : TextBox
        {
            private const int WM_GETDLGCODE = 0x0087;
            private const int WM_KEYDOWN = 0x0100;
            private const int WM_CHAR = 0x0102;
            private const int DLGC_WANTALLKEYS = 0x0004;

            private bool _swallowReturnChar;

            // The VS Options dialog runs an IsDialogMessage-style loop: it treats Enter as OK
            // unless the control claims the key, and it dispatches the keystroke without
            // translating it into the WM_CHAR an edit control needs to insert the newline.
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_KEYDOWN && m.WParam.ToInt64() == (int)Keys.Enter)
                {
                    int caret = SelectionStart;
                    SelectedText = Environment.NewLine;
                    SelectionStart = caret + Environment.NewLine.Length;
                    ScrollToCaret();
                    _swallowReturnChar = true;
                    return;
                }

                if (m.Msg == WM_CHAR && m.WParam.ToInt64() == '\r' && _swallowReturnChar)
                {
                    _swallowReturnChar = false;
                    return;
                }

                base.WndProc(ref m);

                if (m.Msg == WM_GETDLGCODE && m.WParam.ToInt64() == (int)Keys.Enter)
                    m.Result = new IntPtr(m.Result.ToInt64() | DLGC_WANTALLKEYS);
            }
        }

        private sealed class ModeListItem
        {
            public readonly string Id;
            public readonly string DisplayName;

            public ModeListItem(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private ListBox _modeList;
        private TextBox _displayNameBox;
        private TextBox _promptBox;
        private RadioButton _allToolsRadio;
        private RadioButton _allowListRadio;
        private CheckedListBox _toolList;
        private TableLayoutPanel _editorLayout;
        private Button _addButton;
        private Button _deleteButton;
        private Button _resetButton;

        private Dictionary<string, ModeDefinition> _builtInOverrides =
            new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);

        private List<ModeDefinition> _customModes = new List<ModeDefinition>();
        private List<string> _allToolNames = new List<string>();
        private string _selectedModeId;
        private bool _loadingEditor;

        public ModesOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(310);
            this.AutoScroll = false;
            BuildContent();
            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
        }

        private const int MainPanelHeight = 210;
        private const int PromptHeight = 88;
        private const int AllowListPromptHeight = 64;
        private const int ToolListHeight = 60;

        private void BuildContent()
        {
            AddRow(MakeSectionTitle("Modes"), new Padding(0, 0, 0, 8), false);

            TableLayoutPanel mainPanel = new TableLayoutPanel();
            mainPanel.ColumnCount = 2;
            mainPanel.RowCount = 1;
            mainPanel.AutoSize = false;
            mainPanel.Height = MainPanelHeight;
            mainPanel.Dock = DockStyle.Top;
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _modeList = new ListBox();
            _modeList.Dock = DockStyle.Fill;
            _modeList.IntegralHeight = false;
            _modeList.SelectedIndexChanged += ModeList_SelectedIndexChanged;
            mainPanel.Controls.Add(_modeList, 0, 0);

            _editorLayout = new TableLayoutPanel();
            _editorLayout.ColumnCount = 1;
            _editorLayout.Dock = DockStyle.Fill;
            _editorLayout.RowCount = 6;
            _editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, PromptHeight));
            _editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

            Label displayLabel = new Label { AutoSize = true, Text = "Display name:", Margin = new Padding(0) };
            _editorLayout.Controls.Add(displayLabel, 0, 0);

            _displayNameBox = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 0, 4)
            };
            _editorLayout.Controls.Add(_displayNameBox, 0, 1);

            Label promptLabel = new Label { AutoSize = true, Text = "System prompt:", Margin = new Padding(0) };
            _editorLayout.Controls.Add(promptLabel, 0, 2);

            _promptBox = new PromptTextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 4)
            };
            _editorLayout.Controls.Add(_promptBox, 0, 3);

            FlowLayoutPanel policyPanel = new FlowLayoutPanel();
            policyPanel.AutoSize = true;
            policyPanel.FlowDirection = FlowDirection.LeftToRight;
            policyPanel.WrapContents = false;
            policyPanel.Margin = new Padding(0, 0, 0, 2);

            Label toolsLabel = new Label { AutoSize = true, Text = "Tools:", Margin = new Padding(0, 4, 8, 0) };
            _allToolsRadio = new RadioButton { AutoSize = true, Text = "All", Checked = true, Margin = new Padding(0, 2, 12, 0) };
            _allToolsRadio.CheckedChanged += ToolPolicy_Changed;
            _allowListRadio = new RadioButton { AutoSize = true, Text = "Allow list", Margin = new Padding(0, 2, 0, 0) };
            _allowListRadio.CheckedChanged += ToolPolicy_Changed;
            policyPanel.Controls.Add(toolsLabel);
            policyPanel.Controls.Add(_allToolsRadio);
            policyPanel.Controls.Add(_allowListRadio);
            _editorLayout.Controls.Add(policyPanel, 0, 4);

            _toolList = new CheckedListBox();
            _toolList.CheckOnClick = true;
            _toolList.BorderStyle = BorderStyle.FixedSingle;
            _toolList.Dock = DockStyle.Fill;
            _toolList.IntegralHeight = false;
            _toolList.Enabled = false;
            _toolList.Visible = false;
            _editorLayout.Controls.Add(_toolList, 0, 5);

            mainPanel.Controls.Add(_editorLayout, 1, 0);
            AddRow(mainPanel, new Padding(0, 0, 0, 8), false);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0);

            _addButton = new Button { Text = "Add Custom Mode", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _addButton.Click += AddButton_Click;
            _deleteButton = new Button { Text = "Delete", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _deleteButton.Click += DeleteButton_Click;
            _resetButton = new Button { Text = "Reset to Default", AutoSize = true, Margin = new Padding(0) };
            _resetButton.Click += ResetButton_Click;

            buttons.Controls.Add(_addButton);
            buttons.Controls.Add(_deleteButton);
            buttons.Controls.Add(_resetButton);
            AddRow(buttons, new Padding(0, 0, 0, 0), true);
        }

        public void LoadFromConfig()
        {
            ExternalToolRegistry.EnsureLoaded();
            _allToolNames = BuildAllToolNames();
            PopulateToolList();

            ModeRegistry.GetEditableSnapshot(out _builtInOverrides, out _customModes);
            RefreshModeList();

            if (_modeList.Items.Count > 0)
            {
                _modeList.SelectedIndex = 0;
            }
            else
            {
                ClearEditor();
            }
        }

        public void SaveToConfig()
        {
            CommitEditorToSelection();
            ModeRegistry.Save(_builtInOverrides, _customModes);
        }

        private static List<string> BuildAllToolNames()
        {
            var allTools = new List<string>();
            foreach (string toolName in ToolDefinitions.BuiltInToolNames)
                allTools.Add(toolName);
            foreach (ExternalToolRegistry.PackageInfo pkg in ExternalToolRegistry.GetPackages())
            {
                foreach (string toolName in pkg.ToolNames)
                    allTools.Add(toolName);
            }
            allTools.Sort(StringComparer.OrdinalIgnoreCase);
            return allTools;
        }

        private void PopulateToolList()
        {
            _toolList.Items.Clear();
            foreach (string toolName in _allToolNames)
                _toolList.Items.Add(toolName, false);
        }

        private void RefreshModeList()
        {
            string preserveId = _selectedModeId;
            _modeList.Items.Clear();

            var localModes = new List<ModeDefinition>();
            foreach (string id in ModeIds.BuiltInOrder)
                localModes.Add(GetResolvedMode(id));
            foreach (ModeDefinition mode in _customModes)
                localModes.Add(mode.Clone());

            foreach (ModeDefinition mode in ModeRegistry.OrderForDisplay(localModes))
                _modeList.Items.Add(new ModeListItem(mode.Id, mode.DisplayName));

            SelectModeInList(preserveId);
        }

        private void SelectModeInList(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
            {
                if (_modeList.Items.Count > 0)
                    _modeList.SelectedIndex = 0;
                return;
            }

            for (int i = 0; i < _modeList.Items.Count; i++)
            {
                ModeListItem item = _modeList.Items[i] as ModeListItem;
                if (item != null && string.Equals(item.Id, modeId, StringComparison.OrdinalIgnoreCase))
                {
                    _modeList.SelectedIndex = i;
                    return;
                }
            }

            if (_modeList.Items.Count > 0)
                _modeList.SelectedIndex = 0;
        }

        private ModeDefinition GetResolvedMode(string id)
        {
            ModeDefinition custom = FindCustomMode(id);
            if (custom != null)
                return custom.Clone();

            ModeDefinition defaults = ModeDefaults.CreateBuiltInDefault(id);
            ModeDefinition overrideDef;
            if (_builtInOverrides.TryGetValue(id, out overrideDef))
            {
                return new ModeDefinition
                {
                    Id = id,
                    DisplayName = defaults.DisplayName,
                    SystemPrompt = overrideDef.SystemPrompt ?? string.Empty,
                    ToolPolicy = overrideDef.ToolPolicy,
                    Tools = overrideDef.ToolPolicy == ModeToolPolicy.AllowList
                        ? CloneTools(overrideDef.Tools, defaults.Tools)
                        : new string[0],
                    IsBuiltIn = true
                };
            }

            return defaults;
        }

        private ModeDefinition FindCustomMode(string id)
        {
            foreach (ModeDefinition mode in _customModes)
            {
                if (string.Equals(mode.Id, id, StringComparison.OrdinalIgnoreCase))
                    return mode;
            }
            return null;
        }

        private static string[] CloneTools(string[] primary, string[] fallback)
        {
            if (primary != null && primary.Length > 0)
                return (string[])primary.Clone();
            if (fallback != null && fallback.Length > 0)
                return (string[])fallback.Clone();
            return new string[0];
        }

        private void ModeList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingEditor)
                return;

            CommitEditorToSelection();

            ModeListItem item = _modeList.SelectedItem as ModeListItem;
            if (item == null)
            {
                ClearEditor();
                return;
            }

            _selectedModeId = item.Id;
            LoadEditor(GetResolvedMode(item.Id));
        }

        private void LoadEditor(ModeDefinition mode)
        {
            _loadingEditor = true;
            try
            {
                bool isBuiltIn = ModeRegistry.IsBuiltInId(mode.Id);
                _displayNameBox.Text = mode.DisplayName ?? string.Empty;
                _displayNameBox.ReadOnly = isBuiltIn;

                string prompt = isBuiltIn && string.IsNullOrWhiteSpace(mode.SystemPrompt)
                    ? ModeDefaults.GetDefaultSystemPrompt(mode.Id)
                    : (mode.SystemPrompt ?? string.Empty);
                _promptBox.Text = string.IsNullOrEmpty(prompt)
                    ? string.Empty
                    : prompt.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);

                _allToolsRadio.Checked = mode.ToolPolicy != ModeToolPolicy.AllowList;
                _allowListRadio.Checked = mode.ToolPolicy == ModeToolPolicy.AllowList;
                ApplyToolChecks(mode.Tools);
                UpdateToolListEnabled();

                _deleteButton.Enabled = !isBuiltIn;
                _resetButton.Enabled = isBuiltIn;
            }
            finally
            {
                _loadingEditor = false;
            }
        }

        private void ClearEditor()
        {
            _loadingEditor = true;
            try
            {
                _selectedModeId = null;
                _displayNameBox.Clear();
                _displayNameBox.ReadOnly = true;
                _promptBox.Clear();
                _allToolsRadio.Checked = true;
                ApplyToolChecks(null);
                UpdateToolListEnabled();
                _deleteButton.Enabled = false;
                _resetButton.Enabled = false;
            }
            finally
            {
                _loadingEditor = false;
            }
        }

        private void CommitEditorToSelection()
        {
            if (_loadingEditor || string.IsNullOrEmpty(_selectedModeId))
                return;

            ModeDefinition edited = ReadEditor();
            edited.Id = _selectedModeId;

            if (ModeRegistry.IsBuiltInId(_selectedModeId))
            {
                if (IsBuiltInAtDefaults(_selectedModeId, edited))
                    _builtInOverrides.Remove(_selectedModeId);
                else
                    _builtInOverrides[_selectedModeId] = edited;
            }
            else
            {
                ModeDefinition existing = FindCustomMode(_selectedModeId);
                if (existing != null)
                {
                    existing.DisplayName = edited.DisplayName;
                    existing.SystemPrompt = edited.SystemPrompt;
                    existing.ToolPolicy = edited.ToolPolicy;
                    existing.Tools = edited.Tools;
                }
            }
        }

        private ModeDefinition ReadEditor()
        {
            var tools = new List<string>();
            if (_allowListRadio.Checked)
            {
                for (int i = 0; i < _toolList.Items.Count; i++)
                {
                    if (_toolList.GetItemChecked(i))
                        tools.Add((string)_toolList.Items[i]);
                }
            }

            return new ModeDefinition
            {
                DisplayName = _displayNameBox.Text != null ? _displayNameBox.Text.Trim() : string.Empty,
                SystemPrompt = string.IsNullOrEmpty(_promptBox.Text)
                    ? string.Empty
                    : _promptBox.Text.Replace("\r\n", "\n").Replace('\r', '\n'),
                ToolPolicy = _allowListRadio.Checked ? ModeToolPolicy.AllowList : ModeToolPolicy.All,
                Tools = tools.ToArray(),
                IsBuiltIn = ModeRegistry.IsBuiltInId(_selectedModeId)
            };
        }

        private bool IsBuiltInAtDefaults(string id, ModeDefinition edited)
        {
            string defaultPrompt = ModeDefaults.GetDefaultSystemPrompt(id);
            string editedPrompt = edited.SystemPrompt != null ? edited.SystemPrompt.Trim() : string.Empty;
            if (!string.Equals(editedPrompt, defaultPrompt, StringComparison.Ordinal))
                return false;

            if (edited.ToolPolicy != ModeDefaults.GetDefaultToolPolicy(id))
                return false;

            if (edited.ToolPolicy == ModeToolPolicy.AllowList)
            {
                string[] defaultTools = ModeDefaults.GetDefaultTools(id);
                if (!SameToolSet(edited.Tools, defaultTools))
                    return false;
            }

            return true;
        }

        private static bool SameToolSet(string[] a, string[] b)
        {
            var setA = new HashSet<string>(a ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var setB = new HashSet<string>(b ?? new string[0], StringComparer.OrdinalIgnoreCase);
            return setA.SetEquals(setB);
        }

        private void ApplyToolChecks(string[] tools)
        {
            var allowed = new HashSet<string>(tools ?? new string[0], StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _toolList.Items.Count; i++)
                _toolList.SetItemChecked(i, allowed.Contains((string)_toolList.Items[i]));
        }

        private void UpdateToolListEnabled()
        {
            bool allowList = _allowListRadio.Checked;
            _toolList.Enabled = allowList;
            _toolList.Visible = allowList;
            if (_editorLayout != null)
            {
                _editorLayout.RowStyles[3] = new RowStyle(SizeType.Absolute, allowList ? AllowListPromptHeight : PromptHeight);
                _editorLayout.RowStyles[5] = new RowStyle(SizeType.Absolute, allowList ? ToolListHeight : 0);
            }
        }

        private void ToolPolicy_Changed(object sender, EventArgs e)
        {
            if (_loadingEditor)
                return;
            UpdateToolListEnabled();
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            CommitEditorToSelection();

            var mode = new ModeDefinition
            {
                Id = ModeRegistry.GenerateCustomModeId(_customModes),
                DisplayName = "Custom Mode",
                SystemPrompt = string.Empty,
                ToolPolicy = ModeToolPolicy.All,
                Tools = new string[0],
                IsBuiltIn = false
            };
            _customModes.Add(mode);
            RefreshModeList();
            SelectModeInList(mode.Id);
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedModeId) || ModeRegistry.IsBuiltInId(_selectedModeId))
                return;

            CommitEditorToSelection();
            _customModes.RemoveAll(m => string.Equals(m.Id, _selectedModeId, StringComparison.OrdinalIgnoreCase));
            _selectedModeId = null;
            RefreshModeList();
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedModeId) || !ModeRegistry.IsBuiltInId(_selectedModeId))
                return;

            _builtInOverrides.Remove(_selectedModeId);
            LoadEditor(ModeDefaults.CreateBuiltInDefault(_selectedModeId));
        }
    }
}
