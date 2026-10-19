#include "../can_canusb_win32/serial_overlapped.h"
#include <iostream>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

using CanUsbSerial::IoResult;
using CanUsbSerial::IoStatus;

static void Require(bool condition, const char* description)
{
    if (!condition)
    {
        throw std::runtime_error(description);
    }
}

struct FakeIo
{
    static DWORD error;
    static DWORD waitResult;
    static DWORD completionError;
    static DWORD bytes;
    static bool cancelSucceeds;
    static bool incompleteSignal;
    static std::string calls;
    static OVERLAPPED* expected;

    static void Reset(OVERLAPPED& operation)
    {
        error = ERROR_IO_PENDING;
        waitResult = WAIT_OBJECT_0;
        completionError = ERROR_SUCCESS;
        bytes = 7;
        cancelSucceeds = true;
        incompleteSignal = false;
        calls.clear();
        expected = &operation;
    }

    static DWORD LastError()
    {
        return error;
    }

    static DWORD Wait(HANDLE event, DWORD timeout)
    {
        Require(event == expected->hEvent && timeout == 12, "Wrong event or timeout");
        calls += 'W';
        if (waitResult == WAIT_FAILED)
        {
            error = ERROR_INVALID_HANDLE;
        }

        return waitResult;
    }

    static BOOL Cancel(HANDLE, OVERLAPPED* operation)
    {
        Require(operation == expected, "Cancelled unrelated operation");
        calls += 'C';
        error = cancelSucceeds ? ERROR_SUCCESS : ERROR_NOT_FOUND;
        return cancelSucceeds ? TRUE : FALSE;
    }

    static BOOL Result(HANDLE, OVERLAPPED* operation, DWORD* transferred, BOOL wait)
    {
        Require(operation == expected, "Drained unrelated operation");
        calls += wait ? 'D' : 'R';
        *transferred = bytes;
        error = !wait && incompleteSignal ? ERROR_IO_INCOMPLETE : completionError;
        return error == ERROR_SUCCESS ? TRUE : FALSE;
    }
};

DWORD FakeIo::error;
DWORD FakeIo::waitResult;
DWORD FakeIo::completionError;
DWORD FakeIo::bytes;
bool FakeIo::cancelSucceeds;
bool FakeIo::incompleteSignal;
std::string FakeIo::calls;
OVERLAPPED* FakeIo::expected;

static IoResult Finish(OVERLAPPED& operation)
{
    return CanUsbSerial::Complete<FakeIo>(INVALID_HANDLE_VALUE, operation, FALSE, 99, 12);
}

static void DeterministicCases()
{
    OVERLAPPED operation = {};
    FakeIo::Reset(operation);
    IoResult result = CanUsbSerial::Complete<FakeIo>(INVALID_HANDLE_VALUE, operation, TRUE, 4, 12);
    Require(result.status == IoStatus::Completed && result.bytes == 4
        && FakeIo::calls.empty(), "Immediate completion waited or cancelled");

    FakeIo::Reset(operation);
    FakeIo::error = ERROR_ACCESS_DENIED;
    result = Finish(operation);
    Require(result.status == IoStatus::Failed && result.error == ERROR_ACCESS_DENIED
        && result.bytes == 0 && FakeIo::calls.empty(), "Immediate error treated as pending");

    FakeIo::Reset(operation);
    result = Finish(operation);
    Require(result.status == IoStatus::Completed && result.bytes == 7
        && FakeIo::calls == "WR", "Pending completion failed");

    FakeIo::Reset(operation);
    FakeIo::completionError = ERROR_OPERATION_ABORTED;
    result = Finish(operation);
    Require(result.status == IoStatus::Failed && result.bytes == 0
        && FakeIo::calls == "WR", "External cancellation treated as data");

    FakeIo::Reset(operation);
    FakeIo::waitResult = WAIT_TIMEOUT;
    FakeIo::completionError = ERROR_OPERATION_ABORTED;
    result = Finish(operation);
    Require(result.status == IoStatus::TimedOut && result.bytes == 0
        && FakeIo::calls == "WCD", "Timeout returned before cancel and drain");

    // CancelIoEx can return ERROR_NOT_FOUND when completion wins the race.
    for (bool cancelSucceeds : { false, true })
    {
        FakeIo::Reset(operation);
        FakeIo::waitResult = WAIT_TIMEOUT;
        FakeIo::cancelSucceeds = cancelSucceeds;
        result = Finish(operation);
        Require(result.status == IoStatus::Completed && result.bytes == 7
            && FakeIo::calls == "WCD", "Timeout race dropped completed bytes");
    }

    FakeIo::Reset(operation);
    FakeIo::waitResult = WAIT_TIMEOUT;
    FakeIo::completionError = ERROR_DEVICE_NOT_CONNECTED;
    result = Finish(operation);
    Require(result.status == IoStatus::Failed && result.bytes == 0
        && result.error == ERROR_DEVICE_NOT_CONNECTED && FakeIo::calls == "WCD",
        "Device removal treated as idle timeout");

    FakeIo::Reset(operation);
    FakeIo::waitResult = WAIT_FAILED;
    FakeIo::completionError = ERROR_OPERATION_ABORTED;
    result = Finish(operation);
    Require(result.status == IoStatus::Failed && result.error == ERROR_INVALID_HANDLE
        && FakeIo::calls == "WCD", "Wait failure left operation pending");

    FakeIo::Reset(operation);
    FakeIo::incompleteSignal = true;
    result = Finish(operation);
    Require(result.status == IoStatus::Completed && result.bytes == 7
        && FakeIo::calls == "WRCD", "Early signal dropped completed bytes");

    FakeIo::Reset(operation);
    FakeIo::incompleteSignal = true;
    FakeIo::completionError = ERROR_OPERATION_ABORTED;
    result = Finish(operation);
    Require(result.status == IoStatus::Failed && FakeIo::calls == "WRCD",
        "Spurious signal left operation pending");
}

class Handle
{
public:
    explicit Handle(HANDLE value) : value(value)
    {
        Require(value != NULL && value != INVALID_HANDLE_VALUE, "Handle creation failed");
    }

    ~Handle()
    {
        ::CloseHandle(value);
    }

    HANDLE value;

private:
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};

static void RealWindowsCancellation()
{
    const std::string name = "\\\\.\\pipe\\canusb-io-tests-" + std::to_string(::GetCurrentProcessId());
    Handle server(::CreateNamedPipeA(name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1, 1024, 1024, 0, NULL));
    Handle client(::CreateFileA(name.c_str(), GENERIC_READ | GENERIC_WRITE,
        0, NULL, OPEN_EXISTING, 0, NULL));
    Handle event(::CreateEvent(NULL, TRUE, FALSE, NULL));
    OVERLAPPED operation = {};
    operation.hEvent = event.value;
    const BOOL connected = ::ConnectNamedPipe(server.value, &operation);
    Require(connected || ::GetLastError() == ERROR_PIPE_CONNECTED, "Pipe connection failed");

    // Exercise real kernel cancellation, then reuse the same event/OVERLAPPED.
    // A cancelled read must not consume bytes from the next iteration.
    for (int iteration = 0; iteration < 10; ++iteration)
    {
        Require(::ResetEvent(event.value) != FALSE, "Event reset failed");
        operation = {};
        operation.hEvent = event.value;
        char buffer[32] = {};
        DWORD bytes = 0;
        BOOL started = ::ReadFile(server.value, buffer, sizeof(buffer), &bytes, &operation);
        IoResult result = CanUsbSerial::Complete(server.value, operation, started, bytes, 5);
        Require(result.status == IoStatus::TimedOut, "Empty pipe did not time out");
        Require(!::GetOverlappedResult(server.value, &operation, &bytes, FALSE)
            && ::GetLastError() == ERROR_OPERATION_ABORTED, "Cancelled read is still pending");

        Require(::WriteFile(client.value, "abc", 3, &bytes, NULL) && bytes == 3, "Pipe write failed");
        Require(::ResetEvent(event.value) != FALSE, "Event reuse failed");
        operation = {};
        operation.hEvent = event.value;
        started = ::ReadFile(server.value, buffer, sizeof(buffer), &bytes, &operation);
        result = CanUsbSerial::Complete(server.value, operation, started, bytes, 1000);
        Require(result.status == IoStatus::Completed && result.bytes == 3
            && std::string(buffer, 3) == "abc", "Read after cancellation lost data");
    }

    // Also exercise successful asynchronous completion against the real kernel,
    // rather than only reads whose bytes were queued before ReadFile.
    Require(::ResetEvent(event.value) != FALSE, "Pending read event reset failed");
    operation = {};
    operation.hEvent = event.value;
    char pendingBuffer[32] = {};
    DWORD pendingBytes = 0;
    bool writerSucceeded = false;
    std::thread writer([&]()
    {
        ::Sleep(20);
        DWORD written = 0;
        writerSucceeded = ::WriteFile(client.value, "def", 3, &written, NULL) && written == 3;
    });
    const BOOL pendingStarted = ::ReadFile(server.value, pendingBuffer,
        sizeof(pendingBuffer), &pendingBytes, &operation);
    const IoResult pendingResult = CanUsbSerial::Complete(
        server.value, operation, pendingStarted, pendingBytes, 1000);
    writer.join();
    Require(writerSucceeded && pendingResult.status == IoStatus::Completed
        && pendingResult.bytes == 3 && std::string(pendingBuffer, 3) == "def",
        "Pending read did not complete with arriving data");

    // Fill the outbound pipe without a reader to force a pending write.
    std::vector<char> largeWrite(1024 * 1024, 'x');
    Require(::ResetEvent(event.value) != FALSE, "Write event reset failed");
    operation = {};
    operation.hEvent = event.value;
    DWORD bytes = 0;
    const BOOL started = ::WriteFile(server.value, largeWrite.data(),
        static_cast<DWORD>(largeWrite.size()), &bytes, &operation);
    const IoResult result = CanUsbSerial::Complete(server.value, operation, started, bytes, 5);
    Require(result.status == IoStatus::TimedOut, "Blocked write did not time out");
    Require(!::GetOverlappedResult(server.value, &operation, &bytes, FALSE)
        && ::GetLastError() == ERROR_OPERATION_ABORTED, "Cancelled write is still pending");
}

int main()
{
    try
    {
        DeterministicCases();
        RealWindowsCancellation();
        std::cout << "PASS: completion, timeouts, cancellation races and real Windows reads/writes\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
