using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace libCanopenSimple
{
    /// <summary>A typed OD download used by a configuration batch.</summary>
    public sealed class SdoParameterWrite
    {
        public ushort Index { get; private set; }
        public byte Subindex { get; private set; }
        public byte[] Data { get; private set; }

        public SdoParameterWrite(ushort index, byte subindex, byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentException("A parameter value is required.", "data");
            }
            Index = index;
            Subindex = subindex;
            Data = (byte[])data.Clone();
        }
    }

    internal interface IPdoConfigurationIo
    {
        byte[] Read(ushort index, byte subindex);
        void Write(ushort index, byte subindex, byte[] data);
        void SetPreOperational();
        void SetOperational();
    }

    public sealed class PdoConfigurationResult
    {
        public bool NmtStateChanged { get; internal set; }
        public bool NmtStateConfirmed { get; internal set; }
    }

    /// <summary>Applies PDO communication/mapping changes as one node configuration.</summary>
    public static class PdoConfiguration
    {
        private const uint Invalid = 0x80000000;

        public static bool IsPdoParameter(ushort index)
        {
            return index >= 0x1400 && index <= 0x1BFF;
        }

        private static bool IsMapping(ushort index)
        {
            return (index >= 0x1600 && index <= 0x17FF) || (index >= 0x1A00 && index <= 0x1BFF);
        }

        /// <summary>
        /// Applies writes off the calling thread, holding exclusive SDO admission
        /// for this node. Confirms NMT state when heartbeat is configured;
        /// otherwise reports the NMT command as unconfirmed without enabling
        /// heartbeat or modifying unrelated device parameters.
        /// On failure no further parameters or Operational command are sent.
        /// This does not store configuration in nonvolatile memory.
        /// </summary>
        public static Task<PdoConfigurationResult> ApplyAsync(libCanopenSimple can, byte node, IEnumerable<SdoParameterWrite> writes)
        {
            if (can == null || node == 0 || node > 127)
            {
                throw new ArgumentException("An open connection and node 1 through 127 are required.");
            }
            var snapshot = writes.Select(write => new SdoParameterWrite(write.Index, write.Subindex, write.Data)).ToArray();
            return Task.Run(() => can.WithExclusiveSdo(node, () =>
            {
                var io = new CanIo(can, node);
                Apply(io, snapshot);
                return new PdoConfigurationResult { NmtStateChanged = snapshot.Any(write => IsPdoParameter(write.Index)),
                    NmtStateConfirmed = io.StateConfirmed };
            }));
        }

        private sealed class Group
        {
            internal ushort Communication;
            internal ushort Mapping;
            internal SdoParameterWrite[] Writes;
            internal uint FinalCob;
            internal byte FinalCount;
            internal bool Remap;
        }

        internal static void Apply(IPdoConfigurationIo io, SdoParameterWrite[] writes)
        {
            if (writes.GroupBy(write => ((uint)write.Index << 8) | write.Subindex).Any(group => group.Count() != 1))
            {
                throw new ArgumentException("A configuration batch contains duplicate OD entries.");
            }
            var groups = writes.Where(write => IsPdoParameter(write.Index))
                .GroupBy(write => (ushort)(IsMapping(write.Index) ? write.Index - 0x200 : write.Index))
                .OrderBy(group => group.Key)
                .Select(group => new Group { Communication = group.Key, Mapping = (ushort)(group.Key + 0x200), Writes = group.ToArray() })
                .ToArray();
            // Reject malformed input before changing the device's NMT state.
            foreach (var group in groups)
            {
                foreach (var write in group.Writes)
                {
                    int width;
                    if (IsMapping(write.Index))
                    {
                        width = write.Subindex == 0 ? 1 : 4;
                    }
                    else
                    {
                        switch (write.Subindex)
                        {
                            case 1: width = 4; break;
                            case 2: case 6: width = 1; break;
                            case 3: case 5: width = 2; break;
                            default: throw new ArgumentException("PDO communication subindex 0/reserved fields cannot be downloaded.");
                        }
                    }
                    if (write.Data.Length != width)
                    {
                        throw new ArgumentException(string.Format("Wrong width for {0:X4}:{1:X2}; expected {2} bytes.", write.Index, write.Subindex, width));
                    }
                }
            }
            if (groups.Length != 0)
            {
                io.SetPreOperational();
            }
            // Snapshot and validate every affected PDO before disabling any.
            foreach (var group in groups)
            {
                uint original = ReadU32(io, group.Communication, 1);
                var requestedCob = group.Writes.FirstOrDefault(write => write.Index == group.Communication && write.Subindex == 1);
                group.FinalCob = requestedCob == null ? original : BitConverter.ToUInt32(requestedCob.Data, 0);
                group.Remap = group.Writes.Any(write => write.Index == group.Mapping);
                if (group.Remap)
                {
                    var requestedCount = group.Writes.FirstOrDefault(write => write.Index == group.Mapping && write.Subindex == 0);
                    group.FinalCount = requestedCount == null ? ReadByte(io, group.Mapping, 0) : requestedCount.Data[0];
                    int bits = 0;
                    for (int sub = 1; sub <= group.FinalCount; sub++)
                    {
                        var entry = group.Writes.FirstOrDefault(write => write.Index == group.Mapping && write.Subindex == sub);
                        uint mapping = entry == null ? ReadU32(io, group.Mapping, (byte)sub) : BitConverter.ToUInt32(entry.Data, 0);
                        int length = (byte)mapping;
                        if (length == 0 || length > 64 || (bits += length) > 64)
                        {
                            throw new ArgumentException("Active classic-CAN PDO mappings must contain 1 through 64 bits per entry and at most 64 bits total.");
                        }
                    }
                }
            }
            foreach (var group in groups)
            {
                // Disable using the device's current identifier first. Only then
                // change the identifier/flags requested by the caller.
                uint original = ReadU32(io, group.Communication, 1);
                CheckedWrite(io, group.Communication, 1, BitConverter.GetBytes(original | Invalid));
            }
            foreach (var group in groups)
            {
                if (group.Remap)
                {
                    CheckedWrite(io, group.Mapping, 0, new byte[] { 0 });
                }
                foreach (var write in group.Writes.OrderBy(write => write.Index).ThenBy(write => write.Subindex))
                {
                    if (write.Index == group.Mapping && write.Subindex == 0)
                    {
                        continue;
                    }
                    byte[] data = write.Index == group.Communication && write.Subindex == 1
                        ? BitConverter.GetBytes(group.FinalCob | Invalid) : write.Data;
                    CheckedWrite(io, write.Index, write.Subindex, data);
                }
                if (group.Remap)
                {
                    CheckedWrite(io, group.Mapping, 0, new byte[] { group.FinalCount });
                }
            }
            foreach (var write in writes.Where(write => !IsPdoParameter(write.Index)))
            {
                CheckedWrite(io, write.Index, write.Subindex, write.Data);
            }
            // Enable only after all groups and other batch entries succeeded.
            // Preserve an intentionally disabled PDO and every other COB-ID bit.
            foreach (var group in groups)
            {
                CheckedWrite(io, group.Communication, 1, BitConverter.GetBytes(group.FinalCob));
            }
            if (groups.Length != 0)
            {
                io.SetOperational();
            }
        }

        private static void CheckedWrite(IPdoConfigurationIo io, ushort index, byte subindex, byte[] data)
        {
            io.Write(index, subindex, data);
            byte[] actual = io.Read(index, subindex);
            if (!data.SequenceEqual(actual))
            {
                throw new InvalidOperationException(string.Format("Read-back mismatch at {0:X4}:{1:X2}. Configuration stopped; inspect the PDO/NMT state before resuming.", index, subindex));
            }
        }

        private static uint ReadU32(IPdoConfigurationIo io, ushort index, byte subindex)
        {
            byte[] data = io.Read(index, subindex);
            if (data.Length != 4)
            {
                throw new InvalidOperationException("Expected a 32-bit PDO parameter.");
            }
            return BitConverter.ToUInt32(data, 0);
        }

        private static byte ReadByte(IPdoConfigurationIo io, ushort index, byte subindex)
        {
            byte[] data = io.Read(index, subindex);
            if (data.Length != 1)
            {
                throw new InvalidOperationException("Expected an 8-bit mapping count.");
            }
            return data[0];
        }

        private sealed class CanIo : IPdoConfigurationIo
        {
            internal bool StateConfirmed;
            private readonly libCanopenSimple can;
            private readonly byte node;

            internal CanIo(libCanopenSimple can, byte node)
            {
                this.can = can;
                this.node = node;
            }

            public byte[] Read(ushort index, byte subindex)
            {
                SDO transfer = can.SDOread(node, index, subindex, null);
                Check(transfer);
                int length = transfer.expitided ? transfer.returnlen / 8 : transfer.databuffer.Length;
                return transfer.databuffer.Take(length).ToArray();
            }

            public void Write(ushort index, byte subindex, byte[] data)
            {
                Check(can.SDOwrite(node, index, subindex, data, null));
            }

            private static void Check(SDO transfer)
            {
                if (!transfer.WaitOne(3500))
                {
                    transfer.SDOFinish();
                    throw new TimeoutException("Configuration SDO did not complete.");
                }
                if (transfer.state != SDO.SDO_STATE.SDO_FINISHED)
                {
                    throw new InvalidOperationException(string.Format("SDO {0:X4}:{1:X2} failed: 0x{2:X8}. Configuration stopped; the node/PDO may remain pre-operational/disabled.",
                        transfer.index, transfer.subindex, transfer.expitideddata));
                }
            }

            public void SetPreOperational()
            {
                SetState(127, () => can.NMT_preop(node));
            }

            public void SetOperational()
            {
                SetState(5, () => can.NMT_start(node));
            }

            private void SetState(byte state, Action command)
            {
                byte[] periodBytes = Read(0x1017, 0);
                if (periodBytes.Length != 2)
                {
                    throw new InvalidOperationException("Expected a 16-bit heartbeat producer period.");
                }
                int period = BitConverter.ToUInt16(periodBytes, 0);
                StateConfirmed = false;
                if (period == 0)
                {
                    // NMT is an unconfirmed CANopen service. With heartbeat
                    // disabled, send the targeted command before subsequent SDO
                    // traffic and allow the device's application loop to run.
                    // Do not silently enable heartbeat just to obtain an ACK.
                    command();
                    Thread.Sleep(50);
                    return;
                }
                var changed = new TaskCompletionSource<bool>();
                {
                    libCanopenSimple.NMTECEvent handler = (packet, timestamp) =>
                    {
                        if (packet.cob == 0x700 + node && packet.data.Length == 1 && packet.data[0] == state)
                        {
                            changed.TrySetResult(true);
                        }
                    };
                    can.nmtecevent += handler;
                    try
                    {
                        command();
                        if (!changed.Task.Wait(Math.Max(4000, period * 2 + 250)))
                        {
                            throw new TimeoutException("No heartbeat confirmation of requested NMT state " + state + "; configuration stopped.");
                        }
                        StateConfirmed = true;
                    }
                    finally
                    {
                        can.nmtecevent -= handler;
                    }
                }
            }
        }
    }
}
