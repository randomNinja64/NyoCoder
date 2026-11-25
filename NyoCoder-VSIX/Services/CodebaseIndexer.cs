using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace NyoCoder
{
    /// <summary>
    /// Builds and maintains the codebase index. All public entry points run on a background
    /// thread and are serialized by a single-run guard (concurrent requests are dropped; the
    /// layered triggers and reconcile sweeps self-heal any missed update). Publishes progress
    /// and completion via <see cref="IndexingStatusReporter"/>.
    /// </summary>
    internal static class CodebaseIndexer
    {
        private static int _running; // 0 = idle, 1 = running
        private static int _pendingReconcile; // 1 = run reconcile again when idle
        private const int ProgressInterval = 25;

        // ── Public requests ────────────────────────────────────────────

        /// <summary>Manual "Index Now": reconciles the whole workspace. Builds at least the
        /// symbol index even when the mode is Off (explicit user action).</summary>
        public static void RequestFullIndex()
        {
            StartBackground(() => RunIndex(forceRebuild: false));
        }

        /// <summary>Reconcile sweep (e.g. on solution open). No-op when indexing is Off.</summary>
        public static void RequestReconcile()
        {
            if (ConfigHandler.GetIndexingMode() == IndexingMode.Off)
            {
                CodebaseIndex.PublishStatus();
                return;
            }
            StartBackground(() => RunIndex(forceRebuild: false), rememberReconcileIfBusy: true);
        }

        /// <summary>Incremental single-file update (e.g. on save). No-op when indexing is Off.</summary>
        public static void RequestIndexFile(string path)
        {
            if (string.IsNullOrEmpty(path) || ConfigHandler.GetIndexingMode() == IndexingMode.Off)
                return;
            StartBackground(() => RunIndexFile(path));
        }

        /// <summary>Prunes a single file from the index (e.g. on delete).</summary>
        public static void RequestRemoveFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            StartBackground(() => RunRemoveFile(path));
        }

        /// <summary>Handles a rename by pruning the old path and indexing the new one.</summary>
        public static void RequestRenameFile(string oldPath, string newPath)
        {
            if (ConfigHandler.GetIndexingMode() == IndexingMode.Off)
                return;
            StartBackground(() =>
            {
                if (!string.IsNullOrEmpty(oldPath))
                    RunRemoveFile(oldPath);
                if (!string.IsNullOrEmpty(newPath) && File.Exists(newPath))
                    RunIndexFile(newPath);
            });
        }

        /// <summary>Deletes the persisted index for the current workspace.</summary>
        public static void RequestClearIndex()
        {
            StartBackground(() =>
            {
                CodebaseIndex index = CodebaseIndex.GetCurrent();
                index.Clear();
                FinalizeIndex(index, null);
            });
        }

        private static void StartBackground(Action work, bool rememberReconcileIfBusy = false)
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                if (rememberReconcileIfBusy)
                    Interlocked.Exchange(ref _pendingReconcile, 1);
                return; // already running
            }

            Thread thread = new Thread(() =>
            {
                try { work(); }
                catch (Exception ex) { IndexingStatusReporter.ReportError(ex.Message); }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                    if (Interlocked.Exchange(ref _pendingReconcile, 0) == 1)
                        RequestReconcile();
                }
            });
            thread.IsBackground = true;
            thread.Name = "NyoCoder-Indexer";
            thread.Start();
        }

        // ── Core index / reconcile ─────────────────────────────────────

        private static void RunIndex(bool forceRebuild)
        {
            string solutionKey, workspaceRoot;
            CodebaseIndex.ResolveWorkspace(out solutionKey, out workspaceRoot);
            CodebaseIndex index = CodebaseIndex.LoadFor(workspaceRoot, solutionKey);
            CodebaseIndex.SetCurrent(index);
            IndexingStatusReporter.BeginProgress("Scanning");

            bool semantic = ConfigHandler.GetIndexingMode() == IndexingMode.Semantic;
            string embedError = null;
            EmbeddingsClient embeddings = TryCreateEmbeddings(ref semantic, ref embedError, warnOnMissing: true);

            string currentModel = ConfigHandler.GetEmbeddingsModel();
            // If the embeddings model changed, existing vectors are incompatible: rebuild.
            if (semantic && index.HasIndex &&
                !string.Equals(index.Manifest.EmbeddingsModel, currentModel, StringComparison.Ordinal))
                forceRebuild = true;

            bool rebuildAll = forceRebuild || !index.HasIndex;
            if (rebuildAll)
                index.ClearData();

            int chunkLines = ConfigHandler.GetIndexChunkLines();
            int overlap = ConfigHandler.GetIndexChunkOverlap();

            List<string> files = new List<string>(FileScanFilter.EnumerateFiles(workspaceRoot));
            int total = files.Count;
            int done = 0;
            IndexingStatusReporter.ReportProgress(0, total, "Scanning");

            List<string> embedTexts = new List<string>();
            List<ChunkVector> embedChunks = new List<ChunkVector>();
            HashSet<string> currentSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Content of files re-indexed this run — reused for the reference pass (no second disk read).
            Dictionary<string, string> changedContents =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int chunkBudget = ConfigHandler.GetIndexMaxChunksTotal();

            foreach (string rawFile in files)
            {
                string file = FileScanFilter.NormalizePath(rawFile);
                currentSet.Add(file);
                done++;
                if (done % ProgressInterval == 0)
                    IndexingStatusReporter.ReportProgress(done, total, "Indexing");

                IndexFileEntry existing = null;
                long mtime = SafeMtime(file);
                // Skip unchanged files without reading: mtime match is sufficient to avoid I/O.
                // Hash is still the content fingerprint once we do read (mtime can lie).
                if (!rebuildAll
                    && mtime != 0
                    && index.Manifest.Files.TryGetValue(file, out existing)
                    && existing != null
                    && existing.Mtime == mtime)
                {
                    continue;
                }

                string content = TryReadText(file);
                if (content == null)
                    continue;

                string hash = CodebaseIndex.HashMd5Hex(content, lowercase: false);
                bool changed = rebuildAll
                    || !index.Manifest.Files.TryGetValue(file, out existing)
                    || existing == null
                    || !string.Equals(existing.Hash, hash, StringComparison.Ordinal);

                if (!changed)
                {
                    // Content identical; refresh stored mtime so the next reconcile can skip the read.
                    if (existing != null && existing.Mtime != mtime)
                        existing.Mtime = mtime;
                    continue;
                }

                List<SymbolEntry> symbols = AddFileSymbols(index, file, content, removeExisting: !rebuildAll);

                int chunkCount = 0;
                if (semantic)
                    chunkCount = CollectChunks(file, content, symbols, workspaceRoot, chunkLines, overlap,
                        ref chunkBudget, embedChunks, embedTexts);

                index.Manifest.Files[file] = BuildManifestEntry(hash, mtime, symbols.Count, chunkCount);
                changedContents[file] = content;
            }

            // Prune files that no longer exist on disk.
            List<string> toRemove = new List<string>();
            foreach (string key in index.Manifest.Files.Keys)
                if (!currentSet.Contains(key))
                    toRemove.Add(key);
            foreach (string key in toRemove)
                index.RemoveFileData(key);

            // Reference pass: only rescans files that were re-indexed (same model as RunIndexFile).
            // Full rebuild still scans every indexed file, but uses preloaded content — no second read.
            if (rebuildAll || changedContents.Count > 0)
            {
                IndexingStatusReporter.ReportProgress(total, total, "References");
                if (rebuildAll)
                {
                    ScanReferences(index, index.Manifest.Files.Keys, clearAllCallers: true,
                        preloaded: changedContents);
                }
                else
                {
                    foreach (string file in changedContents.Keys)
                        index.RemoveCallersFromFile(file);
                    ScanReferences(index, changedContents.Keys, clearAllCallers: false,
                        preloaded: changedContents);
                }
            }

            // Embed all collected chunks in one pass.
            if (semantic)
            {
                if (embedChunks.Count > 0)
                {
                    IndexingStatusReporter.ReportProgress(total, total, "Embedding");
                    int added;
                    string embedFailure = ApplyEmbeddings(embeddings, embedChunks, embedTexts, index, out added);
                    if (embedFailure != null)
                    {
                        // Hashes were stamped before embed; saving would skip those files forever.
                        AbandonUnsavedIndex("Embeddings failed: " + embedFailure + " (index not saved).");
                        return;
                    }
                }
                index.Manifest.EmbeddingsModel = currentModel;
            }
            FinalizeIndex(index, embedError);
        }

        // ── Single-file operations ─────────────────────────────────────

        private static void RunIndexFile(string rawPath)
        {
            string path = EditorService.NormalizeFilePath(rawPath) ?? rawPath;
            if (!File.Exists(path))
            {
                RunRemoveFile(path);
                return;
            }
            if (FileScanFilter.ShouldSkipFile(path))
                return;

            CodebaseIndex index = CodebaseIndex.GetCurrent();
            IndexingStatusReporter.BeginProgress("Indexing");

            bool semantic = ConfigHandler.GetIndexingMode() == IndexingMode.Semantic;
            string embedError = null;
            EmbeddingsClient embeddings = TryCreateEmbeddings(ref semantic, ref embedError, warnOnMissing: false);

            string content = TryReadText(path);
            if (content == null)
                return;

            List<SymbolEntry> symbols = AddFileSymbols(index, path, content, removeExisting: true);

            int chunkCount = 0;
            if (semantic)
            {
                int chunkLines = ConfigHandler.GetIndexChunkLines();
                int overlap = ConfigHandler.GetIndexChunkOverlap();
                List<string> texts = new List<string>();
                List<ChunkVector> chunks = new List<ChunkVector>();
                int chunkBudget = int.MaxValue;
                CollectChunks(path, content, symbols, index.WorkspaceRoot, chunkLines, overlap,
                    ref chunkBudget, chunks, texts);

                int added;
                string embedFailure = ApplyEmbeddings(embeddings, chunks, texts, index, out added);
                if (embedFailure != null)
                {
                    AbandonUnsavedIndex("Embeddings failed: " + embedFailure + " (index not saved).");
                    return;
                }
                chunkCount = added;
                index.Manifest.EmbeddingsModel = ConfigHandler.GetEmbeddingsModel();
            }

            index.Manifest.Files[path] = BuildManifestEntry(
                CodebaseIndex.HashMd5Hex(content, lowercase: false), SafeMtime(path), symbols.Count, chunkCount);
            RebuildReferencesForFile(index, path, content);
            FinalizeIndex(index, embedError);
        }

        private static void RunRemoveFile(string rawPath)
        {
            string path = EditorService.NormalizeFilePath(rawPath) ?? rawPath;
            CodebaseIndex index = CodebaseIndex.GetCurrent();
            if (!index.Manifest.Files.ContainsKey(path))
                return;

            index.RemoveFileData(path);
            FinalizeIndex(index, null);
        }

        private static void FinalizeIndex(CodebaseIndex index, string error)
        {
            index.Manifest.LastIndexedUtc = DateTime.UtcNow;
            index.Save();
            CodebaseIndex.SetCurrent(index);
            PublishReady(index, error);
        }

        /// <summary>
        /// Drops an in-memory pass that must not be persisted (e.g. embeddings failed after
        /// hashes were stamped) and restores the last saved index from disk.
        /// </summary>
        private static void AbandonUnsavedIndex(string error)
        {
            CodebaseIndex.Invalidate();
            PublishReady(CodebaseIndex.GetCurrent(), error);
        }

        private static void PublishReady(CodebaseIndex index, string error)
        {
            IndexingStatusSnapshot snapshot = index.GetStatus();
            snapshot.Phase = "ready";
            snapshot.Error = error;
            IndexingStatusReporter.Publish(snapshot);
        }

        // ── Shared indexing helpers ────────────────────────────────────

        private static EmbeddingsClient TryCreateEmbeddings(ref bool semantic, ref string error, bool warnOnMissing)
        {
            if (!semantic)
                return null;

            EmbeddingsClient client = EmbeddingsClient.CreateFromConfig();
            if (client != null)
                return client;

            semantic = false;
            if (warnOnMissing)
                error = "Embeddings endpoint/model not configured; built symbol index only.";
            return null;
        }

        private static List<SymbolEntry> AddFileSymbols(CodebaseIndex index, string file, string content, bool removeExisting)
        {
            if (removeExisting)
                index.RemoveFileData(file);
            List<SymbolEntry> symbols = SymbolExtractor.Extract(file, content);
            index.AddSymbols(symbols);
            return symbols;
        }

        private static void RebuildReferencesForFile(CodebaseIndex index, string file, string content)
        {
            index.RemoveCallersFromFile(file);
            Dictionary<string, string> preloaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            preloaded[file] = content;
            ScanReferences(index, new[] { file }, clearAllCallers: false, preloaded: preloaded);
        }

        private static void ScanReferences(
            CodebaseIndex index,
            IEnumerable<string> files,
            bool clearAllCallers,
            Dictionary<string, string> preloaded = null)
        {
            if (clearAllCallers)
                index.ClearAllCallers();

            SymbolReferenceExtractor.SymbolIndexTables tables =
                SymbolReferenceExtractor.BuildTables(index.Symbols);
            Dictionary<string, int> refCounts = clearAllCallers
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : index.BuildCallerCounts();

            foreach (string file in files)
            {
                string content = null;
                if (preloaded != null)
                    preloaded.TryGetValue(file, out content);
                if (content == null)
                    content = TryReadText(file);
                if (content == null)
                    continue;

                SymbolReferenceExtractor.ScanFile(file, content, tables, refCounts);
            }
        }

        private static int CollectChunks(
            string file, string content, List<SymbolEntry> symbols, string workspaceRoot,
            int chunkLines, int overlap, ref int chunkBudget,
            List<ChunkVector> embedChunks, List<string> embedTexts)
        {
            int chunkCount = 0;
            if (chunkBudget == 0)
                return 0;

            // What is left of the embedding character limit after the "File: ..." prefix.
            int maxChars = ConfigHandler.GetEmbeddingsMaxChars()
                - BuildEmbedText(workspaceRoot, file, string.Empty).Length;

            foreach (ChunkInfo ci in Chunk(content, symbols, chunkLines, overlap, maxChars))
            {
                if (chunkBudget <= 0)
                    break;
                embedChunks.Add(new ChunkVector { File = file, StartLine = ci.Start, EndLine = ci.End });
                embedTexts.Add(BuildEmbedText(workspaceRoot, file, ci.Text));
                chunkCount++;
                chunkBudget--;
            }
            return chunkCount;
        }

        private static string ApplyEmbeddings(
            EmbeddingsClient embeddings,
            List<ChunkVector> chunks,
            List<string> texts,
            CodebaseIndex index,
            out int added)
        {
            added = 0;
            if (embeddings == null || chunks == null || chunks.Count == 0)
                return null;

            try
            {
                List<float[]> vectors = embeddings.EmbedBatch(texts);
                for (int i = 0; i < chunks.Count && i < vectors.Count; i++)
                {
                    chunks[i].Embedding = vectors[i];
                    index.AddChunk(chunks[i]);
                    added++;
                }
                return null;
            }
            catch (EmbeddingsException ex)
            {
                return ex.Message;
            }
        }

        private static IndexFileEntry BuildManifestEntry(
            string hash, long mtime, int symbolCount, int chunkCount)
        {
            return new IndexFileEntry
            {
                Hash = hash,
                Mtime = mtime,
                SymbolCount = symbolCount,
                ChunkCount = chunkCount
            };
        }

        // ── Chunking / IO helpers ──────────────────────────────────────

        private struct ChunkInfo
        {
            public int Start;   // 1-based inclusive
            public int End;     // 1-based inclusive
            public string Text;
        }

        /// <summary>
        /// Splits a file into chunks that start at symbol definitions where possible. The
        /// file is cut into units at each symbol's start line (moved up over the comments and
        /// attributes above it); consecutive units are merged while they fit within
        /// <paramref name="chunkLines"/> and <paramref name="maxChars"/>, and a unit too large
        /// for one chunk is split into overlapping line windows. A file with no symbols is
        /// chunked into plain line windows.
        /// </summary>
        private static IEnumerable<ChunkInfo> Chunk(
            string content, List<SymbolEntry> symbols, int chunkLines, int overlap, int maxChars)
        {
            if (string.IsNullOrEmpty(content))
                yield break;

            string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            if (chunkLines < 1) chunkLines = 60;

            List<int> cuts = FindCutPoints(lines, symbols);
            int unit = 0;
            while (unit < cuts.Count - 1)
            {
                int start = cuts[unit];
                int end = cuts[unit + 1];
                unit++;

                if (!FitsInChunk(lines, start, end, chunkLines, maxChars))
                {
                    foreach (ChunkInfo window in WindowChunks(lines, start, end, chunkLines, overlap, maxChars))
                        yield return window;
                    continue;
                }

                // Absorb following units while the merged span still fits.
                while (unit < cuts.Count - 1 && FitsInChunk(lines, start, cuts[unit + 1], chunkLines, maxChars))
                {
                    end = cuts[unit + 1];
                    unit++;
                }

                ChunkInfo chunk;
                if (TryMakeChunk(lines, start, end, out chunk))
                    yield return chunk;
            }
        }

        /// <summary>
        /// Ascending 0-based line indices where units begin, starting with 0 and ending with
        /// lines.Length. Each symbol contributes its start line, moved up over directly
        /// preceding comment / attribute / decorator lines so they stay with the definition.
        /// </summary>
        private static List<int> FindCutPoints(string[] lines, List<SymbolEntry> symbols)
        {
            List<int> starts = new List<int>();
            if (symbols != null)
            {
                foreach (SymbolEntry symbol in symbols)
                {
                    if (symbol != null && symbol.Line >= 1 && symbol.Line <= lines.Length)
                        starts.Add(symbol.Line - 1);
                }
            }
            starts.Sort();

            List<int> cuts = new List<int>();
            cuts.Add(0);
            int previousSymbol = -1;
            foreach (int line in starts)
            {
                if (line == previousSymbol)
                    continue;

                int cut = line;
                while (cut - 1 > previousSymbol && IsLeadingDecoration(lines[cut - 1]))
                    cut--;

                if (cut > cuts[cuts.Count - 1])
                    cuts.Add(cut);
                previousSymbol = line;
            }
            cuts.Add(lines.Length);
            return cuts;
        }

        /// <summary>A comment, attribute or decorator line that belongs to the definition below it.</summary>
        private static bool IsLeadingDecoration(string line)
        {
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0)
                return false;
            return SymbolExtractor.IsCommentLine(trimmed)
                || trimmed.StartsWith("[")
                || trimmed.StartsWith("@");
        }

        /// <summary>Whether lines [start, end) fit in one chunk (same character accounting as the windows).</summary>
        private static bool FitsInChunk(string[] lines, int start, int end, int chunkLines, int maxChars)
        {
            if (end - start > chunkLines)
                return false;
            if (maxChars <= 0)
                return true;

            int chars = 0;
            for (int i = start; i < end; i++)
            {
                chars += lines[i].Length + Environment.NewLine.Length;
                if (chars > maxChars)
                    return false;
            }
            return true;
        }

        private static bool TryMakeChunk(string[] lines, int start, int end, out ChunkInfo chunk)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = start; i < end; i++)
                sb.AppendLine(lines[i]);

            string text = sb.ToString();
            chunk = new ChunkInfo { Start = start + 1, End = end, Text = text };
            return !TextNormalization.IsNullOrWhiteSpace(text);
        }

        /// <summary>Overlapping fixed-size line windows over lines [from, to).</summary>
        private static IEnumerable<ChunkInfo> WindowChunks(
            string[] lines, int from, int to, int chunkLines, int overlap, int maxChars)
        {
            int start = from;
            while (start < to)
            {
                // Close the chunk at chunkLines or at maxChars, whichever comes first, so the
                // embedding character limit never cuts off lines that the next chunk does not cover.
                int limit = Math.Min(start + chunkLines, to);
                int end = start;
                StringBuilder sb = new StringBuilder();
                while (end < limit)
                {
                    if (maxChars > 0 && end > start
                        && sb.Length + lines[end].Length + Environment.NewLine.Length > maxChars)
                        break;
                    sb.AppendLine(lines[end]);
                    end++;
                }

                string text = sb.ToString();
                if (!TextNormalization.IsNullOrWhiteSpace(text))
                {
                    yield return new ChunkInfo
                    {
                        Start = start + 1,
                        End = end,
                        Text = text
                    };
                }

                if (end >= to)
                    break;

                // Step back by the overlap, unless the chunk was too short to allow it.
                start = end - overlap > start ? end - overlap : end;
            }
        }

        private static string BuildEmbedText(string workspaceRoot, string file, string chunkText)
        {
            string rel = file;
            try
            {
                if (!string.IsNullOrEmpty(workspaceRoot) && file.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
                    rel = file.Substring(workspaceRoot.Length).TrimStart('\\', '/');
            }
            catch { }
            return "File: " + rel + "\n" + chunkText;
        }

        private static string TryReadText(string file)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                if (FileScanFilter.LooksBinary(bytes, FileScanFilter.BinarySampleBytes))
                    return null;
                return DecodeText(bytes);
            }
            catch
            {
                return null;
            }
        }

        private static string DecodeText(byte[] bytes)
        {
            // Honor a UTF-8 BOM; otherwise decode as UTF-8 (lenient).
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            return new UTF8Encoding(false).GetString(bytes);
        }

        private static long SafeMtime(string file)
        {
            try { return File.GetLastWriteTimeUtc(file).Ticks; }
            catch { return 0; }
        }
    }
}
