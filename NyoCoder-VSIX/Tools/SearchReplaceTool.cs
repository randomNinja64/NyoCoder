using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvDTE;
using EnvDTE80;

namespace NyoCoder
{
    public static class SearchReplaceTool
    {
        internal struct Block
        {
            public string Search;
            public string Replace;
        }

        public enum ChangeType
        {
            Addition,
            Deletion,
            Modification
        }

        public struct InlineSpan
        {
            public int Start;
            public int Length;
            public ChangeType Type;
        }

        internal sealed class InlinePreview
        {
            public string Content;
            public List<InlineSpan> Spans;

            public InlinePreview()
            {
                Content = string.Empty;
                Spans = new List<InlineSpan>();
            }
        }

        internal sealed class Change
        {
            // Start index in FINAL content (after all blocks applied)
            public int StartIndex;
            // Start index in ORIGINAL content (for pre-apply preview adornments)
            public int OriginalIndex;
            public int OldLength;
            public int NewLength;
            public string OldText;
            public string NewText;
            public ChangeType Type;
        }

        internal sealed class ApplyResult
        {
            public string OriginalContent;
            public string NewContent;
            public List<Block> Blocks;
            public List<Change> Changes;
            public List<string> Errors;
            public string PreviewDiff;
            public string NormalizedFilePath;

            public ApplyResult()
            {
                Blocks = new List<Block>();
                Changes = new List<Change>();
                Errors = new List<string>();
                PreviewDiff = string.Empty;
                NormalizedFilePath = string.Empty;
            }
        }

        internal static ApplyResult Preview(string filePath, List<Block> blocks)
        {
            ApplyResult res = new ApplyResult();

            string expandedPath = EditorService.NormalizeFilePath(filePath);
            if (string.IsNullOrEmpty(expandedPath))
            {
                res.Errors.Add("File path cannot be empty");
                return res;
            }

            if (!File.Exists(expandedPath))
            {
                res.Errors.Add("File does not exist: " + expandedPath);
                return res;
            }

            if (blocks == null || blocks.Count == 0)
            {
                res.Errors.Add("No edits provided. Provide at least one edit with old_string and new_string.");
                return res;
            }

            res.NormalizedFilePath = expandedPath;
            res.Blocks = blocks;

            // Open the file in the editor first so the user sees the changes
            EditorService.TryOpenFileInVisualStudio(expandedPath);

            // Prefer editing an open document buffer (so VS updates live)
            string original = null;
            EditorService.TryReadOpenDocument(expandedPath, out original);

            if (original == null)
            {
                original = File.ReadAllText(expandedPath, Encoding.UTF8);
            }

            // Normalize line endings for consistent matching
            original = TextNormalization.NormalizeLineEndings(original);

            res.OriginalContent = original;

            ApplyBlocksInMemory(res);

            // Build a preview diff even if we fail (helps debugging)
            res.PreviewDiff = BuildUnifiedDiff(res.OriginalContent ?? string.Empty, res.NewContent ?? string.Empty, 200);

            // Scroll to the first change so the user can see where the diff is
            if (res.Changes.Count > 0)
            {
                int firstChangeOffset = res.Changes[0].OriginalIndex;
                EditorService.TryScrollToOffset(expandedPath, res.OriginalContent, firstChangeOffset);
            }

            return res;
        }

        internal static InlinePreview BuildInlinePreview(ApplyResult res)
        {
            InlinePreview preview = new InlinePreview();

            if (res == null || res.Errors.Count > 0)
            {
                preview.Content = res != null ? (res.OriginalContent ?? string.Empty) : string.Empty;
                return preview;
            }

            string current = res.OriginalContent ?? string.Empty;
            List<InlineSpan> spans = new List<InlineSpan>();

            // Build inline preview using line-level LCS diff so that unchanged lines
            // are kept without highlighting — only deleted / inserted lines are marked.
            //
            // Process from the end so indices remain stable.
            List<Change> changes = new List<Change>(res.Changes);
            changes.Sort((a, b) => b.OriginalIndex.CompareTo(a.OriginalIndex));

            for (int i = 0; i < changes.Count; i++)
            {
                Change c = changes[i];

                int start = c.OriginalIndex;
                if (start < 0) start = 0;
                if (start > current.Length) start = current.Length;

                int oldLen = Math.Max(0, c.OldLength);
                if (start + oldLen > current.Length) oldLen = current.Length - start;

                // old_string/new_string often start mid-line (e.g. after leading
                // indentation that wasn't part of the match). The interleaved
                // preview below joins the old and new text with a synthetic '\n',
                // so without the same-line prefix the "new" side would render
                // flush-left instead of lining up under the "old" side. Pull that
                // shared prefix in on both sides so they render consistently; this
                // is a preview-only concern — the real applied content never goes
                // through this synthetic line join.
                int lineStart = start;
                if (start > 0)
                {
                    int newlineIndex = current.LastIndexOf('\n', start - 1);
                    lineStart = newlineIndex < 0 ? 0 : newlineIndex + 1;
                }
                string linePrefix = current.Substring(lineStart, start - lineStart);

                string oldText = linePrefix + (c.OldText ?? string.Empty);
                string newText = linePrefix + (c.NewText ?? string.Empty);

                // Compute interleaved line-level diff
                string interleaved;
                List<InlineSpan> localSpans;
                BuildInterleavedDiff(oldText, newText, out interleaved, out localSpans);

                // Replace the old text region (extended back to the line start) with the interleaved content
                int regionLength = (start - lineStart) + oldLen;
                current = current.Substring(0, lineStart) + interleaved + current.Substring(start + oldLen);

                // Shift existing spans that sit after this region
                int delta = interleaved.Length - regionLength;
                if (delta != 0)
                {
                    for (int j = 0; j < spans.Count; j++)
                    {
                        InlineSpan sp = spans[j];
                        if (sp.Start >= start + oldLen)
                        {
                            sp.Start += delta;
                            spans[j] = sp;
                        }
                    }
                }

                // Offset local spans to document position and add
                foreach (InlineSpan ls in localSpans)
                {
                    spans.Add(new InlineSpan
                    {
                        Start = lineStart + ls.Start,
                        Length = ls.Length,
                        Type = ls.Type
                    });
                }
            }

            preview.Content = current;
            preview.Spans = spans;
            return preview;
        }

        // ---- Line-level diff helpers ----------------------------------------

        private enum DiffOpType { Equal, Delete, Insert }

        private struct DiffOp
        {
            public DiffOpType Type;
            public string Text;
        }

        /// <summary>
        /// Builds an interleaved diff string from oldText and newText using LCS-based
        /// line diffing. Equal lines appear once (unhighlighted), deleted lines are
        /// marked as Deletion spans, inserted lines as Addition spans.
        /// Falls back to block-level diff for very large inputs.
        /// </summary>
        private static void BuildInterleavedDiff(string oldText, string newText,
            out string interleaved, out List<InlineSpan> spans)
        {
            spans = new List<InlineSpan>();

            if (string.IsNullOrEmpty(oldText) && string.IsNullOrEmpty(newText))
            {
                interleaved = string.Empty;
                return;
            }
            if (string.IsNullOrEmpty(oldText))
            {
                interleaved = newText;
                spans.Add(new InlineSpan { Start = 0, Length = newText.Length, Type = ChangeType.Addition });
                return;
            }
            if (string.IsNullOrEmpty(newText))
            {
                interleaved = oldText;
                spans.Add(new InlineSpan { Start = 0, Length = oldText.Length, Type = ChangeType.Deletion });
                return;
            }

            string[] oldLines = oldText.Split('\n');
            string[] newLines = newText.Split('\n');

            // Guard against very large inputs — fall back to block-level diff
            const int MaxLines = 1000;
            if (oldLines.Length > MaxLines || newLines.Length > MaxLines)
            {
                StringBuilder fb = new StringBuilder();
                fb.Append(oldText);
                fb.Append('\n');
                fb.Append(newText);
                interleaved = fb.ToString();
                spans.Add(new InlineSpan { Start = 0, Length = oldText.Length, Type = ChangeType.Deletion });
                spans.Add(new InlineSpan { Start = oldText.Length + 1, Length = newText.Length, Type = ChangeType.Addition });
                return;
            }

            List<DiffOp> ops = ComputeLineDiff(oldLines, newLines);

            StringBuilder sb = new StringBuilder();
            bool first = true;

            foreach (DiffOp op in ops)
            {
                if (!first) sb.Append('\n');
                first = false;

                int lineStart = sb.Length;
                sb.Append(op.Text);

                if (op.Type == DiffOpType.Delete)
                    spans.Add(new InlineSpan { Start = lineStart, Length = op.Text.Length, Type = ChangeType.Deletion });
                else if (op.Type == DiffOpType.Insert)
                    spans.Add(new InlineSpan { Start = lineStart, Length = op.Text.Length, Type = ChangeType.Addition });
            }

            interleaved = sb.ToString();
        }

        /// <summary>
        /// LCS-based line diff producing Equal / Delete / Insert operations.
        /// Deletions are ordered before insertions within each hunk.
        /// </summary>
        private static List<DiffOp> ComputeLineDiff(string[] oldLines, string[] newLines)
        {
            int m = oldLines.Length;
            int n = newLines.Length;

            // Build LCS table
            int[,] dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (string.Equals(oldLines[i - 1], newLines[j - 1], StringComparison.Ordinal))
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }

            // Backtrack — prefer consuming from new first (Insert) when tied,
            // so that after reversal deletes precede inserts within each hunk.
            List<DiffOp> ops = new List<DiffOp>();
            int ii = m, jj = n;
            while (ii > 0 || jj > 0)
            {
                if (ii > 0 && jj > 0 && string.Equals(oldLines[ii - 1], newLines[jj - 1], StringComparison.Ordinal))
                {
                    ops.Add(new DiffOp { Type = DiffOpType.Equal, Text = oldLines[ii - 1] });
                    ii--; jj--;
                }
                else if (jj > 0 && (ii == 0 || dp[ii, jj - 1] >= dp[ii - 1, jj]))
                {
                    ops.Add(new DiffOp { Type = DiffOpType.Insert, Text = newLines[jj - 1] });
                    jj--;
                }
                else
                {
                    ops.Add(new DiffOp { Type = DiffOpType.Delete, Text = oldLines[ii - 1] });
                    ii--;
                }
            }

            ops.Reverse();
            return ops;
        }

        internal static bool ApplyPreview(ApplyResult res)
        {
            if (res == null) return false;
            if (res.Errors != null && res.Errors.Count > 0) return false;
            if (string.IsNullOrEmpty(res.NormalizedFilePath)) return false;
            if (string.Equals(res.NewContent, res.OriginalContent, StringComparison.Ordinal)) return true;

            // Apply to open document if it is open; otherwise write to disk
            bool appliedToOpen = EditorService.TrySetOpenDocumentContent(res.NormalizedFilePath, res.NewContent, true);
            if (!appliedToOpen)
            {
                File.WriteAllText(res.NormalizedFilePath, res.NewContent, Encoding.UTF8);
            }
            return true;
        }

        private static void ApplyBlocksInMemory(ApplyResult res)
        {
            string current = res.OriginalContent ?? string.Empty;
            string originalBase = current;
            List<Change> recorded = new List<Change>();
            int cumulativeDeltaForMonotonicMap = 0;
            int lastFoundIndex = -1;

            for (int i = 0; i < res.Blocks.Count; i++)
            {
                Block block = res.Blocks[i];
                int blockNum = i + 1;

                int occurrences = CountOccurrences(current, block.Search);
                if (occurrences == 0)
                {
                    res.Errors.Add(BuildNotFoundError(current, block.Search, blockNum));
                    continue;
                }

                // Enforce the "exactly once" rule (this also makes highlighting reliable)
                if (occurrences != 1)
                {
                    res.Errors.Add(
                        "Edit " + blockNum + " failed: old_string appears " + occurrences + " times.\n" +
                        "old_string must match EXACTLY once. Add more surrounding context to make it unique.");
                    continue;
                }

                int index = current.IndexOf(block.Search, StringComparison.Ordinal);

                ChangeType type = DetermineType(block.Search, block.Replace);

                int originalIndex = FindUniqueIndex(originalBase, block.Search);
                if (originalIndex < 0)
                {
                    // Fallback: approximate mapping for content that only exists after earlier replacements.
                    originalIndex = index - cumulativeDeltaForMonotonicMap;
                    if (originalIndex < 0) originalIndex = 0;
                    if (originalIndex > originalBase.Length) originalIndex = originalBase.Length;
                }

                Change change = new Change
                {
                    // We'll adjust StartIndex for later edits using delta shifting
                    StartIndex = index,
                    OriginalIndex = originalIndex,
                    OldLength = block.Search.Length,
                    NewLength = block.Replace.Length,
                    OldText = block.Search,
                    NewText = block.Replace,
                    Type = type
                };

                // Replace in current content
                current = current.Substring(0, index) + block.Replace + current.Substring(index + block.Search.Length);

                // Shift previously recorded changes if this replacement happened before them
                int delta = block.Replace.Length - block.Search.Length;
                if (delta != 0)
                {
                    for (int r = 0; r < recorded.Count; r++)
                    {
                        if (recorded[r].StartIndex > index)
                        {
                            recorded[r].StartIndex += delta;
                        }
                    }
                }

                recorded.Add(change);

                // Track monotonic delta mapping best-effort (helps later OriginalIndex approximations)
                if (index >= lastFoundIndex)
                {
                    cumulativeDeltaForMonotonicMap += delta;
                    lastFoundIndex = index;
                }
            }

            res.NewContent = current;
            res.Changes = recorded;
        }

        private static int FindUniqueIndex(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return -1;

            int first = haystack.IndexOf(needle, StringComparison.Ordinal);
            if (first < 0) return -1;

            int second = haystack.IndexOf(needle, first + needle.Length, StringComparison.Ordinal);
            if (second >= 0) return -1; // not unique

            return first;
        }

        private static ChangeType DetermineType(string oldText, string newText)
        {
            if (string.IsNullOrEmpty(oldText) && !string.IsNullOrEmpty(newText)) return ChangeType.Addition;
            if (!string.IsNullOrEmpty(oldText) && string.IsNullOrEmpty(newText)) return ChangeType.Deletion;
            return ChangeType.Modification;
        }

        /// <summary>
        /// Converts the "edits" tool argument (an array of { old_string, new_string }
        /// objects) into Blocks. Malformed entries (not an object) are skipped.
        /// </summary>
        internal static List<Block> ParseEdits(JArray edits)
        {
            List<Block> blocks = new List<Block>();
            if (edits == null) return blocks;

            foreach (JToken token in edits)
            {
                JObject obj = token as JObject;
                if (obj == null) continue;

                string search = (string)obj["old_string"] ?? string.Empty;
                string replace = (string)obj["new_string"] ?? string.Empty;

                blocks.Add(new Block
                {
                    Search = TextNormalization.NormalizeLineEndings(search),
                    Replace = TextNormalization.NormalizeLineEndings(replace)
                });
            }

            return blocks;
        }

        private static int CountOccurrences(string text, string search)
        {
            if (string.IsNullOrEmpty(search)) return 0;

            int count = 0;
            int index = 0;
            while (true)
            {
                index = text.IndexOf(search, index, StringComparison.Ordinal);
                if (index < 0) break;
                count++;
                index += search.Length;
            }
            return count;
        }

        private static string BuildNotFoundError(string currentContent, string searchText, int blockNum)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Edit " + blockNum + " failed: old_string not found.");
            sb.AppendLine("old_string was:");
            sb.AppendLine(searchText);
            sb.AppendLine();
            sb.AppendLine("old_string must match EXACTLY, including whitespace, indentation, and line endings.");
            return sb.ToString();
        }

        internal static string BuildUnifiedDiff(string oldText, string newText, int maxLines)
        {
            // Simple line-based diff (good enough for a preview)
            string[] a = TextNormalization.NormalizeLineEndings(oldText ?? string.Empty).Split('\n');
            string[] b = TextNormalization.NormalizeLineEndings(newText ?? string.Empty).Split('\n');

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("--- original");
            sb.AppendLine("+++ new");

            int i = 0;
            int j = 0;
            int linesOut = 0;

            while (i < a.Length || j < b.Length)
            {
                if (linesOut >= maxLines)
                {
                    sb.AppendLine("...(diff truncated)");
                    break;
                }

                string la = i < a.Length ? a[i] : null;
                string lb = j < b.Length ? b[j] : null;

                if (la == lb)
                {
                    if (la != null)
                    {
                        sb.AppendLine(" " + la);
                        linesOut++;
                    }
                    i++;
                    j++;
                    continue;
                }

                if (la != null)
                {
                    sb.AppendLine("-" + la);
                    linesOut++;
                    i++;
                }

                if (lb != null)
                {
                    sb.AppendLine("+" + lb);
                    linesOut++;
                    j++;
                }
            }

            return sb.ToString();
        }
    }
}
