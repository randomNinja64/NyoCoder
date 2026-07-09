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

        protected override void UpdateHostFromConfig()
        {
            if (Host == null) return;

            Host.Endpoint = ConfigHandler.GetEmbeddingsEndpointRaw();
            Host.Model = ConfigHandler.GetEmbeddingsModel();
            Host.ApiKey = ConfigHandler.GetEmbeddingsApiKeyRaw();
            Host.IndexOnSolutionOpen = ConfigHandler.GetIndexOnSolutionOpen();
            Host.IndexOnSave = ConfigHandler.GetIndexOnSave();
            Host.MaxChunksTotal = ConfigHandler.GetIndexMaxChunksTotal();
            Host.Mode = ConfigHandler.GetIndexingMode();

            CodebaseIndex.PublishStatus();
            Host.RefreshStatus();
        }

        protected override void SaveHostToConfig()
        {
            if (Host == null) return;

            ConfigHandler.SetIndexingMode(Host.Mode);
            ConfigHandler.SetConfigValue("embeddingsEndpoint", Host.Endpoint);
            ConfigHandler.SetConfigValue("embeddingsModel", Host.Model);
            ConfigHandler.SetConfigValue("embeddingsApiKey", Host.ApiKey);
            ConfigHandler.SetConfigValue("indexOnSolutionOpen", Host.IndexOnSolutionOpen ? "1" : "0");
            ConfigHandler.SetConfigValue("indexOnSave", Host.IndexOnSave ? "1" : "0");
            ConfigHandler.SetIndexMaxChunksTotal(Host.MaxChunksTotal);
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
