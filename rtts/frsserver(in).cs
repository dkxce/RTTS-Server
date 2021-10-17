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
    public class FRSUploadServer 
    {
        // STATIC
        public static string welcome = String.Format(TriggerServer.RTTHeader, "FRS");

        public string def_user_phone = "1103";
        private int listerPort = 8006;
        private SimpleServers.SimpleTNCTCPServer server = null;
        private Hashtable clients = new Hashtable();
        private bool _isActive = false;
        private int Online = 0;
        public int ActiveConnections { get { return Online; } }

        public FRSUploadServer() { }
        public FRSUploadServer(int port) { listerPort = port; }
        public TriggerServer Parent = null;

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
                server.OnDataValid2 = new SimpleServers.SimpleServer.ValidData2(onClientData);
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
            while (_isActive)
            {
                pto++;
                if (pto == 0) // in 15 sec
                    BroadcaseMsg(STD.ChecksumAdd2Line("$FRCMD,IMEI,_Ping,Inline"));
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
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("^ " + clientID.ToString() + " GPSGate connected from " + client.Client.RemoteEndPoint.ToString());            
        }

        public void onDisconnect(TcpClient client, ulong clientID)
        {
            Online--;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("^ " + clientID.ToString() + " GPSGate disconnected");

            lock (clients) clients.Remove(clientID);
        }

        public bool onClientData(TcpClient client, ulong clientID, string line)
        {
            ClientInfo ci = null;
            lock (clients) ci = (ClientInfo)clients[clientID];
            if (ci == null) return false;

            ci.lastActivity = DateTime.Now;
            if ((line == null) || (line == String.Empty)) return false;

            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine("--" + clientID.ToString() + "> " + line);

            if (line.IndexOf("$") == 0)
            {
                ci.rxBadPacketsSBS = 0;
                if (line == STD.ChecksumAdd2Line(line.Remove(line.Length - 3)))
                    onClientData(ci, line);
                else
                    SendMsg(ci, STD.ChecksumAdd2Line("$FRERR,CRCBAD,CRC Checksum is wrong"));
            }
            else
            {
                ci.rxBadPacketsSBS++;
                ci.rxBadPacketsTtl++;
                SendMsg(ci, "Incoming data not supported (" + (5 - ci.rxBadPacketsTtl).ToString() + ")");
                if (ci.rxBadPacketsSBS > 5) client.Close();
                return false;
            };

            return true;
        }

        public void onClientData(ClientInfo ci, string line)
        {
            Match rx;

            // $FRPAIR // ???
            if ((rx = Regex.Match(line, STD.const_tx_pair)).Success)
            {
                ci.frsPhone = rx.Groups[2].Value.Replace("+", "");
                ci.rttIMEI = rx.Groups[3].Value;
            };

            // IDENTIFY
            if (ci.frsPhone.IndexOf(def_user_phone) != 0)
            {
                // if (ci.client != null) ci.client.Close();
                // return;
            };

            // $FRCMD,imei,_Ping,Inline*01
            if ((rx = Regex.Match(line, STD.const_trx_cmd)).Success)
            {
                // _Ping
                if (rx.Groups[3].Value.ToLower() == "_ping")
                    SendMsg(ci, STD.ChecksumAdd2Line("$FRRET," + ci.rttIMEI + ",_Ping,Inline"));

                // _SendMessage
                if (rx.Groups[3].Value.ToLower() == "_sendmessage")
                {
                    string val = rx.Groups[4].Value;
                    string val2 = rx.Groups[5].Value;
                    SendMsg(ci, STD.ChecksumAdd2Line("$FRRET," + ci.rttIMEI + ",_SendMessage,Inline"));
                    // 0000.00000,N,00000.00000,E,0.0,0.000,0.0,190117,122708.837,0,BatteryLevel=78
                    // DDMM.mmmm,N,DDMM.mmmm,E,AA.a,SSS.ss,HHH.h,DDMMYY,hhmmss.dd,fixOk,NOTE*xx
                    Match rxa = Regex.Match(val2, STD.const_trx_sndpos);
                    if (rxa.Success)
                    {
                        GPSInfo gi = new GPSInfo();
                        gi.sLat = rxa.Groups[1].Value;
                        gi.lLat = rxa.Groups[2].Value;
                        gi.sLon = rxa.Groups[3].Value;
                        gi.lLon = rxa.Groups[4].Value;
                        gi.sAlt = rxa.Groups[5].Value;
                        gi.sSpeed = rxa.Groups[6].Value;
                        gi.sHeading = rxa.Groups[7].Value;
                        gi.sDate = rxa.Groups[8].Value;
                        gi.sTime = rxa.Groups[9].Value;
                        gi.sFix = rxa.Groups[10].Value;
                        gi.comment = rxa.Groups[11].Value;
                        gi.Parse();
                        if (gi.rFix)
                            ci.rxLastGPSOk = gi;
                        else
                            ci.rxLastGPSBad = gi;
                        onGPSGateData(ci, gi, line);
                    };
                };
            };
        }

        public void onGPSGateData(ClientInfo cli, GPSInfo gi, string line)
        {
            if (this.Parent == null) return;
            //if (cli.frsPhone == null) return;
            if (gi.rFix)
            {
                // Get ID, Event from IMEI //
                // FORWARD TO UDP
                string jsonD = "{"+String.Format("IMEI:'{0}',ID:'{1}',Event:'{2}',DT:'{3}',Lat:{4},Lon:{5},Alt:{6},Hdg:{7},Spd:{8}", new object[] { cli.rttIMEI, "ID", "Ev", gi.rDT.ToUniversalTime().ToString("YYY-MM-ddTHH:mm:ssZ"),
                    gi.rLat.ToString(System.Globalization.CultureInfo.InvariantCulture),gi.rLon.ToString(System.Globalization.CultureInfo.InvariantCulture),gi.rAlt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    gi.rHeading.ToString(System.Globalization.CultureInfo.InvariantCulture), gi.rSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture) }) + "}";
                
                string rttD = "RTT@A02J0:" + jsonD.Length.ToString("D3") + "/" + jsonD + "&&";
                byte[] data = System.Text.Encoding.ASCII.GetBytes(rttD);
                RTTPacket p = RTTPacket.FromBytes(data, 0);
                this.Parent.UDPServer.ReceivePacket(p);

                //UdpClient udpc = new UdpClient("127.0.0.1", TriggerServer.UDPPort);
                //udpc.Send(b, b.Length);
            };
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
                Console.WriteLine("--" + ci.ID.ToString() + "< " + msg);
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

    public class GPSInfo
    {
        public string sFix;
        public string sLat;
        public string lLat;
        public string sLon;
        public string lLon;
        public string sAlt;
        public string sHeading;
        public string sSpeed;
        public string sDate;
        public string sTime;
        public string comment;

        public bool rFix;
        public double rLat;
        public double rLon;
        public double rAlt;
        public double rHeading;
        public double rSpeed;
        public DateTime rDT;

        public void Parse()
        {
            rLat = double.Parse(sLat.Substring(2, 7), System.Globalization.CultureInfo.InvariantCulture);
            rLat = double.Parse(sLat.Substring(0, 2), System.Globalization.CultureInfo.InvariantCulture) + rLat / 60;
            if (lLat == "S") rLat *= -1;

            rLon = double.Parse(sLon.Substring(3, 7), System.Globalization.CultureInfo.InvariantCulture);
            rLon = double.Parse(sLon.Substring(0, 3), System.Globalization.CultureInfo.InvariantCulture) + rLon / 60;
            if (lLon == "W") rLon *= -1;


            rFix = sFix == "1";
            rAlt = double.Parse(sAlt, System.Globalization.CultureInfo.InvariantCulture);
            rHeading = double.Parse(sHeading, System.Globalization.CultureInfo.InvariantCulture);
            rSpeed = double.Parse(sSpeed, System.Globalization.CultureInfo.InvariantCulture) * 1.852;
            rDT = new DateTime(
                int.Parse(sDate.Substring(4, 2)),
                int.Parse(sDate.Substring(2, 2)),
                int.Parse(sDate.Substring(0, 2)),
                int.Parse(sTime.Substring(0, 2)),
                int.Parse(sTime.Substring(2, 2)),
                int.Parse(sTime.Substring(4, 2))
            );
        }
    }
   
    public class STD
    {
        // $FRPAIR,phone,imei*XX
        public const string const_tx_pair = @"^(\$FRPAIR),([\w\+]+),(\w+)\*(\w+)$";
        // $FRLIN,domain,username,password*XX
        public const string const_tx_login_user = @"^(\$FRLIN),(),(\w+),(\w+)\*(\w+)$";
        // $FRLIN,domain,username,password*XX
        public const string const_tx_login_imei = @"^(\$FRLIN),(IMEI),(\w+),()\*(\w{2})$";
        // $FRSES,sessionid*XX
        public const string const_rx_login_ok = @"^(\$FRSES),(\d+)\*(\w{2})$";
        // $FRERR,err_code,err_message*XX
        public const string const_rx_login_err = @"^(\$FRERR),([\w.\s]*),([\w.\s]*)\*(\w{2})$";
        // $FRRDT,username,interval*XX
        public const string const_tx_getUserPos = @"^(\$FRRDT),(\w+),([0-9.]+)\*(\w{2})$";
        // $FRWDT,datatype*XX
        public const string const_tx_forward = @"^(\$FRWDT),(NMEA|ALL)\*(\w{2})$";
        // $FRPOS,DDMM.mmmm,N,DDMM.mmmm,E,AA.a,SSS.ss,HHH.h,DDMMYY,hhmmss.dd,buddy*XX
        public const string const_trx_frpos = @"^(\$FRPOS),(\d{4}.\d+),(N|S),(\d{5}.\d+),(E|W),([0-9.]*),([0-9.]*),([0-9.]*),(\d{6}),([0-9.]{6,}),([\w.]*)\*(\w{2})$";
        // DDMM.mmmm,N,DDMM.mmmm,E,AA.a,SSS.ss,HHH.h,DDMMYY,hhmmss.dd,fixOk,NOTE*xx
        public const string const_trx_sndpos = @"^(\d{4}.\d+),(N|S),(\d{5}.\d+),(E|W),([0-9.]*),([0-9.]*),([0-9.]*),(\d{6}),([0-9.]{6,}),([\w.\s=]),([\w.\s=,]*)$";
        // $GPRMC,hhmmss.dd,A,DDMM.mmmm,N,DDMM.mmmm,E,SSS.ss,HHH.h,DDMMYY,,*0A
        public const string const_trx_gprmc = @"^(\$GPRMC),([0-9.]{6,}),(A|V),(\d{4}.\d+),(N|S),(\d{5}.\d+),(E|W),([0-9.]*),([0-9.]*),(\d{6}),([\w.]*),\*(\w{2})$";
        // $FRVER,major,minor,name_and_version*XX
        public const string const_trx_version = @"^(\$FRVER),(\d),(\d),([\w\s.]*)\*(\w{2})$";
        // $FRCMD,username,command,Nmea,size*XX
        // $FRCMD,username,command,Inline,param1,param2,...,paramN*XX
        public const string const_trx_cmd = @"^(\$FRCMD),(\w*),(\w+),(\w*),?([\w\s.,=]*)\*(\w{2})$";
        // $FRRET,username,command,Nmea,size*XX
        public const string const_trx_ret = @"$FRRET,Johan,_getupdaterules,Nmea,4*43";
        public const string const_trx_ret_val = @"$FRVAL,DistanceFilter,500.0*67";

        public static string[] incoming_commands = new string[] { "_getupdaterules", "_SendPosition", }; // Version 1.2
        public static string[] _getupdaterules_vars = new string[] { "DistanceFilter", "TimeFilter", "SpeedFilter", "DirectionFilter", "DirectionThreshold" };

        private static int Checksum(string str)
        {
            int checksum = 0;
            for (int i = 1; i < str.Length; i++)
            {
                checksum ^= Convert.ToByte(str[i]);
            };
            return checksum;
        }

        private static string ChecksumHex(string str)
        {
            int checksum = 0;
            for (int i = 1; i < str.Length; i++)
                checksum ^= Convert.ToByte(str[i]);
            return checksum.ToString("X2");
        }

        public static string ChecksumAdd2Line(string line)
        {
            return line + "*" + ChecksumHex(line);
        }

        public static string SimplePassword(string strToInvert)
        {
            StringBuilder builder = null;
            if (strToInvert != null)
            {
                builder = new StringBuilder();
                int iLength = strToInvert.Length;
                for (int iIndex = iLength - 1; iIndex >= 0; iIndex--)
                {
                    char c = strToInvert[iIndex];
                    if (c >= '0' && c <= '9')
                    {
                        builder.Append((char)(9 - (c - '0') + '0'));
                    }
                    else if (c >= 'a' && c <= 'z')
                    {
                        builder.Append((char)(('z' - 'a') - (c - 'a') + 'A'));
                    }
                    else if (c >= 'A' && c <= 'Z')
                    {
                        builder.Append((char)(('Z' - 'A') - (c - 'A') + 'a'));
                    }
                }
            }
            return builder != null ? builder.ToString() : null;
        }
    }
}
