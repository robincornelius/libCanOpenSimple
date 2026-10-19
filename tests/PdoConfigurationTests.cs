using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using libCanopenSimple;
using Can = libCanopenSimple.libCanopenSimple;

internal static class PdoConfigurationTests
{
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private sealed class Device : IPdoConfigurationIo
    {
        internal readonly Dictionary<int, byte[]> Values = new Dictionary<int, byte[]>();
        internal readonly List<string> Operations = new List<string>();
        internal int FailIndex = -1;
        internal bool Mismatch;
        internal bool Written;

        internal Device()
        {
            Set(0x1017, 0, BitConverter.GetBytes((ushort)100));
            Set(0x1800, 1, BitConverter.GetBytes(0x40000181U));
            Set(0x1800, 3, BitConverter.GetBytes((ushort)2000));
            Set(0x1A00, 0, new byte[] { 1 });
            Set(0x1A00, 1, BitConverter.GetBytes(0x20000010U));
            Set(0x1A00, 2, BitConverter.GetBytes(0x20010010U));
        }

        internal void Set(ushort index, byte subindex, byte[] data)
        {
            Values[(index << 8) | subindex] = data;
        }

        public byte[] Read(ushort index, byte subindex)
        {
            if (Mismatch && Written && index == 0x1800 && subindex == 3)
            {
                return new byte[] { 1, 0 };
            }
            return (byte[])Values[(index << 8) | subindex].Clone();
        }

        public void Write(ushort index, byte subindex, byte[] data)
        {
            Operations.Add(string.Format("{0:X4}:{1:X2}={2}", index, subindex, BitConverter.ToString(data)));
            if (index == FailIndex)
            {
                throw new InvalidOperationException("Injected SDO abort");
            }
            Set(index, subindex, data);
            Written = true;
        }

        public void SetPreOperational()
        {
            Operations.Add("PREOP");
        }
        public void SetOperational()
        {
            Operations.Add("OPERATIONAL");
        }
    }

    internal static void Run()
    {
        var device = new Device();
        var inhibit = new SdoParameterWrite(0x1800, 3, BitConverter.GetBytes((ushort)200));
        PdoConfiguration.Apply(device, new[] { inhibit });
        Require(device.Operations.SequenceEqual(new[] { "PREOP", "1800:01=81-01-00-C0", "1800:03=C8-00", "1800:01=81-01-00-40", "OPERATIONAL" }),
            "Inhibit-time handshake or COB-ID flags were changed");

        device = new Device();
        PdoConfiguration.Apply(device, new[] {
            inhibit,
            new SdoParameterWrite(0x1A00, 2, BitConverter.GetBytes(0x20020020U)),
            new SdoParameterWrite(0x1A00, 0, new byte[] { 2 }),
            new SdoParameterWrite(0x1800, 1, BitConverter.GetBytes(0x40000281U)) });
        Require(device.Operations.SequenceEqual(new[] { "PREOP", "1800:01=81-01-00-C0", "1A00:00=00",
            "1800:01=81-02-00-C0", "1800:03=C8-00", "1A00:02=20-00-02-20", "1A00:00=02", "1800:01=81-02-00-40", "OPERATIONAL" }),
            "Combined mapping/communication batch had incorrect ordering");

        device = new Device();
        device.Set(0x1800, 1, BitConverter.GetBytes(0xC0000181U));
        PdoConfiguration.Apply(device, new[] { inhibit });
        Require(BitConverter.ToUInt32(device.Read(0x1800, 1), 0) == 0xC0000181, "Originally disabled PDO was enabled");

        device = new Device { FailIndex = 0x1A00 };
        bool failed = false;
        try
        {
            PdoConfiguration.Apply(device, new[] { new SdoParameterWrite(0x1A00, 1, BitConverter.GetBytes(0x20010010U)) });
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }
        Require(failed && !device.Operations.Contains("OPERATIONAL") && (BitConverter.ToUInt32(device.Read(0x1800, 1), 0) & 0x80000000) != 0,
            "Abort resumed an unverified PDO or node");

        device = new Device { Mismatch = true };
        failed = false;
        try
        {
            PdoConfiguration.Apply(device, new[] { inhibit });
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }
        Require(failed && !device.Operations.Contains("OPERATIONAL"), "Read-back mismatch was ignored");

        device = new Device();
        failed = false;
        try
        {
            PdoConfiguration.Apply(device, new[] { new SdoParameterWrite(0x1A00, 0, new byte[] { 2 }),
                new SdoParameterWrite(0x1A00, 1, BitConverter.GetBytes(0x20000040U)) });
        }
        catch (ArgumentException)
        {
            failed = true;
        }
        Require(failed && device.Operations.SequenceEqual(new[] { "PREOP" }), "Invalid mapping was written or enabled");
        ExclusiveAdmission();
        ProtocolHandshake();
        Console.WriteLine("PASS: PDO inhibit/remapping handshake, flags, counts, verification, abort and exclusive SDO admission");
    }

    private static void ExclusiveAdmission()
    {
        var fake = new ListenerRegressionTests.FakeDriver();
        var can = new Can { echo = false };
        Require(can.OpenDriver(fake.Driver, "fake", BUSSPEED.BUS_500Kbit), "Exclusive test open failed");
        var entered = new ManualResetEvent(false);
        var release = new ManualResetEvent(false);
        try
        {
            Task task = Task.Run(() => can.WithExclusiveSdo(1, () =>
            {
                entered.Set();
                release.WaitOne();
                return true;
            }));
            Require(entered.WaitOne(1000), "Exclusive configuration did not start");
            SDO held = can.SDOread(1, 0x2000, 0, null);
            SDO other = can.SDOread(2, 0x2000, 0, null);
            Require(SpinWait.SpinUntil(() => fake.Outgoing.Any(message => message.cob_id == 0x602), 1000), "Exclusive node blocked an independent node");
            Thread.Sleep(30);
            Require(!fake.Outgoing.Any(message => message.cob_id == 0x601), "Ordinary SDO interleaved with configuration");
            release.Set();
            Require(task.Wait(1000), "Exclusive session did not release");
            Require(SpinWait.SpinUntil(() => fake.Outgoing.Any(message => message.cob_id == 0x601), 1000), "Held SDO was stranded after configuration");
        }
        finally
        {
            release.Set();
            can.close();
        }
    }

    private static void ProtocolHandshake()
    {
        var fake = new ListenerRegressionTests.FakeDriver();
        var device = new Device();
        byte state = 5;
        fake.OnSend = message =>
        {
            if (message.cob_id == 0)
            {
                Require(message.len == 2 && (byte)(message.data >> 8) == 1, "Configuration used broadcast NMT");
                state = (byte)message.data == 0x80 ? (byte)127 : (byte)5;
                if (BitConverter.ToUInt16(device.Read(0x1017, 0), 0) != 0)
                {
                    fake.Queue(0x701, 1, state);
                }
                return;
            }
            ushort index = (ushort)(message.data >> 8);
            byte sub = (byte)(message.data >> 24);
            byte command = (byte)message.data;
            ulong address = message.data & 0xFFFFFF00UL;
            if (command == 0x40)
            {
                if (!device.Values.ContainsKey((index << 8) | sub))
                {
                    fake.Queue(0x581, 8, address | 0x80 | ((ulong)0x06090011 << 32));
                    return;
                }
                byte[] data = device.Read(index, sub);
                var padded = new byte[4];
                Array.Copy(data, padded, data.Length);
                byte response = (byte)(0x43 | ((4 - data.Length) << 2));
                fake.Queue(0x581, 8, address | response | ((ulong)BitConverter.ToUInt32(padded, 0) << 32));
            }
            else
            {
                Require(state == 127, "Parameter write occurred outside pre-operational state");
                if (index == 0x1800 && sub != 1)
                {
                    Require((BitConverter.ToUInt32(device.Read(0x1800, 1), 0) & 0x80000000) != 0, "Parameter changed on a valid PDO");
                }
                byte[] data = BitConverter.GetBytes((uint)(message.data >> 32)).Take(4 - ((command >> 2) & 3)).ToArray();
                device.Write(index, sub, data);
                fake.Queue(0x581, 8, address | 0x60);
            }
        };
        var can = new Can { echo = false };
        Require(can.OpenDriver(fake.Driver, "fake", BUSSPEED.BUS_500Kbit), "Protocol configuration test failed to open");
        try
        {
            var configuration = PdoConfiguration.ApplyAsync(can, 1,
                new[] { new SdoParameterWrite(0x1800, 3, BitConverter.GetBytes((ushort)200)) });
            Require(configuration.Wait(5000), "Full protocol handshake did not complete");
            Require(configuration.Result.NmtStateConfirmed, "Configured heartbeat did not confirm NMT");
            Require(state == 5 && BitConverter.ToUInt16(device.Read(0x1800, 3), 0) == 200
                && BitConverter.ToUInt32(device.Read(0x1800, 1), 0) == 0x40000181, "Protocol handshake read-back or restoration failed");
            device.Set(0x1017, 0, BitConverter.GetBytes((ushort)0));
            var disabledHeartbeat = PdoConfiguration.ApplyAsync(can, 1,
                new[] { new SdoParameterWrite(0x1800, 3, BitConverter.GetBytes((ushort)300)) });
            Require(disabledHeartbeat.Wait(5000) && !disabledHeartbeat.Result.NmtStateConfirmed
                && BitConverter.ToUInt16(device.Read(0x1800, 3), 0) == 300,
                "Disabled heartbeat prevented configuration or was falsely reported as confirmed");
            int commands = fake.Outgoing.Count(message => message.cob_id == 0);
            var read = PdoConfiguration.ReadAsync(can, 1, 0x1800, new byte[] { 0, 1, 2, 3 });
            Require(read.Wait(4000) && read.Result.Communication[3].Length == 2
                && BitConverter.ToUInt16(read.Result.Communication[3], 0) == 300
                && read.Result.Unavailable.ContainsKey(0) && read.Result.Unavailable.ContainsKey(2)
                && read.Result.Mappings.SequenceEqual(new uint[] { 0x20000010 }),
                "PDO snapshot lost widths, mappings or optional-field errors");
            Require(fake.Outgoing.Count(message => message.cob_id == 0) == commands,
                "Opening a PDO snapshot changed NMT state");
            device.Values.Remove(0x1A00 << 8);
            var unavailable = PdoConfiguration.ReadAsync(can, 1, 0x1800, new byte[] { 1, 3 });
            Require(unavailable.Wait(4000) && unavailable.Result.MappingError != null && unavailable.Result.Mappings.Count == 0,
                "Unavailable mapping prevented communication-parameter inspection");
        }
        finally
        {
            can.close();
        }
    }
}
