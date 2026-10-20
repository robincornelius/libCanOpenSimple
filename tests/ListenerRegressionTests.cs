using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using libCanopenSimple;
using Can = libCanopenSimple.libCanopenSimple;

internal static class ListenerRegressionTests
{
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Await(Func<bool> condition, string message)
    {
        Require(SpinWait.SpinUntil(condition, 4000), message);
    }

    private sealed class BrokenTrace : TraceListener
    {
        public override void Write(string value) { throw new InvalidOperationException("Trace unavailable"); }
        public override void WriteLine(string value) { throw new InvalidOperationException("Trace unavailable"); }
    }

    internal sealed class FakeDriver
    {
        internal readonly ConcurrentQueue<DriverInstance.Message> Incoming = new ConcurrentQueue<DriverInstance.Message>();
        internal readonly ManualResetEvent ReadEntered = new ManualResetEvent(false);
        internal readonly ManualResetEvent ReleaseRead = new ManualResetEvent(false);
        internal readonly DriverInstance Driver;
        internal int ThrowNext;
        internal int StatusNext;
        internal int SendStatus;
        internal readonly ConcurrentQueue<DriverInstance.Message> Outgoing = new ConcurrentQueue<DriverInstance.Message>();
        internal Action<DriverInstance.Message> OnSend;
        internal int Reads;
        internal int Sends;
        internal int Closed;
        internal int Active;
        internal int MaxActive;
        internal int UnsafeClose;
        internal bool Block;
        internal int EmptyReadDelay = 1;

        internal FakeDriver()
        {
            Driver = new DriverInstance(Receive, Send,
                board => { Interlocked.Exchange(ref Closed, 0); return new IntPtr(1); },
                handle =>
                {
                    if (Volatile.Read(ref Active) != 0)
                    {
                        Interlocked.Increment(ref UnsafeClose);
                    }
                    Interlocked.Exchange(ref Closed, 1);
                    return 1;
                },
                (handle, rate) => 0, callback => { });
        }

        private byte Send(IntPtr handle, IntPtr buffer)
        {
            Interlocked.Increment(ref Sends);
            var message = (DriverInstance.Message)Marshal.PtrToStructure(buffer, typeof(DriverInstance.Message));
            Outgoing.Enqueue(message);
            OnSend?.Invoke(message);
            return (byte)Volatile.Read(ref SendStatus);
        }

        private byte Receive(IntPtr handle, IntPtr buffer)
        {
            int active = Interlocked.Increment(ref Active);
            if (active > MaxActive)
            {
                MaxActive = active;
            }
            Interlocked.Increment(ref Reads);
            try
            {
                if (Interlocked.Exchange(ref ThrowNext, 0) != 0)
                {
                    throw new InvalidOperationException("Injected receive exception");
                }
                if (Interlocked.Exchange(ref StatusNext, 0) != 0)
                {
                    return 1;
                }
                if (Block)
                {
                    ReadEntered.Set();
                    ReleaseRead.WaitOne();
                }
                DriverInstance.Message message;
                if (!Incoming.TryDequeue(out message))
                {
                    Thread.Sleep(EmptyReadDelay);
                }
                Marshal.StructureToPtr(message, buffer, false);
                return 0;
            }
            finally
            {
                Interlocked.Decrement(ref Active);
            }
        }

        internal void Queue(ushort cob, byte length = 1, ulong data = 1)
        {
            Incoming.Enqueue(new DriverInstance.Message { cob_id = cob, len = length, data = data });
        }
    }

    private static void RawCallbacksAndRecovery()
    {
        var fake = new FakeDriver { ThrowNext = 1 };
        var reports = new ConcurrentQueue<ListenerDiagnosticEventArgs>();
        int received = 0;
        fake.Driver.ListenerDiagnostic += (sender, error) => { throw new Exception("Broken logger"); };
        fake.Driver.ListenerDiagnostic += (sender, error) => reports.Enqueue(error);
        fake.Driver.rxmessage += (message, bridge) => { throw new Exception("Broken raw subscriber"); };
        fake.Driver.rxmessage += (message, bridge) => Interlocked.Increment(ref received);
        Require(fake.Driver.open("fake", BUSSPEED.BUS_500Kbit), "Raw open failed");
        try
        {
            Await(() => fake.Driver.ListenerRestartCount >= 1 && fake.Driver.IsListenerRunning,
                "Receive exception was not restarted");
            fake.Queue(0x181);
            fake.Queue(0x182);
            Await(() => Volatile.Read(ref received) == 2, "Raw subscriber stopped later subscribers or packets");
            Await(() => reports.Any(e => e.Kind == ListenerDiagnosticKind.Restarted), "Restart was not reported");
            Require(fake.Driver.LastListenerError != null && fake.Driver.ListenerErrorCount >= 3,
                "Listener errors were not retained");
            long restarts = fake.Driver.ListenerRestartCount;
            Interlocked.Exchange(ref fake.StatusNext, 1);
            Await(() => fake.Driver.ListenerRestartCount > restarts, "Nonzero native receive status was ignored");
            Require(reports.Any(e => e.Exception != null && e.Exception.Message.Contains("driver status 1")),
                "Native receive status was not diagnosed");
        }
        finally
        {
            fake.Driver.close();
        }
        long stoppedRestarts = fake.Driver.ListenerRestartCount;
        int stoppedReads = fake.Reads;
        Thread.Sleep(300);
        Require(!fake.Driver.isOpen() && !fake.Driver.IsListenerRunning
            && fake.Driver.ListenerRestartCount == stoppedRestarts && fake.Reads == stoppedReads,
            "Watchdog resurrected a closed raw listener");
        Require(fake.Driver.open("fake", BUSSPEED.BUS_500Kbit), "Raw reopen failed");
        try
        {
            fake.Queue(0x183);
            Await(() => Volatile.Read(ref received) == 3, "Reopen did not reset listener state");
            fake.SendStatus = 1;
            bool failed = false;
            try { fake.Driver.cansend(new DriverInstance.Message { len = 1 }); }
            catch (InvalidOperationException) { failed = true; }
            Require(failed, "Native send status was ignored");
        }
        finally
        {
            fake.Driver.close();
        }
        Require(fake.MaxActive == 1 && fake.UnsafeClose == 0, "Duplicate reader or unsafe native close");
    }

    private static void CloseWaitsForRead()
    {
        var fake = new FakeDriver { Block = true };
        Require(fake.Driver.open("fake", BUSSPEED.BUS_500Kbit), "Blocking driver open failed");
        Require(fake.ReadEntered.WaitOne(2000), "Reader did not enter driver");
        var closing = new Thread(fake.Driver.close) { IsBackground = true };
        closing.Start();
        Thread.Sleep(100);
        Require(closing.IsAlive && fake.Closed == 0, "Close freed a live native read");
        fake.ReleaseRead.Set();
        Require(closing.Join(2000), "Close did not join reader");
        Require(fake.Closed == 1 && fake.UnsafeClose == 0, "Native close ordering failed");
    }

    private static void SupervisorHandlesUnexpectedReturn()
    {
        var diagnostics = new ListenerDiagnostics();
        int starts = 0;
        int run = 1;
        var reports = new ConcurrentQueue<ListenerDiagnosticEventArgs>();
        diagnostics.Reported += (sender, error) => { throw new Exception("Broken diagnostic subscriber"); };
        diagnostics.Reported += (sender, error) => reports.Enqueue(error);
        var worker = new ListenerWorker("test listener", () =>
        {
            if (Interlocked.Increment(ref starts) == 1)
            {
                return;
            }
            while (Volatile.Read(ref run) != 0) { Thread.Sleep(1); }
        }, diagnostics);
        worker.Start();
        Await(() => Volatile.Read(ref starts) == 2 && worker.RestartCount == 1,
            "Watchdog did not recover unexpected normal return");
        Require(reports.Any(e => e.Exception != null && e.Operation.Contains("unexpectedly")),
            "Unexpected return was silent");
        worker.RequestStop();
        Interlocked.Exchange(ref run, 0);
        worker.Stop();
        int stopped = starts;
        Thread.Sleep(300);
        Require(worker.IsStopped && starts == stopped, "Stopped supervisor restarted");
    }

#if NETFRAMEWORK
    private static void WatchdogReportsUnreportedExit()
    {
        int starts = 0;
        int run = 1;
        var diagnostics = new ListenerDiagnostics();
        var worker = new ListenerWorker("abort injection", () =>
        {
            if (Interlocked.Increment(ref starts) == 1)
            {
                Thread.CurrentThread.Abort();
            }
            while (Volatile.Read(ref run) != 0) { Thread.Sleep(1); }
        }, diagnostics);
        worker.Start();
        Await(() => worker.RestartCount == 1 && diagnostics.LastError != null,
            "Unreported thread exit was not diagnosed and restarted");
        Require(diagnostics.LastError.Operation.Contains("unreported"), "Missing fallback exit reason");
        worker.RequestStop();
        Interlocked.Exchange(ref run, 0);
        worker.Stop();
    }

#endif

    private static void ProtocolCallbacksAndMalformedPackets()
    {
        var fake = new FakeDriver();
        var can = new Can();
        var reports = new ConcurrentQueue<ListenerDiagnosticEventArgs>();
        int packets = 0;
        int pdoCallbacks = 0;
        int batches = 0;
        int events = 0;
        can.ListenerDiagnostic += (sender, error) => reports.Enqueue(error);
        can.connectionevent += (sender, args) => { throw new Exception("Connection callback"); };
        can.packetevent += (packet, timestamp) => { if (packet.cob == 0x181) throw new Exception("Packet callback"); };
        can.packetevent += (packet, timestamp) => Interlocked.Increment(ref packets);
        Action<byte[]> badPdo = data => { throw new Exception("PDO callback"); };
        can.registerPDOhandler(0x181, badPdo + (data => Interlocked.Increment(ref pdoCallbacks)));
        can.pdoevent += (batch, timestamp) => { throw new Exception("PDO batch callback"); };
        can.pdoevent += (batch, timestamp) => Interlocked.Add(ref batches, batch.Length);
        can.sdoevent += (packet, timestamp) => { throw new Exception("SDO callback"); };
        can.sdoevent += (packet, timestamp) => Interlocked.Increment(ref events);
        can.nmtevent += (packet, timestamp) => { throw new Exception("NMT callback"); };
        can.nmtevent += (packet, timestamp) => Interlocked.Increment(ref events);
        can.nmtecevent += (packet, timestamp) => { throw new Exception("Heartbeat callback"); };
        can.nmtecevent += (packet, timestamp) => Interlocked.Increment(ref events);
        can.emcyevent += (packet, timestamp) => { throw new Exception("EMCY callback"); };
        can.emcyevent += (packet, timestamp) => Interlocked.Increment(ref events);
        can.timeevent += (packet, timestamp) => { throw new Exception("TIME callback"); };
        can.timeevent += (packet, timestamp) => Interlocked.Increment(ref events);
        can.syncevent += (packet, timestamp) => { throw new Exception("SYNC callback"); };
        can.syncevent += (packet, timestamp) => Interlocked.Increment(ref events);
        can.lssevent += (packet, timestamp) => { throw new Exception("LSS callback"); };
        can.lssevent += (packet, timestamp) => Interlocked.Increment(ref events);
        Require(can.OpenDriver(fake.Driver, "fake", BUSSPEED.BUS_500Kbit), "Protocol open failed");
        try
        {
            fake.Queue(0x581, 2);
            fake.Queue(0x181);
            fake.Queue(0, 2);
            fake.Queue(0x701);
            fake.Queue(0x81, 8);
            fake.Queue(0x100, 6);
            fake.Queue(0x80);
            fake.Queue(0x7E5, 8);
            fake.Queue(0x601, 8);
            fake.Queue(0x581, 8);
            fake.Queue(0x182, 9);
            Await(() => Volatile.Read(ref packets) == 10 && Volatile.Read(ref pdoCallbacks) == 1
                && Volatile.Read(ref batches) == 1 && Volatile.Read(ref events) == 8,
                "Malformed SDO or callback failure stopped protocol delivery");
            Require(can.IsListenerRunning && can.ListenerRestartCount == 0,
                "Recoverable callbacks or malformed packets killed the worker");
            Require(reports.Any(e => e.Operation == "Invalid SDO reply")
                && reports.Any(e => e.Operation == "Invalid DLC"), "Malformed input was not reported");

            var completed = new ManualResetEvent(false);
            Action<SDO> bad = transfer => { throw new Exception("SDO completion callback"); };
            SDO sdo = can.SDOwrite(1, 0x2000, 0, new byte[] { 1 }, bad + (transfer => completed.Set()));
            Await(() => Volatile.Read(ref fake.Sends) > 0, "SDO was not sent");
            fake.Queue(0x581, 8, 0x00200060);
            Require(completed.WaitOne(2000), "SDO completion callback suppressed later subscriber");
            Require(Task.Run(() => sdo.WaitOne()).Wait(2000), "SDO completion failed to signal waiter");
            Require(reports.Any(e => e.Operation == "SDO completion callback failed"),
                "SDO completion failure was silent");

            // Force a real worker exit while it sleeps; the monitor must remain.
            var supervisor = (ListenerWorker)typeof(Can).GetField("listenerWorker", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(can);
            var thread = (Thread)typeof(ListenerWorker).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(supervisor);
            thread.Interrupt();
            Await(() => can.ListenerRestartCount == 1 && can.IsListenerRunning, "Protocol watchdog did not restart");
            int before = packets;
            fake.Queue(0x182);
            Await(() => Volatile.Read(ref packets) > before, "Restarted protocol listener did not resume");
        }
        finally
        {
            can.close();
        }
        long restarts = can.ListenerRestartCount;
        Thread.Sleep(300);
        Require(!can.isopen() && !can.IsListenerRunning && can.ListenerRestartCount == restarts,
            "Protocol watchdog resurrected closed connection");
        Require(can.OpenDriver(fake.Driver, "fake", BUSSPEED.BUS_500Kbit), "Protocol reopen failed");
        can.close();
        Require(fake.UnsafeClose == 0 && fake.MaxActive == 1, "Protocol close raced native reads");
    }

    private static void CloseFromCallbackAndBurst()
    {
        var fake = new FakeDriver();
        var can = new Can();
        int packets = 0;
        int pdos = 0;
        int orderErrors = 0;
        can.packetevent += (packet, timestamp) =>
        {
            int expected = Interlocked.Increment(ref packets) - 1;
            if (packet.data[0] != (byte)expected)
            {
                Interlocked.Increment(ref orderErrors);
            }
        };
        can.pdoevent += (batch, timestamp) => Interlocked.Add(ref pdos, batch.Length);
        for (int index = 0; index < 5000; index++)
        {
            fake.Queue(0x181, 1, (byte)index);
        }
        Require(can.OpenDriver(fake.Driver, "fake", BUSSPEED.BUS_500Kbit), "Burst open failed");
        try
        {
            Await(() => Volatile.Read(ref packets) == 5000 && Volatile.Read(ref pdos) == 5000,
                "Listener burst lost frames or failed to deliver PDO batches");
            Require(orderErrors == 0 && can.ListenerErrorCount == 0, "Burst reordered packets or reported false errors");
            can.packetevent += (packet, timestamp) => can.close();
            fake.Queue(0x181, 1, unchecked((byte)5000));
            Await(() => fake.Closed == 1 && !can.IsListenerRunning, "Close from packet callback deadlocked");
        }
        finally
        {
            can.close();
        }
        Thread.Sleep(300);
        Require(can.ListenerRestartCount == 0 && fake.UnsafeClose == 0 && can.ListenerErrorCount == 0,
            "Normal callback close reported a failure, restarted, or freed a pending read");
    }

    private static void ShutdownIsNotFailure()
    {
        var diagnostics = new ListenerDiagnostics();
        int run = 1;
        var ready = new ManualResetEvent(false);
        var worker = new ListenerWorker("normal shutdown", () =>
        {
            ready.Set();
            while (Volatile.Read(ref run) != 0) { Thread.SpinWait(10); }
        }, diagnostics);
        worker.Start();
        Require(ready.WaitOne(2000), "Shutdown test did not start");
        worker.RequestStop();
        Interlocked.Exchange(ref run, 0);
        // Force the worker to exit before the join; this used to be mistaken
        // for failure if the owner cleared its loop flag before RequestStop.
        Await(() => worker.IsStopped, "Normal listener did not stop");
        worker.Stop();
        Require(diagnostics.ErrorCount == 0 && worker.RestartCount == 0, "Normal shutdown reported failure");

        var fake = new FakeDriver();
        for (int cycle = 0; cycle < 20; cycle++)
        {
            Require(fake.Driver.open("fake", BUSSPEED.BUS_500Kbit), "Repeated open failed");
            fake.Driver.close();
        }
        Thread.Sleep(300);
        Require(fake.Driver.ListenerErrorCount == 0 && fake.Driver.ListenerRestartCount == 0,
            "Repeated raw shutdown produced a false failure");
    }

    private static void NativeNanomsg(string deploymentDirectory)
    {
        string previousDirectory = Environment.CurrentDirectory;
        Environment.CurrentDirectory = Path.GetFullPath(deploymentDirectory);
        try
        {
            var loader = new DriverLoader();
            DriverInstance driver = loader.loaddriver(Path.Combine(Environment.CurrentDirectory,
                "drivers", "can_nanomsg_win32"));
            string bus = "inproc://listener-regression-" + Guid.NewGuid().ToString("N");
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Require(driver.open(bus, BUSSPEED.BUS_500Kbit), "Native nanomsg open/rebind failed");
                try
                {
                    Thread.Sleep(300);
                    Require(driver.ListenerErrorCount == 0 && driver.ListenerRestartCount == 0
                        && driver.IsListenerRunning, "Nanomsg idle polling triggered recovery");
                    driver.cansend(new DriverInstance.Message { cob_id = 0x181, len = 1, data = 1 });
                }
                finally
                {
                    driver.close();
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
        }
    }

    private static void IdleProtocolCpu()
    {
        var fake = new FakeDriver { EmptyReadDelay = 20 };
        var can = new Can();
        Require(can.OpenDriver(fake.Driver, "fake", BUSSPEED.BUS_500Kbit), "Idle test open failed");
        try
        {
            Thread.Sleep(200);
            using (Process process = Process.GetCurrentProcess())
            {
                TimeSpan start = process.TotalProcessorTime;
                Stopwatch wall = Stopwatch.StartNew();
                Thread.Sleep(1500);
                double cpu = (process.TotalProcessorTime - start).TotalMilliseconds;
                double percent = cpu / wall.Elapsed.TotalMilliseconds * 100;
                Console.WriteLine("Idle protocol: {0:F2} ms CPU, {1:F2}% of one core", cpu, percent);
                Require(percent < 20, "Idle asyncprocess consumes excessive CPU");
            }
            int received = 0;
            can.pdoevent += (packets, time) => Interlocked.Add(ref received, packets.Length);
            fake.Queue(0x181);
            Await(() => Volatile.Read(ref received) == 1, "Idle sleep prevented later packet delivery");
        }
        finally
        {
            can.close();
        }
        Console.WriteLine("PASS #3: idle protocol CPU and delivery after an idle interval");
    }

    private static void NativeNull(string deploymentDirectory)
    {
        var can = new Can { echo = false };
        int received = 0;
        can.pdoevent += (packets, time) => Interlocked.Add(ref received, packets.Length);
        string driver = Path.Combine(Path.GetFullPath(deploymentDirectory), "drivers", "can_null_win32");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Require(can.open("null://null1", BUSSPEED.BUS_500Kbit, driver), "Native null open failed");
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    Thread.Sleep(100);
                    TimeSpan start = process.TotalProcessorTime;
                    Stopwatch wall = Stopwatch.StartNew();
                    Thread.Sleep(750);
                    double percent = (process.TotalProcessorTime - start).TotalMilliseconds / wall.Elapsed.TotalMilliseconds * 100;
                    Console.WriteLine("Null driver cycle {0}: {1:F2}% of one CPU core", cycle + 1, percent);
                    Require(percent < 20, "Real native null driver busy-spins");
                }
                can.SendPacket(new canpacket { cob = 0x181, len = 1, data = new byte[] { 1 } });
                Require(can.ListenerErrorCount == 0 && received == 0, "Null bus fabricated packets or reported send/receive failure");
            }
            finally
            {
                var closing = Stopwatch.StartNew();
                can.close();
                Require(closing.ElapsedMilliseconds < 1500, "Null idle receive prevents timely close");
            }
        }
        Console.WriteLine("PASS: real native null idle CPU, send status, close and reopen");
    }

    private static void HardwareReadback(string deploymentDirectory)
    {
        // Opt-in only: no writes, NMT transitions or persistent configuration changes.
        var can = new Can { echo = false };
        string driver = Path.Combine(Path.GetFullPath(deploymentDirectory), "drivers", "can_canusbd2xx_win32");
        can.enumerate(driver);
        Require(can.ports[driver].Contains("ftdi://0/"), "FTDI adapter zero was not discovered");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Require(can.open("ftdi://0/", BUSSPEED.BUS_500Kbit, driver), "Hardware FTDI open failed");
            try
            {
                for (int read = 0; read < 5; read++)
                {
                    SDO transfer = can.SDOread(0x19, 0x1000, 0, null);
                    Require(transfer.WaitOne(3000) && transfer.state == SDO.SDO_STATE.SDO_FINISHED,
                        "Node 0x19 device-type read failed");
                    Require(transfer.databuffer != null && transfer.databuffer.Length == 4, "Unexpected device-type payload");
                    Console.WriteLine("FTDI cycle {0}, read {1}: node 0x19 0x1000:00 = 0x{2:X8}",
                        cycle + 1, read + 1, BitConverter.ToUInt32(transfer.databuffer, 0));
                }
                Require(can.IsListenerRunning && can.ListenerErrorCount == 0 && can.ListenerRestartCount == 0,
                    "Hardware listener stopped or reported an error/restart");
            }
            finally
            {
                can.close();
            }
        }
        Console.WriteLine("PASS: FTDI 500 kbit/s, 15 node 0x19 reads and three connect/disconnect cycles");
    }

    private static int Main(string[] args)
    {
        using (var deadline = new Timer(state =>
        {
            Console.Error.WriteLine("FAIL: Listener test deadline exceeded");
            Environment.Exit(2);
        }, null, 45000, Timeout.Infinite))
        {
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new BrokenTrace());
            try
            {
                IdleProtocolCpu();
                RawCallbacksAndRecovery();
                Console.WriteLine("PASS: raw callbacks, diagnostics, restart, reopen and native status");
                CloseWaitsForRead();
                Console.WriteLine("PASS: shutdown joins pending receive before native close");
                SupervisorHandlesUnexpectedReturn();
                Console.WriteLine("PASS: unexpected-return watchdog and broken logger isolation");
#if NETFRAMEWORK
                // Thread.Abort can terminate a Framework thread without reaching the normal failure path.
                WatchdogReportsUnreportedExit();
                Console.WriteLine("PASS: watchdog reports an exit that bypasses normal failure reporting");
#else
                Console.WriteLine("SKIP: Thread.Abort injection is unavailable on modern .NET; exception/return watchdog regressions still run");
#endif
                ProtocolCallbacksAndMalformedPackets();
                SdoRegressionTests.Run();
                PdoConfigurationTests.Run();
                Console.WriteLine("PASS: protocol validation, callbacks, SDO completion, watchdog and lifecycle");
                CloseFromCallbackAndBurst();
                Console.WriteLine("PASS: 5000-frame ordered burst and callback-initiated close");
                ShutdownIsNotFailure();
                Console.WriteLine("PASS: normal and repeated shutdown never report worker failure");
                if (args.Length == 2 && args[0] == "--native")
                {
                    NativeNanomsg(args[1]);
                    NativeNull(args[1]);
                    Console.WriteLine("PASS: native nanomsg idle, send status, close and rebind");
                }
                if (args.Length == 2 && args[0] == "--hardware")
                {
                    HardwareReadback(args[1]);
                }
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
