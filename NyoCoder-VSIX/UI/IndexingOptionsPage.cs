using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("48CFF078-94C5-45AC-9B93-60678D2022DB")]
    public class IndexingOptionsPage : ConfigDialogPage<IndexingOptionsPageHost>
    {
        protected override IndexingOptionsPageHost CreateHost()
        {
            IndexingOptionsPageHost host = new IndexingOptionsPageHost();
            host.IndexNowClicked += OnIndexNow;
            host.ClearClicked += OnClear;
            return host;
        }

        private void OnIndexNow()
        {
            // Persist any unsaved edits so the indexer uses the current field values.
            SaveHostToConfig();
            ConfigHandler.SaveConfig();
            ConfigHandler.ReloadConfig();
            CodebaseIndexer.RequestFullIndex();
        }

        private void OnClear()
        {
            CodebaseIndexer.RequestClearIndex();
        }
    }
}
