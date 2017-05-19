# libCanopenSimple
libCanopenSimple is a "simple" canopen library for C# that uses native dll/so drivers from CanFestival to access CAN hardware. The library provides callbacks for the defined COB types NMT/PDO/SDO etc as well as allowing arbatary injection of CANOpen packets.
The API also provides some common functions such as NMT controls for start/stop/reset and full SDO client behaviour so that remote nodes object dictionaries can be read/written via SDO using the library. The SDO supports expidited/segmented and block transfers.

### What it does not do
 - Its not a CanOpen device
 - There is no object dictionary
 - There is no SDO server support
 - There are no other features that you would expect in a can open device
 
Despite the above it would technicaly be possible to add all of the above features using the callbacks add API but this is outside the scope of this project as there are perfectly good opensource CanOpenStacks already out there so creating another is not helpful.

### Drivers

libCanopenSimple uses the C API drivers from CanFestival. Can Festival is included as a git submodule in the project and the top level solution includes the C# libcanopensimple code and the canfestival drivers.
Only 2 drivers are enabled these are
 - can_uvccm_win32
 - can_canusb-win32
These drivers do not depend on any external installed files so will build out of the box, the ixxat,peak,copican and anagate drivers all require files supplied by the hardware manufacture 

### Create your own driver
All drivers must confirm to the CanFestival driver API that is it must export the following symbols
   - canReceive_driver
   - canSend_driver
   - canOpen_driver
   - canClose_driver
   - (optional) canChangeBaudRate_driver

And the C API looks like 

 - uint8_t __stdcall canReceive_driver(CAN_HANDLE fd0, Message *m)
 - uint8_t __stdcall canSend_driver(CAN_HANDLE fd0, Message const *m)
 - CAN_HANDLE __stdcall canOpen_driver(s_BOARD *board)
 - uint32_t __stdcall canClose_driver(CAN_HANDLE inst)
 - uint8_t __stdcall canChangeBaudRate_driver( CAN_HANDLE fd, char* baud)

Where 
 CAN_HANDLE is a void *
 Message is a struct, ensure it is packed! 
 s_BOARD is a struct
 board is a char* pointer to a string that contains bus id
 
    struct Message
        {
           UInt16 cob_id; /* message's ID */
           byte rtr;       /* 0 if not rtr message, 1 if rtr message */
           byte len;       /* message's length (0 to 8) */
           UInt64 data;
        }
        
        struct struct_s_BOARD
        {
            char*  busname;  /**< The bus name on which the CAN board is connected */
            char * baudrate; /**< The board baudrate */
        };
        
Opening a driver is just a call to canOpen_driver() with a s_BOARD paramater, the busname and baudrate parts are entirly down to the driver to interprate and handle as needed, A handle should be retured that will be used for all other API calls (CAN_HANDLE fd0)

To send data canSend_driver() is used with the above handle and a pointer to a message
To recieve data, keep polling canReceive_driver() and if data is ready the passed struct will be populated

### Mono
Probably yes...
libcanopenSimple itsself is no problem and will work on mono, the driverloader and driverinstance again have been designed to work with .net or mono and the Marshall and pinvoke calls have code to use kernel32.dll or ld.so for loading the CanFestival drivers.
Can Festival drivers are all linux compatable and in fact there are more options for linux that windows. But you will need to manually build the canfestival drivers (using the normal canfestival makefile) and then copy the final driver.so files to the libdl search path.




