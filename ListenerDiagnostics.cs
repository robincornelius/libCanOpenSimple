using System;
using System.Diagnostics;
using System.Threading;

namespace libCanopenSimple
{
    /// <summary>Distinguishes listener failures from watchdog recovery notices.</summary>
    public enum ListenerDiagnosticKind
    {
        Error,
        Restarted
    }

    /// <summary>Details of a listener failure or recovery. Handlers must return promptly.</summary>
    public sealed class ListenerDiagnosticEventArgs : EventArgs
    {
        public string Listener { get; private set; }
        public string Operation { get; private set; }
        public Exception Exception { get; private set; }
        public ListenerDiagnosticKind Kind { get; private set; }
        public DateTime TimestampUtc { get; private set; }

        internal ListenerDiagnosticEventArgs(string listener, string operation, Exception exception,
            ListenerDiagnosticKind kind)
        {
            Listener = listener;
            Operation = operation;
            Exception = exception;
            Kind = kind;
            TimestampUtc = DateTime.UtcNow;
        }
    }

    internal sealed class ListenerDiagnostics
    {
        private ListenerDiagnosticEventArgs lastError;
        private long errorCount;
        internal object Source { get; set; }
        internal event EventHandler<ListenerDiagnosticEventArgs> Reported;
        internal ListenerDiagnosticEventArgs LastError
        {
            get
            {
                return Volatile.Read(ref lastError);
            }
        }
        internal long ErrorCount
        {
            get
            {
                return Interlocked.Read(ref errorCount);
            }
        }

        internal void Report(string listener, string operation, Exception exception,
            ListenerDiagnosticKind kind = ListenerDiagnosticKind.Error, bool writeTrace = true)
        {
            var diagnostic = new ListenerDiagnosticEventArgs(listener, operation, exception, kind);
            if (kind == ListenerDiagnosticKind.Error)
            {
                Volatile.Write(ref lastError, diagnostic);
                Interlocked.Increment(ref errorCount);
            }

            // A host's logging handler must never disable listener recovery.
            if (writeTrace)
            {
                WriteTrace(diagnostic);
            }

            var handlers = Reported;
            if (handlers == null)
            {
                return;
            }

            foreach (EventHandler<ListenerDiagnosticEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(Source ?? this, diagnostic);
                }
                catch (Exception error)
                {
                    WriteTrace(new ListenerDiagnosticEventArgs(listener,
                        "Diagnostic subscriber failed", error, ListenerDiagnosticKind.Error));
                }
            }
        }

        internal void Invoke(Delegate handlers, Action<Delegate> invoke, string listener, string operation)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    invoke(handler);
                }
                catch (Exception error)
                {
                    Report(listener, operation, error);
                }
            }
        }

        private static void WriteTrace(ListenerDiagnosticEventArgs diagnostic)
        {
            try
            {
                Trace.WriteLine(string.Format("CAN listener {0}: {1}: {2}. {3}",
                    diagnostic.Listener, diagnostic.Kind, diagnostic.Operation, diagnostic.Exception));
            }
            catch (Exception)
            {
                // Trace listeners are supplied by the host too. Recovery cannot
                // depend on a file being writable or a logging callback succeeding.
            }
        }
    }
}
