using System;
using libCanopenSimple;

namespace SimpleTest
{
    class SimpleTest
    {

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

                lco.open(4, bitrate, driver);

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

        private static void Lco_nmtecevent(canpacket p)
        {
            Console.WriteLine("NMTEC :" + p.ToString());
        }

        private static void Lco_sdoevent(libCanopenSimple.canpacket p)
        {
            Console.WriteLine("SDO :" + p.ToString());
        }

        private static void Lco_pdoevent(libCanopenSimple.canpacket[] ps)
        {
            foreach(canpacket p in ps)
                Console.WriteLine("PDO :" + p.ToString());
        }

        private static void Lco_nmtevent(libCanopenSimple.canpacket p)
        {
            Console.WriteLine("NMT :" + p.ToString());
        }
    }
}
