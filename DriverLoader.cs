using System;
using System.Runtime.InteropServices;

namespace libCanopenSimple
{
    public class DriverLoader
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libname);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private IntPtr Handle = IntPtr.Zero;

        DriverInstance driver;

        public DriverInstance loaddriver(string fileName)
        {

            IntPtr Handle = LoadLibrary(fileName);
            if (Handle == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                throw new Exception(string.Format("Failed to load library (ErrorCode: {0})", errorCode));
            }

            IntPtr funcaddr;

            funcaddr = GetProcAddress(Handle, "canReceive_driver");
            DriverInstance.canReceive_T canReceive = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canReceive_T)) as DriverInstance.canReceive_T;

            funcaddr = GetProcAddress(Handle, "canSend_driver");
            DriverInstance.canSend_T canSend = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canSend_T)) as DriverInstance.canSend_T; ;

            funcaddr = GetProcAddress(Handle, "canOpen_driver");
            DriverInstance.canOpen_T canOpen = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canOpen_T)) as DriverInstance.canOpen_T; ;

            funcaddr = GetProcAddress(Handle, "canClose_driver");
            DriverInstance.canClose_T canClose = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canClose_T)) as DriverInstance.canClose_T; ;

            funcaddr = GetProcAddress(Handle, "canChangeBaudRate_driver");
            DriverInstance.canChangeBaudRate_T canChangeBaudRate = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canChangeBaudRate_T)) as DriverInstance.canChangeBaudRate_T; ;

            driver = new DriverInstance(canReceive, canSend, canOpen, canClose, canChangeBaudRate);

            return driver;
        }
    }

    public class DriverInstance
    {

        private bool threadrun = true;
        System.Threading.Thread rxthread;

        public delegate void RxMessage(Message msg);
        public event RxMessage rxmessage;

        [StructLayout(LayoutKind.Sequential)]
        public struct Message
        {
            public UInt16 cob_id; /**< message's ID */
            public byte rtr;       /**< remote transmission request. (0 if not rtr message, 1 if rtr message) */
            public byte len;       /**< message's length (0 to 8) */
            public UInt64 data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct struct_s_BOARD
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public String busname;  /**< The bus name on which the CAN board is connected */

            [MarshalAs(UnmanagedType.LPStr)]
            public String baudrate; /**< The board baudrate */
        };


        public DriverInstance(canReceive_T canReceive,canSend_T canSend,canOpen_T canOpen,canClose_T canClose, canChangeBaudRate_T canChangeBaudrate)
        {
            this.canReceive = canReceive;
            this.canSend = canSend;
            this.canOpen = canOpen;
            this.canClose = canClose;
            this.canChangeBaudrate = canChangeBaudrate;
        }

        
        public delegate byte canReceive_T(IntPtr handle, IntPtr msg);
        private canReceive_T canReceive;

        public delegate byte canSend_T(IntPtr handle, IntPtr msg);
        private canSend_T canSend;

        public delegate IntPtr canOpen_T(IntPtr brd);
        private canOpen_T canOpen;

        public delegate UInt32 canClose_T(IntPtr handle);
        private canClose_T canClose;

        public delegate byte canChangeBaudRate_T(IntPtr handle, string rate);
        private canChangeBaudRate_T canChangeBaudrate;

        private IntPtr handle;

        public void open(struct_s_BOARD brd)
        {
           
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(brd));
            Marshal.StructureToPtr(brd, ptr, false);

            handle = canOpen(ptr);

            rxthread = new System.Threading.Thread(rxthreadworker);
            rxthread.Start();


        }

        public Message canreceive()
        {
            Message msg = new Message();

            IntPtr msgptr = Marshal.AllocHGlobal(Marshal.SizeOf(msg));
            Marshal.StructureToPtr(msg, msgptr, false);

            canReceive(handle, msgptr);

            msg = (Message) Marshal.PtrToStructure(msgptr, typeof(Message));

            Marshal.FreeHGlobal(msgptr);

            return msg;

        }

        public void cansend(Message msg)
        {
            
            IntPtr msgptr = Marshal.AllocHGlobal(Marshal.SizeOf(msg));
            Marshal.StructureToPtr(msg, msgptr, false);

            canSend(handle, msgptr);

            Marshal.FreeHGlobal(msgptr);

        }

        private void rxthreadworker()
        {
            while(threadrun)
            {

                DriverInstance.Message rxmsg = canreceive();

                if (rxmsg.len != 0)
                {
                    if (rxmessage != null)
                        rxmessage(rxmsg);
                }

                System.Threading.Thread.Sleep(0);
            }
        }


    }

}
