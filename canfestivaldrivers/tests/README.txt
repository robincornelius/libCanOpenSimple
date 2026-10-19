CANUSB streaming parser regression tests

Run from PowerShell:
    ./run-tests.ps1

Requires Visual Studio 2026 with v145 C++ tools, Windows SDK and CMake tools.
The default matrix is x64 and Win32, Debug and Release. Select one using
-Platform x64 or Win32 and -Configuration Debug or Release.

The production parser is tested directly, without hardware or a driver DLL.
Coverage includes ordered bursts of up to 10,000 frames, transport chunks from
one byte to a full burst, every split point in a timestamped eight-byte frame,
partial tails after complete frames, strict hexadecimal and DLC validation,
malformed input recovery, bounded unterminated junk and connection reset.
Both COM and FTDI drivers use this parser and its retained byte buffer.

Only standard data frames (lowercase t) are decoded, as in the existing drivers.
Optional four-digit CANUSB timestamps are accepted but not exposed in Message.
Extended and remote frames remain unsupported. These tests do not measure the
adapter FIFO, Windows serial I/O or hardware packet loss under bus load.

For other C++11 platforms, configure/build this directory with CMake and run
CTest. Test build products are ignored under build/.

COM overlapped I/O regression tests (Windows only)

The same runner also tests serial_overlapped.h, used only by the COM driver.
Deterministic cases cover immediate success/failure, pending completion, external
abort, timeout cancellation, completion racing cancellation (including
ERROR_NOT_FOUND), device removal, wait failure and unexpected event signalling.
Every pending timeout/failure path must cancel the specific OVERLAPPED and drain
it before returning. Completed bytes are retained when cancellation loses a race.
Real Windows named-pipe tests cancel ten pending reads, reuse the event and
OVERLAPPED for successful reads, and cancel a blocked write. No adapter is needed.
CTest limits the I/O test process to 20 seconds to catch cancellation hangs.

COM reads use EV_RXCHAR and check queued bytes before waiting. Idle notifications
and reads use a 500 ms deadline; writes use 1000 ms. Cancellation then waits for
terminal completion, as required for buffer/OVERLAPPED lifetime safety. A broken
kernel driver that never completes cancellation can exceed those deadlines;
returning early would allow Windows to access freed memory. CancelIoEx is scoped
to one operation, so a read timeout does not abort an unrelated write.

As with the existing native driver ABI, callers must stop receiving/sending before
destroying the driver instance. This patch does not change managed listener
lifecycle or FTDI transport handling. Hardware traffic and unplug tests remain
separate from the deterministic tests.
