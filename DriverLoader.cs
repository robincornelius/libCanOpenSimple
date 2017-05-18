/// Can Festival driver loader for C#
/// libCanopenSimple
/// Robin Cornelius <robin.cornelius@gmail.com>

using System;
using System.Runtime.InteropServices;

namespace libCanopenSimple
{
    /// <summary> DriverLoader - dynamic pinvoke can festival drivers
    /// This class will select the approprate win or mono loader and try to load the requested 
    /// can festival library
    /// Info on pinvoke for win/mono :-
    /// http://stackoverflow.com/questions/13461989/p-invoke-to-dynamically-loaded-library-on-mono
    /// by gordonmleigh
    /// </summary>

    public class DriverLoader
    {
        public static bool IsRunningOnMono()
        {
            return Type.GetType("Mono.Runtime") != null;
        }

        /// <summary>
        /// Attempt to load the requested can festival driver and return a DriverInstance class
        /// </summary>
        /// <param name="fileName"> Name of the dynamic library to load, note do not append .dll or .so</param>
        /// <returns></returns>
        public DriverInstance loaddriver(string fileName)
        {
            if (IsRunningOnMono())
            {
                fileName += ".so";
                DriverLoaderMono dl = new DriverLoaderMono();
                return dl.loaddriver(fileName);
            }
            else
            {
                fileName += ".dll";
                DriverLoaderWin dl = new DriverLoaderWin();
                return dl.loaddriver(fileName);
            }

        }
    }

    #region windows

    /// <summary>
    /// CanFestival driver loader for windows, this class will load kernel32 then attept to use LoadLibrary()
    /// and GetProcAddress() to hook the can festival driver functions these are then exposed as delagates
    /// for eash C# access
    /// </summary>

    public class DriverLoaderWin
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libname);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private IntPtr Handle = IntPtr.Zero;

        DriverInstance driver;

        /// <summary>
        /// Clean up and free the library
        /// </summary>
        ~DriverLoaderWin()
        {
            if (Handle != IntPtr.Zero)
            {
                FreeLibrary(Handle);
                Handle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Attempt to load the requested can festival driver and return a DriverInstance class
        /// </summary>
        /// <param name="fileName">Load can festival driver (Windows .Net runtime version) .dll must be appeneded in this case to fileName</param>
        /// <returns></returns>
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
    #endregion

    #region mono

    /// <summary>
    /// CanFestival driver loader for mono, this class will load libdl then attept to use dlopen() and dlsym()
    /// and GetProcAddress to hook the can festival driver functions these are then exposed as delagates
    /// for eash C# access
    /// </summary>
    /// 
    public class DriverLoaderMono
    {

        [DllImport("libdl.so")]
        protected static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libdl.so")]
        protected static extern IntPtr dlsym(IntPtr handle, string symbol);

        DriverInstance driver;

        const int RTLD_NOW = 2; // for dlopen's flags 

        /// <summary>
        /// Attempt to load the requested can festival driver and return a DriverInstance class
        /// </summary>
        /// <param name="fileName">Load can festival driver (Mono runtime version) .so must be appeneded in this case to fileName</param>
        /// <returns></returns>
        public DriverInstance loaddriver(string fileName)
        {
            IntPtr Handle = dlopen(fileName, RTLD_NOW);
            if (Handle == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                throw new Exception(string.Format("Failed to load library (ErrorCode: {0})", errorCode));
            }

            IntPtr funcaddr;

            funcaddr = dlsym(Handle, "canReceive_driver");
            DriverInstance.canReceive_T canReceive = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canReceive_T)) as DriverInstance.canReceive_T;

            funcaddr = dlsym(Handle, "canSend_driver");
            DriverInstance.canSend_T canSend = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canSend_T)) as DriverInstance.canSend_T; ;

            funcaddr = dlsym(Handle, "canOpen_driver");
            DriverInstance.canOpen_T canOpen = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canOpen_T)) as DriverInstance.canOpen_T; ;

            funcaddr = dlsym(Handle, "canClose_driver");
            DriverInstance.canClose_T canClose = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canClose_T)) as DriverInstance.canClose_T; ;

            funcaddr = dlsym(Handle, "canChangeBaudRate_driver");
            DriverInstance.canChangeBaudRate_T canChangeBaudRate = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canChangeBaudRate_T)) as DriverInstance.canChangeBaudRate_T; ;

            driver = new DriverInstance(canReceive, canSend, canOpen, canClose, canChangeBaudRate);

            return driver;
        }
    }

    #endregion

    /// <summary>
    /// DriverInstace represents a specific instance of a loaded canfestival driver
    /// </summary>
    /// 

    public class DriverInstance
    {

        private bool threadrun = true;
        System.Threading.Thread rxthread;

        /// <summary>
        /// CANOpen message recieved callback, this will be fired upon any recieved complete message on the bus
        /// </summary>
        /// <param name="msg">The CanOpen message</param>
        public delegate void RxMessage(Message msg);
        public event RxMessage rxmessage;

        /// <summary>
        /// CanFestival message packet. Note we set data to be a UInt64 as inside canfestival its a fixed char[8] array
        /// we cannout use fixed arrays in C# without UNSAFE so instead we just use a UInt64
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 12, Pack = 1)]
        public struct Message
        {
            public UInt16 cob_id; /**< message's ID */
            public byte rtr;       /**< remote transmission request. (0 if not rtr message, 1 if rtr message) */
            public byte len;       /**< message's length (0 to 8) */
            public UInt64 data;
        }

        /// <summary>
        /// This contains the bus name on which the can board is connected and the bit rate of the board
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct struct_s_BOARD
        {

            [MarshalAs(UnmanagedType.LPStr)]
            public String busname;  /**< The bus name on which the CAN board is connected */

            [MarshalAs(UnmanagedType.LPStr)]
            public String baudrate; /**< The board baudrate */
        };


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
        IntPtr brdptr;

        struct_s_BOARD brd;

        /// <summary>
        /// Create a new DriverInstance, this class provides a wrapper between the C# world and the C API dlls from canfestival that
        /// provide access to the CAN hardware devices. The exposed delegates represent the 5 defined entry points that all can festival
        /// drivers expose to form the common driver interface API. Usualy the DriverLoader class will directly call this constructor.
        /// </summary>
        /// <param name="canReceive">pInvoked delegate for canReceive function</param>
        /// <param name="canSend">pInvoked delegate for canSend function</param>
        /// <param name="canOpen">pInvoked delegate for canOpen function</param>
        /// <param name="canClose">pInvoked delegate for canClose function</param>
        /// <param name="canChangeBaudrate">pInvoked delegate for canChangeBaudrate functipn</param>
        public DriverInstance(canReceive_T canReceive, canSend_T canSend, canOpen_T canOpen, canClose_T canClose, canChangeBaudRate_T canChangeBaudrate)
        {
            this.canReceive = canReceive;
            this.canSend = canSend;
            this.canOpen = canOpen;
            this.canClose = canClose;
            this.canChangeBaudrate = canChangeBaudrate;

            handle = IntPtr.Zero;
            brdptr = IntPtr.Zero;

        }

        /// <summary>
        /// Open the CAN device, the bus ID and bit rate are passed to driver. For Serial/USb Seral pass COMx etc.
        /// </summary>
        /// <param name="bus">The requested bus ID are provided here.</param>
        /// <param name="speed">The requested CAN bit rate</param>
        /// <returns>True on succesful opening of device</returns>
        public bool open(string bus, BUSSPEED speed)
        {

            try
            {


                brd.busname = bus;

                // Map BUSSPEED to CanFestival speed options
                switch (speed)
                {
                    case BUSSPEED.BUS_10Kbit:
                        brd.baudrate = "10K";
                        break;
                    case BUSSPEED.BUS_20Kbit:
                        brd.baudrate = "20K";
                        break;
                    case BUSSPEED.BUS_50Kbit:
                        brd.baudrate = "50K";
                        break;
                    case BUSSPEED.BUS_100Kbit:
                        brd.baudrate = "100K";
                        break;
                    case BUSSPEED.BUS_125Kbit:
                        brd.baudrate = "125K";
                        break;
                    case BUSSPEED.BUS_250Kbit:
                        brd.baudrate = "250K";
                        break;
                    case BUSSPEED.BUS_500Kbit:
                        brd.baudrate = "500K";
                        break;
                    case BUSSPEED.BUS_1Mbit:
                        brd.baudrate = "1M";
                        break;

                }

                brdptr = Marshal.AllocHGlobal(Marshal.SizeOf(brd));
                Marshal.StructureToPtr(brd, brdptr, false);

                handle = canOpen(brdptr);

                if (handle != IntPtr.Zero)
                {

                    rxthread = new System.Threading.Thread(rxthreadworker);
                    rxthread.Start();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// See if the CAN device is open
        /// </summary>
        /// <returns>Open status of can device</returns>
        public bool isOpen()
        {
            if (handle == IntPtr.Zero)
                return false;


            return true;
        }

        /// <summary>
        /// Close the CAN hardware device
        /// </summary>
        public void close()
        {
            threadrun = false;

            if (handle != IntPtr.Zero)
                canClose(handle);

            handle = IntPtr.Zero;

            if (brdptr != IntPtr.Zero)
                Marshal.FreeHGlobal(brdptr);

            brdptr = IntPtr.Zero;
        }

        /// <summary>
        /// Message pump function. This should be called in a fast loop
        /// </summary>
        /// <returns></returns>
        public Message canreceive()
        {

            // I think we can do better here and not allocated/deallocate to heap every pump loop
            Message msg = new Message();

            IntPtr msgptr = Marshal.AllocHGlobal(Marshal.SizeOf(msg));
            Marshal.StructureToPtr(msg, msgptr, false);

            byte status = canReceive(handle, msgptr);

            msg = (Message)Marshal.PtrToStructure(msgptr, typeof(Message));

            Marshal.FreeHGlobal(msgptr);

            return msg;

        }

        /// <summary>
        /// Send a CanOpen mesasge to the hardware device
        /// </summary>
        /// <param name="msg">CanOpen message to be sent</param>
        public void cansend(Message msg)
        {

            IntPtr msgptr = Marshal.AllocHGlobal(Marshal.SizeOf(msg));
            Marshal.StructureToPtr(msg, msgptr, false);

            canSend(handle, msgptr);

            Marshal.FreeHGlobal(msgptr);

        }

        /// <summary>
        /// Private worker thread to keep the rxmessage() function pumped
        /// </summary>
        private void rxthreadworker()
        {
            while (threadrun)
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
