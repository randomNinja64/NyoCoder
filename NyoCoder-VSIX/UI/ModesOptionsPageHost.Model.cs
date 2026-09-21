using System;
using System.Collections.Generic;

namespace NyoCoder
{
    public partial class ModesOptionsPageHost
    {
        public override void LoadFromConfig()
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

        public override void SaveToConfig()
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
