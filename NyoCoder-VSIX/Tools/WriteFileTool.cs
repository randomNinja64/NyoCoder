using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NyoCoder
{
    internal static class WriteFileTool
    {
        internal static string Write(string filename, string newContent, out int exitCode)
        {
            exitCode = 0;
            try
            {
                string expandedPath = EditorService.NormalizeFilePath(filename);
                if (string.IsNullOrEmpty(expandedPath))
                    expandedPath = Environment.ExpandEnvironmentVariables(filename);

                // New file — write and open it, then ask for approval. If rejected, close and delete.
                if (!File.Exists(expandedPath))
                {
                    string directory = Path.GetDirectoryName(expandedPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllText(expandedPath, newContent, Encoding.UTF8);
                    EditorService.TryOpenFileInVisualStudio(expandedPath);

                    if (!ConfigHandler.RequiresApprovalAfterPreview("write_file"))
                    {
                        string projectDetail;
                        if (EditorService.TryAddFileToProject(expandedPath, out projectDetail))
                            return "File written successfully: " + expandedPath + "\n" + projectDetail;
                        return "File written successfully: " + expandedPath;
                    }

                    ApprovalResult newFileApproval = ApprovalResult.Rejected;
                    string newFileRejectedMsg = "Rejected by user. File deleted.";
                    if (ToolApprovalService.IsAvailable)
                    {
                        StringBuilder newFileArgs = new StringBuilder();
                        newFileArgs.AppendLine("Create this new file?");
                        newFileArgs.AppendLine("File: " + expandedPath);
                        newFileApproval = ToolApprovalService.Request("write_file", newFileArgs.ToString());
                        if (newFileApproval == ApprovalResult.Stopped)
                            newFileRejectedMsg = "Session stopped by user. File deleted.";
                    }
                    else
                    {
                        exitCode = 1;
                        EditorService.TryCloseFileInVisualStudio(expandedPath);
                        try { File.Delete(expandedPath); } catch { }
                        return "Error: Approval UI unavailable. File deleted.";
                    }

                    if (newFileApproval != ApprovalResult.Approved)
                    {
                        EditorService.TryCloseFileInVisualStudio(expandedPath);
                        try { File.Delete(expandedPath); } catch { }
                        return newFileRejectedMsg;
                    }

                    string approvedProjectDetail;
                    if (EditorService.TryAddFileToProject(expandedPath, out approvedProjectDetail))
                        return "File written successfully: " + expandedPath + "\n" + approvedProjectDetail;
                    return "File written successfully: " + expandedPath;
                }

                // --- Existing file: compute diff and show preview ---
                string originalRaw = null;
                EditorService.TryReadOpenDocument(expandedPath, out originalRaw);
                if (originalRaw == null)
                    originalRaw = File.ReadAllText(expandedPath, Encoding.UTF8);

                string normalizedOriginal = TextNormalization.NormalizeLineEndings(originalRaw);
                string normalizedNew = TextNormalization.NormalizeLineEndings(newContent ?? string.Empty);

                if (string.Equals(normalizedOriginal, normalizedNew, StringComparison.Ordinal))
                    return "File written successfully: " + expandedPath + " (no changes)";

                SearchReplaceTool.ApplyResult res = new SearchReplaceTool.ApplyResult();
                res.NormalizedFilePath = expandedPath;
                res.OriginalContent = normalizedOriginal;
                res.NewContent = normalizedNew;
                res.Changes.Add(new SearchReplaceTool.Change
                {
                    StartIndex    = 0,
                    OriginalIndex = 0,
                    OldLength     = normalizedOriginal.Length,
                    NewLength     = normalizedNew.Length,
                    OldText       = normalizedOriginal,
                    NewText       = normalizedNew,
                    Type          = SearchReplaceTool.ChangeType.Modification
                });
                res.PreviewDiff = SearchReplaceTool.BuildUnifiedDiff(normalizedOriginal, normalizedNew, 200);

                if (!ConfigHandler.RequiresApprovalAfterPreview("write_file"))
                {
                    if (!EditorService.TrySetOpenDocumentContent(expandedPath, newContent, true))
                        File.WriteAllText(expandedPath, newContent, Encoding.UTF8);
                    return "File written successfully: " + expandedPath;
                }

                EditorService.TryOpenFileInVisualStudio(expandedPath);

                DiffApprovalHelper.Result flow = DiffApprovalHelper.RunAfterPreview(
                    "write_file",
                    res,
                    expandedPath,
                    originalRaw,
                    newContent,
                    () =>
                    {
                        File.WriteAllText(expandedPath, newContent, Encoding.UTF8);
                        return true;
                    });

                if (flow.Approval != ApprovalResult.Approved)
                {
                    if (flow.ApprovalUiUnavailable)
                        exitCode = 1;
                    return flow.NotApprovedMessage;
                }

                if (!flow.Applied)
                {
                    exitCode = 1;
                    return "Error: Failed to apply changes.";
                }

                return "File written successfully: " + expandedPath;
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error writing file: " + ex.Message;
            }
        }
    }
}
