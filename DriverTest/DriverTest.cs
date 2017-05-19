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

        private static void Instance_rxmessage(DriverInstance.Message msg)
        {
            canpacket cp = new canpacket(msg);
            Console.WriteLine("RX :" + cp.ToString());
        }
    }
}
