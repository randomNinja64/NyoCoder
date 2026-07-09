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
            int chunkBudget = ConfigHandler.GetIndexMaxChunksTotal();

            foreach (string rawFile in files)
            {
                string file = FileScanFilter.NormalizePath(rawFile);
                currentSet.Add(file);
                done++;
                if (done % ProgressInterval == 0)
                    IndexingStatusReporter.ReportProgress(done, total, "Indexing");

                string content = TryReadText(file);
                if (content == null)
                    continue;

                long mtime = SafeMtime(file);
                string hash = CodebaseIndex.HashMd5Hex(content, lowercase: false);

                IndexFileEntry existing;
                bool changed = rebuildAll
                    || !index.Manifest.Files.TryGetValue(file, out existing)
                    || existing == null
                    || !string.Equals(existing.Hash, hash, StringComparison.Ordinal);

                if (!changed)
                    continue;

                int symbolCount = AddFileSymbols(index, file, content, removeExisting: !rebuildAll);

                int chunkCount = 0;
                if (semantic)
                    chunkCount = CollectChunks(file, content, workspaceRoot, chunkLines, overlap,
                        ref chunkBudget, embedChunks, embedTexts);

                index.Manifest.Files[file] = BuildManifestEntry(hash, mtime, symbolCount, chunkCount);
            }

            // Prune files that no longer exist on disk.
            List<string> toRemove = new List<string>();
            foreach (string key in index.Manifest.Files.Keys)
                if (!currentSet.Contains(key))
                    toRemove.Add(key);
            foreach (string key in toRemove)
                index.RemoveFileData(key);

            IndexingStatusReporter.ReportProgress(total, total, "References");
            ScanReferences(index, index.Manifest.Files.Keys, clearAllCallers: true);

            // Embed all collected chunks in one pass.
            if (semantic)
            {
                bool embedOk = true;
                if (embedChunks.Count > 0)
                {
                    IndexingStatusReporter.ReportProgress(total, total, "Embedding");
                    int added;
                    string embedFailure = ApplyEmbeddings(embeddings, embedChunks, embedTexts, index, out added);
                    if (embedFailure != null)
                    {
                        embedOk = false;
                        embedError = "Embeddings failed: " + embedFailure + " (symbol index saved).";
                    }
                }
                // Stamp the model when embedding succeeded or nothing needed embedding this run
                // (e.g. no files changed); an outright failure leaves the manifest as-is so a
                // stale/incomplete vector set is retried on the next run.
                if (embedOk)
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

            int symbolCount = AddFileSymbols(index, path, content, removeExisting: true);

            int chunkCount = 0;
            if (semantic)
            {
                int chunkLines = ConfigHandler.GetIndexChunkLines();
                int overlap = ConfigHandler.GetIndexChunkOverlap();
                List<string> texts = new List<string>();
                List<ChunkVector> chunks = new List<ChunkVector>();
                int chunkBudget = int.MaxValue;
                CollectChunks(path, content, index.WorkspaceRoot, chunkLines, overlap,
                    ref chunkBudget, chunks, texts);

                int added;
                string embedFailure = ApplyEmbeddings(embeddings, chunks, texts, index, out added);
                chunkCount = added;
                if (embedFailure != null)
                    embedError = "Embeddings failed: " + embedFailure;
                else
                    index.Manifest.EmbeddingsModel = ConfigHandler.GetEmbeddingsModel();
            }

            index.Manifest.Files[path] = BuildManifestEntry(
                CodebaseIndex.HashMd5Hex(content, lowercase: false), SafeMtime(path), symbolCount, chunkCount);
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

        private static int AddFileSymbols(CodebaseIndex index, string file, string content, bool removeExisting)
        {
            if (removeExisting)
                index.RemoveFileData(file);
            List<SymbolEntry> symbols = SymbolExtractor.Extract(file, content);
            index.AddSymbols(symbols);
            return symbols.Count;
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
            string file, string content, string workspaceRoot,
            int chunkLines, int overlap, ref int chunkBudget,
            List<ChunkVector> embedChunks, List<string> embedTexts)
        {
            int chunkCount = 0;
            if (chunkBudget == 0)
                return 0;

            foreach (ChunkInfo ci in Chunk(content, chunkLines, overlap))
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

        private static IEnumerable<ChunkInfo> Chunk(string content, int chunkLines, int overlap)
        {
            if (string.IsNullOrEmpty(content))
                yield break;

            string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            if (chunkLines < 1) chunkLines = 60;
            int step = chunkLines - overlap;
            if (step < 1) step = chunkLines;

            for (int start = 0; start < lines.Length; start += step)
            {
                int end = Math.Min(start + chunkLines, lines.Length);
                StringBuilder sb = new StringBuilder();
                for (int i = start; i < end; i++)
                    sb.AppendLine(lines[i]);

                string text = sb.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return new ChunkInfo
                    {
                        Start = start + 1,
                        End = end,
                        Text = text
                    };
                }

                if (end >= lines.Length)
                    break;
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
