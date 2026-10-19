Listener reliability regression tests

Run ./run-tests.ps1 in PowerShell. Prerequisites: MSBuild and the .NET Framework
4.8 targeting pack. Defaults: x64 and x86, Debug and Release. Select individual
variants with -Platform x64 or x86 and -Configuration Debug or Release.

The test project compiles the production C# source files directly, allowing
internal lifecycle tests without exposing test-only public APIs. Native DLLs,
NuGet test packages and CAN hardware are not required. A 45-second process
deadline catches hangs. Generated executables and intermediate files are ignored.

Coverage:
- Receive subscriber and diagnostic/Trace handler exceptions cannot kill capture.
- Native receive exceptions and nonzero status are diagnosed and restarted.
- Unexpected worker return is diagnosed and restarted by the watchdog.
- Close joins a blocked native receive before destroying the handle.
- No restart or receive occurs after close; reopening starts a fresh worker.
- Malformed SDO replies and invalid DLCs do not terminate the protocol listener.
- Individual packet, PDO, SDO, heartbeat, NMT, EMCY, TIME, SYNC and LSS subscribers
  are isolated, and later subscribers/packets still run after a failure.
- SDO completion callback failure does not suppress later subscribers or waiters.
- Protocol worker termination is recovered by the monitor. A CLR-enforced abort
  of a managed-only test thread also verifies fallback diagnostics when normal
  exit reporting is bypassed. The library never aborts worker threads.
- An ordered 5,000-frame burst reaches packet and PDO delivery.
- Closing from a protocol packet callback does not deadlock or restart a worker.

Diagnostics and recovery API:
Subscribe to ListenerDiagnostic on libCanopenSimple or DriverInstance. The former
forwards transport diagnostics too. Event args identify the Listener, Operation,
Exception, TimestampUtc and Kind (Error or Restarted). The sender is the subscribed library or DriverInstance; use the Listener field to distinguish transport and protocol.
LastListenerError and ListenerErrorCount remain available without a subscriber.
Trace also receives diagnostics. Configure the host's Trace listeners or route
the event into the host's logger; the library does not choose a log file path.
Diagnostic handlers must return promptly, and exceptions from them are contained.

IsListenerRunning and ListenerRestartCount describe the worker at each layer.
isopen()/isOpen() require both a native handle and a running listener. During
watchdog recovery they can temporarily be false even though the handle is held.
Both monitors run every 200 ms while active, catch ordinary worker failures,
restart after termination, and disable restart before shutdown joins the worker.
An idle bus is healthy. The watchdog does not reset a live but blocked callback
or abort a pending kernel operation; shutdown waits for those calls to return.
Callbacks should be nonblocking and hand longer work to their own queues.
Process termination, stack exhaustion and corrupted native memory are outside
managed exception recovery. Queues remain in memory across worker restarts;
packet callbacks are not replayed after failure, to avoid duplicate side effects.

The watchdog is generic and stays in the reusable library. Existing consumer-side
monitors can remain as additional supervision; they should use public status and
diagnostics rather than independently starting a second internal protocol worker.
No customer-specific logging locations or private dependencies are required.

Nanomsg compatibility: an empty nonblocking receive returns success with DLC=0,
send uses zero for success, and socket ownership ends at native close. This keeps
normal idle polling from triggering receive-failure recovery.

Optional native nanomsg regression (after building the application):
    ./build/x64/Debug/ListenerRegressionTests.exe --native 'C:/path/to/application/bin/x64/Debug'

Use the executable architecture/configuration matching that deployment directory.
The deployment must contain drivers/can_nanomsg_win32.dll and its nanomsg runtime.
The test uses an in-process endpoint, checks idle polling and send status, then
closes and rebinds the same endpoint three times. It does not use CAN hardware.

Shutdown ordering regression: disable supervision before publishing the loop stop
flag, then join before destroying native resources. Tests force the worker to exit
before the join and exercise twenty rapid open/close cycles, checking that normal
shutdown produces neither a failure diagnostic nor a watchdog restart.
