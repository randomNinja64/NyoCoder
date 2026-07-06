using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NyoCoder
{
    /// <summary>
    /// Implements the codebase_search tool. Resolves a backend (semantic or symbol) from the
    /// configured indexing mode, then ranks results from the persisted index. Always usable:
    /// when no index is available or a backend yields nothing, it falls back to grep_search
    /// and prepends a note explaining the fallback.
    /// </summary>
    internal static class CodebaseSearchTool
    {
        private const int MaxResults = 10;
        private const int MaxCallersShown = 6;
        private const int SnippetLines = 6;

        public static string Search(string query, out int exitCode)
        {
            exitCode = 0;

            if (string.IsNullOrWhiteSpace(query))
            {
                exitCode = 1;
                return "Error: 'query' is required.";
            }

            IndexingMode mode = ConfigHandler.GetIndexingMode();

            CodebaseIndex index;
            try { index = CodebaseIndex.GetCurrent(); }
            catch { index = null; }

            switch (mode)
            {
                case IndexingMode.Semantic:
                    if (index != null)
                    {
                        string semanticNote;
                        string semanticResult = TrySemantic(index, query, out semanticNote);
                        if (semanticResult != null)
                            return semanticResult;
                        if (!string.IsNullOrEmpty(semanticNote))
                            return FallToSymbolOrGrep(index, query, semanticNote, ref exitCode);
                    }
                    break;

                case IndexingMode.Symbol:
                    if (index != null)
                    {
                        string symbolResult = TrySymbol(index, query);
                        if (symbolResult != null)
                            return symbolResult;
                    }
                    break;
            }

            string reason = mode == IndexingMode.Off
                ? "Indexing is Off"
                : (index == null || !index.HasIndex ? "No usable index" : "No index matches");
            return GrepFallback(index, query, "[codebase_search] " + reason + "; fell back to grep_search.", ref exitCode);
        }

        private static string FallToSymbolOrGrep(CodebaseIndex index, string query,
            string carriedNote, ref int exitCode)
        {
            string symbolResult = TrySymbol(index, query, carriedNote);
            if (symbolResult != null)
                return symbolResult;
            return GrepFallback(index, query, "[codebase_search] " + carriedNote + " Fell back to grep_search.", ref exitCode);
        }

        // ── Semantic ───────────────────────────────────────────────────

        /// <summary>Returns formatted results, or null to signal a fallback (with a note).</summary>
        private static string TrySemantic(CodebaseIndex index, string query, out string note)
        {
            note = null;

            EmbeddingsClient client = EmbeddingsClient.CreateFromConfig();
            if (client == null)
            {
                note = "Semantic search is not configured (set embeddings endpoint + model).";
                return null;
            }

            if (index.Vectors.Count == 0)
            {
                note = "No semantic vectors in the index (run indexing in Semantic mode).";
                return null;
            }

            float[] queryVector;
            try { queryVector = client.Embed(query); }
            catch (EmbeddingsException ex) { note = "Embedding the query failed: " + ex.Message; return null; }

            if (queryVector == null)
            {
                note = "Embedding the query returned no vector.";
                return null;
            }

            List<ChunkHit> hits = index.SearchSemantic(queryVector, MaxResults * 2);
            if (hits.Count == 0)
            {
                note = "No semantic matches.";
                return null;
            }

            StringBuilder sb = new StringBuilder();
            int shown = 0;
            int dropped = 0;
            foreach (ChunkHit hit in hits)
            {
                if (shown >= MaxResults)
                    break;
                if (!File.Exists(hit.Chunk.File))
                {
                    dropped++;
                    continue;
                }
                sb.AppendLine(string.Format("{0}:{1}-{2}  (score {3:F3})",
                    hit.Chunk.File, hit.Chunk.StartLine, hit.Chunk.EndLine, hit.Score));
                string snippet = ReadSnippet(hit.Chunk.File, hit.Chunk.StartLine, hit.Chunk.EndLine);
                if (!string.IsNullOrEmpty(snippet))
                {
                    sb.AppendLine(snippet);
                }
                sb.AppendLine();
                shown++;
            }

            if (shown == 0)
            {
                note = "All semantic matches pointed to files that no longer exist.";
                return null;
            }

            StringBuilder header = new StringBuilder();
            header.AppendLine("Semantic search results for: " + query);
            if (dropped > 0)
                header.AppendLine("(" + dropped + " stale result(s) to missing files were dropped; consider re-indexing.)");
            header.AppendLine();
            return header.ToString() + sb.ToString().TrimEnd();
        }

        // ── Symbol ─────────────────────────────────────────────────────

        /// <summary>Returns formatted results, or null to signal a fallback.</summary>
        private static string TrySymbol(CodebaseIndex index, string query, string carriedNote = null)
        {
            if (index == null || !index.HasIndex)
                return null;

            List<SymbolEntry> hits = index.SearchSymbols(query, MaxResults * 2);
            if (hits.Count == 0)
                return null;

            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(carriedNote))
                sb.AppendLine("[codebase_search] " + carriedNote + " Showing symbol-index results instead.").AppendLine();
            sb.AppendLine("Symbol map search for: " + query);
            sb.AppendLine();

            int shown = 0;
            int dropped = 0;
            foreach (SymbolEntry symbol in hits)
            {
                if (shown >= MaxResults)
                    break;
                if (!File.Exists(symbol.File))
                {
                    dropped++;
                    continue;
                }
                sb.AppendLine(string.Format("{0} {1}  ->  {2}:{3}",
                    symbol.Kind, symbol.Name, index.ToDisplayPath(symbol.File), symbol.Line));
                if (!string.IsNullOrEmpty(symbol.Signature))
                    sb.AppendLine("    " + symbol.Signature);

                AppendCallers(sb, index, symbol);
                shown++;
            }

            if (shown == 0)
                return null;

            if (dropped > 0)
                sb.AppendLine().AppendLine("(" + dropped + " stale result(s) to missing files were dropped; consider re-indexing.)");

            return sb.ToString().TrimEnd();
        }

        private static void AppendCallers(StringBuilder sb, CodebaseIndex index, SymbolEntry symbol)
        {
            if (symbol == null)
                return;

            List<SymbolCaller> callers = index.GetDistinctCallers(symbol, MaxCallersShown);
            if (callers.Count == 0)
                return;

            sb.Append("    called from: ");
            for (int i = 0; i < callers.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                SymbolCaller caller = callers[i];
                sb.Append(index.ToDisplayPath(caller.File));
                sb.Append(':');
                sb.Append(caller.Line);
            }

            int total = symbol.Callers != null ? symbol.Callers.Count : 0;
            if (total > callers.Count)
                sb.Append(" (+").Append(total - callers.Count).Append(" more)");
            sb.AppendLine();
        }

        // ── grep fallback ──────────────────────────────────────────────

        private static string GrepFallback(CodebaseIndex index, string query, string note, ref int exitCode)
        {
            string directory = index != null ? index.WorkspaceRoot : null;
            int grepExit;
            string grepOutput = GrepSearchTool.Search(query, directory, null, "true", out grepExit);
            exitCode = grepExit;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(note);
            sb.AppendLine();
            sb.Append(grepOutput);
            return sb.ToString();
        }

        // ── Snippet ────────────────────────────────────────────────────

        private static string ReadSnippet(string file, int startLine, int endLine)
        {
            try
            {
                string[] lines = File.ReadAllLines(file);
                int from = Math.Max(1, startLine);
                int to = Math.Min(endLine, from + SnippetLines - 1);
                to = Math.Min(to, lines.Length);

                StringBuilder sb = new StringBuilder();
                for (int i = from; i <= to; i++)
                    sb.AppendLine("    " + lines[i - 1]);
                if (endLine > to)
                    sb.AppendLine("    ...");
                return sb.ToString().TrimEnd();
            }
            catch
            {
                return null;
            }
        }
    }
}
