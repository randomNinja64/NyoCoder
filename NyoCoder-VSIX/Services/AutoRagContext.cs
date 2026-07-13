using System;
using System.Collections.Generic;
using System.IO;

namespace NyoCoder
{
    /// <summary>
    /// Opt-in automatic retrieval of semantic (embeddings) snippets for new sessions and plan steps.
    /// Calls <see cref="CodebaseSearchTool.SearchSemantic"/> only — no symbol or grep fallback.
    /// </summary>
    internal static class AutoRagContext
    {
        private const int MaxResults = 5;

        internal enum Status
        {
            /// <summary>Feature disabled or query unusable — no UI, no prompt injection.</summary>
            Skipped,
            /// <summary>Enabled but no semantic index — notify user, no prompt injection.</summary>
            NoIndex,
            /// <summary>Semantic search ran but nothing useful to inject — silent omit.</summary>
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

            if (ConfigHandler.GetIndexingMode() != IndexingMode.Semantic)
            {
                result.Outcome = Status.NoIndex;
                result.UserStatusLine = "[Auto-RAG failed: no codebase index]";
                return result;
            }

            CodebaseIndex index;
            try { index = CodebaseIndex.GetCurrent(); }
            catch { index = null; }

            if (index == null || !index.HasIndex || index.Vectors == null || index.Vectors.Count == 0)
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
                string unusedNote;
                hits = CodebaseSearchTool.SearchSemantic(index, query.Trim(), MaxResults, activePath, out unusedNote);
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
