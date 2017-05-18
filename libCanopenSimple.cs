using System;
using System.Collections.Generic;
using System.Threading;
using System.Collections.Concurrent;
using System.Reflection;

namespace libCanopenSimple
{
    public enum BUSSPEED
    {

        BUS_10Kbit = 0,
        BUS_20Kbit,
        BUS_50Kbit,
        BUS_100Kbit,
        BUS_125Kbit,
        BUS_250Kbit,
        BUS_500Kbit,
        BUS_800Kbit,
        BUS_1Mbit,

    }

    public enum debuglevel
    {
        DEBUG_ALL,
        DEBUG_NONE
    }

    public class canpacket
    {
        public UInt16 cob;
        public byte len;
        public byte[] data;
     
        

        public string ToString()
        {
            string output;

            output = string.Format("{0:x3} {1:x1}", cob, len);

            for(int x=0;x<len;x++)
            {
                output += string.Format(" {0:x2}", data[x]);
            }
            return output;
        }

    }

    public class NMTState
    {
        public enum e_NMTState
        {
            BOOT = 0,
            STOPPED = 4,
            OPERATIONAL = 5,
            PRE_OPERATIONAL = 127,
            INVALID = 0xff,

        }

        public e_NMTState state;
        public e_NMTState laststate;
        public DateTime lastping;
        public bool compulsory;

        public Action<e_NMTState> NMT_boot = null;
        public Action<int> NMT_guard = null;

        public NMTState()
        {
            state = e_NMTState.INVALID;
            laststate = e_NMTState.INVALID;
        }

        public void changestate(e_NMTState newstate)
        {
            laststate = state;
            state = newstate;
            lastping = DateTime.Now;

            if (newstate == e_NMTState.BOOT)
            {
                if (state != laststate && NMT_boot != null)
                {
                    NMT_boot(state);
                }
            }
        }
    }

    public class libCanopen
    {

        public debuglevel dbglevel = debuglevel.DEBUG_NONE;
        public bool echo = true;

        public bool ipcisopen = false;

        DriverLoader loader;
        
       
        DriverInstance driver;

        Dictionary<UInt16, NMTState> nmtstate = new Dictionary<ushort, NMTState>();

        private Queue<SDO> sdo_queue = new Queue<SDO>();

        public libCanopen()
        {

            loader = new DriverLoader();
          
            //preallocate all NMT guards
            for (byte x=0;x<0x80;x++)
            {
                NMTState nmt = new NMTState();
                nmtstate[x] = nmt;
            }
        }

        #region driverinterface

        public void open(int comport, BUSSPEED speed)
        {

            driver = loader.loaddriver("can_usb_win32");
            DriverInstance.struct_s_BOARD brd = new DriverInstance.struct_s_BOARD();
            brd.busname = string.Format("COM{0}", comport);
            brd.baudrate = "500K";
            driver.open(brd);

            driver.rxmessage += Driver_rxmessage;

            threadrun = true;
            Thread thread = new Thread(new ThreadStart(asyncprocess));
            thread.Name = "CAN Open worker";
            thread.Start();

        }

        public bool isopen()
        {
            //FIXME
            return true;
        }

        public void SendPacket(canpacket p)
        {

            DriverInstance.Message msg = new DriverInstance.Message();
            msg.cob_id = p.cob;
            msg.len = p.len;
            msg.rtr = 0;

            byte[] temp = new byte[8];
            Array.Copy(p.data, temp, p.len);
            msg.data = BitConverter.ToUInt64(temp,0);

            driver.cansend(msg);

            if (echo == true)
            {
                Driver_rxmessage(msg);
            }

        }

        private void Driver_rxmessage(DriverInstance.Message msg)
        {
            canpacket cp = new canpacket();
            cp.cob = msg.cob_id;
            cp.len = msg.len;
            cp.data = new byte[cp.len];

            byte[] temp = BitConverter.GetBytes(msg.data);
            Array.Copy(temp, cp.data, msg.len);
            packetqueue.Enqueue(cp);
        }


        public void close()
        {
            if (driver == null)
                return;

            driver.close();
        }

        #endregion

        Dictionary<UInt16, Action<byte[]>> PDOcallbacks = new Dictionary<ushort, Action<byte[]>>();
        public Dictionary<UInt16, SDO> SDOcallbacks = new Dictionary<ushort, SDO>();
        ConcurrentQueue<canpacket> packetqueue = new ConcurrentQueue<canpacket>();

        public delegate void SDOEvent(canpacket p);
        public event SDOEvent sdoevent;

        public delegate void NMTEvent(canpacket p);
        public event NMTEvent nmtevent;

        public delegate void NMTECEvent(canpacket p);
        public event NMTECEvent nmtecevent;

        public delegate void PDOEvent(canpacket[] p);
        public event PDOEvent pdoevent;

        public delegate void EMCYEvent(canpacket p);
        public event EMCYEvent emcyevent;

        public delegate void LSSEvent(canpacket p);
        public event LSSEvent lssevent;

        public delegate void TIMEEvent(canpacket p);
        public event TIMEEvent timeevent;

        public delegate void SYNCEvent(canpacket p);
        public event SYNCEvent syncevent;

        bool threadrun = true;

        public void registerPDOhandler(UInt16 cob, Action<byte[]> handler)
        {
            PDOcallbacks[cob] = handler;
        }

        void asyncprocess()
        {
            while (threadrun)
            {
                canpacket cp;
                List<canpacket> pdos = new List<canpacket>();
                while (packetqueue.TryDequeue(out cp))
                {
                    //PDO 0x180 -- 0x57F
                    if (cp.cob >= 0x180 && cp.cob <= 0x57F)
                    {

                        if (PDOcallbacks.ContainsKey(cp.cob))
                            PDOcallbacks[cp.cob](cp.data);

                        pdos.Add(cp);
                    }

                    //SDO replies 0x601-0x67F
                    if (cp.cob >= 0x580 && cp.cob < 0x600)
                    {
                        if (cp.len != 8)
                            return;

                        if (SDOcallbacks.ContainsKey(cp.cob))
                        {
                            if (SDOcallbacks[cp.cob].SDOProcess(cp))
                            {
                                SDOcallbacks.Remove(cp.cob);
                            }
                        }

                        if (sdoevent != null)
                            sdoevent(cp);
                    }

                    if (cp.cob >= 0x600 && cp.cob < 0x680)
                    {
                        if (sdoevent != null)
                            sdoevent(cp);
                    }

                    //NMT
                    if (cp.cob > 0x700 && cp.cob <= 0x77f)
                    {
                        byte node = (byte)(cp.cob & 0x07F);

                        nmtstate[node].changestate((NMTState.e_NMTState)cp.data[0]);
                        nmtstate[node].lastping = DateTime.Now;

                        if (nmtecevent != null)
                            nmtecevent(cp);
                    }

                    if(cp.cob==000)
                    {

                        if (nmtevent != null)
                            nmtevent(cp);
                    }
                    if (cp.cob == 0x80)
                    {
                        if (syncevent != null)
                            syncevent(cp);
                    }

                    if (cp.cob > 0x080 && cp.cob <= 0xFF)
                    {
                        if(emcyevent!=null)
                        {
                            emcyevent(cp);
                        }
                    }

                    if (cp.cob == 0x100)
                    {
                        if (timeevent != null)
                            timeevent(cp);
                    }

                    if (cp.cob > 0x7E4 && cp.cob <= 0x7E5)
                    {
                        if (lssevent != null)
                            lssevent(cp);
                    }
                }

                if(pdos.Count>0)
                {
                    if (pdoevent != null)
                        pdoevent(pdos.ToArray());
                }

                    SDO.kick_SDO();

                    if (sdo_queue.Count > 0)
                    {
                        SDO front = sdo_queue.Peek();
                        if (front != null)
                        {
                            if (!SDOcallbacks.ContainsKey((UInt16)(front.node + 0x580)))
                            {
                                front = sdo_queue.Dequeue();
                                //Listen for the reply on 0x580+node id
                                SDOcallbacks.Add((UInt16)(front.node + 0x580), front);
                                front.sendSDO();
                            }
                        }
                    }
                   // System.Threading.Thread.Sleep(1);
            }
        }


        #region SDOHelpers

        public SDO SDOwrite(byte node, UInt16 index, byte subindex, UInt32 udata, Action<SDO> completedcallback)
        {
            byte[] bytes = BitConverter.GetBytes(udata);
            return SDOwrite(node, index, subindex, bytes, completedcallback);
        }

        public SDO SDOwrite(byte node, UInt16 index, byte subindex, Int32 udata, Action<SDO> completedcallback)
        {
            byte[] bytes = BitConverter.GetBytes(udata);
            return SDOwrite(node, index, subindex, bytes, completedcallback);
        }

        public SDO SDOwrite(byte node, UInt16 index, byte subindex, Int16 udata, Action<SDO> completedcallback)
        {
            byte[] bytes = BitConverter.GetBytes(udata);
            return SDOwrite(node, index, subindex, bytes, completedcallback);
        }

        public SDO SDOwrite(byte node, UInt16 index, byte subindex, UInt16 udata, Action<SDO> completedcallback)
        {
            byte[] bytes = BitConverter.GetBytes(udata);
            return SDOwrite(node, index, subindex, bytes, completedcallback);
        }

        public SDO SDOwrite(byte node, UInt16 index, byte subindex, float ddata, Action<SDO> completedcallback)
        {
            byte[] bytes = BitConverter.GetBytes(ddata);
            return SDOwrite(node, index, subindex, bytes, completedcallback);
        }

        public SDO SDOwrite(byte node, UInt16 index, byte subindex, byte udata, Action<SDO> completedcallback)
        {
            byte[] bytes = new byte[1];
            bytes[0] = udata;
            return SDOwrite(node, index, subindex, bytes, completedcallback);
        }

        public SDO SDOwrite(byte node, UInt16 index, byte subindex, sbyte udata, Action<SDO> completedcallback)
        {
            byte[] bytes = new byte[1];
            bytes[0] = (byte)udata;
            return SDOwrite(node, index, subindex, bytes, completedcallback);
        }

        public SDO SDOwrite(byte node, UInt16 index, byte subindex, byte[] data, Action<SDO> completedcallback)
        {

            SDO sdo = new SDO(this, node, index, subindex, SDO.direction.SDO_WRITE, completedcallback, data);
            sdo_queue.Enqueue(sdo);
            return sdo;
        }

        public SDO SDOread(byte node, UInt16 index, byte subindex,Action<SDO> completedcallback)
        {
            SDO sdo = new SDO(this, node, index, subindex, SDO.direction.SDO_READ, completedcallback,null);
            sdo_queue.Enqueue(sdo);
            return sdo;
        }

        #endregion

        #region NMTHelpers

        public void NMT_start(byte nodeid = 0)
        {
            canpacket p = new canpacket();
            p.cob = 000;
            p.len = 2;
            p.data = new byte[2];
            p.data[0] = 0x01;
            p.data[1] = nodeid;
            SendPacket(p);
        }

        public void NMT_preop(byte nodeid = 0)
        {
            canpacket p = new canpacket();
            p.cob = 000;
            p.len = 2;
            p.data = new byte[2];
            p.data[0] = 0x80;
            p.data[1] = nodeid;
            SendPacket(p);
        }

        public void NMT_stop(byte nodeid = 0)
        {
            canpacket p = new canpacket();
            p.cob = 000;
            p.len = 2;
            p.data = new byte[2];
            p.data[0] = 0x80;
            p.data[1] = nodeid;
            SendPacket(p);
        }

        public void NMT_ResetNode(byte nodeid = 0)
        {
            canpacket p = new canpacket();
            p.cob = 000;
            p.len = 2;
            p.data = new byte[2];
            p.data[0] = 0x81;
            p.data[1] = nodeid;

            SendPacket(p);
        }

        public void NMT_ResetComms(byte nodeid = 0)
        {
            canpacket p = new canpacket();
            p.cob = 000;
            p.len = 2;
            p.data = new byte[2];
            p.data[0] = 0x82;
            p.data[1] = nodeid;

            SendPacket(p);
        }

        public void NMT_SetStateTransitionCallback(byte node,Action<NMTState.e_NMTState> callback)
        {
            nmtstate[node].NMT_boot = callback;
        }

        public bool NMT_isNodeFound(byte node)
        {
            return nmtstate[node].state != NMTState.e_NMTState.INVALID;
        }

        public void NMT_ReseCommunication(byte nodeid = 0)
        {
            canpacket p = new canpacket();
            p.cob = 000;
            p.len = 2;
            p.data = new byte[2];
            p.data[0] = 0x81;
            p.data[1] = nodeid;

            SendPacket(p);
        }

        public bool checkguard(int node, TimeSpan maxspan)
        {
            if (DateTime.Now - nmtstate[(ushort)node].lastping > maxspan)
                return false;

            return true;
        }

        #endregion

        #region PDOhelpers

        public void writePDO(UInt16 cob,byte[] payload)
        {
            canpacket p = new canpacket();
            p.cob = cob;
            p.len = (byte) payload.Length;
            p.data = new byte[p.len];
            for (int x = 0; x < payload.Length; x++)
                p.data[x] = payload[x];

            SendPacket(p);
        }

        #endregion

    }
}
