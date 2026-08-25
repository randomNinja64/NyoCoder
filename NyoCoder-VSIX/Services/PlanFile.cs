using System;
using System.IO;
using EnvDTE80;

namespace NyoCoder
{
    /// <summary>
    /// Locates and reads the PLAN.md file that Plan mode uses as the source of truth
    /// for the proposed plan. The file lives at the solution root so the user can
    /// view or edit it directly; it can be deleted at any time.
    /// </summary>
    internal static class PlanFile
    {
        /// <summary>The plan file name, always at the solution directory root.</summary>
        internal const string FileName = "PLAN.md";

        /// <summary>
        /// Returns the absolute path to PLAN.md in the current solution directory,
        /// or null when no solution is open.
        /// </summary>
        internal static string GetPath()
        {
            try
            {
                DTE2 dte = EditorService.GetDte();
                if (dte == null || dte.Solution == null)
                    return null;

                string solutionFullName = dte.Solution.FullName;
                if (string.IsNullOrEmpty(solutionFullName))
                    return null;

                string dir = Path.GetDirectoryName(solutionFullName);
                if (string.IsNullOrEmpty(dir))
                    return null;

                return Path.Combine(dir, FileName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true when the given path refers to the solution's PLAN.md file.
        /// Comparison is case-insensitive and tolerant of relative paths and
        /// different path separators.
        /// </summary>
        internal static bool IsPlanFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string planPath = GetPath();
            if (string.IsNullOrEmpty(planPath))
                return false;

            try
            {
                string full = Path.GetFullPath(path);
                return string.Equals(
                    NormalizePath(full),
                    NormalizePath(planPath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reads the full contents of PLAN.md, or null when it does not exist or
        /// cannot be read.
        /// </summary>
        internal static string ReadAll()
        {
            string path = GetPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                return File.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizePath(string path)
        {
            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                       .TrimEnd(Path.DirectorySeparatorChar);
        }
    }
}
