using System;
using System.IO;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

namespace rtts
{
    public class AISViewerServer
    {
        public static string welcome = String.Format(TriggerServer.RTTHeader, "AIS");

        private int listerPort = 0;
        private SimpleServers.SimpleTNCTCPServer server = null;
        private Hashtable clients = new Hashtable();
        private bool _isActive = false;
        private int Online = 0;
        public int ActiveConnections { get { return Online; } }

        public delegate void onConnectEvent(ClientInfo ci);
        public onConnectEvent onClientConnect;

        public AISViewerServer(int port) { listerPort = port; }

        public void Start(int port)
        {
            if (_isActive) return;
            this.listerPort = port;
            Start();
        }

        public void Start()
        {
            if (_isActive) return;            

            if (listerPort > 0)
            {
                _isActive = true;
                server = new SimpleServers.SimpleTNCTCPServer(listerPort);
                server.OnConnect = new SimpleServers.SimpleServer.ValidClient(onConnect);
                server.OnDisconnect = new SimpleServers.SimpleServer.ValidClient(onDisconnect);
                server.Start();
                (new Thread(PingThread)).Start();                
            };
        }

        public void Stop()
        {
            if (!_isActive) return;
            _isActive = false;
            if (server != null) server.Stop();
            server = null;
        }

        private void PingThread()
        {
            byte pto = 1;
            while(_isActive)
            {
                pto++;
                if(pto == 0) // in 15 sec
                    BroadcaseMsg("#PING");
                Thread.Sleep(58);
            };
        }

        public bool IsActive
        {
            get
            {
                return _isActive;
            }
        }

        public void onConnect(TcpClient client, ulong clientID)
        {
            Online++;
            ClientInfo ci = new ClientInfo(clientID, client);
            ci.connected = DateTime.Now;
            lock (clients) clients.Add(clientID, ci);
            
            SendMsg(ci, welcome);

            if (onClientConnect != null) onClientConnect(ci);
            return; 
            
            //Console.ForegroundColor = ConsoleColor.Cyan;
            //Console.WriteLine("& " + clientID.ToString() + " AIS connected from " + client.Client.RemoteEndPoint.ToString());

            //{
            //    PositionReportClassB clc = new PositionReportClassB();
            //    clc.a03mmsi = 11030047;
            //    clc.accuracy = true;
            //    clc.lat = 55.57;
            //    clc.lon = 37.59;
            //    clc.speed = 31;
            //    clc.heading = 189;
            //    clc.course = 189;

            //    string cmpa = "!AIVDM,1,1,,A," + clc.ToString() + ",0";
            //    cmpa += "*" + AISTransCoder.Checksum(cmpa);
            //    byte[] ba2 = System.Text.Encoding.GetEncoding(1251).GetBytes(cmpa + "\r\n");
            //    ci.client.GetStream().Write(ba2, 0, ba2.Length);
            //};


            //PositionReportClassA prca = new PositionReportClassA();
            //prca.a09lat = 55.55;
            //prca.a08lon = 37.55;
            //prca.a03mmsi = 11030048;
            //prca.a10course = 75;
            //prca.a11heading = 75;
            //prca.a06speed = 10;
            //string packeds = prca.ToString();
            //string cmpd = "!AIVDM,1,1,,A," + packeds + ",0";
            //cmpd += "*" + AISTransCoder.Checksum(cmpd);
            //byte[] ba2s = System.Text.Encoding.GetEncoding(1251).GetBytes(cmpd + "\r\n");
            //ci.client.GetStream().Write(ba2s, 0, ba2s.Length);

            //StaVoyData sd = new StaVoyData();
            //sd.a03mmsi = 110300048;
            //sd.callsign = "ANTOXA";
            //sd.destination = "UNKNOWN";
            //sd.name = "RUZA";
            //sd.shipNo = 11030048;
            //sd.shiptype = 79;
            //{
            //    string cmpa = "!AIVDM,1,1,,A," + sd.ToString() + ",0";
            //    cmpa += "*" + AISTransCoder.Checksum(cmpa);
            //    byte[] ba2 = System.Text.Encoding.GetEncoding(1251).GetBytes(cmpa + "\r\n");
            //    ci.client.GetStream().Write(ba2, 0, ba2s.Length);
            //};

            //{
            //    StaticDataReport sdr = new StaticDataReport();
            //    sdr.mmsi = 11030047;
            //    sdr.name = "TEMYCH";
            //    sdr.callsign = "UB3APB";
            //    string cmpa = "!AIVDM,1,1,,A," + sdr.ToString() + ",0";
            //    cmpa += "*" + AISTransCoder.Checksum(cmpa);
            //    byte[] ba2 = System.Text.Encoding.GetEncoding(1251).GetBytes(cmpa + "\r\n");
            //    ci.client.GetStream().Write(ba2, 0, ba2s.Length);
            //};

        }

        public string GetBroadcastAIS(PT0102 json)
        {
            PositionReportClassBE pr = new PositionReportClassBE();
            pr.a03mmsi = 111100000 + (uint)rttutils.CSChecksum(json.ID);
            pr.name = json.ID.ToUpper();
            pr.shiptype = 70;
            double h, s;
            pr.lat = json.Lat;
            pr.lon = json.Lon;
            pr.heading = (ushort)json.Hdg;
            pr.speed = (uint)(json.Spd * 1.852);
            pr.accuracy = (pr.lat != 0) && (pr.lon != 0);
            pr.course = pr.heading;
            string cmpa = "!AIVDM,1,1,,A," + pr.ToString() + ",0";
            cmpa += "*" + AISTransCoder.Checksum(cmpa);
            return cmpa;
        }

        public void onDisconnect(TcpClient client, ulong clientID)
        {
            Online--;
            Console.ForegroundColor = ConsoleColor.Cyan;
            //Console.WriteLine("& " + clientID.ToString() + " AIS disconnected");

            lock(clients) clients.Remove(clientID);
        }

        public bool SendMsg(ClientInfo ci, string msg)
        {
            byte[] ba2s = System.Text.Encoding.GetEncoding(1251).GetBytes(msg + "\r\n");
            try
            {
                if (ci.client == null) return false;
                if (!ci.client.Connected) return false;
                ci.client.GetStream().Write(ba2s, 0, ba2s.Length);
                Console.ForegroundColor = ConsoleColor.Magenta;
                //Console.WriteLine("&&" + ci.ID.ToString() + "< " + msg);
                return true;
            }
            catch { };
            return false;
        }

        public void BroadcaseMsg(string msg)
        {
            ClientInfo[] cls = Clients;
            foreach (ClientInfo ci in cls)
                SendMsg(ci, msg);
        }

        public uint Count
        {
            get
            {
                return (uint)clients.Count;
            }
        }

        public ClientInfo[] Clients
        {
            get
            {
                List<ClientInfo> cls = new List<ClientInfo>();
                lock (clients) foreach (object val in clients.Values) cls.Add((ClientInfo)val);
                return cls.ToArray();
            }
        }
    }


    public class PositionReportClassA
    {
        public bool valid = false;
        public byte length = 168;

        private uint a01type = 1;
        private uint a02repeat = 0;
        public uint a03mmsi;
        private uint a04status = 15;
        private int a05turn = 0;
        public uint a06speed = 0;
        public bool a07accuracy = false;
        public double a08lon = 0;
        public double a09lat = 0;
        public double a10course = 0;
        public ushort a11heading = 0;
        private uint a12second = 60;
        private uint a13maneuver = 1;
        private uint a16radio = 0;

        public static PositionReportClassA FromAIS(byte[] unpackedBytes)
        {
            PositionReportClassA res = new PositionReportClassA();
            res.a01type = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 0, 6);
            if ((res.a01type < 1) || (res.a01type > 3)) return res;

            res.valid = true;
            res.a02repeat = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 6, 2);
            res.a03mmsi = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 8, 30);
            res.a04status = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 38, 4);
            res.a05turn = AISTransCoder.GetBitsAsSignedInt(unpackedBytes, 42, 8);
            res.a06speed = (uint)(AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 50, 10) / 10 * 1.852);
            res.a07accuracy = (byte)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 60, 1) == 1 ? true : false;
            res.a08lon = AISTransCoder.GetBitsAsSignedInt(unpackedBytes, 61, 28) / 600000.0;
            res.a09lat = AISTransCoder.GetBitsAsSignedInt(unpackedBytes, 89, 27) / 600000.0;
            res.a10course = AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 116, 12) / 10.0;
            res.a11heading = (ushort)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 128, 9);
            res.a12second = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 137, 6);
            res.a13maneuver = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 143, 2);
            res.a16radio = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 149, 19);
            return res;
        }

        public static PositionReportClassA FromAIS(string ais)
        {
            byte[] unp = AISTransCoder.UnpackAisEncoding(ais);
            return FromAIS(unp);
        }

        public override string ToString()
        {
            byte[] unpackedBytes = new byte[21];
            if ((a01type < 0) || (a01type > 3)) a01type = 3;
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 0, 6, (int)a01type); // type
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 6, 2, (int)a02repeat); // repeat (no)
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 8, 30, (int)a03mmsi); // mmsi
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 38, 4, (int)a04status); // status (default)
            AISTransCoder.SetBitsAsSignedInt(unpackedBytes, 42, 8, (int)a05turn); // turn (off)
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 50, 10, (int)(a06speed / 1.852 * 10)); // speed                                                
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 60, 1, a07accuracy ? 1 : 0); // FixOk
            AISTransCoder.SetBitsAsSignedInt(unpackedBytes, 61, 28, (int)(a08lon * 600000)); // lon
            AISTransCoder.SetBitsAsSignedInt(unpackedBytes, 89, 27, (int)(a09lat * 600000)); // lat        
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 116, 12, (int)(a10course * 10)); // course
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 128, 9, (int)a11heading); // heading
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 137, 6, (int)a12second); // timestamp (not available (default))
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 143, 2, (int)a13maneuver); // no Maneuver 
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 149, 19, (int)a16radio); // no Maneuver 
            return AISTransCoder.EnpackAisToString(unpackedBytes);
        }
    }

    public class StaVoyData
    {
        public bool valid = false;
        public int length = 424;

        private uint a01type = 5;
        private uint a02repeat = 0;
        public uint a03mmsi;
        private int aisv = 0;
        public uint shipNo;
        public string callsign;
        public string name;
        public int shiptype = 31;
        private int posfixt = 1;
        public string destination = "";

        public static StaVoyData FromAIS(byte[] unpackedBytes)
        {
            StaVoyData res = new StaVoyData();
            res.a01type = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 0, 6);
            if (res.a01type != 5) return res;
            res.valid = true;

            res.a02repeat = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 6, 2);
            res.a03mmsi = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 8, 30);
            res.aisv = AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 38, 2);
            res.shipNo = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 40, 30);
            res.callsign = AISTransCoder.GetAisString(unpackedBytes, 70, 42);
            res.name = AISTransCoder.GetAisString(unpackedBytes, 112, 120);
            res.shiptype = AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 232, 8); //30 - fishing, 31 - towing; 34 - diving; 36 - sailing; 37 - pleasure craft; 
            // 40 - hi speed; 50 - pilot vessel; 52 - tug; 60/69 - passenger; 70/79 - cargo; 80/89 - tanker
            res.posfixt = AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 270, 4);
            res.destination = AISTransCoder.GetAisString(unpackedBytes, 302, 120);

            return res;
        }

        public static StaVoyData FromAIS(string ais)
        {
            byte[] unp = AISTransCoder.UnpackAisEncoding(ais);
            return FromAIS(unp);
        }

        public override string ToString()
        {
            byte[] unpackedBytes = new byte[54];
            a01type = 5;
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 0, 6, (int)a01type);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 6, 2, (int)a02repeat);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 8, 30, (int)a03mmsi);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 38, 2, (int)aisv);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 40, 30, (int)shipNo);
            AISTransCoder.SetAisString(unpackedBytes, 70, 42, callsign);
            AISTransCoder.SetAisString(unpackedBytes, 112, 120, name);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 232, 8, (int)shiptype); //30 - fishing, 31 - towing; 34 - diving; 36 - sailing; 37 - pleasure craft; 
            // 40 - hi speed; 50 - pilot vessel; 52 - tug; 60/69 - passenger; 70/79 - cargo; 80/89 - tanker
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 240, 9, 4);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 249, 9, 1);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 258, 6, 1);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 264, 6, 2);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 270, 4, (int)posfixt);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 274, 4, DateTime.UtcNow.Month);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 278, 5, DateTime.UtcNow.Day);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 283, 5, DateTime.UtcNow.Hour);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 288, 6, DateTime.UtcNow.Minute);
            AISTransCoder.SetAisString(unpackedBytes, 302, 120, destination);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 422, 1, 0);
            return AISTransCoder.EnpackAisToString(unpackedBytes);
        }
    }

    public class PositionReportClassB
    {
        public bool valid = false;
        public int length = 168;

        private uint a01type = 18;
        private uint a02repeat = 0;
        public uint a03mmsi;
        public uint speed;
        public bool accuracy;
        public double lon;
        public double lat;
        public double course = 0;
        public ushort heading = 0;
        private uint second = 60;

        public static PositionReportClassB FromAIS(byte[] unpackedBytes)
        {
            PositionReportClassB res = new PositionReportClassB();
            res.a01type = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 0, 6);
            if (res.a01type != 18) return res;

            res.valid = true;
            res.a02repeat = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 6, 2);
            res.a03mmsi = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 8, 30);
            res.speed = (uint)(AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 46, 10) / 10 * 1.852);
            res.accuracy = (byte)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 56, 1) == 1 ? true : false;
            res.lon = AISTransCoder.GetBitsAsSignedInt(unpackedBytes, 57, 28) / 600000.0;
            res.lat = AISTransCoder.GetBitsAsSignedInt(unpackedBytes, 85, 27) / 600000.0;
            res.course = AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 112, 12) / 10.0;
            res.heading = (ushort)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 124, 9);
            res.second = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 133, 6);
            return res;
        }

        public static PositionReportClassB FromAIS(string ais)
        {
            byte[] unp = AISTransCoder.UnpackAisEncoding(ais);
            return FromAIS(unp);
        }

        public override string ToString()
        {
            byte[] unpackedBytes = new byte[21];
            a01type = 18;
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 0, 6, (int)a01type); // type
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 6, 2, (int)a02repeat);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 8, 30, (int)a03mmsi);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 46, 10, (int)(speed / 1.852 * 10)); // speed            
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 56, 1, accuracy ? 1 : 0);
            AISTransCoder.SetBitsAsSignedInt(unpackedBytes, 57, 28, (int)(lon * 600000));
            AISTransCoder.SetBitsAsSignedInt(unpackedBytes, 85, 27, (int)(lat * 600000));
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 112, 12, (int)(course * 10.0));
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 124, 9, heading);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 133, 6, 60);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 142, 1, 1);
            return AISTransCoder.EnpackAisToString(unpackedBytes);
        }

    }

    public class PositionReportClassBE
    {
        public bool valid = false;
        public int length = 312;

        private uint a01type = 19;
        private uint a02repeat = 0;
        public uint a03mmsi;
        public uint speed;
        public bool accuracy;
        public double lon;
        public double lat;
        public double course = 0;
        public ushort heading = 0;
        private uint second = 60;
        public string name;
        public int shiptype = 31;

        public static PositionReportClassBE FromAIS(byte[] unpackedBytes)
        {
            PositionReportClassBE res = new PositionReportClassBE();
            res.a01type = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 0, 6);
            if (res.a01type != 19) return res;

            res.valid = true;
            res.a02repeat = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 6, 2);
            res.a03mmsi = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 8, 30);
            res.speed = (uint)(AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 46, 10) / 10 * 1.852);
            res.accuracy = (byte)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 56, 1) == 1 ? true : false;
            res.lon = AISTransCoder.GetBitsAsSignedInt(unpackedBytes, 57, 28) / 600000.0;
            res.lat = AISTransCoder.GetBitsAsSignedInt(unpackedBytes, 85, 27) / 600000.0;
            res.course = AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 112, 12) / 10.0;
            res.heading = (ushort)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 124, 9);
            res.second = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 133, 6);
            res.name = AISTransCoder.GetAisString(unpackedBytes, 143, 120);
            res.shiptype = AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 263, 8);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 271, 9, 4);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 280, 9, 1);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 289, 6, 1);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 295, 6, 2);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 301, 4, 1);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 306, 6, 1);
            return res;
        }

        public static PositionReportClassBE FromAIS(string ais)
        {
            byte[] unp = AISTransCoder.UnpackAisEncoding(ais);
            return FromAIS(unp);
        }

        public override string ToString()
        {
            byte[] unpackedBytes = new byte[39];
            a01type = 19;
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 0, 6, (int)a01type); // type
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 6, 2, (int)a02repeat);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 8, 30, (int)a03mmsi);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 46, 10, (int)(speed / 1.852 * 10)); // speed            
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 56, 1, accuracy ? 1 : 0);
            AISTransCoder.SetBitsAsSignedInt(unpackedBytes, 57, 28, (int)(lon * 600000));
            AISTransCoder.SetBitsAsSignedInt(unpackedBytes, 85, 27, (int)(lat * 600000));
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 112, 12, (int)(course * 10.0));
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 124, 9, heading);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 133, 6, 60);
            AISTransCoder.SetAisString(unpackedBytes, 143, 120, name);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 263, 8, shiptype);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 301, 4, 1);
            return AISTransCoder.EnpackAisToString(unpackedBytes);
        }

    }

    public class StaticDataReport
    {
        public bool valid = false;
        public int length = 168;

        private uint type = 24;
        private uint repeat = 0;
        public uint mmsi;
        public string name;
        public int shiptype = 31;
        public uint shipNo;
        public string callsign;

        public static StaticDataReport FromAIS(byte[] unpackedBytes)
        {
            StaticDataReport res = new StaticDataReport();
            res.type = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 0, 6);
            if (res.type != 24) return res;
            res.valid = true;
            res.repeat = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 6, 2);
            res.mmsi = (uint)AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 8, 30);
            res.name = AISTransCoder.GetAisString(unpackedBytes, 40, 120);
            res.shiptype = AISTransCoder.GetBitsAsUnsignedInt(unpackedBytes, 40, 8);
            res.callsign = AISTransCoder.GetAisString(unpackedBytes, 90, 42);
            return res;
        }

        public static StaticDataReport FromAIS(string ais)
        {
            byte[] unp = AISTransCoder.UnpackAisEncoding(ais);
            return FromAIS(unp);
        }

        public override string ToString()
        {
            byte[] unpackedBytes = new byte[21];
            this.type = 24;
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 0, 6, (int)this.type);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 6, 2, (int)repeat);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 8, 30, (int)mmsi);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 38, 2, 0); // partA
            AISTransCoder.SetAisString(unpackedBytes, 40, 120, name);
            //AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 38, 2, 1);
            //AISTransCoder.SetAisString(unpackedBytes, 40, 120, name);
            return AISTransCoder.EnpackAisToString(unpackedBytes);
        }
        public string ToStringA()
        {
            return this.ToString();
        }

        public string ToStringB()
        {
            byte[] unpackedBytes = new byte[21];
            this.type = 24;
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 0, 6, (int)this.type);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 6, 2, (int)repeat);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 8, 30, (int)mmsi);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 38, 2, 1); // partB            
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 40, 8, (int)shiptype);
            AISTransCoder.SetAisString(unpackedBytes, 90, 42, callsign);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 132, 9, 4);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 141, 9, 1);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 150, 6, 1);
            AISTransCoder.SetBitsAsUnsignedInt(unpackedBytes, 156, 6, 2);
            return AISTransCoder.EnpackAisToString(unpackedBytes);
        }

    }

    public class AISTransCoder
    {
        // desc http://www.bosunsmate.org/ais
        // test http://ais.tbsalling.dk/
        // test http://www.aggsoft.com/ais-decoder.htm
        // api  http://catb.org/gpsd/AIVDM.html        

        // Orux Decode:  AIS 1,2,3,5,18,19,24 ( http://www.oruxmaps.com/foro/viewtopic.php?t=1627 )
        // http://wiki.openseamap.org/wiki/OruxMaps
        //
        // AIS Message Types
        // 01 - Position Report with SOTDMA
        // 02 - Position Report with SOTDMA
        // 03 - Position Report with ITDMA
        // 05 - Static and Voyage Related Dat;; http://www.navcen.uscg.gov/?pageName=AISMessagesAStatic
        // 18 - Standard Class B CS Position Report
        // 19 - Extended Class B CS Position Report
        // 24 - Static Data Report

        public static string Checksum(string sentence)
        {
            int iFrom = 0;
            if (sentence.IndexOf('$') == 0) iFrom++;
            if (sentence.IndexOf('!') == 0) iFrom++;
            int iTo = sentence.Length;
            if (sentence.LastIndexOf('*') == (sentence.Length - 3))
                iTo = sentence.IndexOf('*');
            int checksum = Convert.ToByte(sentence[iFrom]);
            for (int i = iFrom + 1; i < iTo; i++)
                checksum ^= Convert.ToByte(sentence[i]);
            return checksum.ToString("X2");
        }

        public static byte[] UnpackAisEncoding(string s)
        {
            return UnpackAisEncoding(Encoding.UTF8.GetBytes(s));
        }

        private static byte[] UnpackAisEncoding(byte[] data)
        {
            int outputLen = ((data.Length * 6) + 7) / 8;
            byte[] result = new byte[outputLen];

            // We are always combining two input bytes into one or two output bytes.
            // This happens in three phases.  The phases are
            //  0 == 6,2  (six bits of the current source byte, plus 2 bits of the next)
            //  1 == 4,4
            //  2 == 2,6;
            int iSrcByte = 0;
            byte nextByte = ConvertSixBit(data[iSrcByte]);
            for (int iDstByte = 0; iDstByte < outputLen; ++iDstByte)
            {
                byte currByte = nextByte;
                if (iSrcByte < data.Length - 1)
                    nextByte = ConvertSixBit(data[++iSrcByte]);
                else
                    nextByte = 0;

                // iDstByte % 3 is the 'phase' we are in and determins the shifting pattern to use
                switch (iDstByte % 3)
                {
                    case 0:
                        // 11111122 2222xxxx
                        result[iDstByte] = (byte)((currByte << 2) | (nextByte >> 4));
                        break;
                    case 1:
                        // 11112222 22xxxxxx
                        result[iDstByte] = (byte)((currByte << 4) | (nextByte >> 2));
                        break;
                    case 2:
                        // 11222222 xxxxxxxx
                        result[iDstByte] = (byte)((currByte << 6) | (nextByte));
                        // There are now no remainder bits, so we need to eat another input byte
                        if (iSrcByte < data.Length - 1)
                            nextByte = ConvertSixBit(data[++iSrcByte]);
                        else
                            nextByte = 0;
                        break;
                }
            }

            return result;
        }

        public static string EnpackAisToString(byte[] ba)
        {
            return Encoding.UTF8.GetString(EnpackAisEncoding(ba));
        }

        private static byte[] EnpackAisEncoding(byte[] ba)
        {
            List<byte> res = new List<byte>();
            for (int i = 0; i < ba.Length; i++)
            {
                int val = 0;
                int val2 = 0;
                switch (i % 3)
                {
                    case 0:
                        val = (byte)((ba[i] >> 2) & 0x3F);
                        break;
                    case 1:
                        val = (byte)((ba[i - 1] & 0x03) << 4) | (byte)((ba[i] & 0xF0) >> 4);
                        break;
                    case 2:
                        val = (byte)((ba[i - 1] & 0x0F) << 2) | (byte)((ba[i] & 0xC0) >> 6);
                        val2 = (byte)((ba[i] & 0x3F)) + 48;
                        if (val2 > 87) val2 += 8;
                        break;
                };
                val += 48;
                if (val > 87) val += 8;
                res.Add((byte)val);
                if ((i % 3) == 2) res.Add((byte)val2);
            };
            return res.ToArray();
        }

        public static byte ConvertSixBit(byte b)
        {
            byte result = (byte)(b - 48);
            if (result > 39)
                result -= 8;
            return result;
        }

        public static string GetAisString(byte[] source, int start, int len)
        {
            string key = "@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_ !\"#$%&'()*+,-./0123456789:;<=>?";
            int l = key.Length;
            string val = "";
            for (int i = 0; i < len; i += 6)
            {
                byte c = (byte)(GetBitsAsSignedInt(source, start + i, 6) & 0x3F);
                val += key[c];
            };
            return val.Trim();
        }

        public static void SetAisString(byte[] source, int start, int len, string val)
        {
            if (val == null) val = "";
            string key = "@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_ !\"#$%&'()*+,-./0123456789:;<=>?;";
            int strlen = len / 6;
            if (val.Length > strlen) val = val.Substring(0, strlen);
            while (val.Length < strlen) val += " ";
            int s = 0;
            for (int i = 0; i < len; i += 6, s++)
            {
                byte c = (byte)key.IndexOf(val[s]);
                SetBitsAsSignedInt(source, start + i, 6, c);
            };
        }

        public static int GetBitsAsSignedInt(byte[] source, int start, int len)
        {
            int value = GetBitsAsUnsignedInt(source, start, len);
            if ((value & (1 << (len - 1))) != 0)
            {
                // perform 32 bit sign extension
                for (int i = len; i < 32; ++i)
                {
                    value |= (1 << i);
                }
            };
            return value;
        }

        public static void SetBitsAsSignedInt(byte[] source, int start, int len, int val)
        {
            int value = val;
            if (value < 0)
            {
                value = ~value;
                for (int i = len; i < 32; ++i)
                {
                    value |= (1 << i);
                };
            }
            SetBitsAsUnsignedInt(source, start, len, val);
        }

        public static int GetBitsAsUnsignedInt(byte[] source, int start, int len)
        {
            int result = 0;

            for (int i = start; i < (start + len); ++i)
            {
                int iByte = i / 8;
                int iBit = 7 - (i % 8);
                result = result << 1 | (((source[iByte] & (1 << iBit)) != 0) ? 1 : 0);
            }

            return result;
        }

        public static void SetBitsAsUnsignedInt(byte[] source, int start, int len, int val)
        {
            int bit = len - 1;
            for (int i = start; i < (start + len); ++i, --bit)
            {
                int iByte = i / 8;
                int iBit = 7 - (i % 8);
                byte mask = (byte)(0xFF - (byte)(1 << iBit));
                byte bitm = (byte)~mask;
                byte b = (byte)(((val >> bit) & 0x01) << iBit);
                source[iByte] = (byte)((source[iByte] & mask) | b);
            }
        }
    }

    public class ClientInfo
    {
        public ulong ID;
        public TcpClient client;
        public DateTime connected;
        public DateTime lastActivity;
        public byte clientType = 0; // 0 - AIS // 1 - APRS Client // 2 - APRS Not Registered // 3 - APRS Registered
        
        public ClientInfo(ulong ID, TcpClient client)
        {
            this.ID = ID;
            this.client = client;
        }

        public string rttIMEI;
        public string rttID;
        public string rttEvent;
        public APRSIOServer.ClientAPRSFilter rttFilter = null;

        public string SetFilter(string filter)
        {
            this.rttFilter = new APRSIOServer.ClientAPRSFilter(filter);
            return this.rttFilter.ToString();
        }

        public int rxBadPacketsTtl;
        public byte rxBadPacketsSBS;

        public string frsPhone = "";
        
        public GPSInfo rxLastGPSOk;
        public GPSInfo rxLastGPSBad;
    }

}
