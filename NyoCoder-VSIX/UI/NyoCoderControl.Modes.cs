using System;
using System.Windows.Controls;

namespace NyoCoder
{
    public partial class NyoCoderControl
    {
        private void OnModesChanged()
        {
            EditorService.BeginInvokeOnUIThread(RefreshModeSelector, Dispatcher);
        }

        private void ModeSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SyncTokenTrackerMode();
        }

        private void RefreshModeSelector()
        {
            string preserveId = ModeSelector.SelectedValue as string;
            if (string.IsNullOrEmpty(preserveId))
                preserveId = _tokenTracker.CurrentModeId ?? ModeIds.Agent;

            ModeSelector.ItemsSource = ModeRegistry.GetOrderedForDisplay();
            SelectModeById(preserveId);
            SyncTokenTrackerMode();
        }

        private void SelectModeById(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
                modeId = ModeIds.Agent;

            foreach (ModeDefinition mode in ModeSelector.Items)
            {
                if (string.Equals(mode.Id, modeId, StringComparison.OrdinalIgnoreCase))
                {
                    ModeSelector.SelectedItem = mode;
                    return;
                }
            }

            if (ModeSelector.Items.Count > 0)
                ModeSelector.SelectedIndex = 0;
        }

        private void SyncTokenTrackerMode()
        {
            string modeId = ModeSelector.SelectedValue as string;
            if (string.IsNullOrEmpty(modeId) && ModeSelector.SelectedItem is ModeDefinition)
                modeId = ((ModeDefinition)ModeSelector.SelectedItem).Id;

            if (string.IsNullOrEmpty(modeId))
                modeId = ModeIds.Agent;

            _tokenTracker.CurrentModeId = modeId;
            _tokenTracker.ResetCharacterCount(_tokenTracker.TotalCharacterCount);
        }
    }
}
