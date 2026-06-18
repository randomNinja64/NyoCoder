using System;

namespace NyoCoder
{
    /// <summary>
    /// Single entry point for tool approval UI during an active AI session.
    /// Bound when a conversation starts and cleared when it ends.
    /// </summary>
    internal static class ToolApprovalService
    {
        private static Func<string, string, ApprovalResult> _handler;

        public static bool IsAvailable
        {
            get { return _handler != null; }
        }

        public static void Bind(Func<string, string, ApprovalResult> handler)
        {
            _handler = handler;
        }

        public static void Clear()
        {
            _handler = null;
        }

        public static ApprovalResult Request(string toolName, string arguments)
        {
            if (_handler == null)
                return ApprovalResult.Rejected;
            return _handler(toolName, arguments);
        }
    }
}
