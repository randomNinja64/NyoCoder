using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace NyoCoder
{
    /// <summary>
    /// Second-pass, text-based reference extraction: scans source lines for identifier tokens
    /// and links them to indexed symbol definitions. Heuristic by design (no Roslyn).
    /// </summary>
    internal static class SymbolReferenceExtractor
    {
        /// <summary>Only link callers for names at least this long (drops Get, Run, etc.).</summary>
        private const int MinNameLength = 5;

        /// <summary>Skip names with more than this many definitions repo-wide (too ambiguous).</summary>
        private const int MaxDefsPerName = 10;

        /// <summary>Max caller sites stored per definition (index-time cap; search shows fewer).</summary>
        internal const int MaxStoredCallersPerSymbol = 25;

        private static readonly Regex IdentifierRegex =
            new Regex(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled);

        internal sealed class SymbolIndexTables
        {
            public Dictionary<string, List<SymbolEntry>> ByName;
            public HashSet<string> SkippedNames;
        }

        internal static SymbolIndexTables BuildTables(List<SymbolEntry> symbols)
        {
            Dictionary<string, List<SymbolEntry>> byName =
                new Dictionary<string, List<SymbolEntry>>(StringComparer.OrdinalIgnoreCase);

            if (symbols != null)
            {
                foreach (SymbolEntry symbol in symbols)
                {
                    if (symbol == null || string.IsNullOrEmpty(symbol.Name))
                        continue;

                    List<SymbolEntry> bucket;
                    if (!byName.TryGetValue(symbol.Name, out bucket))
                    {
                        bucket = new List<SymbolEntry>();
                        byName[symbol.Name] = bucket;
                    }
                    bucket.Add(symbol);
                }
            }

            HashSet<string> skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<SymbolEntry>> pair in byName)
            {
                if (pair.Key.Length < MinNameLength)
                    skipped.Add(pair.Key);
                else if (pair.Value.Count > MaxDefsPerName)
                    skipped.Add(pair.Key);
            }

            return new SymbolIndexTables { ByName = byName, SkippedNames = skipped };
        }

        /// <summary>
        /// Scans one file for identifier uses and appends caller sites onto matching symbols.
        /// <paramref name="refCounts"/> tracks per-definition caps across multiple files.
        /// </summary>
        internal static void ScanFile(
            string referencerFile,
            string content,
            SymbolIndexTables tables,
            Dictionary<string, int> refCounts)
        {
            if (tables == null || tables.ByName == null || tables.ByName.Count == 0
                || string.IsNullOrEmpty(referencerFile) || string.IsNullOrEmpty(content))
                return;

            if (refCounts == null)
                refCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            HashSet<string> seenOnLine = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;

                string trimmed = line.TrimStart();
                if (trimmed.Length == 0 || SymbolExtractor.IsCommentLine(trimmed))
                    continue;

                seenOnLine.Clear();
                int lineNo = i + 1;

                foreach (Match match in IdentifierRegex.Matches(line))
                {
                    string name = match.Value;
                    if (string.IsNullOrEmpty(name) || tables.SkippedNames.Contains(name))
                        continue;
                    if (!seenOnLine.Add(name))
                        continue;

                    List<SymbolEntry> candidates;
                    if (!tables.ByName.TryGetValue(name, out candidates) || candidates == null || candidates.Count == 0)
                        continue;

                    SymbolEntry definition = ResolveDefinition(name, referencerFile, lineNo, candidates);
                    if (definition == null || string.IsNullOrEmpty(definition.File))
                        continue;

                    if (IsDefinitionSite(definition, referencerFile, lineNo))
                        continue;

                    string defKey = definition.DefSiteKey();
                    int count;
                    refCounts.TryGetValue(defKey, out count);
                    if (count >= MaxStoredCallersPerSymbol)
                        continue;

                    if (definition.Callers == null)
                        definition.Callers = new List<SymbolCaller>();
                    definition.Callers.Add(new SymbolCaller(referencerFile, lineNo));
                    refCounts[defKey] = count + 1;
                }
            }
        }

        private static bool IsDefinitionSite(SymbolEntry definition, string file, int line)
        {
            return definition != null
                && string.Equals(definition.File, file, StringComparison.OrdinalIgnoreCase)
                && definition.Line == line;
        }

        private static SymbolEntry ResolveDefinition(
            string name,
            string referencerFile,
            int referencerLine,
            List<SymbolEntry> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return null;
            if (candidates.Count == 1)
                return candidates[0];

            SymbolEntry bestSameFile = null;
            int bestLine = -1;
            foreach (SymbolEntry candidate in candidates)
            {
                if (!string.Equals(candidate.File, referencerFile, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (candidate.Line <= referencerLine && candidate.Line > bestLine)
                {
                    bestLine = candidate.Line;
                    bestSameFile = candidate;
                }
            }
            if (bestSameFile != null)
                return bestSameFile;

            string referencerDir = SafeDir(referencerFile);
            List<SymbolEntry> sameDir = new List<SymbolEntry>();
            foreach (SymbolEntry candidate in candidates)
            {
                if (string.Equals(SafeDir(candidate.File), referencerDir, StringComparison.OrdinalIgnoreCase))
                    sameDir.Add(candidate);
            }
            if (sameDir.Count == 1)
                return sameDir[0];
            if (sameDir.Count > 1)
                return PickShortestPath(sameDir);

            return PickShortestPath(candidates);
        }

        private static SymbolEntry PickShortestPath(List<SymbolEntry> candidates)
        {
            SymbolEntry best = candidates[0];
            int bestLen = best.File != null ? best.File.Length : int.MaxValue;
            for (int i = 1; i < candidates.Count; i++)
            {
                SymbolEntry candidate = candidates[i];
                int len = candidate.File != null ? candidate.File.Length : int.MaxValue;
                if (len < bestLen)
                {
                    bestLen = len;
                    best = candidate;
                }
            }
            return best;
        }

        private static string SafeDir(string file)
        {
            try { return Path.GetDirectoryName(file); }
            catch { return null; }
        }
    }
}
