using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NyoCoder
{
    /// <summary>Per-file bookkeeping stored in the manifest for incremental re-indexing.
    /// <see cref="Mtime"/> is used to skip disk reads on reconcile; <see cref="Hash"/>
    /// confirms content when a file is actually read.</summary>
    public class IndexFileEntry
    {
        public string Hash;
        public long Mtime;
        public int SymbolCount;
        public int ChunkCount;
    }

    /// <summary>Index metadata persisted as manifest.json.</summary>
    public class IndexManifest
    {
        public string SolutionKey;
        public string EmbeddingsModel;
        public int Dimension;
        public bool SemanticPresent;
        public DateTime LastIndexedUtc;
        public Dictionary<string, IndexFileEntry> Files =
            new Dictionary<string, IndexFileEntry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads/holds a solution's persisted index (manifest + symbol map + vectors) and provides
    /// symbol and semantic search plus a status snapshot for the UI. Also owns resolution and
    /// caching of the "current" index for the active solution.
    /// </summary>
    public class CodebaseIndex
    {
        public string WorkspaceRoot { get; private set; }
        public string SolutionKey { get; private set; }
        public string RootDir { get; private set; }
        public IndexManifest Manifest { get; private set; }
        public List<SymbolEntry> Symbols { get; private set; }
        internal VectorStore Vectors { get; private set; }

        private CodebaseIndex()
        {
            Manifest = new IndexManifest();
            Symbols = new List<SymbolEntry>();
            Vectors = new VectorStore();
        }

        // ── Paths ──────────────────────────────────────────────────────

        public string ManifestPath { get { return Path.Combine(RootDir, "manifest.json"); } }
        public string SymbolsPath { get { return Path.Combine(RootDir, "symbols.json"); } }
        public string VectorsPath { get { return Path.Combine(RootDir, "vectors.bin"); } }

        public static string GetIndexRootDir(string solutionKey)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "NyoCoder", "index", HashMd5Hex(solutionKey, lowercase: true));
        }

        /// <summary>MD5 hex digest for fingerprints (content) or stable keys (paths).</summary>
        internal static string HashMd5Hex(string value, bool lowercase)
        {
            string text = value ?? string.Empty;
            if (lowercase)
                text = text.ToLowerInvariant();
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public string SolutionName
        {
            get
            {
                if (!string.IsNullOrEmpty(SolutionKey) &&
                    SolutionKey.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileNameWithoutExtension(SolutionKey);
                if (!string.IsNullOrEmpty(WorkspaceRoot))
                    return new DirectoryInfo(WorkspaceRoot).Name;
                return "(no solution)";
            }
        }

        // ── Load / Save ────────────────────────────────────────────────

        /// <summary>Creates an in-memory index bound to a workspace, loading persisted data if present.</summary>
        public static CodebaseIndex LoadFor(string workspaceRoot, string solutionKey)
        {
            CodebaseIndex index = new CodebaseIndex
            {
                WorkspaceRoot = workspaceRoot,
                SolutionKey = solutionKey,
                RootDir = GetIndexRootDir(solutionKey)
            };
            index.LoadFromDisk();
            return index;
        }

        private void LoadFromDisk()
        {
            try
            {
                if (File.Exists(ManifestPath))
                {
                    string json = File.ReadAllText(ManifestPath, Encoding.UTF8);
                    IndexManifest manifest = JsonConvert.DeserializeObject<IndexManifest>(json);
                    if (manifest != null)
                    {
                        manifest.Files = new Dictionary<string, IndexFileEntry>(
                            manifest.Files ?? new Dictionary<string, IndexFileEntry>(),
                            StringComparer.OrdinalIgnoreCase);
                        Manifest = manifest;
                    }
                }
            }
            catch { Manifest = new IndexManifest(); }

            try
            {
                if (File.Exists(SymbolsPath))
                {
                    string json = File.ReadAllText(SymbolsPath, Encoding.UTF8);
                    List<SymbolEntry> symbols = JsonConvert.DeserializeObject<List<SymbolEntry>>(json);
                    Symbols = symbols ?? new List<SymbolEntry>();
                }
            }
            catch { Symbols = new List<SymbolEntry>(); }

            try { Vectors = VectorStore.Load(VectorsPath); }
            catch { Vectors = new VectorStore(); }
        }

        /// <summary>Persists manifest, symbol map, and (if any) vectors to disk.</summary>
        public void Save()
        {
            if (!Directory.Exists(RootDir))
                Directory.CreateDirectory(RootDir);

            Manifest.SolutionKey = SolutionKey;
            Manifest.Dimension = Vectors.Dimension;
            Manifest.SemanticPresent = Vectors.Count > 0;

            File.WriteAllText(ManifestPath,
                JsonConvert.SerializeObject(Manifest, Formatting.Indented), Encoding.UTF8);
            File.WriteAllText(SymbolsPath,
                JsonConvert.SerializeObject(Symbols), Encoding.UTF8);
            Vectors.Save(VectorsPath);

            // Legacy sidecar from the split symbols/references model.
            try
            {
                string legacyRefs = Path.Combine(RootDir, "references.json");
                if (File.Exists(legacyRefs))
                    File.Delete(legacyRefs);
            }
            catch { }
        }

        /// <summary>Deletes all on-disk index files and clears in-memory data.</summary>
        public void Clear()
        {
            try { if (Directory.Exists(RootDir)) Directory.Delete(RootDir, true); }
            catch { }
            Manifest = new IndexManifest();
            Symbols = new List<SymbolEntry>();
            Vectors = new VectorStore();
        }

        public bool HasIndex
        {
            get { return Manifest != null && Manifest.Files != null && Manifest.Files.Count > 0; }
        }

        // ── Mutation helpers (used by the indexer) ─────────────────────

        /// <summary>Removes all symbols, vectors, and the manifest entry for a single file.</summary>
        internal void RemoveFileData(string file)
        {
            if (string.IsNullOrEmpty(file))
                return;

            if (Symbols != null)
                Symbols.RemoveAll(s => s != null && string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase));

            RemoveCallersFromFile(file);
            Vectors.RemoveByFile(file);
            Manifest.Files.Remove(file);
        }

        /// <summary>Clears all per-file data (symbols, vectors, manifest file entries).</summary>
        internal void ClearData()
        {
            if (Symbols != null) Symbols.Clear();
            Vectors.Clear();
            Manifest.Files.Clear();
        }

        internal void AddSymbols(IEnumerable<SymbolEntry> symbols)
        {
            if (symbols != null)
                Symbols.AddRange(symbols);
        }

        internal void ClearAllCallers()
        {
            if (Symbols == null)
                return;
            foreach (SymbolEntry symbol in Symbols)
            {
                if (symbol != null && symbol.Callers != null)
                    symbol.Callers.Clear();
            }
        }

        internal void RemoveCallersFromFile(string file)
        {
            if (string.IsNullOrEmpty(file) || Symbols == null)
                return;

            foreach (SymbolEntry symbol in Symbols)
            {
                if (symbol == null || symbol.Callers == null)
                    continue;
                symbol.Callers.RemoveAll(c =>
                    c != null && string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase));
            }
        }

        internal Dictionary<string, int> BuildCallerCounts()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (Symbols == null)
                return counts;

            foreach (SymbolEntry symbol in Symbols)
            {
                if (symbol == null || symbol.Callers == null || symbol.Callers.Count == 0)
                    continue;
                counts[symbol.DefSiteKey()] = symbol.Callers.Count;
            }
            return counts;
        }

        /// <summary>Returns caller sites for a symbol, de-duplicated by file:line.</summary>
        internal List<SymbolCaller> GetDistinctCallers(SymbolEntry symbol, int maxResults)
        {
            List<SymbolCaller> results = new List<SymbolCaller>();
            if (symbol == null || symbol.Callers == null || symbol.Callers.Count == 0)
                return results;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SymbolCaller caller in symbol.Callers)
            {
                if (caller == null || string.IsNullOrEmpty(caller.File))
                    continue;
                string siteKey = caller.File + ":" + caller.Line;
                if (!seen.Add(siteKey))
                    continue;
                results.Add(caller);
                if (maxResults > 0 && results.Count >= maxResults)
                    break;
            }
            return results;
        }

        internal string ToDisplayPath(string file)
        {
            if (string.IsNullOrEmpty(file))
                return file;
            if (!string.IsNullOrEmpty(WorkspaceRoot)
                && file.StartsWith(WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
            {
                return file.Substring(WorkspaceRoot.Length).TrimStart('\\', '/');
            }
            return file;
        }

        internal void AddChunk(ChunkVector chunk)
        {
            Vectors.Add(chunk);
        }

        // ── Search ─────────────────────────────────────────────────────

        /// <summary>Ranks symbols by token overlap against name, signature, and caller paths.</summary>
        public List<SymbolEntry> SearchSymbols(string query, int topK)
        {
            List<SymbolEntry> results = new List<SymbolEntry>();
            if (Symbols == null || Symbols.Count == 0 || string.IsNullOrWhiteSpace(query))
                return results;

            List<string> tokens = Tokenize(query);
            if (tokens.Count == 0)
                return results;

            string wholeLower = query.Trim().ToLowerInvariant();
            List<KeyValuePair<SymbolEntry, int>> scored = new List<KeyValuePair<SymbolEntry, int>>();

            foreach (SymbolEntry symbol in Symbols)
            {
                if (symbol == null || string.IsNullOrEmpty(symbol.Name))
                    continue;

                string nameLower = symbol.Name.ToLowerInvariant();
                string sigLower = string.IsNullOrEmpty(symbol.Signature) ? string.Empty : symbol.Signature.ToLowerInvariant();
                string fileLower = string.IsNullOrEmpty(symbol.File) ? string.Empty : symbol.File.ToLowerInvariant();

                int score = 0;
                foreach (string token in tokens)
                {
                    if (nameLower == token) score += 100;
                    else if (nameLower.StartsWith(token, StringComparison.Ordinal)) score += 60;
                    else if (nameLower.IndexOf(token, StringComparison.Ordinal) >= 0) score += 40;
                    else if (sigLower.IndexOf(token, StringComparison.Ordinal) >= 0) score += 15;
                    else if (fileLower.IndexOf(token, StringComparison.Ordinal) >= 0) score += 10;
                    else if (symbol.Callers != null)
                    {
                        foreach (SymbolCaller caller in symbol.Callers)
                        {
                            string callerPath = caller != null && caller.File != null
                                ? caller.File.ToLowerInvariant()
                                : string.Empty;
                            if (callerPath.IndexOf(token, StringComparison.Ordinal) >= 0)
                            {
                                score += 8;
                                break;
                            }
                        }
                    }
                }

                if (nameLower == wholeLower) score += 50;

                int callerCount = symbol.Callers != null ? symbol.Callers.Count : 0;
                if (callerCount > 0)
                    score += Math.Min(callerCount, 15);

                if (score > 0)
                    scored.Add(new KeyValuePair<SymbolEntry, int>(symbol, score));
            }

            scored.Sort((a, b) =>
            {
                int cmp = b.Value.CompareTo(a.Value);
                if (cmp != 0) return cmp;
                return a.Key.Name.Length.CompareTo(b.Key.Name.Length);
            });

            int limit = topK > 0 ? Math.Min(topK, scored.Count) : scored.Count;
            for (int i = 0; i < limit; i++)
                results.Add(scored[i].Key);
            return results;
        }

        /// <summary>Returns top-K chunks by cosine similarity to the query vector.</summary>
        public List<ChunkHit> SearchSemantic(float[] queryVector, int topK)
        {
            return Vectors.Search(queryVector, topK);
        }

        private static List<string> Tokenize(string query)
        {
            List<string> tokens = new List<string>();
            if (string.IsNullOrEmpty(query))
                return tokens;

            StringBuilder current = new StringBuilder();
            foreach (char c in query)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    current.Append(char.ToLowerInvariant(c));
                }
                else if (current.Length > 0)
                {
                    AddToken(tokens, current.ToString());
                    current.Length = 0;
                }
            }
            if (current.Length > 0)
                AddToken(tokens, current.ToString());

            return tokens;
        }

        private static void AddToken(List<string> tokens, string token)
        {
            if (token.Length >= 2 && !tokens.Contains(token))
                tokens.Add(token);
        }

        // ── Status ─────────────────────────────────────────────────────

        public IndexingStatusSnapshot GetStatus()
        {
            IndexingStatusSnapshot s = new IndexingStatusSnapshot();
            s.Mode = ConfigHandler.GetIndexingMode();
            s.SolutionName = SolutionName;

            if (HasIndex)
            {
                s.Phase = "ready";
                s.HasIndex = true;
                s.FileCount = Manifest.Files.Count;
                s.SymbolCount = Symbols != null ? Symbols.Count : 0;
                s.ChunkCount = Vectors != null ? Vectors.Count : 0;
                s.SemanticPresent = Manifest.SemanticPresent && s.ChunkCount > 0;
                s.EmbeddingsModel = Manifest.EmbeddingsModel;
                s.Dimension = Manifest.Dimension;
                s.LastIndexedUtc = Manifest.LastIndexedUtc == default(DateTime)
                    ? (DateTime?)null
                    : Manifest.LastIndexedUtc;
            }
            else
            {
                s.Phase = "idle";
                s.HasIndex = false;
                s.EmbeddingsModel = ConfigHandler.GetEmbeddingsModel();
            }

            return s;
        }

        // ── Current-index cache ────────────────────────────────────────

        private static readonly object _currentGate = new object();
        private static CodebaseIndex _current;

        /// <summary>
        /// Resolves the active solution's key + root using the DTE, falling back to the active
        /// document folder or the current directory. Runs DTE access on the UI thread.
        /// </summary>
        public static bool ResolveWorkspace(out string solutionKey, out string workspaceRoot)
        {
            string resolved = null;

            EditorService.InvokeOnUIThread(() =>
            {
                try
                {
                    EnvDTE80.DTE2 dte = EditorService.GetDte();
                    if (dte == null) return;
                    if (dte.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
                        resolved = dte.Solution.FullName;
                    else if (dte.ActiveDocument != null && !string.IsNullOrEmpty(dte.ActiveDocument.FullName))
                        resolved = dte.ActiveDocument.FullName;
                }
                catch { }
            });

            if (!string.IsNullOrEmpty(resolved))
            {
                solutionKey = resolved;
                try { workspaceRoot = Path.GetDirectoryName(resolved); }
                catch { workspaceRoot = null; }
                if (string.IsNullOrEmpty(workspaceRoot))
                    workspaceRoot = Environment.CurrentDirectory;
                return true;
            }

            workspaceRoot = Environment.CurrentDirectory;
            solutionKey = workspaceRoot;
            return !string.IsNullOrEmpty(workspaceRoot);
        }

        /// <summary>
        /// Returns the cached index for the active solution, (re)loading it if the solution
        /// changed or the cache was invalidated.
        /// </summary>
        public static CodebaseIndex GetCurrent()
        {
            string solutionKey, workspaceRoot;
            ResolveWorkspace(out solutionKey, out workspaceRoot);

            lock (_currentGate)
            {
                if (_current != null &&
                    string.Equals(_current.SolutionKey, solutionKey, StringComparison.OrdinalIgnoreCase))
                    return _current;

                _current = LoadFor(workspaceRoot, solutionKey);
                return _current;
            }
        }

        /// <summary>Replaces the cached current index (called by the indexer after a run).</summary>
        public static void SetCurrent(CodebaseIndex index)
        {
            lock (_currentGate) { _current = index; }
        }

        /// <summary>Drops the cached index so the next access reloads from disk.</summary>
        public static void Invalidate()
        {
            lock (_currentGate) { _current = null; }
        }

        /// <summary>Publishes the current index status to the reporter (loads if needed).</summary>
        public static void PublishStatus()
        {
            try
            {
                CodebaseIndex index = GetCurrent();
                IndexingStatusSnapshot snapshot = index.GetStatus();
                IndexingStatusReporter.Publish(snapshot);
            }
            catch { }
        }
    }
}
