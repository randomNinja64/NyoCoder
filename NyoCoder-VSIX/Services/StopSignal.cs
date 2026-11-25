using System;
using System.Collections.Generic;

namespace NyoCoder
{
    /// <summary>
    /// The Stop flag for the running conversation, plus abort actions for whatever the run
    /// is blocked on (an HTTP request, a curl or tool process). Polling the flag alone can't
    /// end a call that is waiting, so Stop also runs these actions to end it at once.
    /// Only code on the conversation thread (inside <see cref="RunScope"/>) registers, so
    /// background work such as indexing is never aborted.
    /// </summary>
    internal static class StopSignal
    {
        [ThreadStatic] private static bool _onRunThread;

        private static readonly object _lock = new object();
        private static readonly List<Action> _aborts = new List<Action>();
        private static volatile bool _requested;

        public static bool Requested
        {
            get { return _requested; }
        }

        /// <summary>Sets the flag and aborts whatever the run is blocked on.</summary>
        public static void Request()
        {
            Action[] aborts;
            lock (_lock)
            {
                if (_requested)
                    return;
                _requested = true;
                aborts = _aborts.ToArray();
            }
            foreach (Action abort in aborts)
            {
                try { abort(); } catch { }
            }
        }

        public static void Reset()
        {
            _requested = false;
        }

        /// <summary>Marks the current thread as the conversation thread until disposed.</summary>
        public static IDisposable RunScope()
        {
            _onRunThread = true;
            return new Scope(() => _onRunThread = false);
        }

        /// <summary>
        /// Registers <paramref name="abort"/> until disposed. Runs it at once if Stop was
        /// already requested. Off the conversation thread it does nothing.
        /// </summary>
        public static IDisposable OnStop(Action abort)
        {
            if (!_onRunThread)
                return new Scope(null);

            lock (_lock)
            {
                if (!_requested)
                {
                    _aborts.Add(abort);
                    return new Scope(() => { lock (_lock) _aborts.Remove(abort); });
                }
            }
            try { abort(); } catch { }
            return new Scope(null);
        }

        private sealed class Scope : IDisposable
        {
            private Action _onDispose;

            public Scope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                Action onDispose = _onDispose;
                _onDispose = null;
                if (onDispose != null)
                    onDispose();
            }
        }
    }
}
