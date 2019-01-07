/*
    This file is part of libCanopenSimple.
    libCanopenSimple is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    libCanopenSimple is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
    You should have received a copy of the GNU General Public License
    along with libCanopenSimple.  If not, see <http://www.gnu.org/licenses/>.
 
    Copyright(c) 2017 Robin Cornelius <robin.cornelius@gmail.com>
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using libCanopenSimple;

namespace DriverTest
{
    /// <summary>
    /// This demonstrates how to load a can festival driver, open the channel and recieve data
    /// </summary>
    class DriverTest
    {
        static void Main(string[] args)
        {

            //Change these to load correct driver and connect it to correct bus  
            string driver = "can_usb_win32";
            string bus = "COM4";
            BUSSPEED bitrate = BUSSPEED.BUS_500Kbit;


            try
            {
                DriverLoader loader = new DriverLoader();
                DriverInstance instance = loader.loaddriver(driver);

                Console.WriteLine("Opening CAN device ");
                instance.open(bus, bitrate);

                instance.rxmessage += Instance_rxmessage;
                Console.WriteLine("listening for any traffic, press any key to exit ..");
                while (!Console.KeyAvailable)
                {

                }

                instance.close();
            }
            catch(Exception e)
            {
                Console.WriteLine("That did not work out, exception message was \n" + e.ToString());
            }

            Console.WriteLine("Press any key to exit");

            while (!Console.KeyAvailable)
            {
            }
        }

        private static void Instance_rxmessage(DriverInstance.Message msg, bool bridge = false)
        {
            canpacket cp = new canpacket(msg);
            Console.WriteLine("RX :" + cp.ToString());
        }
    }
}
