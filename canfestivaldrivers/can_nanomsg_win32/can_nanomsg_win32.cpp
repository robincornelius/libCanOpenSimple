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
#include <cstddef>        // std::size_t

#include <nanomsg/nn.h>
#include <nanomsg/bus.h>

extern "C" {
#include "can_driver.h"
}
class can_nanomsg_win32
   {
   public:
      class error
        {
        };
	  can_nanomsg_win32(s_BOARD *board);
	  ~can_nanomsg_win32();
      bool send(const Message *m);
      bool receive(Message *m);
   private:
      bool open_rs232(std::string port ="COM1", int baud_rate = 57600);
      bool close_rs232();
   private:
      HANDLE m_port;
      HANDLE m_read_event;
      HANDLE m_write_event;
      std::string m_residual_buffer;

	  int fd;
   };

can_nanomsg_win32::can_nanomsg_win32(s_BOARD *board) : m_port(INVALID_HANDLE_VALUE),
      m_read_event(0),
      m_write_event(0)
   {

	open_rs232(board->busname, 0);
	
   }

can_nanomsg_win32::~can_nanomsg_win32()
   {

   }



bool can_nanomsg_win32::send(const Message *m)
   {
		if (nn_send(fd, m, sizeof(Message), 0) < 0) {
			fprintf(stderr, "nn_send: %s\n", nn_strerror(nn_errno()));
			nn_close(fd);
			return false;
	}

		return true;
   }

bool can_nanomsg_win32::receive(Message *m)
   {

	int rc;
	/*  Here we ask the library to allocate response buffer for us (NN_MSG). */

	rc = nn_recv(fd, m, 14, NN_DONTWAIT);
	//rc = nn_recv(fd, &m2, NN_MSG, 0);
	if (rc < 0) {
		m->len = 0;

		if (m->cob_sender_id != 0) //we are 0 as we are not really the bus
			nn_send(fd, m, 14, NN_DONTWAIT);

		//fprintf(stderr, "nn_recv: %s\n", nn_strerror(nn_errno()));
		//nn_close(fd);
		return false;
	}

	//memcpy(m, m2, rc<12?rc:12);

	return true;
   }

bool can_nanomsg_win32::open_rs232(std::string port, int baud_rate)
   {

	fd = nn_socket(AF_SP, NN_BUS);
	if (fd < 0) {
		fprintf(stderr, "nn_socket: %s\n", nn_strerror(nn_errno()));
		return false;
	}

	if (nn_bind(fd, port.c_str()) < 0) {
		fprintf(stderr, "nn_socket: %s\n", nn_strerror(nn_errno()));
		nn_close(fd);
		return false;
	}

	return true;
  
   }

bool can_nanomsg_win32::close_rs232()
   {
	nn_close(fd);
	return true;
   }



//------------------------------------------------------------------------
extern "C"
   UNS8 __stdcall canReceive_driver(CAN_HANDLE fd0, Message *m)
   {
	   return (UNS8)(!(reinterpret_cast<can_nanomsg_win32*>(fd0)->receive(m)));
   }

extern "C"
   UNS8 __stdcall canSend_driver(CAN_HANDLE fd0, Message const *m)
   {
	   return (UNS8)reinterpret_cast<can_nanomsg_win32*>(fd0)->send(m);
   }

extern "C"
   CAN_HANDLE __stdcall canOpen_driver(s_BOARD *board)
   {
   try
      {
		  return (CAN_HANDLE) new can_nanomsg_win32(board);
      }
   catch (can_nanomsg_win32::error&)
      {
      return NULL;
      }
   }

extern "C"
   int __stdcall canClose_driver(CAN_HANDLE inst)
   {
	   delete reinterpret_cast<can_nanomsg_win32*>(inst);
   return 1;
   }

extern "C"
	UNS8 __stdcall canChangeBaudRate_driver( CAN_HANDLE fd, char* baud)
	{
	return 0;
	} 

typedef void(__stdcall *setStringValuesCB_t) (char *pStringValues[], int nValues);
static setStringValuesCB_t __stdcall gSetStringValuesCB;

void __stdcall NativeCallDelegate(char *pStringValues[], int nValues)
{
	if (gSetStringValuesCB)
		gSetStringValuesCB(pStringValues, nValues);
}

extern "C" void __stdcall canEnumerate2_driver(setStringValuesCB_t callback)
{
	
	#define MAX_BUF_SIZE 20

	DWORD numDevs = 1;
	gSetStringValuesCB = callback;
	char **Values = (char**)malloc(sizeof(void*)*numDevs);

	char buf[MAX_BUF_SIZE];
	sprintf_s(buf,MAX_BUF_SIZE, "ipc://can_id1");

	int len = 1+ strnlen_s(buf, MAX_BUF_SIZE);

	*(Values) = (char*)malloc(len);
	strcpy_s(*(Values), len, buf);

	NativeCallDelegate(Values, numDevs);
}