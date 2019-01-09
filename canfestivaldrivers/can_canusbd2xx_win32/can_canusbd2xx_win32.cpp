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

// D2XX VERSION


#include <sstream>
#include <iomanip>
#include <algorithm>
#include "windows.h"


#include "atlbase.h"

typedef void * PVOID;
typedef unsigned long ULONG;
#include "FTD2XX.H"

// string::find_first_of
#include <iostream>       // std::cout
#include <string>         // std::string
#include <cstddef>        // std::size_t

FT_HANDLE ftHandle;
FT_STATUS ftStatus;

#define MAX_BUF_SIZE 20

extern "C" {
#include "can_driver.h"
}
class can_canusbd2xx_win32
   {
   public:
      class error
        {
        };
	  can_canusbd2xx_win32(s_BOARD *board);
	  ~can_canusbd2xx_win32();
      bool send(const Message *m);
      bool receive(Message *m);
   private:
      bool open_rs232(std::string port ="COM1", int baud_rate = 57600);
      bool close_rs232();
      bool get_can_data(const char* can_cmd_buf, long& bufsize, Message* m);
      bool set_can_data(const Message& m, std::string& can_cmd);
	  bool can_canusbd2xx_win32::doTX(std::string can_cmd);
   private:
      HANDLE m_port;
      HANDLE m_read_event;
      HANDLE m_write_event;
      std::string m_residual_buffer;
   };

can_canusbd2xx_win32::can_canusbd2xx_win32(s_BOARD *board) : m_port(INVALID_HANDLE_VALUE),
      m_read_event(0),
      m_write_event(0)
   {
   if (!open_rs232(board->busname))
      throw error();

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

   doTX("C\r");

   if (!strcmp(board->baudrate, "10K"))
   {
	   doTX("S0\r");
   }

   if (!strcmp(board->baudrate, "20K"))
   {
	   doTX("S1\r");
   }

   if (!strcmp(board->baudrate, "50K"))
   {
	   doTX("S2\r");
   }

   if (!strcmp(board->baudrate, "100K"))
   {
	   doTX("S3\r");
   }

   if (!strcmp(board->baudrate, "125K"))
   {
	   doTX("S4\r");
   }

   if (!strcmp(board->baudrate, "250K"))
   {
	   doTX("S5\r");
   }

   if (!strcmp(board->baudrate, "500K"))
   {
	   doTX("S6\r");
   }

   if (!strcmp(board->baudrate, "800K"))
   {
	   doTX("S7\r");
   }

   if (!strcmp(board->baudrate, "1M"))
   {
	   doTX("S8\r");
   }

   doTX("O\r");


   }

can_canusbd2xx_win32::~can_canusbd2xx_win32()
   {
   close_rs232();
   }


bool can_canusbd2xx_win32::doTX(std::string can_cmd)
{

	unsigned long BytesWritten = 0;

	ftStatus = FT_Write(ftHandle, (LPVOID)can_cmd.c_str(), can_cmd.length(), &BytesWritten);
	if (ftStatus == FT_OK) 
	{
		// FT_Write OK
	}
	else {
		// FT_Write Failed
	}

	bool result = (BytesWritten == can_cmd.length());

	return result;

}

bool can_canusbd2xx_win32::send(const Message *m)
{


   if (ftHandle == NULL)
      return true;

   // build can_uvccm_win32 command string
   std::string can_cmd;
   set_can_data(*m, can_cmd);

   bool result = doTX(can_cmd);

   return false;
   }

#define RX_BUF_SIZE 1024

bool can_canusbd2xx_win32::receive(Message *m)
   {
	
	DWORD EventDWord;
	DWORD TxBytes;
	DWORD RxBytes;
	DWORD BytesReceived;

	m->cob_id = 0;
	m->len = 0;

	char RxBuffer[RX_BUF_SIZE];

	long res_buffer_size = (long)m_residual_buffer.size();
	bool result = get_can_data(m_residual_buffer.c_str(), res_buffer_size, m);

	if (result)
	{
		m_residual_buffer.erase(0, res_buffer_size);
		return true;
	}

	FT_GetStatus(ftHandle, &RxBytes, &TxBytes, &EventDWord);

	if (RxBytes > 0) {
		ftStatus = FT_Read(ftHandle, RxBuffer, RxBytes< RX_BUF_SIZE ?RxBytes: RX_BUF_SIZE, &BytesReceived);
		if (ftStatus == FT_OK) 
		{

			if (BytesReceived > 0)
			{
				m_residual_buffer.append(RxBuffer, BytesReceived);
				res_buffer_size = (long)m_residual_buffer.size();
				result = get_can_data(m_residual_buffer.c_str(), res_buffer_size, m);
				if (result)
					m_residual_buffer.erase(0, res_buffer_size);
			}

		}
		else {
			// FT_Read Failed
		}
	}

   return true;
   }

bool can_canusbd2xx_win32::open_rs232(std::string port, int baud_rate)
   {

	int portno;
	sscanf_s(port.c_str(), "ftdi://%d/", &portno);

	ftStatus = FT_Open(portno, &ftHandle);
	if (ftStatus == FT_OK) {
		// FT_Open OK, use ftHandle to access device

		ftStatus = FT_SetBaudRate(ftHandle, 115200); // Set baud rate to 115200
		ftStatus = FT_SetDataCharacteristics(ftHandle, FT_BITS_8, FT_STOP_BITS_1,FT_PARITY_NONE);

	}
	else {
		// FT_Open failed
		return false;
	}

	return true;
  }

bool can_canusbd2xx_win32::close_rs232()
   {

	if (ftHandle != NULL)
	{
		FT_Close(ftHandle);
	}

	return true;
   }

bool can_canusbd2xx_win32::get_can_data(const char* can_cmd_buf, long& bufsize, Message* m)
{
	if (bufsize < 5)
	{
		bufsize = 0;
		return false;
	}

	Message msg;
	::memset(&msg, 0, sizeof(msg));
	char colon = 0, type = 0, request = 0;

	std::string meh = can_cmd_buf;

	std::size_t found = meh.find_first_of('t');

	if (found == -1)
	{
		return true;
	}

	if (found > 0)
	{
		bufsize = found;
		return true;
	}

   std::istringstream buf(std::string(can_cmd_buf),32);

   std::string cob;
   std::string len;

   buf >> type >> std::setw(3) >> cob >> std::setw(1) >> len;

   buf.str();


   if (type != 't')
   {
	   bufsize = 0;
	   return false;
   }



   msg.cob_id = std::stoi(cob, 0, 16);
   msg.len = std::stoi(len, 0, 16);

   UNS8 pos;

   if (type == 't')
      {
      msg.rtr = 0;
	  for (pos = 0; pos < msg.len; pos++)
         {
         std::string data_byte_str;
         buf >> std::setw(2) >> data_byte_str;

         if (data_byte_str[0] == '\r')
            break;
         long byte_val = -1;
         std::istringstream(data_byte_str) >> std::hex >> byte_val;
         if (byte_val == -1)
            {
            bufsize = 0;
            return false;
            }
         msg.data[pos] = (UNS8)byte_val;
         }

      if (msg.len > 0)
         {
			
		 char semicolon = buf.get();
         if (semicolon != '\r')
            {
            bufsize = 0;
            return false;
            }
         }

      }
   else if (type == 'r')
      {
      msg.rtr = 1;
      buf >> msg.len;
      }
   else
      {
      bufsize = 0;
      return false;
      }

   bufsize = (long)buf.tellg();

   *m = msg;
   return true;
   }

bool can_canusbd2xx_win32::set_can_data(const Message& m, std::string& can_cmd)
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


//------------------------------------------------------------------------
extern "C"
   UNS8 __stdcall canReceive_driver(CAN_HANDLE fd0, Message *m)
   {
	   return (UNS8)(!(reinterpret_cast<can_canusbd2xx_win32*>(fd0)->receive(m)));
   }

extern "C"
   UNS8 __stdcall canSend_driver(CAN_HANDLE fd0, Message const *m)
   {
	   return (UNS8)reinterpret_cast<can_canusbd2xx_win32*>(fd0)->send(m);
   }

extern "C"
   CAN_HANDLE __stdcall canOpen_driver(s_BOARD *board)
   {
   try
      {
		  return (CAN_HANDLE) new can_canusbd2xx_win32(board);
      }
   catch (can_canusbd2xx_win32::error&)
      {
      return NULL;
      }
   }

extern "C"
   int __stdcall canClose_driver(CAN_HANDLE inst)
   {
	   delete reinterpret_cast<can_canusbd2xx_win32*>(inst);
   return 1;
   }

extern "C"
	UNS8 __stdcall canChangeBaudRate_driver( CAN_HANDLE fd, char* baud)
	{
	return 0;
	} 

typedef void (__stdcall *setStringValuesCB_t) (char *pStringValues[], int nValues);
static setStringValuesCB_t __stdcall gSetStringValuesCB;

void __stdcall NativeCallDelegate(char *pStringValues[], int nValues)
{
	if (gSetStringValuesCB)
		gSetStringValuesCB(pStringValues, nValues);
}

extern "C" void __stdcall canEnumerate2_driver(setStringValuesCB_t callback)
{

	DWORD numDevs;
	ftStatus = FT_ListDevices(&numDevs, NULL, FT_LIST_NUMBER_ONLY);

    gSetStringValuesCB = callback;
	char **Values = (char**)malloc(sizeof(void*)*numDevs);

	for (DWORD x = 0; x < numDevs; x++)
	{
		char buf[MAX_BUF_SIZE];
		sprintf_s(buf, MAX_BUF_SIZE, "ftdi://%d/", x);
		int len = 1+strnlen_s(buf, MAX_BUF_SIZE);
		*(Values+x) = (char*)malloc(len);
		strcpy_s(*(Values+x),len,buf);
	}


	NativeCallDelegate(Values, numDevs);
}
