using System;
using System.Text;

namespace NyoCoder
{
    /// <summary>
    /// Shared post-preview approval flow for file-editing tools: inline diff preview,
    /// user approval, restore on reject, apply on approve.
    /// </summary>
    internal static class DiffApprovalHelper
    {
        private const int InlinePreviewMaxChars = 100000;

        internal sealed class Result
        {
            public ApprovalResult Approval;
            public bool ApprovalUiUnavailable;
            public string NotApprovedMessage;
            public bool Applied;
        }

        internal static Result RunAfterPreview(
            string toolName,
            SearchReplaceTool.ApplyResult preview,
            string filePathFallback,
            string restoreContent,
            string applyContent,
            Func<bool> applyFallback)
        {
            string filePath = ResolvePath(preview, filePathFallback);
            var result = new Result
            {
                Approval = ApprovalResult.Rejected,
                NotApprovedMessage = "Rejected by user. No changes applied."
            };

            bool previewShownInline = TryShowInlinePreview(preview, filePath);
            result.Approval = RequestApproval(toolName, filePath, preview.PreviewDiff, out result.ApprovalUiUnavailable, out result.NotApprovedMessage);

            if (result.Approval != ApprovalResult.Approved)
            {
                RevertPreview(filePath, previewShownInline, restoreContent);
                return result;
            }

            ToolHandler.RaiseDiffPreviewCleared(filePath);
            result.Applied = ApplyApproved(filePath, previewShownInline, applyContent, applyFallback);
            return result;
        }

        private static string ResolvePath(SearchReplaceTool.ApplyResult preview, string filePathFallback)
        {
            if (!string.IsNullOrEmpty(preview.NormalizedFilePath))
                return preview.NormalizedFilePath;
            return EditorService.NormalizeFilePath(filePathFallback) ?? filePathFallback;
        }

        private static bool TryShowInlinePreview(SearchReplaceTool.ApplyResult preview, string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string original = preview.OriginalContent ?? string.Empty;
            string updated = preview.NewContent ?? string.Empty;
            if (original.Length + updated.Length >= InlinePreviewMaxChars)
                return false;

            SearchReplaceTool.InlinePreview inline = SearchReplaceTool.BuildInlinePreview(preview);
            if (!EditorService.TrySetOpenDocumentContent(filePath, inline.Content, false))
                return false;

            if (inline.Spans.Count > 0)
                ToolHandler.RaiseDiffChangesPreview(filePath, inline.Spans);
            return true;
        }

        private static ApprovalResult RequestApproval(
            string toolName,
            string filePath,
            string previewDiff,
            out bool approvalUiUnavailable,
            out string notApprovedMessage)
        {
            approvalUiUnavailable = false;
            notApprovedMessage = "Rejected by user. No changes applied.";

            if (!ToolApprovalService.IsAvailable)
            {
                approvalUiUnavailable = true;
                notApprovedMessage = "Error: Approval UI unavailable. No changes applied.";
                return ApprovalResult.Rejected;
            }

            StringBuilder approvalArgs = new StringBuilder();
            approvalArgs.AppendLine("Apply these changes?");
            approvalArgs.AppendLine("File: " + filePath);
            approvalArgs.AppendLine();
            if (!string.IsNullOrEmpty(previewDiff))
                approvalArgs.Append(previewDiff);

            ApprovalResult approvalResult = ToolApprovalService.Request(toolName, approvalArgs.ToString());
            if (approvalResult == ApprovalResult.Stopped)
                notApprovedMessage = "Session stopped by user. No changes applied.";
            return approvalResult;
        }

        private static void RevertPreview(string filePath, bool previewShownInline, string restoreContent)
        {
            ToolHandler.RaiseDiffPreviewCleared(filePath);
            if (previewShownInline && !string.IsNullOrEmpty(filePath))
                EditorService.TrySetOpenDocumentContent(filePath, restoreContent ?? string.Empty, false);
        }

        private static bool ApplyApproved(string filePath, bool previewShownInline, string applyContent, Func<bool> applyFallback)
        {
            bool appliedOk = false;
            if (previewShownInline && !string.IsNullOrEmpty(filePath))
                appliedOk = EditorService.TrySetOpenDocumentContent(filePath, applyContent ?? string.Empty, true);
            if (!appliedOk)
                appliedOk = applyFallback != null && applyFallback();
            return appliedOk;
        }
    }
}
