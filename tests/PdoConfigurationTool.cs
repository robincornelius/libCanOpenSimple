using System;
using System.Globalization;
using System.IO;
using libCanopenSimple;
using Can = libCanopenSimple.libCanopenSimple;

internal static class PdoConfigurationTool
{
    private static byte[] Read(Can can, byte node, ushort index, byte subindex)
    {
        SDO transfer = can.SDOread(node, index, subindex, null);
        if (!transfer.WaitOne(3000) || transfer.state != SDO.SDO_STATE.SDO_FINISHED)
        {
            throw new InvalidOperationException(string.Format("Read {0:X4}:{1:X2} failed: 0x{2:X8}", index, subindex, transfer.expitideddata));
        }
        int length = transfer.expitided ? transfer.returnlen / 8 : transfer.databuffer.Length;
        var result = new byte[length];
        Array.Copy(transfer.databuffer, result, length);
        return result;
    }

    private static int Main(string[] args)
    {
        // No hardware is opened without an explicit operation and connection.
        if ((args.Length != 6 || args[0] != "inspect") && (args.Length != 11 || args[0] != "write-pdo"))
        {
            Console.Error.WriteLine("inspect <app-directory> <driver-name> <port> <bitrate> <node-hex>");
            Console.Error.WriteLine("write-pdo <app-directory> <driver-name> <port> <bitrate> <node-hex> <index-hex> <sub-hex> <expected-decimal> <value-decimal> <width>");
            return 2;
        }
        var can = new Can();
        try
        {
            string directory = Path.GetFullPath(args[1]);
            Environment.CurrentDirectory = directory;
            byte node = byte.Parse(args[5], NumberStyles.HexNumber);
            BUSSPEED speed = (BUSSPEED)Enum.Parse(typeof(BUSSPEED), "BUS_" + args[4] + "Kbit");
            can.ListenerDiagnostic += (sender, error) => Console.Error.WriteLine(error.Operation + ": " + error.Exception.Message);
            can.packetevent += (packet, time) =>
            {
                if ((packet.cob == 0 && packet.len == 2 && packet.data[1] == node)
                    || packet.cob == 0x600 + node || packet.cob == 0x580 + node || packet.cob == 0x700 + node)
                {
                    Console.WriteLine(time.ToString("HH:mm:ss.fff") + " " + packet);
                }
            };
            if (!can.open(args[3], speed, Path.Combine(directory, "drivers", args[2])))
            {
                throw new InvalidOperationException("Cannot open adapter: " + can.LastListenerError?.Exception.Message);
            }
            uint cob = BitConverter.ToUInt32(Read(can, node, 0x1800, 1), 0);
            ushort inhibit = BitConverter.ToUInt16(Read(can, node, 0x1800, 3), 0);
            Console.WriteLine(string.Format("BEFORE: COB-ID=0x{0:X8}; inhibit={1} (100 us units)", cob, inhibit));
            if (args[0] == "inspect")
            {
                ushort heartbeat = BitConverter.ToUInt16(Read(can, node, 0x1017, 0), 0);
                Console.WriteLine("Heartbeat producer period (0x1017:00)=" + heartbeat + " ms");
                return 0;
            }
            ushort index = ushort.Parse(args[6], NumberStyles.HexNumber);
            byte sub = byte.Parse(args[7], NumberStyles.HexNumber);
            uint expected = uint.Parse(args[8], CultureInfo.InvariantCulture);
            uint value = uint.Parse(args[9], CultureInfo.InvariantCulture);
            int width = int.Parse(args[10], CultureInfo.InvariantCulture);
            if (!PdoConfiguration.IsPdoParameter(index) || (width != 1 && width != 2 && width != 4))
            {
                throw new ArgumentException("Select a PDO parameter and width 1, 2 or 4.");
            }
            byte[] original = Read(can, node, index, sub);
            byte[] expectedBytes = BitConverter.GetBytes(expected);
            for (int offset = 0; offset < 4; offset++)
            {
                if ((offset < original.Length ? original[offset] : 0) != expectedBytes[offset])
                {
                    throw new InvalidOperationException("Current parameter differs from the expected value; no configuration was sent.");
                }
            }
            if (original.Length != width || (width < 4 && value >= (1U << (width * 8))))
            {
                throw new ArgumentException("Parameter width/value mismatch; no configuration was sent.");
            }
            var data = new byte[width];
            Array.Copy(BitConverter.GetBytes(value), data, width);
            PdoConfigurationResult result = PdoConfiguration.ApplyAsync(can, node, new[] { new SdoParameterWrite(index, sub, data) }).GetAwaiter().GetResult();
            uint afterCob = BitConverter.ToUInt32(Read(can, node, 0x1800, 1), 0);
            ushort afterInhibit = BitConverter.ToUInt16(Read(can, node, 0x1800, 3), 0);
            Console.WriteLine(string.Format("AFTER: COB-ID=0x{0:X8}; inhibit={1}; {2}. No nonvolatile-store command sent.", afterCob, afterInhibit,
                result.NmtStateConfirmed ? "Operational heartbeat confirmed" : "Operational NMT sent; heartbeat disabled, NMT state unconfirmed"));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAILED: " + error.Message);
            return 1;
        }
        finally
        {
            can.close();
        }
    }
}
