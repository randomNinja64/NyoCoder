using System;
using System.Collections.Generic;
using System.IO;

namespace NyoCoder
{
    /// <summary>
    /// Shared file/directory exclusion rules for codebase scanning. Mirrors the
    /// exclude lists used by <see cref="GrepSearchTool"/> so the indexer and grep
    /// agree on which files matter. Also provides a filesystem walker that skips
    /// excluded directories, binary/generated files, and oversized files.
    /// </summary>
    internal static class FileScanFilter
    {
        /// <summary>Files larger than this are skipped (bytes). Keeps indexing responsive.</summary>
        public const long MaxFileBytes = 2 * 1024 * 1024;

        /// <summary>Byte sample size for the text/binary heuristic (matches the indexer).</summary>
        public const int BinarySampleBytes = 8000;

        private static readonly HashSet<string> ExcludedDirs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", ".hg",
            ".venv", "venv", "__pycache__",
            "node_modules", "bin", "obj",
            ".vs", "packages", "dist",
            "build", ".idea", ".vscode",
            "target", "vendor", "bower_components",
            ".nuget", "TestResults"
        };

        private static readonly HashSet<string> ExcludedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pyc", ".pyo", ".exe", ".dll",
            ".so", ".dylib", ".obj", ".o",
            ".a", ".lib", ".pdb", ".ilk",
            ".class", ".jar", ".war", ".ear",
            ".zip", ".tar", ".gz", ".rar",
            ".png", ".jpg", ".jpeg", ".gif",
            ".bmp", ".ico", ".svg", ".pdf",
            ".mp3", ".mp4", ".avi", ".mov",
            ".ttf", ".woff", ".woff2", ".eot",
            ".map", ".lock", ".cache"
        };

        // Minified/generated assets excluded by full-name suffix (not a simple extension).
        private static readonly string[] ExcludedNameSuffixes =
        {
            ".min.js", ".min.css"
        };

        /// <summary>Directory leaf names that should be skipped when scanning.</summary>
        public static IEnumerable<string> ExcludedDirectoryNames
        {
            get { return ExcludedDirs; }
        }

        /// <summary>
        /// File exclusion globs (e.g. <c>*.dll</c>, <c>*.min.js</c>) suitable for tools like grep.
        /// Derived from the same lists used by <see cref="ShouldSkipFile"/> so they stay in sync.
        /// </summary>
        public static IEnumerable<string> ExcludedFileGlobs
        {
            get
            {
                foreach (string ext in ExcludedExtensions)
                    yield return "*" + ext;
                foreach (string suffix in ExcludedNameSuffixes)
                    yield return "*" + suffix;
            }
        }

        /// <summary>Returns true if a directory (by leaf name) should be skipped entirely.</summary>
        public static bool ShouldSkipDir(string dirName)
        {
            if (string.IsNullOrEmpty(dirName))
                return false;
            return ExcludedDirs.Contains(dirName);
        }

        /// <summary>Returns true if a file should be skipped based on its name/extension.</summary>
        public static bool ShouldSkipFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return true;

            string name = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(name))
                return true;

            // Minified/generated assets that grep also excludes by pattern.
            for (int i = 0; i < ExcludedNameSuffixes.Length; i++)
            {
                if (name.EndsWith(ExcludedNameSuffixes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            string ext = Path.GetExtension(name);
            return !string.IsNullOrEmpty(ext) && ExcludedExtensions.Contains(ext);
        }

        /// <summary>Canonical full path for consistent manifest keys.</summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        /// <summary>
        /// Recursively enumerates indexable files under <paramref name="rootPath"/>,
        /// skipping excluded directories, excluded/oversized files, and unreadable paths.
        /// </summary>
        public static IEnumerable<string> EnumerateFiles(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                yield break;

            Stack<string> dirs = new Stack<string>();
            dirs.Push(rootPath);

            while (dirs.Count > 0)
            {
                string current = dirs.Pop();

                string[] subDirs = null;
                try { subDirs = Directory.GetDirectories(current); }
                catch { subDirs = null; }

                if (subDirs != null)
                {
                    foreach (string dir in subDirs)
                    {
                        string leaf = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (ShouldSkipDir(leaf))
                            continue;
                        dirs.Push(dir);
                    }
                }

                string[] files = null;
                try { files = Directory.GetFiles(current); }
                catch { files = null; }

                if (files == null)
                    continue;

                foreach (string file in files)
                {
                    if (ShouldSkipFile(file))
                        continue;

                    long length;
                    try { length = new FileInfo(file).Length; }
                    catch { continue; }

                    if (length == 0 || length > MaxFileBytes)
                        continue;

                    yield return file;
                }
            }
        }

        /// <summary>
        /// Heuristic text/binary check on a byte sample: treats content as binary if it
        /// contains a NUL byte within the sampled prefix.
        /// </summary>
        public static bool LooksBinary(byte[] sample, int sampleLength)
        {
            if (sample == null)
                return false;
            int limit = Math.Min(sampleLength, sample.Length);
            for (int i = 0; i < limit; i++)
            {
                if (sample[i] == 0)
                    return true;
            }
            return false;
        }
    }
}
