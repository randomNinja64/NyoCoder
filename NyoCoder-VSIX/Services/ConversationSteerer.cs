using System.Collections.Generic;

namespace NyoCoder
{
    /// <summary>
    /// Thread-safe queue for user steering messages injected between LLM turns.
    /// </summary>
    internal class ConversationSteerer
    {
        private readonly object _lock = new object();
        private readonly Queue<string> _pending = new Queue<string>();

        public void Queue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            lock (_lock)
            {
                _pending.Enqueue(message.Trim());
            }
        }

        /// <summary>
        /// Removes and returns the next pending message, or null if none.
        /// </summary>
        public string TryDequeue()
        {
            lock (_lock)
            {
                if (_pending.Count == 0)
                    return null;
                return _pending.Dequeue();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _pending.Clear();
            }
        }
    }
}
