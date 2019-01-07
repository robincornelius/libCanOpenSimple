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
using libCanopenSimple;

namespace SimpleTest
{
    class SimpleTest
    {
        /// <summary>
        /// A simple test that uses libcanopensimple to open a can device, set up some COB specific callbacks
        /// then send a bus reset all nodes NMT command after 5 seconds
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {

            //Change these to load correct driver and connect it to correct bus  
            string driver = "can_usb_win32";
            string bus = "COM4";
            BUSSPEED bitrate = BUSSPEED.BUS_500Kbit;

            try
            {
                libCanopenSimple.libCanopenSimple lco = new libCanopenSimple.libCanopenSimple();

                lco.nmtevent += Lco_nmtevent;
                lco.nmtecevent += Lco_nmtecevent;
                lco.pdoevent += Lco_pdoevent;
                lco.sdoevent += Lco_sdoevent;

                lco.open("com4", bitrate, driver);

                Console.WriteLine("listening for any traffic");

                Console.WriteLine("Sending NMT reset all nodes in 5 seconds");

                System.Threading.Thread.Sleep(5000);

                lco.NMT_ResetNode(); //reset all

                Console.WriteLine("Press any key to exit test..");

                while (!Console.KeyAvailable)
                {

                }

                lco.close();

            }
            catch(Exception e)
            {
                Console.WriteLine("That did not work out, exception message was \n" + e.ToString());
            }


        }

        private static void Lco_nmtecevent(canpacket p, DateTime dt)
        {
            Console.WriteLine("NMTEC :" + p.ToString());
        }

        private static void Lco_sdoevent(libCanopenSimple.canpacket p, DateTime dt)
        {
            Console.WriteLine("SDO :" + p.ToString());
        }

        private static void Lco_pdoevent(libCanopenSimple.canpacket[] ps, DateTime dt)
        {
            foreach(canpacket p in ps)
                Console.WriteLine("PDO :" + p.ToString());
        }

        private static void Lco_nmtevent(libCanopenSimple.canpacket p, DateTime dt)
        {
            Console.WriteLine("NMT :" + p.ToString());
        }
    }
}
