using System;
using System.Threading;

namespace libCanopenSimple
{
    // Supervises thread termination, not a hung callback or blocked kernel call.
    // Stop disables restart before joining so no replacement can outlive close.
    internal sealed class ListenerWorker
    {
        private readonly object sync = new object();
        private readonly string name;
        private readonly Action run;
        private readonly ListenerDiagnostics diagnostics;
        private readonly Timer monitor;
        private Thread worker;
        private bool wanted;
        private long restartCount;
        private int terminationReported;

        internal ListenerWorker(string name, Action run, ListenerDiagnostics diagnostics)
        {
            this.name = name;
            this.run = run;
            this.diagnostics = diagnostics;
            monitor = new Timer(Check, null, Timeout.Infinite, Timeout.Infinite);
        }

        internal bool IsRunning
        {
            get
            {
                lock (sync)
                {
                    return wanted && worker != null && worker.IsAlive;
                }
            }
        }

        internal bool IsStopped
        {
            get
            {
                lock (sync)
                {
                    return !wanted && (worker == null || !worker.IsAlive);
                }
            }
        }

        internal long RestartCount
        {
            get
            {
                return Interlocked.Read(ref restartCount);
            }
        }

        internal void Start()
        {
            lock (sync)
            {
                if (wanted || (worker != null && worker.IsAlive))
                {
                    throw new InvalidOperationException("The previous listener has not stopped.");
                }

                wanted = true;
                try
                {
                    StartWorker();
                    monitor.Change(200, 200);
                }
                catch
                {
                    wanted = false;
                    throw;
                }
            }
        }

        // Disable supervision before the owner clears its loop flag. Otherwise
        // a fast listener can exit between those steps and look like a failure.
        internal void RequestStop()
        {
            lock (sync)
            {
                wanted = false;
                monitor.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        internal void Stop()
        {
            RequestStop();
            Thread stopping;
            lock (sync)
            {
                stopping = worker;
            }

            // Closing from a packet callback is allowed. The current invocation
            // unwinds normally; a new session cannot start until it has returned.
            if (stopping != null && stopping != Thread.CurrentThread && stopping.IsAlive)
            {
                stopping.Join();
            }
        }

        private void StartWorker()
        {
            Volatile.Write(ref terminationReported, 0);
            worker = new Thread(Run) { Name = name, IsBackground = true };
            worker.Start();
        }

        private void Run()
        {
            Exception failure = null;
            try
            {
                run();
            }
            catch (Exception error)
            {
                failure = error;
            }

            bool report;
            lock (sync)
            {
                report = wanted;
            }

            if (report)
            {
                Volatile.Write(ref terminationReported, 1);
                diagnostics.Report(name, "Worker stopped unexpectedly",
                    failure ?? new InvalidOperationException("Listener returned while the connection was active."));
            }
        }

        private void Check(object state)
        {
            try
            {
                bool restarted = false;
                bool missingReason = false;
                lock (sync)
                {
                    if (wanted && (worker == null || !worker.IsAlive))
                    {
                        missingReason = Volatile.Read(ref terminationReported) == 0;
                        StartWorker();
                        Interlocked.Increment(ref restartCount);
                        restarted = true;
                    }
                }

                if (restarted)
                {
                    // Restart first: diagnostic failures cannot prevent recovery.
                    if (missingReason)
                    {
                        diagnostics.Report(name, "Watchdog detected an unreported worker exit",
                            new InvalidOperationException("The listener exited without a reported reason."));
                    }

                    diagnostics.Report(name, "Watchdog restarted worker", null, ListenerDiagnosticKind.Restarted);
                }
            }
            catch (Exception error)
            {
                diagnostics.Report(name, "Listener monitor failed", error);
            }
        }
    }
}
