using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace rtts
{
    public class TriggerServer
    {
        public const string RTTHeader = "# RTT {0} Server 0.1a";
        public const string ServerName = "RTTS#N#1";
        private static string srvr = String.Format(RTTHeader,"TRG");

        // http
        public static int HTTPPort = 0;// 5780;

        // upload only
        public UDPUploadServer UDPServer = null;
        public static int UDPPort = 5781;

        // download + filter
        public TCPViewerServer TCPServer = null;
        public static int TCPPort = 5782;

        // download only
        public AISViewerServer AISServer = null;
        public static int AISPort = 5783;

        // download + filter
        public APRSIOServer APRServer = null;
        public static int APRSPort = 5784;
        
        // upload only
        public FRSUploadServer FRSServer = null;
        public static int FRSPort = 5785;

        public delegate void onReceivePacket(RTTPacket PacketData, PT0102 JSONData, byte Source);

        public TriggerServer()
        {
            Console.WriteLine(srvr + "\r\n# http:{4},udp:{0},tcp:{1},ais:{2},aprs:{3},frs:{5}\r\n", UDPPort, TCPPort, AISPort, APRSPort, HTTPPort, FRSPort);
        }

        public void Start()
        {
            if (UDPPort != 0)
            {
                UDPServer = new UDPUploadServer(UDPPort);
                UDPServer.onPacket = ReceivePacket;
                UDPServer.Start();
            };

            if (TCPPort != 0)
            {
                TCPServer = new TCPViewerServer(TCPPort);
                TCPServer.Start();
            };

            if (AISPort != 0)
            {
                AISServer = new AISViewerServer(AISPort);
                AISServer.Start();
            };

            if (APRSPort != 0)
            {
                APRServer = new APRSIOServer(APRSPort);
                APRServer.Start();
            };

            if (FRSPort != 0)
            {
                FRSServer = new FRSUploadServer(FRSPort);
                FRSServer.Parent = this;
                FRSServer.Start();
            };
        }

        public void Stop()
        {
            if (UDPServer != null)
            {
                UDPServer.Stop();
                UDPServer = null;
            };

            if (TCPServer != null)
            {
                TCPServer.Stop();
                TCPServer = null;
            };

            if(AISServer != null)
            {
                AISServer.Stop();
                AISServer = null;
            };


            if (APRServer != null)
            {
                APRServer.Stop();
                APRServer = null;
            };

            if (FRSServer != null)
            {
                FRSServer.Stop();
                FRSServer = null;
            };
        }

        // Source = 0 -- UDP
        public void ReceivePacket(RTTPacket PacketData, PT0102 JSONData, byte Source)
        {
            Console.WriteLine(PacketData.datatext);
            // forward
            if (JSONData.FixOk)
            {
                if ((TCPServer != null) && (TCPServer.IsActive))
                    TCPServer.BroadcaseMsg(PacketData, JSONData);
                if ((AISServer != null) && (AISServer.IsActive))
                    AISServer.BroadcaseMsg(AISServer.GetBroadcastAIS(JSONData));
                if ((APRServer != null) && (APRServer.IsActive))
                {
                    System.Text.RegularExpressions.Regex rx = new System.Text.RegularExpressions.Regex(@"\d");
                    System.Text.RegularExpressions.Match mx = rx.Match(JSONData.ID);
                    int v = -1;
                    if (mx.Success) v = 0;
                    while (mx.Success) { v += byte.Parse(mx.Value); mx = mx.NextMatch(); };
                    string symbol = v == -1 ? "/\"" : "/"+(v % 10).ToString();
                    string txt = APRServer.GetPacketText_APRS(JSONData.DT, JSONData.ID, "APRS", JSONData.Lat, JSONData.Lon, JSONData.Spd, JSONData.Hdg, JSONData.Alt, symbol, "");
                    byte[] data = System.Text.Encoding.ASCII.GetBytes(txt);
                    APRServer.Broadcast(data, JSONData);
                };
            };
        }
    }

    public struct RTTPacket
    {
        public string version;
        public string ptype;
        public string dtype;
        public byte dencoding;
        public int dlength;
        public byte[] data;
        public string datatext;
        public string packet_text;
        public int packet_length;

        public bool valid;

        public Encoding GetEncoding()
        {
            if (dencoding == 1) return Encoding.GetEncoding(1251);
            if (dencoding == 2) return Encoding.UTF8;
            return Encoding.ASCII;
        }

        public static RTTPacket FromBytes(byte[] data, int startIndex)
        {
            RTTPacket p = new RTTPacket();
            if ((data == null) || (data.Length == 0))
            {
                p.valid = false;
                return p;
            };
            p.valid = ((data[startIndex] == 0x52) && (data[startIndex + 1] == 0x54) && (data[startIndex + 2] == 0x54) && (data[startIndex + 3] == 0x40));
            if (!p.valid) return p;

            p.version = System.Text.Encoding.ASCII.GetString(data, startIndex + 4, 1);
            p.ptype = System.Text.Encoding.ASCII.GetString(data, startIndex + 5, 2);
            p.dtype = System.Text.Encoding.ASCII.GetString(data, startIndex + 7, 1);
            p.dencoding = byte.Parse(System.Text.Encoding.ASCII.GetString(data, startIndex + 8, 1));
            p.dlength = int.Parse(System.Text.Encoding.ASCII.GetString(data, startIndex + 10, 3));
            p.data = new byte[p.dlength];
            Array.Copy(data, startIndex + 14, p.data, 0, p.dlength);
            p.datatext = null;
            if (p.dlength != 0) p.datatext = p.GetEncoding().GetString(p.data);
            p.packet_length = 14 + p.dlength;
            p.valid = ((data[startIndex + 14 + p.dlength] == 0x26) && (data[startIndex + 14 + p.dlength + 1] == 0x26));
            if (!p.valid) return p;
            p.packet_length += 2;
            p.packet_text = System.Text.Encoding.ASCII.GetString(data, startIndex, 14) + p.datatext + System.Text.Encoding.ASCII.GetString(data, startIndex + 14 + p.dlength, 2);
            return p;
        }
    }

    [Serializable]
    public struct PT0102
    {

        public string IMEI;
        public string ID;
        public string Event;
        public DateTime DT;
        public float Lat;
        public float Lon;
        public float Alt;
        public float Hdg;
        public float Spd;

        public string Filter;

        public static PT0102 FromJSON(string text)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<PT0102>(text);
        }

        public bool FixOk
        {
            get
            {
                return (Lat != 0) && (Lon != 0);
            }
        }
    }
}
