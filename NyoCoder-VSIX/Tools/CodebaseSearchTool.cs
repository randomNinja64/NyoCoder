using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NyoCoder
{
    /// <summary>
    /// Implements the codebase_search tool. Prefers the configured indexing mode, then the
    /// other indexed backend, then falls back to grep_search with an explanatory note.
    /// </summary>
    internal static class CodebaseSearchTool
    {
        private const int MaxResults = 10;
        private const int MaxCallersShown = 6;
        private const int SnippetLines = 6;
        private const string StaleNote = "({0} stale result(s) to missing files were dropped; consider re-indexing.)";

        /// <summary>Result of an index search (semantic or symbol). Null fields mean no hits.</summary>
        internal sealed class IndexedHitSet
        {
            public string FormattedText;
            public List<string> FilePaths;
        }

        public static string Search(string query, out int exitCode)
        {
            exitCode = 0;
            if (string.IsNullOrWhiteSpace(query))
            {
                exitCode = 1;
                return "Error: 'query' is required.";
            }

            IndexingMode mode = ConfigHandler.GetIndexingMode();
            CodebaseIndex index = TryGetIndex();
            bool hasIndex = index != null && index.HasIndex;
            IndexedHitSet hits = null;
            string note;

            if (mode != IndexingMode.Off && hasIndex)
            {
                if (mode == IndexingMode.Semantic)
                {
                    hits = SearchSemantic(index, query, MaxResults, null, out note);
                    if (hits == null)
                        hits = WithBanner(SearchSymbols(index, query, MaxResults, null),
                            string.IsNullOrEmpty(note) ? null
                                : "[codebase_search] " + note + " Showing symbol-index results instead."
                                    + Environment.NewLine + Environment.NewLine);
                }
                else
                {
                    hits = SearchSymbols(index, query, MaxResults, null);
                    if (hits == null)
                        hits = WithBanner(SearchSemantic(index, query, MaxResults, null, out note),
                            "[codebase_search] No symbol-index matches. Showing semantic results instead.\n\n");
                }
            }

            if (hits != null && !string.IsNullOrWhiteSpace(hits.FormattedText))
                return hits.FormattedText;

            string reason = mode == IndexingMode.Off ? "Indexing is Off"
                : (!hasIndex ? "No usable index" : "No index matches");
            string directory = index != null ? index.WorkspaceRoot : null;
            string grepOutput = GrepSearchTool.Search(query, directory, null, "true", out exitCode);
            return "[codebase_search] " + reason + "; fell back to grep_search."
                + Environment.NewLine + Environment.NewLine + grepOutput;
        }

        /// <summary>Embeddings / vector search against the semantic index. Returns null on failure.</summary>
        public static IndexedHitSet SearchSemantic(CodebaseIndex index, string query, int maxResults,
            string excludeFilePath, out string note)
        {
            note = null;
            if (index == null) { note = "No index available."; return null; }

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
            if (queryVector == null) { note = "Embedding the query returned no vector."; return null; }

            List<ChunkHit> hits = index.SearchSemantic(queryVector, maxResults);
            if (hits.Count == 0) { note = "No semantic matches."; return null; }

            var sb = new StringBuilder();
            var files = new List<string>();
            int shown = 0, dropped = 0;
            foreach (ChunkHit hit in hits)
            {
                if (shown >= maxResults) break;
                string file = hit.Chunk.File;
                if (!File.Exists(file)) { dropped++; continue; }
                if (IsExcluded(file, excludeFilePath)) continue;

                sb.AppendLine(string.Format("{0}:{1}-{2}  (score {3:F3})",
                    file, hit.Chunk.StartLine, hit.Chunk.EndLine, hit.Score));
                string snippet = ReadSnippet(file, hit.Chunk.StartLine, hit.Chunk.EndLine);
                if (!string.IsNullOrEmpty(snippet))
                    sb.AppendLine(snippet);
                sb.AppendLine();
                AddUniquePath(files, file);
                shown++;
            }

            if (shown == 0) { note = "No semantic matches."; return null; }

            var header = new StringBuilder();
            header.AppendLine("Semantic search results for: " + query);
            if (dropped > 0)
                header.AppendLine(string.Format(StaleNote, dropped));
            header.AppendLine();

            return new IndexedHitSet
            {
                FormattedText = header.ToString() + sb.ToString().TrimEnd(),
                FilePaths = files
            };
        }

        /// <summary>Symbol-map search against the index. Returns null when there are no usable hits.</summary>
        public static IndexedHitSet SearchSymbols(CodebaseIndex index, string query, int maxResults,
            string excludeFilePath)
        {
            if (index == null || !index.HasIndex)
                return null;

            List<SymbolEntry> hits = index.SearchSymbols(query, maxResults);
            if (hits.Count == 0)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("Symbol map search for: " + query);
            sb.AppendLine();

            var files = new List<string>();
            int shown = 0, dropped = 0;
            foreach (SymbolEntry symbol in hits)
            {
                if (shown >= maxResults) break;
                if (!File.Exists(symbol.File)) { dropped++; continue; }
                if (IsExcluded(symbol.File, excludeFilePath)) continue;

                sb.AppendLine(string.Format("{0} {1}  ->  {2}:{3}",
                    symbol.Kind, symbol.Name, index.ToDisplayPath(symbol.File), symbol.Line));
                if (!string.IsNullOrEmpty(symbol.Signature))
                    sb.AppendLine("    " + symbol.Signature);
                AppendCallers(sb, index, symbol);
                AddUniquePath(files, symbol.File);
                shown++;
            }

            if (shown == 0)
                return null;
            if (dropped > 0)
                sb.AppendLine().AppendLine(string.Format(StaleNote, dropped));

            return new IndexedHitSet { FormattedText = sb.ToString().TrimEnd(), FilePaths = files };
        }

        private static IndexedHitSet WithBanner(IndexedHitSet hits, string banner)
        {
            if (hits == null || string.IsNullOrEmpty(banner))
                return hits;
            hits.FormattedText = banner + hits.FormattedText;
            return hits;
        }

        private static CodebaseIndex TryGetIndex()
        {
            try { return CodebaseIndex.GetCurrent(); }
            catch { return null; }
        }

        private static void AppendCallers(StringBuilder sb, CodebaseIndex index, SymbolEntry symbol)
        {
            List<SymbolCaller> callers = index.GetDistinctCallers(symbol, MaxCallersShown);
            if (callers.Count == 0)
                return;

            sb.Append("    called from: ");
            for (int i = 0; i < callers.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(index.ToDisplayPath(callers[i].File)).Append(':').Append(callers[i].Line);
            }

            int total = symbol.Callers != null ? symbol.Callers.Count : 0;
            if (total > callers.Count)
                sb.Append(" (+").Append(total - callers.Count).Append(" more)");
            sb.AppendLine();
        }

        private static bool IsExcluded(string filePath, string excludeFilePath)
        {
            return !string.IsNullOrEmpty(excludeFilePath)
                && !string.IsNullOrEmpty(filePath)
                && string.Equals(filePath, excludeFilePath, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddUniquePath(List<string> files, string path)
        {
            if (!string.IsNullOrEmpty(path)
                && !files.Exists(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                files.Add(path);
        }

        private static string ReadSnippet(string file, int startLine, int endLine)
        {
            try
            {
                string[] lines = File.ReadAllLines(file);
                int from = Math.Max(1, startLine);
                int to = Math.Min(Math.Min(endLine, from + SnippetLines - 1), lines.Length);

                var sb = new StringBuilder();
                for (int i = from; i <= to; i++)
                    sb.AppendLine("    " + lines[i - 1]);
                if (endLine > to)
                    sb.AppendLine("    ...");
                return sb.ToString().TrimEnd();
            }
            catch { return null; }
        }
    }
}
