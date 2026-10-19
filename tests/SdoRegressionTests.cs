using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using libCanopenSimple;
using Can = libCanopenSimple.libCanopenSimple;

internal static class SdoRegressionTests
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

    private static Can Open(ListenerRegressionTests.FakeDriver fake)
    {
        var can = new Can { echo = false };
        Require(can.OpenDriver(fake.Driver, "fake", BUSSPEED.BUS_500Kbit), "SDO test open failed");
        return can;
    }

    private static void Reply(ListenerRegressionTests.FakeDriver fake, ushort index, byte command = 0x43, byte node = 1, uint value = 42)
    {
        fake.Queue((ushort)(0x580 + node), 8, command | ((ulong)index << 8) | ((ulong)value << 32));
    }

    internal static void Run()
    {
        BlockedCallbackAndSuccessor();
        ReentrantWaitAndConcurrentConnections();
        TimeoutFlushAndClose();
        SegmentedAndAbort();
        Console.WriteLine("PASS: SDO serialization, callback isolation/reentrancy, connection ownership, cancellation and segmented completion");
    }

    private static void BlockedCallbackAndSuccessor()
    {
        var fake = new ListenerRegressionTests.FakeDriver();
        var entered = new ManualResetEvent(false);
        var release = new ManualResetEvent(false);
        int callbacks = 0;
        Can can = Open(fake);
        try
        {
            SDO first = can.SDOread(1, 0x2000, 0, transfer =>
            {
                Require(transfer.WaitOne(0), "Waiter was signalled after callback");
                entered.Set();
                release.WaitOne();
                Interlocked.Increment(ref callbacks);
            });
            SDO second = can.SDOwrite(1, 0x2001, 0, (byte)7, transfer => Interlocked.Increment(ref callbacks));
            SDO otherNode = can.SDOread(2, 0x2010, 0, null);
            Await(() => fake.Outgoing.Count >= 2, "Busy node blocked another node");
            Require(fake.Outgoing.Count(message => message.cob_id == 0x601) == 1, "Same-node transfers overlapped");
            Reply(fake, 0x2000);
            Require(entered.WaitOne(2000), "Completion callback did not start");
            Await(() => fake.Outgoing.Count(message => message.cob_id == 0x601) == 2, "Callback blocked successor send");
            // Reproduce the UI side of the lock cycle while the completion
            // callback is blocked. Submission must not need the callback's lock.
            Task<SDO> enqueue = Task.Run(() => can.SDOread(1, 0x2002, 0, null));
            Require(enqueue.Wait(1000), "Concurrent enqueue deadlocked on completion");
            Thread.Sleep(30);
            Reply(fake, 0x2000); // Delayed old reply must not complete the write.
            Require(!second.WaitOne(30), "Delayed reply completed the wrong object");
            Reply(fake, 0x2001, 0x60);
            Require(second.WaitOne(2000) && second.state == SDO.SDO_STATE.SDO_FINISHED,
                "Old transfer cleanup removed the successor registration");
            Reply(fake, 0x2010, node: 2);
            Require(otherNode.WaitOne(2000), "Independent node did not complete");
            Await(() => fake.Outgoing.Count(message => message.cob_id == 0x601) == 3, "Third transfer stalled");
            Reply(fake, 0x2002);
            Require(enqueue.Result.WaitOne(2000), "Blocked callback stopped protocol replies");
            Require(callbacks == 0, "Callbacks were not serialized per connection");
            release.Set();
            Await(() => Volatile.Read(ref callbacks) == 2, "Completion callbacks missing or duplicated");
            Reply(fake, 0x2001, 0x60);
            Thread.Sleep(30);
            Require(callbacks == 2 && first.state == SDO.SDO_STATE.SDO_FINISHED, "Duplicate reply repeated completion");
        }
        finally
        {
            release.Set();
            can.close();
        }
    }

    private static void AutoReply(ListenerRegressionTests.FakeDriver fake, uint value)
    {
        fake.OnSend = message =>
        {
            byte command = (byte)message.data;
            ushort index = (ushort)(message.data >> 8);
            Reply(fake, index, command == 0x40 ? (byte)0x43 : (byte)0x60,
                (byte)(message.cob_id - 0x600), value);
        };
    }

    private static void ReentrantWaitAndConcurrentConnections()
    {
        var fake = new ListenerRegressionTests.FakeDriver();
        var otherFake = new ListenerRegressionTests.FakeDriver();
        AutoReply(fake, 11);
        AutoReply(otherFake, 22);
        Can can = Open(fake);
        Can other = Open(otherFake);
        var result = new TaskCompletionSource<bool>();
        try
        {
            can.SDOread(1, 0x2000, 0, transfer =>
            {
                SDO nested = can.SDOread(1, 0x2001, 0, null);
                result.SetResult(nested.WaitOne(2000) && nested.state == SDO.SDO_STATE.SDO_FINISHED);
            });
            Require(result.Task.Wait(3000) && result.Task.Result, "Callback waiting for another SDO blocked listener");
            var transfers = new ConcurrentBag<Tuple<SDO, uint>>();
            Parallel.For(0, 100, index =>
            {
                Can owner = index % 2 == 0 ? can : other;
                transfers.Add(Tuple.Create(owner.SDOread(1, (ushort)(0x2100 + index), 0, null),
                    index % 2 == 0 ? 11U : 22U));
            });
            foreach (var transfer in transfers)
            {
                Require(transfer.Item1.WaitOne(4000) && transfer.Item1.state == SDO.SDO_STATE.SDO_FINISHED
                    && transfer.Item1.expitideddata == transfer.Item2, "Concurrent connection/producer lost or crossed a transaction");
            }
            Require(can.ListenerRestartCount == 0 && other.ListenerRestartCount == 0, "Concurrency crashed a listener");
        }
        finally
        {
            can.close();
            other.close();
        }
    }

    private static void TimeoutFlushAndClose()
    {
        var fake = new ListenerRegressionTests.FakeDriver();
        Can can = Open(fake);
        int completed = 0;
        try
        {
            SDO first = can.SDOread(1, 0x2200, 0, transfer => Interlocked.Increment(ref completed));
            SDO queued = can.SDOread(1, 0x2201, 0, transfer => Interlocked.Increment(ref completed));
            Await(() => fake.Outgoing.Count == 1, "Timeout transfer not sent");
            can.flushSDOqueue();
            Require(queued.WaitOne(1000) && queued.state == SDO.SDO_STATE.SDO_ERROR, "Flushed queue stranded waiter");
            Require(first.WaitOne(2500) && first.state == SDO.SDO_STATE.SDO_ERROR
                && first.expitideddata == 0x05040000, "Timeout did not finish exactly once");
            SDO active = can.SDOread(1, 0x2202, 0, transfer => Interlocked.Increment(ref completed));
            SDO pending = can.SDOread(1, 0x2203, 0, transfer => Interlocked.Increment(ref completed));
            Await(() => fake.Outgoing.Count == 2, "Timeout blocked next transaction");
            can.close();
            Require(active.WaitOne(1000) && pending.WaitOne(1000)
                && active.state == SDO.SDO_STATE.SDO_ERROR && pending.state == SDO.SDO_STATE.SDO_ERROR,
                "Close stranded active or pending SDO");
            Await(() => Volatile.Read(ref completed) == 4, "Cancellation callback count wrong");
            SDO closed = can.SDOread(1, 0x2204, 0, null);
            Require(closed.WaitOne(1000) && closed.state == SDO.SDO_STATE.SDO_ERROR, "Closed connection request stranded waiter");
            Require(can.OpenDriver(fake.Driver, "fake", BUSSPEED.BUS_500Kbit), "SDO test reopen failed");
            AutoReply(fake, 33);
            SDO reopened = can.SDOread(1, 0x2205, 0, null);
            Require(reopened.WaitOne(2000) && reopened.expitideddata == 33, "Reopen retained stale registration");
        }
        finally
        {
            can.close();
        }
    }

    private static void SegmentedAndAbort()
    {
        var fake = new ListenerRegressionTests.FakeDriver();
        Can can = Open(fake);
        try
        {
            SDO read = can.SDOread(1, 0x2300, 0, null);
            Await(() => fake.Outgoing.Count == 1, "Segmented read not sent");
            Reply(fake, 0x2300, 0x41, value: 8);
            Await(() => fake.Outgoing.Count == 2, "Segment request missing");
            fake.Queue(0x581, 8, 0x0706050403020100);
            Await(() => fake.Outgoing.Count == 3, "Next segment request missing");
            fake.Queue(0x581, 8, 0x081D);
            Require(read.WaitOne(2000) && read.databuffer.SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
                "Segmented upload did not release channel");
            SDO write = can.SDOwrite(1, 0x2301, 0, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, null);
            Await(() => fake.Outgoing.Count == 4, "Segmented write not sent");
            Reply(fake, 0x2301, 0x60);
            Await(() => fake.Outgoing.Count == 5, "Write segment missing");
            fake.Queue(0x581, 8, 0x20);
            Await(() => fake.Outgoing.Count == 6, "Final write segment missing");
            fake.Queue(0x581, 8, 0x30);
            Require(write.WaitOne(2000) && write.state == SDO.SDO_STATE.SDO_FINISHED, "Segmented download did not finish");
            SDO abort = can.SDOread(1, 0x2302, 0, null);
            Await(() => fake.Outgoing.Count == 7, "Abort transfer not sent");
            Reply(fake, 0x2302, 0x80, value: 0x06020000);
            Require(abort.WaitOne(2000) && abort.state == SDO.SDO_STATE.SDO_ERROR && abort.expitideddata == 0x06020000,
                "Abort did not finish transfer");
        }
        finally
        {
            can.close();
        }
    }
}
