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

extern "C" {
#include "can_driver.h"
}
class can_null_win32
   {
   public:
      class error
        {
        };
	  can_null_win32(s_BOARD *board);
	  ~can_null_win32();
      bool send(const Message *m);
      bool receive(Message *m);
   };

can_null_win32::can_null_win32(s_BOARD *board)
   {

	
   }

can_null_win32::~can_null_win32()
   {

   }



bool can_null_win32::send(const Message *m)
   {
		return true;
   }

bool can_null_win32::receive(Message *m)
   {

	m->len = 0;
	return true;
   
   }


//------------------------------------------------------------------------
extern "C"
   UNS8 __stdcall canReceive_driver(CAN_HANDLE fd0, Message *m)
   {
	   return (UNS8)(!(reinterpret_cast<can_null_win32*>(fd0)->receive(m)));
   }

extern "C"
   UNS8 __stdcall canSend_driver(CAN_HANDLE fd0, Message const *m)
   {
	   return (UNS8)reinterpret_cast<can_null_win32*>(fd0)->send(m);
   }

extern "C"
   CAN_HANDLE __stdcall canOpen_driver(s_BOARD *board)
   {
   try
      {
		  return (CAN_HANDLE) new can_null_win32(board);
      }
   catch (can_null_win32::error&)
      {
      return NULL;
      }
   }

extern "C"
   int __stdcall canClose_driver(CAN_HANDLE inst)
   {
	   delete reinterpret_cast<can_null_win32*>(inst);
   return 1;
   }

extern "C"
	UNS8 __stdcall canChangeBaudRate_driver( CAN_HANDLE fd, char* baud)
	{
	return 0;
	} 

typedef void(__stdcall *setStringValuesCB_t) (char *pStringValues[], int nValues);
static setStringValuesCB_t gSetStringValuesCB;

void __stdcall NativeCallDelegate(char *pStringValues[], int nValues)
{
	if (gSetStringValuesCB)
		gSetStringValuesCB(pStringValues, nValues);
}

extern "C" void __stdcall canEnumerate2_driver(setStringValuesCB_t callback)
{
	
	DWORD numDevs = 1;
	gSetStringValuesCB = callback;
	char **Values = (char**)malloc(sizeof(void*)*numDevs);

	char buf[20];
	sprintf_s(buf,20, "null://null1");
	*(Values) = (char*)malloc(strlen(buf));
	strcpy_s(*(Values),20, buf);

	NativeCallDelegate(Values, numDevs);
}