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

                if (!ConfigHandler.RequiresApprovalAfterPreview("search_replace"))
                {
                    if (!SearchReplaceTool.ApplyPreview(preview))
                    {
                        exitCode = 1;
                        addSpacer();
                        sb.AppendLine("Error: Failed to apply changes.");
                        return sb.ToString();
                    }

                    exitCode = 0;
                    sb.AppendLine("Applied " + preview.Changes.Count + " block(s).");
                    return sb.ToString();
                }

                DiffApprovalHelper.Result flow = DiffApprovalHelper.RunAfterPreview(
                    "search_replace",
                    preview,
                    filePath,
                    preview.OriginalContent ?? string.Empty,
                    preview.NewContent ?? string.Empty,
                    () => SearchReplaceTool.ApplyPreview(preview));

                if (flow.Approval != ApprovalResult.Approved)
                {
                    if (flow.ApprovalUiUnavailable)
                        exitCode = 1;
                    addSpacer();
                    sb.AppendLine(flow.NotApprovedMessage);
                    return sb.ToString();
                }

                if (!flow.Applied)
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
