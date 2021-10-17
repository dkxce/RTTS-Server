using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Drawing;

namespace rtts
{
    class Program
    {
        static void Main(string[] args)
        {
            TriggerServer ts = new TriggerServer();
            ts.Start();
            
            Console.WriteLine("Press Enter to Emulate");
            Console.WriteLine();
            Console.ReadLine();

            Emulate();

            Console.WriteLine("Press Enter to Stop Server");
            Console.WriteLine();
            Console.ReadLine();
            ts.Stop();
        }

        static void GenerateQR()
        {
            /*
            string url = rttutils.GenerateLink("", "GRM/18/6");
            Console.WriteLine(url);
            ThoughtWorks.QRCode.Codec.QRCodeEncoder qrCodeEncoder = new ThoughtWorks.QRCode.Codec.QRCodeEncoder();
            qrCodeEncoder.QRCodeEncodeMode = ThoughtWorks.QRCode.Codec.QRCodeEncoder.ENCODE_MODE.BYTE;
            qrCodeEncoder.QRCodeScale = 5;
            qrCodeEncoder.QRCodeVersion = 7;
            qrCodeEncoder.QRCodeErrorCorrect = ThoughtWorks.QRCode.Codec.QRCodeEncoder.ERROR_CORRECTION.M;
            Image image;
            String data = url;
            image = qrCodeEncoder.Encode(data);
            string loc = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            image.Save(loc+@"\rtts.jpg");
            */
        }

        static void Emulate()
        {
            UdpClient udpc = new UdpClient("127.0.0.1", 5781);

            string p = "RTT@A00T0:004/PING&&";
            byte[] b = System.Text.Encoding.ASCII.GetBytes(p);
            udpc.Send(b, b.Length);

            p = "RTT@A01J0:058/{IMEI:'5553578951420',ID:'001',Event:'GRM-18-6',Filter:''}&&";
            p += "RTT@A02J1:117/{IMEI:'5553578951423',ID:'002',Event:'GRM-18-6',DT:'2017-10-16T09:15:00Z',Lat:55.55,Lon:37.5,Alt:0.0,Hdg:0.0,Spd:0.0}&&";
            p += "RTT@A02J1:104/{IMEI:'5553578951425',ID:'MaxRiv',DT:'2017-10-16T09:18:00Z',Lat:55.45,Lon:37.39,Alt:0.0,Hdg:0.0,Spd:0.0}&&";
            b = System.Text.Encoding.ASCII.GetBytes(p);
            udpc.Send(b, b.Length);

        }
    }
}
