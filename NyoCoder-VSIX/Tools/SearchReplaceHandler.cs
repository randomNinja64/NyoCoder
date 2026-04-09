using System;
using System.Collections.Generic;
using System.Text;

namespace NyoCoder
{
    internal static class SearchReplaceHandler
    {
        internal static string Apply(string filePath, string content, out int exitCode)
        {
            try
            {
                // 1) Preview only (no changes applied yet)
                SearchReplaceTool.ApplyResult preview = SearchReplaceTool.Preview(filePath, content);

                exitCode = preview.Errors.Count > 0 ? 1 : 0;

                StringBuilder sb = new StringBuilder();
                Action addSpacer = () => { if (sb.Length > 0) sb.AppendLine(); };

                if (preview.Errors.Count > 0)
                {
                    addSpacer();
                    sb.AppendLine("Errors:");
                    foreach (string err in preview.Errors) sb.AppendLine(err);
                    return sb.ToString();
                }

                if (string.Equals(preview.NewContent, preview.OriginalContent, StringComparison.Ordinal))
                {
                    addSpacer();
                    sb.AppendLine("No changes were necessary (file already matches).");
                    return sb.ToString();
                }

                // 1b) Build an inline preview buffer (old + new right next to each other)
                SearchReplaceTool.InlinePreview inline = SearchReplaceTool.BuildInlinePreview(preview);

                // Try to apply the inline preview to the open document (no save) so it shows inline in the editor.
                bool previewShownInline = false;
                if (!string.IsNullOrEmpty(preview.NormalizedFilePath))
                    previewShownInline = EditorService.TrySetOpenDocumentContent(preview.NormalizedFilePath, inline.Content, false);

                // Show inline highlight adornments (background + strikethrough) for preview spans
                if (previewShownInline && inline.Spans.Count > 0)
                {
                    string p = string.IsNullOrEmpty(preview.NormalizedFilePath)
                        ? EditorService.NormalizeFilePath(filePath)
                        : preview.NormalizedFilePath;
                    ToolHandler.RaiseDiffChangesPreview(p, inline.Spans);
                }

                // 2) Ask user to approve/reject using the bottom bar in the NyoCoder panel
                NyoCoderControl toolWindowControl = null;
                try { toolWindowControl = NyoCoder_VSIXPackage.Instance != null ? NyoCoder_VSIXPackage.Instance.ToolWindowControl : null; } catch { }

                // Fail-closed: do not apply changes unless explicitly approved via UI.
                ApprovalResult approvalResult = ApprovalResult.Rejected;
                string notApprovedMessage = "Rejected by user. No changes applied.";
                if (toolWindowControl != null)
                {
                    StringBuilder approvalArgs = new StringBuilder();
                    approvalArgs.AppendLine("Apply these changes?");
                    approvalArgs.AppendLine("File: " + (string.IsNullOrEmpty(preview.NormalizedFilePath) ? filePath : preview.NormalizedFilePath));
                    approvalArgs.AppendLine();
                    if (!string.IsNullOrEmpty(preview.PreviewDiff))
                        approvalArgs.Append(preview.PreviewDiff);
                    approvalResult = toolWindowControl.RequestToolApproval("search_replace", approvalArgs.ToString());
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
                    string p = string.IsNullOrEmpty(preview.NormalizedFilePath)
                        ? EditorService.NormalizeFilePath(filePath)
                        : preview.NormalizedFilePath;
                    ToolHandler.RaiseDiffPreviewCleared(p);

                    if (previewShownInline && !string.IsNullOrEmpty(preview.NormalizedFilePath))
                        EditorService.TrySetOpenDocumentContent(preview.NormalizedFilePath, preview.OriginalContent ?? "", false);

                    addSpacer();
                    sb.AppendLine(notApprovedMessage);
                    return sb.ToString();
                }

                // 3) Clear preview adornments, then apply changes
                {
                    string p = string.IsNullOrEmpty(preview.NormalizedFilePath)
                        ? EditorService.NormalizeFilePath(filePath)
                        : preview.NormalizedFilePath;
                    ToolHandler.RaiseDiffPreviewCleared(p);
                }

                bool appliedOk = false;

                if (previewShownInline && !string.IsNullOrEmpty(preview.NormalizedFilePath))
                    appliedOk = EditorService.TrySetOpenDocumentContent(preview.NormalizedFilePath, preview.NewContent ?? "", true);

                if (!appliedOk)
                    appliedOk = SearchReplaceTool.ApplyPreview(preview);

                if (!appliedOk)
                {
                    exitCode = 1;
                    addSpacer();
                    sb.AppendLine("Error: Failed to apply changes.");
                    return sb.ToString();
                }

                exitCode = 0;
                sb.AppendLine("Approved and applied " + preview.Changes.Count + " block(s).");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                exitCode = 1;
                return "Error: " + ex.Message;
            }
        }
    }
}
