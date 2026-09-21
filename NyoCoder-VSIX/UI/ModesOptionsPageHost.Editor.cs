using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace NyoCoder
{
    public partial class ModesOptionsPageHost
    {
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

    }
}
