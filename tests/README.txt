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

SDO concurrency and completion contract:
- Requests are serialized per default SDO channel (node) on each connection.
  Independent nodes can progress concurrently, preserving each node's FIFO order.
- The protocol worker pumps only its own transfers. Replies with the wrong
  object index/subindex or phase are ignored. Terminal transfers release only
  their own registration, preventing old cleanup from removing a successor.
- A transfer completes once: remove registration, signal WaitOne, then queue its
  completion callback. Completion callbacks execute in order on a per-connection
  ThreadPool dispatcher, outside SDO locks and independently of the listener.
  They no longer execute on the protocol thread. Use BeginInvoke/Post for UI
  work. WaitOne signals transfer completion, not callback completion. A callback
  may enqueue and WaitOne on a subsequent transfer; it must not wait for that
  transfer's callback, which is ordered behind it. Slow callbacks delay later
  completion callbacks on that connection, but not protocol replies/timeouts.
- WaitOne(int millisecondsTimeout) permits a bounded wait. The original WaitOne
  remains available. Do not block a UI thread waiting for callback UI work.
- A timeout finishes with SDO_ERROR and code 0x05040000. Queue flush cancels
  pending requests only; close cancels active and pending requests. Both release
  waiters and queue callbacks once with local error 0x08000000. Requests made
  while the protocol is closed complete immediately with that local error.
- Public SDOcallbacks is retained for compatibility; library access is internally
  synchronized. Consumers must not mutate this implementation registry.
- Default CANopen SDO has no transaction identifier. A delayed response for the
  exact same object/direction can be indistinguishable from a new response;
  these fixes cannot infer a missing wire-level transaction ID.

Additional regressions use fake drivers, without hardware: overlapping reads and
writes, blocked/reentrant callbacks, waiter-before-callback ordering, two
connections with the same node, concurrent producers, old/double replies,
timeout, queue flush, close/reopen, abort and segmented upload/download.

PDO configuration API and protocol sequencing
--------------------------------------------
PdoConfiguration.ApplyAsync accepts a node and typed SdoParameterWrite entries.
The OD Editor uses it for individual PDO edits and complete DCF batches. PDO
communication ranges are 1400-15FF and 1800-19FF; mapping ranges are 1600-17FF
and 1A00-1BFF. Mapping index minus 0200 selects its communication record.

The sequence is targeted Pre-operational, snapshot/validate affected PDOs,
set bit 31 of each current COB-ID, clear mapping counts for remapped PDOs,
write requested parameters/mappings, restore the final mapping counts, restore
final COB-IDs, then targeted Operational. Every download is acknowledged and
read back before continuing. A requested COB-ID is first written with bit 31
set. Other flags and intentionally disabled PDOs are preserved. Changes are
not automatically stored through 1010h. On failure processing stops; no
Operational command is sent after a failed parameter write. There is no blind
rollback of a partially changed mapping. Inspect the failed step and device
state before retrying.

Active classic-CAN mapping descriptors are validated against the 64-bit payload
budget. Partial edits preserve the current count unless a new count is supplied;
untouched active descriptors are read for validation. Communication subindex 0
and reserved communication fields are not treated as writable mapping counts.

NMT state confirmation uses the configured heartbeat producer period at 1017:00.
If nonzero, a missing target-state heartbeat stops configuration. If zero, the
node-targeted NMT command is sent before following SDO traffic, with a 50 ms
application-loop settling interval. The result explicitly reports the NMT state
as unconfirmed. Heartbeat is not silently enabled. An SDO acknowledgement verifies
an OD operation, not the remote NMT state. The OD Editor reports that distinction.
This implementation requires readable 1017:00 and readable configuration entries
for verification. DCF application selects readable/writable ParameterValue
entries, not read-only metadata or write-only command objects.

A per-node configuration lease holds ordinary SDO requests (including refresh)
until the whole batch completes; other nodes continue. Each configuration SDO
carries the lease identity, so no node-local interleaving can corrupt the mapping
handshake. The lease is released even after an SDO abort or verification error.

Sources checked:
https://www.can-cia.org/can-knowledge/pdo-protocol
https://www.can-cia.org/can-knowledge/network-management/
https://www.can-cia.org/can-knowledge/error-control-protocols
https://canopennode.github.io/CANopenNode/group__CO__PDO.html
These public CiA descriptions document the mapping procedure and variable versus
dynamic mapping. The full CiA 301 specification is not bundled with this project.

PdoConfigurationTool.csproj is an explicit opt-in hardware diagnostic. With no
arguments it only prints usage. inspect reads TPDO1 COB-ID/inhibit time and the
heartbeat period. write-pdo requires an expected current value and performs no
configuration if that precondition fails. It calls the same production API as
the editor. Pass the deployment directory, driver name (without extension),
port, bitrate, and node explicitly. Native FTDI port syntax is ftdi://0/.
Never run hardware commands as part of the automated fake-driver test suite.

Listener and driver regressions
===============================

Run ./tests/run-tests.ps1 from this repository to rebuild and exercise Debug and
Release on x64 and x86. The suite uses simulated drivers unless an explicit
integration option is passed to the resulting ListenerRegressionTests.exe.

Issue #3: IdleProtocolCpu measures process CPU while the production protocol
worker runs against a blocking simulated receive driver, then verifies that
incoming traffic is still delivered. The regression allows up to 20% of one
CPU core to avoid treating scheduler noise as a busy loop.

Native parser, overlapped-I/O and null-driver regressions:

  ./canfestivaldrivers/tests/run-tests.ps1

The null-driver test executes production native source. It checks bounded idle
waiting, no fabricated packets, and zero-success native send status. The native
build also produces missing-runtime startup fixtures for CanMonitor tests.

With a matching built application deployment, run the managed/native integration:

  ./tests/build/x64/Debug/ListenerRegressionTests.exe --native C:/path/to/deployment

This additionally exercises real nanomsg and null DLLs, including idle CPU,
send status, shutdown and reopen. Repeat with Release and matching x86 outputs.

Optional physical-hardware check, only when adapter zero is available and the
connected device is node 0x19 at 500 kbit/s:

  ./tests/build/x64/Debug/ListenerRegressionTests.exe --hardware C:/path/to/deployment

This discovers the FTDI port, performs fifteen SDO reads of 0x1000:00 over three
connect/disconnect cycles and checks listener health. It does not write device
parameters or issue NMT transitions. Never run two hardware clients concurrently.
