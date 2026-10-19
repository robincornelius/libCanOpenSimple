/*
This file is part of CanFestival, a library implementing CanOpen Stack.

CanFestival Copyright (C): Edouard TISSERANT and Francis DUPIN
CanFestival Win32 port Copyright (C) 2007 Leonid Tochinski, ChattenAssociates, Inc.

See COPYING file for copyrights details.

This library is free software; you can redistribute it and/or
modify it under the terms of the GNU Lesser General Public
License as published by the Free Software Foundation; either
version 2.1 of the License, or (at your option) any later version.

This library is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
Lesser General Public License for more details.

You should have received a copy of the GNU Lesser General Public
License along with this library; if not, write to the Free Software
Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
*/

// LAWICEL AB CANUSB adapter (http://www.can232.com/)
// driver for CanFestival-3 Win32 port

#define MAX_BUF_SIZE 20

#include <sstream>
#include <iomanip>
#if 0  // change to 1 if you use boost
#include <boost/algorithm/string/case_conv.hpp>
#else
#include <algorithm>
#endif


// string::find_first_of
#include <iostream>       // std::cout
#include <string>         // std::string
#include <mutex>
#include "serial_overlapped.h"
#include <cstddef>        // std::size_t
#include "../canusb_ascii_parser.h"

#include <atlbase.h>
#include <vector>
#include "atlstr.h"

#include "enumser.h"

extern "C" {
#include "can_driver.h"
}
class can_canusbwin32
{
public:
	class error
	{
	};
	can_canusbwin32(s_BOARD* board);
	~can_canusbwin32();
	bool send(const Message* m);
	bool receive(Message* m);
private:
	bool open_rs232(std::string port = "COM1", int baud_rate = 57600);
	bool close_rs232(bool sendCloseCommand = true);
	bool set_can_data(const Message& m, std::string& can_cmd);
	bool doTX(std::string can_cmd);
private:
	HANDLE m_port;
	HANDLE m_read_event;
	HANDLE m_write_event;
    CanUsbAsciiParser m_receiveParser;
    std::mutex m_writeMutex;
};

can_canusbwin32::can_canusbwin32(s_BOARD* board) : m_port(INVALID_HANDLE_VALUE),
    m_read_event(0),
    m_write_event(0)
{
    if (!open_rs232(board->busname))
    {
        throw error();
    }
    /*
     S0 Setup 10Kbit
     S1 Setup 20Kbit
     S2 Setup 50Kbit
     S3 Setup 100Kbit
     S4 Setup 125Kbit
     S5 Setup 250Kbit
     S6 Setup 500Kbit
     S7 Setup 800Kbit
     S8 Setup 1Mbit
    */

    auto sendCommand = [this](const char* command)
    {
        if (!doTX(command))
        {
            close_rs232(false);
            throw error();
        }
    };

    sendCommand("C\r");

    if (!strcmp(board->baudrate, "10K"))
    {
        sendCommand("S0\r");
    }

    if (!strcmp(board->baudrate, "20K"))
    {
        sendCommand("S1\r");
    }

    if (!strcmp(board->baudrate, "50K"))
    {
        sendCommand("S2\r");
    }

    if (!strcmp(board->baudrate, "100K"))
    {
        sendCommand("S3\r");
    }

    if (!strcmp(board->baudrate, "125K"))
    {
        sendCommand("S4\r");
    }

    if (!strcmp(board->baudrate, "250K"))
    {
        sendCommand("S5\r");
    }

    if (!strcmp(board->baudrate, "500K"))
    {
        sendCommand("S6\r");
    }

    if (!strcmp(board->baudrate, "800K"))
    {
        sendCommand("S7\r");
    }

    if (!strcmp(board->baudrate, "1M"))
    {
        sendCommand("S8\r");
    }

    sendCommand("O\r");


}

can_canusbwin32::~can_canusbwin32()
{
    close_rs232();
}


bool can_canusbwin32::doTX(std::string can_cmd)
{
    // Application and protocol threads can both send. Each write owns the event
    // and command bytes until completion, including cancellation cleanup.
    std::lock_guard<std::mutex> lock(m_writeMutex);
    if (m_port == INVALID_HANDLE_VALUE || m_write_event == NULL)
    {
        return false;
    }

    OVERLAPPED operation = {};
    operation.hEvent = m_write_event;
    if (!::ResetEvent(operation.hEvent))
    {
        return false;
    }

    DWORD bytes = 0;
    const BOOL started = ::WriteFile(m_port, can_cmd.data(),
        static_cast<DWORD>(can_cmd.size()), &bytes, &operation);
    const CanUsbSerial::IoResult result = CanUsbSerial::Complete(
        m_port, operation, started, bytes, 1000);
    return result.status == CanUsbSerial::IoStatus::Completed && result.bytes == can_cmd.size();
}

bool can_canusbwin32::send(const Message* m)
{
    std::string command;
    set_can_data(*m, command);
    // CanFestival's send ABI uses zero for success and nonzero for failure.
    return !doTX(command);
}

bool can_canusbwin32::receive(Message* m)
{
    m->cob_id = 0;
    m->len = 0;
    if (m_port == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    if (m_receiveParser.TryRead(*m))
    {
        return true;
    }

    enum { ReadTimeout = 500 };
    COMSTAT state = {};
    DWORD errors = 0;
    if (!::ClearCommError(m_port, &errors, &state))
    {
        return false;
    }

    // Consume queued data first. EV_RXCHAR need not fire again for bytes that
    // were already present when the previous receive call completed.
    if (state.cbInQue == 0)
    {
        OVERLAPPED notification = {};
        notification.hEvent = m_read_event;
        if (!::ResetEvent(notification.hEvent))
        {
            return false;
        }

        DWORD mask = 0;
        const BOOL started = ::WaitCommEvent(m_port, &mask, &notification);
        const CanUsbSerial::IoResult result = CanUsbSerial::Complete(
            m_port, notification, started, 0, ReadTimeout);
        if (result.status != CanUsbSerial::IoStatus::Completed)
        {
            // An idle bus is normal. A failed operation is not a successful read.
            return result.status == CanUsbSerial::IoStatus::TimedOut;
        }

        if (!::ClearCommError(m_port, &errors, &state))
        {
            return false;
        }
    }

    if (state.cbInQue == 0)
    {
        return true;
    }

    char buffer[3000];
    const DWORD capacity = static_cast<DWORD>(sizeof(buffer));
    const DWORD count = state.cbInQue < capacity ? state.cbInQue : capacity;
    OVERLAPPED read = {};
    read.hEvent = m_read_event;
    if (!::ResetEvent(read.hEvent))
    {
        return false;
    }

    DWORD bytes = 0;
    const BOOL started = ::ReadFile(m_port, buffer, count, &bytes, &read);
    const CanUsbSerial::IoResult result = CanUsbSerial::Complete(
        m_port, read, started, bytes, ReadTimeout);
    if (result.status == CanUsbSerial::IoStatus::Failed)
    {
        return false;
    }

    if (result.status == CanUsbSerial::IoStatus::Completed && result.bytes != 0)
    {
        m_receiveParser.Append(buffer, result.bytes);
        m_receiveParser.TryRead(*m);
    }

    return true;
}

bool can_canusbwin32::open_rs232(std::string port, int baud_rate)
{
    if (m_port != INVALID_HANDLE_VALUE)
    {
        return true;
    }

    const std::string longPortName = "\\\\.\\" + port;
    m_port = ::CreateFileA(longPortName.c_str(), GENERIC_READ | GENERIC_WRITE,
        0, NULL, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, NULL);
    if (m_port == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    auto failOpen = [this]()
    {
        if (m_read_event != NULL)
        {
            ::CloseHandle(m_read_event);
            m_read_event = NULL;
        }

        if (m_write_event != NULL)
        {
            ::CloseHandle(m_write_event);
            m_write_event = NULL;
        }

        ::CloseHandle(m_port);
        m_port = INVALID_HANDLE_VALUE;
        return false;
    };

    m_read_event = ::CreateEvent(NULL, TRUE, FALSE, NULL);
    m_write_event = ::CreateEvent(NULL, TRUE, FALSE, NULL);
    if (m_read_event == NULL || m_write_event == NULL)
    {
        return failOpen();
    }

    COMMTIMEOUTS timeouts = {};
    // Read currently queued bytes without waiting for the buffer to fill.
    // Idle waiting is handled separately by the cancellable EV_RXCHAR operation.
    timeouts.ReadIntervalTimeout = MAXDWORD;
    timeouts.WriteTotalTimeoutConstant = 1000;
    if (!::SetCommMask(m_port, EV_RXCHAR)
        || !::SetCommTimeouts(m_port, &timeouts)
        || !::SetupComm(m_port, 1024, 512))
    {
        return failOpen();
    }

    DCB dcb = {};
    dcb.DCBlength = sizeof(dcb);
    if (!::GetCommState(m_port, &dcb))
    {
        return failOpen();
    }

    dcb.BaudRate = baud_rate;
    dcb.ByteSize = 8;
    dcb.Parity = NOPARITY;
    dcb.StopBits = ONESTOPBIT;
    dcb.fAbortOnError = FALSE;
    dcb.EvtChar = '\r'; // CANUSB terminator; byte notifications do not depend on it.
    if (!::SetCommState(m_port, &dcb)
        || !::PurgeComm(m_port, PURGE_RXABORT | PURGE_RXCLEAR | PURGE_TXABORT | PURGE_TXCLEAR))
    {
        return failOpen();
    }

    return true;
}

bool can_canusbwin32::close_rs232(bool sendCloseCommand)
{
    if (m_port != INVALID_HANDLE_VALUE)
    {

        if (sendCloseCommand)
        {
            doTX("C\r");
        }

        ::PurgeComm(m_port, PURGE_RXABORT | PURGE_RXCLEAR | PURGE_TXABORT | PURGE_TXCLEAR);
        ::CloseHandle(m_port);
        m_port = INVALID_HANDLE_VALUE;
        ::CloseHandle(m_read_event);
        m_read_event = 0;
        ::CloseHandle(m_write_event);
        m_write_event = 0;
        m_receiveParser.Clear();
    }
    return true;
}

bool can_canusbwin32::set_can_data(const Message& m, std::string& can_cmd)
{
	// build can_uvccm_win32 command string
	std::ostringstream can_cmd_str;

	//Normal or RTR, note lowercase for standard can frames, upper case t/r for extended COB IDS

	if (m.rtr == 1)
	{
		can_cmd_str << 'r';
	}
	else
	{
		can_cmd_str << 't';
	}

	//COB next

	can_cmd_str << std::hex << std::setfill('0') << std::setw(3) << m.cob_id;

	//LEN next

	can_cmd_str << std::hex << std::setfill('0') << std::setw(1) << (UNS16)m.len;

	//DATA

	for (int i = 0; i < m.len; ++i)
	{
		can_cmd_str << std::hex << std::setfill('0') << std::setw(2) << (long)m.data[i];
	}

	//Terminate

	can_cmd_str << "\r";

	can_cmd = can_cmd_str.str();

	OutputDebugString(can_cmd.c_str());

	return false;
}



typedef void(__stdcall* setStringValuesCB_t) (char* pStringValues[], int nValues);
static setStringValuesCB_t gSetStringValuesCB;

extern "C" void __stdcall NativeCallDelegate(char* pStringValues[], int nValues)
{
	if (gSetStringValuesCB)
		gSetStringValuesCB(pStringValues, nValues);
}

extern "C" void __stdcall canEnumerate2_driver(setStringValuesCB_t callback)
{


	CEnumerateSerial::CPortsArray ports;
	CEnumerateSerial::CPortAndNamesArray portAndNames;
	CEnumerateSerial::CNamesArray names;

	if (CEnumerateSerial::UsingRegistry(names))
	{

		gSetStringValuesCB = callback;
		char** Values = (char**)malloc(names.size()* MAX_BUF_SIZE*10);

		int x = 0;
		for (const auto& name : names)
		{
			char buf[MAX_BUF_SIZE];
			sprintf_s(buf, MAX_BUF_SIZE, "%s", name.c_str());
			int len = 1 + strnlen_s(buf, MAX_BUF_SIZE);
			*(Values + x) = (char*)malloc(len);
			strcpy_s(*(Values + x), len, buf);

			x++;

		}

		NativeCallDelegate(Values, names.size());

	}
	else
		_tprintf(_T("CEnumerateSerial::UsingRegistry failed, Error:%u\n"), GetLastError());
}



//------------------------------------------------------------------------
extern "C"
UNS8 __stdcall canReceive_driver(CAN_HANDLE fd0, Message * m)
{
	return (UNS8)(!(reinterpret_cast<can_canusbwin32*>(fd0)->receive(m)));
}

extern "C"
UNS8 __stdcall canSend_driver(CAN_HANDLE fd0, Message const* m)
{
	return (UNS8)reinterpret_cast<can_canusbwin32*>(fd0)->send(m);
}

extern "C"
CAN_HANDLE __stdcall canOpen_driver(s_BOARD * board)
{
	try
	{
		return (CAN_HANDLE) new can_canusbwin32(board);
	}
	catch (can_canusbwin32::error&)
	{
		return NULL;
	}
}

extern "C"
int __stdcall canClose_driver(CAN_HANDLE inst)
{
	delete reinterpret_cast<can_canusbwin32*>(inst);
	return 1;
}

extern "C"
UNS8 __stdcall canChangeBaudRate_driver(CAN_HANDLE fd, char* baud)
{
	return 0;
}
