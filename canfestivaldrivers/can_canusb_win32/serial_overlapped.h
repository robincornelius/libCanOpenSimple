#ifndef CANUSB_SERIAL_OVERLAPPED_H
#define CANUSB_SERIAL_OVERLAPPED_H

#include <windows.h>

namespace CanUsbSerial
{
    enum class IoStatus
    {
        Completed,
        TimedOut,
        Failed
    };

    struct IoResult
    {
        IoStatus status;
        DWORD bytes;
        DWORD error;
    };

    // The small API boundary allows deterministic tests of cancellation races.
    struct WindowsIo
    {
        static DWORD LastError()
        {
            return ::GetLastError();
        }

        static DWORD Wait(HANDLE event, DWORD timeout)
        {
            return ::WaitForSingleObject(event, timeout);
        }

        static BOOL Cancel(HANDLE port, OVERLAPPED* operation)
        {
            return ::CancelIoEx(port, operation);
        }

        static BOOL Result(HANDLE port, OVERLAPPED* operation, DWORD* bytes, BOOL wait)
        {
            return ::GetOverlappedResult(port, operation, bytes, wait);
        }
    };

    // Call immediately after ReadFile, WriteFile or WaitCommEvent, before any
    // other Windows API can overwrite the initiating operation's last error.
    template <typename Api = WindowsIo>
    IoResult Complete(HANDLE port, OVERLAPPED& operation, BOOL started,
        DWORD immediateBytes, DWORD timeout)
    {
        if (started)
        {
            return { IoStatus::Completed, immediateBytes, ERROR_SUCCESS };
        }

        const DWORD startError = Api::LastError();
        if (startError != ERROR_IO_PENDING)
        {
            return { IoStatus::Failed, 0, startError };
        }

        const DWORD waitResult = Api::Wait(operation.hEvent, timeout);
        DWORD waitError = ERROR_SUCCESS;
        if (waitResult == WAIT_OBJECT_0)
        {
            DWORD bytes = 0;
            if (Api::Result(port, &operation, &bytes, FALSE))
            {
                return { IoStatus::Completed, bytes, ERROR_SUCCESS };
            }

            waitError = Api::LastError();
            if (waitError != ERROR_IO_INCOMPLETE)
            {
                return { IoStatus::Failed, 0, waitError };
            }
        }
        else if (waitResult != WAIT_TIMEOUT)
        {
            waitError = Api::LastError();
        }

        // Cancellation is a request, not completion. Even ERROR_NOT_FOUND can
        // mean completion won the race. Always drain before freeing the buffer,
        // resetting the event, or reusing this OVERLAPPED for another operation.
        Api::Cancel(port, &operation);
        DWORD bytes = 0;
        const BOOL completed = Api::Result(port, &operation, &bytes, TRUE);
        const DWORD completionError = completed ? ERROR_SUCCESS : Api::LastError();
        if (completed && (waitResult == WAIT_TIMEOUT || waitResult == WAIT_OBJECT_0))
        {
            // Do not lose data that arrived just as the timeout expired, or
            // completed after an unexpected early event notification.
            return { IoStatus::Completed, bytes, ERROR_SUCCESS };
        }

        if (waitResult == WAIT_TIMEOUT)
        {
            if (completionError == ERROR_OPERATION_ABORTED)
            {
                return { IoStatus::TimedOut, 0, WAIT_TIMEOUT };
            }

            return { IoStatus::Failed, 0, completionError };
        }

        return { IoStatus::Failed, 0, waitError };
    }
}

#endif
