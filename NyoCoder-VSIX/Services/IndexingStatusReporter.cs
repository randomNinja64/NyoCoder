using System;
using System.Globalization;
using System.Text;

namespace NyoCoder
{
    /// <summary>
    /// Immutable-ish snapshot of the indexing state, consumed by the options panel and the
    /// chat status bar. <see cref="BriefText"/> and <see cref="DetailText"/> are computed by
    /// <see cref="IndexingStatusReporter"/> when published.
    /// </summary>
    public class IndexingStatusSnapshot
    {
        public IndexingMode Mode;
        public string Phase = "idle"; // idle | indexing | ready | error
        public int Done;
        public int Total;

        public bool HasIndex;
        public string SolutionName;
        public int FileCount;
        public int SymbolCount;
        public int ChunkCount;
        public bool SemanticPresent;
        public string EmbeddingsModel;
        public int Dimension;
        public DateTime? LastIndexedUtc;

        public string Error;

        public string BriefText;
        public string DetailText;

        public IndexingStatusSnapshot Clone()
        {
            return (IndexingStatusSnapshot)MemberwiseClone();
        }
    }

    /// <summary>
    /// Static hub that holds the latest indexing status and raises <see cref="StatusChanged"/>
    /// whenever it changes. The indexer and DTE triggers publish; the options panel and chat
    /// status bar subscribe. Keeps the two UIs in sync without coupling them.
    /// </summary>
    public static class IndexingStatusReporter
    {
        private static readonly object _gate = new object();
        private static IndexingStatusSnapshot _current = new IndexingStatusSnapshot();

        /// <summary>Raised (on the publishing thread) whenever the status changes.</summary>
        public static event Action StatusChanged;

        /// <summary>Returns a copy of the current status snapshot.</summary>
        public static IndexingStatusSnapshot Current
        {
            get { lock (_gate) { return _current.Clone(); } }
        }

        /// <summary>Publishes a status snapshot from the indexer/loader.</summary>
        public static void Publish(IndexingStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return;
            FormatTexts(snapshot);
            lock (_gate) { _current = snapshot; }
            RaiseChanged();
        }

        /// <summary>
        /// Announces that indexing has started for the active workspace. Call this at the
        /// beginning of a run so the status bar switches away from a prior solution's snapshot.
        /// </summary>
        public static void BeginProgress(string phaseLabel = "Indexing")
        {
            IndexingStatusSnapshot snapshot = CreateWorkspaceSnapshot();
            snapshot.Phase = "indexing";
            snapshot.Done = 0;
            snapshot.Total = 0;
            snapshot.Error = null;
            FormatTexts(snapshot, phaseLabel);
            lock (_gate) { _current = snapshot; }
            RaiseChanged();
        }

        /// <summary>Reports indexing progress for the active workspace.</summary>
        public static void ReportProgress(int done, int total, string phaseLabel)
        {
            IndexingStatusSnapshot snapshot = CreateWorkspaceSnapshot();
            snapshot.Phase = "indexing";
            snapshot.Done = done;
            snapshot.Total = total;
            snapshot.Error = null;
            FormatTexts(snapshot, phaseLabel);
            lock (_gate) { _current = snapshot; }
            RaiseChanged();
        }

        /// <summary>Reports an indexing error for the active workspace.</summary>
        public static void ReportError(string message)
        {
            IndexingStatusSnapshot snapshot = CreateWorkspaceSnapshot();
            snapshot.Phase = "error";
            snapshot.Error = message;
            FormatTexts(snapshot);
            lock (_gate) { _current = snapshot; }
            RaiseChanged();
        }

        /// <summary>Builds a snapshot from the active solution's index (not the last published one).</summary>
        private static IndexingStatusSnapshot CreateWorkspaceSnapshot()
        {
            try
            {
                return CodebaseIndex.GetCurrent().GetStatus();
            }
            catch
            {
                IndexingStatusSnapshot fallback = new IndexingStatusSnapshot();
                fallback.Mode = ConfigHandler.GetIndexingMode();
                return fallback;
            }
        }

        private static void RaiseChanged()
        {
            Action handler = StatusChanged;
            if (handler != null)
            {
                try { handler(); }
                catch { }
            }
        }

        private static void FormatTexts(IndexingStatusSnapshot s, string phaseLabel = null)
        {
            string modeName = s.Mode.ToString();

            // Brief (in-bar) text
            switch (s.Phase)
            {
                case "indexing":
                    string label = string.IsNullOrEmpty(phaseLabel) ? "Indexing" : phaseLabel;
                    s.BriefText = s.Total > 0
                        ? string.Format(CultureInfo.CurrentCulture, "{0}... {1}/{2}", label, s.Done, s.Total)
                        : label + "...";
                    break;
                case "error":
                    s.BriefText = "Index: error";
                    break;
                case "ready":
                    if (s.HasIndex)
                    {
                        StringBuilder brief = new StringBuilder();
                        brief.Append("Index: Ready - ");
                        brief.Append(s.FileCount).Append(" files, ");
                        brief.Append(s.SymbolCount).Append(" symbols");
                        if (s.SemanticPresent && s.ChunkCount > 0)
                            brief.Append(", ").Append(s.ChunkCount).Append(" vectors");
                        s.BriefText = brief.ToString();
                    }
                    else
                    {
                        s.BriefText = "Index: " + modeName + " mode (not built)";
                    }
                    break;
                default:
                    s.BriefText = s.HasIndex
                        ? "Index: " + modeName + " mode"
                        : "Index: " + modeName + " mode (not built)";
                    break;
            }

            // Detailed tooltip
            StringBuilder detail = new StringBuilder();
            detail.Append("Indexing mode: ").AppendLine(modeName);
            if (!string.IsNullOrEmpty(s.SolutionName))
                detail.Append("Workspace: ").AppendLine(s.SolutionName);
            if (s.Mode == IndexingMode.Semantic || s.SemanticPresent)
            {
                detail.Append("Embeddings model: ")
                      .AppendLine(string.IsNullOrEmpty(s.EmbeddingsModel) ? "(not set)" : s.EmbeddingsModel);
                if (s.Dimension > 0)
                    detail.Append("Dimension: ").AppendLine(s.Dimension.ToString(CultureInfo.CurrentCulture));
            }
            if (s.HasIndex)
            {
                detail.Append("Files: ").AppendLine(s.FileCount.ToString(CultureInfo.CurrentCulture));
                detail.Append("Symbols: ").AppendLine(s.SymbolCount.ToString(CultureInfo.CurrentCulture));
                detail.Append("Vectors: ").AppendLine(s.ChunkCount.ToString(CultureInfo.CurrentCulture));
                detail.Append("Semantic vectors present: ").AppendLine(s.SemanticPresent ? "yes" : "no");
                if (s.LastIndexedUtc.HasValue)
                    detail.Append("Last indexed: ")
                          .AppendLine(s.LastIndexedUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            }
            else if (s.Phase != "indexing")
            {
                detail.AppendLine("No index built yet.");
            }
            if (!string.IsNullOrEmpty(s.Error))
                detail.Append("Last error: ").AppendLine(s.Error);

            s.DetailText = detail.ToString().TrimEnd();
        }
    }
}
