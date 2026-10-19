/*
    This file is part of libCanopenSimple.
    libCanopenSimple is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    libCanopenSimple is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
    You should have received a copy of the GNU General Public License
    along with libCanopenSimple.  If not, see <http://www.gnu.org/licenses/>.
 
    Copyright(c) 2017 Robin Cornelius <robin.cornelius@gmail.com>
*/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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

            funcaddr = GetProcAddress(Handle, "canEnumerate2_driver");
            DriverInstance.canEnumerate_T canEnumerate = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canEnumerate_T)) as DriverInstance.canEnumerate_T; ;

            driver = new DriverInstance(canReceive, canSend, canOpen, canClose, canChangeBaudRate,canEnumerate);

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

            funcaddr = dlsym(Handle, "canEnumerate_driver");
            DriverInstance.canEnumerate_T canEnumerate = Marshal.GetDelegateForFunctionPointer(funcaddr, typeof(DriverInstance.canEnumerate_T)) as DriverInstance.canEnumerate_T; ;

            driver = new DriverInstance(canReceive, canSend, canOpen, canClose, canChangeBaudRate,canEnumerate);



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

        private volatile bool threadrun;
        private readonly object lifecycle = new object();
        private bool closing;
        private bool opening;
        private bool boardMarshalled;
        private readonly ListenerDiagnostics diagnostics = new ListenerDiagnostics();
        private readonly ListenerWorker listenerWorker;

        /// <summary>Listener errors and watchdog restart notices. Handlers must not block.</summary>
        public event EventHandler<ListenerDiagnosticEventArgs> ListenerDiagnostic
        {
            add { diagnostics.Reported += value; }
            remove { diagnostics.Reported -= value; }
        }

        public ListenerDiagnosticEventArgs LastListenerError
        {
            get
            {
                return diagnostics.LastError;
            }
        }
        public long ListenerErrorCount
        {
            get
            {
                return diagnostics.ErrorCount;
            }
        }
        public bool IsListenerRunning
        {
            get
            {
                return listenerWorker.IsRunning;
            }
        }
        public long ListenerRestartCount
        {
            get
            {
                return listenerWorker.RestartCount;
            }
        }

        /// <summary>
        /// CANOpen message recieved callback, this will be fired upon any recieved complete message on the bus
        /// </summary>
        /// <param name="msg">The CanOpen message</param>
        public delegate void RxMessage(Message msg,bool bridge=false);
        public event RxMessage rxmessage;

        /// <summary>
        /// CanFestival message packet. Note we set data to be a UInt64 as inside canfestival its a fixed char[8] array
        /// we cannout use fixed arrays in C# without UNSAFE so instead we just use a UInt64
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 12, Pack = 1)]
        public struct Message
        {
            public UInt16 cob_source_id; /**< message's ID */
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


        [StructLayout(LayoutKind.Sequential)]
        public struct struct_s_DEVICES
        {
            public UInt32 id;

            [MarshalAs(UnmanagedType.LPStr)]
            public string name;
        };


        [StructLayout(LayoutKind.Sequential)]
        public struct UnmanagedStruct
        {
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr, SizeConst = 100)]
            public IntPtr[] listOfStrings;

            public IEnumerable<string> Strings
            {
                get
                {
                    return listOfStrings.Select(x => Marshal.PtrToStringAnsi(x));
                }
            }
        }

        UnmanagedStruct enumerationresult;

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

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void canEnumerateDelegate_T(
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr, SizeParamIndex = 1)]
        string[] values,
        int valueCount);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void canEnumerate_T(canEnumerateDelegate_T callback);
        private canEnumerate_T canEnumerate;

        private IntPtr instancehandle = IntPtr.Zero;
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
        public DriverInstance(canReceive_T canReceive, canSend_T canSend, canOpen_T canOpen, canClose_T canClose, canChangeBaudRate_T canChangeBaudrate, canEnumerate_T canEnumerate)
        {
            diagnostics.Source = this;
            this.canReceive = canReceive;
            this.canSend = canSend;
            this.canOpen = canOpen;
            this.canClose = canClose;
            this.canChangeBaudrate = canChangeBaudrate;
            this.canEnumerate = canEnumerate;
            listenerWorker = new ListenerWorker("CAN driver receive", rxthreadworker, diagnostics);


            StringBuilder[] b = new StringBuilder[2];

            instancehandle = IntPtr.Zero;
            brdptr = IntPtr.Zero;

        }

        public static List<string> ports = new List<string>();

        public static void PrintReceivedData(string[] values, int valueCount)
        {
            foreach (var item in values)
                ports.Add(item);
        }


        public void enumerate()
        {
            ports = new List<string>();
            this.canEnumerate(PrintReceivedData);
        }

        /// <summary>
        /// Open the CAN device, the bus ID and bit rate are passed to driver. For Serial/USb Seral pass COMx etc.
        /// </summary>
        /// <param name="bus">The requested bus ID are provided here.</param>
        /// <param name="speed">The requested CAN bit rate</param>
        /// <returns>True on succesful opening of device</returns>
        public bool open(string bus, BUSSPEED speed)
        {
            lock (lifecycle)
            {
                if (opening || closing || instancehandle != IntPtr.Zero || !listenerWorker.IsStopped)
                {
                    return false;
                }

                opening = true;
                try
                {
                    brd.busname = bus;
                    string[] rates = { "10K", "20K", "50K", "100K", "125K", "250K", "500K", "800K", "1M" };
                    if ((int)speed < 0 || (int)speed >= rates.Length)
                    {
                        throw new ArgumentOutOfRangeException("speed");
                    }

                    brd.baudrate = rates[(int)speed];
                    brdptr = Marshal.AllocHGlobal(Marshal.SizeOf(brd));
                    Marshal.StructureToPtr(brd, brdptr, false);
                    boardMarshalled = true;
                    instancehandle = canOpen(brdptr);
                    if (instancehandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("The CAN driver could not open the device.");
                    }

                    threadrun = true;
                    listenerWorker.Start();
                    return true;
                }
                catch (Exception error)
                {
                    listenerWorker.RequestStop();
                    threadrun = false;
                    ReleaseDriver();
                    diagnostics.Report("CAN driver receive", "Open failed", error);
                    return false;
                }
                finally
                {
                    opening = false;
                }
            }
        }

        /// <summary>
        /// See if the CAN device is open
        /// </summary>
        /// <returns>Open status of can device</returns>
        public bool isOpen()
        {
            lock (lifecycle)
            {
                return !closing && instancehandle != IntPtr.Zero && listenerWorker.IsRunning;
            }
        }

        /// <summary>Stop and join the reader before destroying its native handle.</summary>
        public void close()
        {
            lock (lifecycle)
            {
                if (closing)
                {
                    return;
                }

                closing = true;
                listenerWorker.RequestStop();
                threadrun = false;
            }

            try
            {
                listenerWorker.Stop();
                lock (lifecycle)
                {
                    ReleaseDriver();
                }
            }
            finally
            {
                lock (lifecycle)
                {
                    closing = false;
                }
            }
        }

        private void ReleaseDriver()
        {
            IntPtr handle = instancehandle;
            instancehandle = IntPtr.Zero;
            Exception failure = null;
            try
            {
                if (handle != IntPtr.Zero)
                {
                    canClose(handle);
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                if (brdptr != IntPtr.Zero)
                {
                    if (boardMarshalled)
                    {
                        Marshal.DestroyStructure(brdptr, typeof(struct_s_BOARD));
                    }

                    Marshal.FreeHGlobal(brdptr);
                    brdptr = IntPtr.Zero;
                    boardMarshalled = false;
                }
            }

            if (failure != null)
            {
                diagnostics.Report("CAN driver receive", "Close failed", failure);
            }
        }

        /// <summary>
        /// Message pump function. This should be called in a fast loop
        /// </summary>
        /// <returns></returns>
        public Message canreceive()
        {
            Message message = new Message();
            IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(message));
            try
            {
                Marshal.StructureToPtr(message, buffer, false);
                byte status = canReceive(instancehandle, buffer);
                if (status != 0)
                {
                    throw new InvalidOperationException("CAN receive failed with driver status " + status + ".");
                }

                message = (Message)Marshal.PtrToStructure(buffer, typeof(Message));
                return message;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Send a packet; serialize sends with native handle destruction.</summary>
        public void cansend(Message message)
        {
            lock (lifecycle)
            {
                if (closing || instancehandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("The CAN driver is closed.");
                }

                IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(message));
                try
                {
                    Marshal.StructureToPtr(message, buffer, false);
                    byte status = canSend(instancehandle, buffer);
                    if (status != 0)
                    {
                        throw new InvalidOperationException("CAN send failed with driver status " + status + ".");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        private void rxthreadworker()
        {
            while (threadrun)
            {
                Message message = canreceive();
                if (!threadrun)
                {
                    break;
                }

                if (message.len > 8)
                {
                    diagnostics.Report("CAN driver receive", "Invalid DLC",
                        new FormatException("The driver returned a CAN payload longer than eight bytes."));
                    continue;
                }

                if (message.len != 0)
                {
                    diagnostics.Invoke(rxmessage, callback => ((RxMessage)callback)(message),
                        "CAN driver receive", "Receive subscriber failed");
                }
            }
        }
    }
}
