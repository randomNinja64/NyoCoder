using System;
using System.Collections.Generic;
using System.IO;

namespace NyoCoder
{
    /// <summary>
    /// Opt-in automatic retrieval of indexed snippets for new sessions and plan steps.
    /// Prefers the configured indexing mode, then the other indexed backend — no grep.
    /// </summary>
    internal static class AutoRagContext
    {
        private const int MaxResults = 5;

        internal enum Status
        {
            /// <summary>Feature disabled or query unusable — no UI, no prompt injection.</summary>
            Skipped,
            /// <summary>Enabled but indexing off / no usable index — notify user, no prompt injection.</summary>
            NoIndex,
            /// <summary>Index search ran but nothing useful to inject — silent omit.</summary>
            NoHits,
            /// <summary>Hits ready for LLM + user "reading …" line.</summary>
            Success
        }

        internal sealed class Result
        {
            public Status Outcome;
            public string PromptBlock;
            public string UserStatusLine;
        }

        /// <summary>
        /// Attempts Auto-RAG for <paramref name="query"/>.
        /// Never throws; returns a result describing what (if anything) to show/inject.
        /// </summary>
        public static Result TryRetrieve(string query)
        {
            var result = new Result { Outcome = Status.Skipped };

            if (!ConfigHandler.GetAutoRagEnabled())
                return result;

            if (string.IsNullOrWhiteSpace(query))
                return result;

            IndexingMode mode = ConfigHandler.GetIndexingMode();
            if (mode == IndexingMode.Off)
            {
                result.Outcome = Status.NoIndex;
                result.UserStatusLine = "[Auto-RAG failed: no codebase index]";
                return result;
            }

            CodebaseIndex index;
            try { index = CodebaseIndex.GetCurrent(); }
            catch { index = null; }

            if (index == null || !index.HasIndex)
            {
                result.Outcome = Status.NoIndex;
                result.UserStatusLine = "[Auto-RAG failed: no codebase index]";
                return result;
            }

            string activePath = null;
            try
            {
                EnvDTE80.DTE2 dte = EditorService.GetDte();
                if (dte != null && dte.ActiveDocument != null
                    && !string.IsNullOrWhiteSpace(dte.ActiveDocument.FullName))
                    activePath = dte.ActiveDocument.FullName;
            }
            catch { }

            CodebaseSearchTool.IndexedHitSet hits;
            try
            {
                hits = SearchPreferringMode(index, query.Trim(), mode, activePath);
            }
            catch
            {
                result.Outcome = Status.NoHits;
                return result;
            }

            if (hits == null || string.IsNullOrWhiteSpace(hits.FormattedText)
                || hits.FilePaths == null || hits.FilePaths.Count == 0)
            {
                result.Outcome = Status.NoHits;
                return result;
            }

            result.Outcome = Status.Success;
            result.PromptBlock = "---Retrieved Context---\n"
                + hits.FormattedText.TrimEnd()
                + "\n---End Retrieved Context---";
            result.UserStatusLine = "[reading " + FormatDisplayNames(hits.FilePaths) + "]";
            return result;
        }

        /// <summary>
        /// Prefers the configured mode, then the other indexed backend. No grep fallback.
        /// </summary>
        private static CodebaseSearchTool.IndexedHitSet SearchPreferringMode(
            CodebaseIndex index, string query, IndexingMode mode, string excludeFilePath)
        {
            string note;
            if (mode == IndexingMode.Semantic)
            {
                CodebaseSearchTool.IndexedHitSet semantic =
                    CodebaseSearchTool.SearchSemantic(index, query, MaxResults, excludeFilePath, out note);
                if (semantic != null)
                    return semantic;
                return CodebaseSearchTool.SearchSymbols(index, query, MaxResults, excludeFilePath);
            }

            // Symbol (default) or any non-Semantic indexed mode: prefer symbols, then embeddings.
            CodebaseSearchTool.IndexedHitSet symbols =
                CodebaseSearchTool.SearchSymbols(index, query, MaxResults, excludeFilePath);
            if (symbols != null)
                return symbols;
            return CodebaseSearchTool.SearchSemantic(index, query, MaxResults, excludeFilePath, out note);
        }

        /// <summary>
        /// Inserts a retrieved-context block into a prompt that may already contain editor context.
        /// If <paramref name="retrievedBlock"/> is null/empty, returns <paramref name="basePrompt"/> unchanged.
        /// </summary>
        public static string MergeIntoPrompt(string basePrompt, string retrievedBlock)
        {
            if (string.IsNullOrWhiteSpace(retrievedBlock))
                return basePrompt ?? string.Empty;

            if (string.IsNullOrWhiteSpace(basePrompt))
                return retrievedBlock;

            // Prefer: editor context, then retrieved, then --- separator before user/step body
            const string separator = "\n\n---\n\n";
            int sep = basePrompt.IndexOf(separator, StringComparison.Ordinal);
            if (sep >= 0)
            {
                string before = basePrompt.Substring(0, sep);
                string after = basePrompt.Substring(sep); // includes separator
                return before + "\n\n" + retrievedBlock + after;
            }

            return retrievedBlock + separator + basePrompt;
        }

        private static string FormatDisplayNames(List<string> filePaths)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in filePaths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name))
                    name = path;
                if (seen.Add(name))
                    names.Add(name);
            }
            return string.Join(", ", names.ToArray());
        }
    }
}
