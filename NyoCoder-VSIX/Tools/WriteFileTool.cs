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

                    NyoCoderControl twc = null;
                    try { twc = NyoCoder_VSIXPackage.Instance != null ? NyoCoder_VSIXPackage.Instance.ToolWindowControl : null; } catch { }

                    ApprovalResult newFileApproval = ApprovalResult.Rejected;
                    string newFileRejectedMsg = "Rejected by user. File deleted.";
                    if (twc != null)
                    {
                        StringBuilder newFileArgs = new StringBuilder();
                        newFileArgs.AppendLine("Create this new file?");
                        newFileArgs.AppendLine("File: " + expandedPath);
                        newFileApproval = twc.RequestToolApproval("write_file", newFileArgs.ToString());
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

                    return "Approved. File written successfully: " + expandedPath;
                }

                // --- Existing file: compute diff and show preview ---
                string originalRaw = null;
                EditorService.TryReadOpenDocument(expandedPath, out originalRaw);
                if (originalRaw == null)
                    originalRaw = File.ReadAllText(expandedPath, Encoding.UTF8);

                string normalizedOriginal = originalRaw.Replace("\r\n", "\n").Replace("\r", "\n");
                string normalizedNew = (newContent ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");

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

                if (!ConfigHandler.ToolRequiresApproval("write_file"))
                {
                    if (!EditorService.TrySetOpenDocumentContent(expandedPath, newContent, true))
                        File.WriteAllText(expandedPath, newContent, Encoding.UTF8);
                    return "File written successfully: " + expandedPath;
                }

                EditorService.TryOpenFileInVisualStudio(expandedPath);

                const int InlinePreviewMaxChars = 100000;
                bool previewShownInline = false;
                if (normalizedOriginal.Length + normalizedNew.Length < InlinePreviewMaxChars)
                {
                    SearchReplaceTool.InlinePreview inline = SearchReplaceTool.BuildInlinePreview(res);
                    previewShownInline = EditorService.TrySetOpenDocumentContent(expandedPath, inline.Content, false);
                    if (previewShownInline && inline.Spans.Count > 0)
                        ToolHandler.RaiseDiffChangesPreview(expandedPath, inline.Spans);
                }

                NyoCoderControl toolWindowControl = null;
                try { toolWindowControl = NyoCoder_VSIXPackage.Instance != null ? NyoCoder_VSIXPackage.Instance.ToolWindowControl : null; } catch { }

                ApprovalResult approvalResult = ApprovalResult.Rejected;
                string notApprovedMessage = "Rejected by user. No changes applied.";
                if (toolWindowControl != null)
                {
                    StringBuilder approvalArgs = new StringBuilder();
                    approvalArgs.AppendLine("Apply these changes?");
                    approvalArgs.AppendLine("File: " + expandedPath);
                    approvalArgs.AppendLine();
                    if (!string.IsNullOrEmpty(res.PreviewDiff))
                        approvalArgs.Append(res.PreviewDiff);
                    approvalResult = toolWindowControl.RequestToolApproval("write_file", approvalArgs.ToString());
                    if (approvalResult == ApprovalResult.Stopped)
                        notApprovedMessage = "Session stopped by user. No changes applied.";
                }
                else
                {
                    exitCode = 1;
                    notApprovedMessage = "Error: Approval UI unavailable. No changes applied.";
                }

                if (approvalResult != ApprovalResult.Approved)
                {
                    ToolHandler.RaiseDiffPreviewCleared(expandedPath);
                    if (previewShownInline)
                        EditorService.TrySetOpenDocumentContent(expandedPath, originalRaw, false);
                    return notApprovedMessage;
                }

                ToolHandler.RaiseDiffPreviewCleared(expandedPath);

                bool appliedOk = false;
                if (previewShownInline)
                    appliedOk = EditorService.TrySetOpenDocumentContent(expandedPath, newContent, true);
                if (!appliedOk)
                    File.WriteAllText(expandedPath, newContent, Encoding.UTF8);

                return "Approved. File written successfully: " + expandedPath;
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error writing file: " + ex.Message;
            }
        }
    }
}
