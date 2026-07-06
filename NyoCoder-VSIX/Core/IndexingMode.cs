namespace NyoCoder
{
    /// <summary>
    /// Selects the codebase-indexing backend used by the codebase_search tool.
    /// Symbol map (definitions + callers) is built for Symbol and Semantic; only Semantic adds vectors.
    /// </summary>
    public enum IndexingMode
    {
        /// <summary>No indexing; codebase_search falls back to grep.</summary>
        Off,

        /// <summary>Offline symbol map search (no embeddings).</summary>
        Symbol,

        /// <summary>Semantic search via embeddings; symbol map built as fallback.</summary>
        Semantic
    }
}
