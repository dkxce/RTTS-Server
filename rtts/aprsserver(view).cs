using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Text.RegularExpressions;
using System.IO;
using System.Web;
using System.Xml;
using System.Xml.Serialization;

namespace rtts
{
    public class APRSIOServer
    {
        public static string welcome = String.Format(TriggerServer.RTTHeader, "APRS");        
        
        
        private Hashtable clientList = new Hashtable();
        private Thread listenThread = null;
        private TcpListener mainListener = null;
        private bool isRunning = false;
        private int Online = 0;
        public int ActiveConnections { get { return Online; } }
        private ulong clientCounter = 0;
        private DateTime started;

        
        private IPAddress ListenIP = IPAddress.Any;
        private int ListenPort = 0;
        private ushort MaxClientAlive = 60;
        
        public APRSIOServer(int Port)
        {
            ListenPort = Port;            
        }

        public bool IsActive { get { return isRunning; } }
        public IPAddress ServerIP { get { return ListenIP; } }
        public int ServerPort { get { return ListenPort; } set { ListenPort = value; } }

        public void Dispose() { Stop(); }
        ~APRSIOServer() { Dispose(); }

        public void Start()
        {
            if (isRunning) return;
            started = DateTime.UtcNow;
            isRunning = true;
            listenThread = new Thread(MainThread);
            listenThread.Start();
        }

        private void MainThread()
        {
            mainListener = new TcpListener(this.ListenIP, this.ListenPort);
            mainListener.Start();
            (new Thread(PingNearestThread)).Start(); // ping clients thread
            while (isRunning)
            {
                try
                {
                    GetClient(mainListener.AcceptTcpClient());
                }
                catch { };
                Thread.Sleep(10);
            };
        }

        private void PingNearestThread()
        {
            ushort pingInterval = 0;
            while (isRunning)
            {
                if (pingInterval++ == 300) // 30 sec
                {
                    pingInterval = 0;
                    try { PingAlive(); }
                    catch { };
                };
                Thread.Sleep(100);
            };
        }

        public void Stop()
        {
            if (!isRunning) return;

            Console.Write("Stopping... ");
            isRunning = false;

            if (mainListener != null) mainListener.Stop();
            mainListener = null;

            listenThread.Join();
            listenThread = null;
        }

        private void PingAlive()
        {
            byte[] pingdata = Encoding.ASCII.GetBytes(welcome + "\r\n");
            Broadcast(pingdata);
        }

        private void GetClient(TcpClient Client)
        {
            Online++;
            ClientData cd = new ClientData(new Thread(ClientThread), Client, ++clientCounter);
            lock (clientList) clientList.Add(cd.id, cd);
            cd.thread.Start(cd);
        }

        private void ClientThread(object param)
        {
            ClientData cd = (ClientData)param;

            string rxText = "";
            byte[] rxBuffer = new byte[4096];
            int rxCount = 0;
            int rxAvailable = 0;

            try
            {
                byte[] pingdata = Encoding.ASCII.GetBytes(welcome + "\r\n");
                cd.client.GetStream().Write(pingdata, 0, pingdata.Length);
            }
            catch { };
            
            while (IsActive && cd.thread.IsAlive && IsConnected(cd.client))
            {
                if (((cd.state == 0)) && (DateTime.UtcNow.Subtract(cd.connected).TotalSeconds >= 15)) break;
                if (((cd.state != 6) && (cd.state != 4)) && (DateTime.UtcNow.Subtract(cd.connected).TotalMinutes >= MaxClientAlive)) break;

                try { rxAvailable = cd.client.Client.Available; }
                catch { break; };

                // AIS Client or APRS Read Only
                if ((cd.state == 6))
                {
                    Thread.Sleep(1000);
                    continue;
                };

                while (rxAvailable > 0)
                {
                    try { rxAvailable -= (rxCount = cd.stream.Read(rxBuffer, 0, rxBuffer.Length > rxAvailable ? rxAvailable : rxBuffer.Length)); }
                    catch { break; };
                    if (rxCount > 0) rxText += Encoding.ASCII.GetString(rxBuffer, 0, rxCount);
                };

                // READ INCOMING DATA //
                try
                {
                    // Identificate Client //
                    if ((cd.state == 0) && (rxText.Length >= 4) && (rxText.IndexOf("\n") > 0))
                    {
                        if (rxText.IndexOf("user") == 0) // APRS
                        {
                            if (OnAPRSClient(cd, rxText.Replace("\r", "").Replace("\n", "")))
                                rxText = "";
                            else
                                break;
                        }                        
                        else
                            break;
                    };

                    // Receive incoming data from identificated clients only
                    if ((cd.state > 0) && (rxText.Length > 0) && (rxText.IndexOf("\n") > 0))
                    {
                        // Verified APRS Client //
                        if (cd.state == 4)
                        {
                            string[] lines = rxText.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                            rxText = "";
                            foreach (string line in lines)
                                OnAPRSData(cd, line);
                        };
                      
                        rxText = "";
                    };
                }
                catch
                { };

                Thread.Sleep(100);
            };

            lock (clientList) clientList.Remove(cd.id);
            cd.client.Close();
            Online--;
            cd.thread.Abort();
        }

        private bool OnAPRSClient(ClientData cd, string loginstring)
        {
            //Console.WriteLine("> " + loginstring);

            string res = "# logresp user unverified (listen mode), server " + TriggerServer.ServerName;

            Match rm = Regex.Match(loginstring, @"^user\s([\w\-]{3,})\spass\s([\d\-]+)\svers\s([\w\d\-.]+)\s([\w\d\-.\+]+)");
            if (rm.Success)
            {
                string callsign = rm.Groups[1].Value.ToUpper();

                string password = rm.Groups[2].Value;
                string doptext = loginstring.Substring(rm.Groups[0].Value.Length).Trim();
                if (doptext.IndexOf("filter") >= 0)
                    cd.SetFilter(doptext.Substring(doptext.IndexOf("filter") + 7));

                int psw = -1;
                int.TryParse(password, out psw);
                // check for valid HAM user or for valid OruxPalsServer user
                // these users can upload data to server
                if ((psw == rttutils.CSChecksum(callsign)) )
                {                   
                    cd.state = 4; //APRS
                    cd.user = callsign; // .user - valid username for callsign                   

                    // remove ssid, `-` not valid symbol in name
                    if (cd.user.Contains("-")) cd.user = cd.user.Substring(0, cd.user.IndexOf("-"));

                    res = "# logresp " + callsign + " verified, server " + TriggerServer.ServerName;
                    byte[] ret = Encoding.ASCII.GetBytes(res + "\r\n");
                    try { cd.stream.Write(ret, 0, ret.Length); }
                    catch { };

                    return true;
                };
            };

            // Invalid user
            // these users cannot upload data to server (receive data only allowed)
            {
                cd.state = 6; // APRS Read-Only
                byte[] ret = Encoding.ASCII.GetBytes(res + "\r\n");
                try { cd.stream.Write(ret, 0, ret.Length); }
                catch { };

                return true;
            };
        }

        // on verified users // they can upload data to server
        private void OnAPRSData(ClientData cd, string line)
        {
            // Console.WriteLine("--> " + line);            
            return;

            // COMMENT STRING
            if (line.IndexOf("#") == 0)
            {
                string filter = "";
                if (line.IndexOf("filter") > 0) filter = line.Substring(line.IndexOf("filter"));
                // filter ... active
                if (filter != "")
                {
                    string fres = cd.SetFilter(filter.Substring(7));
                    string resp = "# filter '" + fres + "' is active\r\n";
                    byte[] bts = Encoding.ASCII.GetBytes(resp);
                    try { cd.stream.Write(bts, 0, bts.Length); }
                    catch { }
                };
                return;
            };
            if (line.IndexOf(">") < 0) return;

            // PARSE NORMAL PACKET
            Buddie b = APRSData.ParseAPRSPacket(line);
            if ((b != null) && (b.name != null) && (b.name != String.Empty))
            {               
                //Broadcast(b.APRSData, b.name);
            };
        }                                        

        public void Broadcast(byte[] data, PT0102 json)
        {
            List<ClientData> cdlist = new List<ClientData>();
            lock (clientList)
                foreach (object obj in clientList.Values)
                {
                    if (obj == null) continue;
                    ClientData cd = (ClientData)obj;
                    {
                        bool add = true;
                        if (cd.filter != null)
                        {
                            int pi = cd.filter.ToString().IndexOf("+");
                            int mi = cd.filter.ToString().IndexOf("-");
                            int ri = cd.filter.ToString().IndexOf("r");

                            if (ri >= 0) add = false;
                            else
                            {
                                if ((mi >= 0) && (pi < 0)) add = true;
                                if ((pi >= 0) && (mi < 0)) add = false;
                                if ((mi >= 0) && (pi >= 0) && (mi < pi)) add = true;
                                if ((mi >= 0) && (pi >= 0) && (mi > pi)) add = false;                                
                                
                            };                            

                            if (cd.filter.inRadiusKM > 0)
                            {
                                float l = GetLengthAB(cd.filter.inLat, cd.filter.inLon, json.Lat, json.Lon);
                                if (l > (cd.filter.inRadiusKM * 1000)) add = false;
                            };
                            if ((cd.filter.allowEndsWith != null) && (cd.filter.allowEndsWith.Length > 0))
                                foreach (string s in cd.filter.allowEndsWith)
                                    if (json.ID.EndsWith(s)) add = true;
                            if (json.Event != null)
                                if ((cd.filter.allowEvents != null) && (cd.filter.allowEvents.Length > 0))
                                    foreach (string s in cd.filter.allowEvents)
                                        if (json.Event.ToUpper() == s.ToUpper()) add = true;
                            if ((cd.filter.allowFullName != null) && (cd.filter.allowFullName.Length > 0))
                                foreach (string s in cd.filter.allowFullName)
                                    if (json.ID.ToUpper() == s.ToUpper()) add = true;
                            if ((cd.filter.allowStartsWith != null) && (cd.filter.allowStartsWith.Length > 0))
                                foreach (string s in cd.filter.allowStartsWith)
                                    if (json.ID.StartsWith(s)) add = true;
                            //
                            if ((cd.filter.denyEndsWith != null) && (cd.filter.denyEndsWith.Length > 0))
                                foreach (string s in cd.filter.denyEndsWith)
                                    if (json.ID.EndsWith(s)) add = false;
                            if (json.Event != null)
                                if ((cd.filter.denyEvents != null) && (cd.filter.denyEvents.Length > 0))
                                    foreach (string s in cd.filter.denyEvents)
                                        if (json.Event.ToUpper() == s.ToUpper()) add = false;
                            if ((cd.filter.denyFullName != null) && (cd.filter.denyFullName.Length > 0))
                                foreach (string s in cd.filter.denyFullName)
                                    if (json.ID.ToUpper() == s.ToUpper()) add = false;
                            if ((cd.filter.denyStartsWith != null) && (cd.filter.denyStartsWith.Length > 0))
                                foreach (string s in cd.filter.denyStartsWith)
                                    if (json.ID.StartsWith(s)) add = false;
                        };
                        if(add)
                            cdlist.Add(cd);
                    };
                };

            foreach (ClientData cd in cdlist)
                try { cd.client.GetStream().Write(data, 0, data.Length); }
                catch { };
        }

        public void Broadcast(byte[] data)
        {
            List<ClientData> cdlist = new List<ClientData>();
            lock (clientList)
                foreach (object obj in clientList.Values)
                {
                    if (obj == null) continue;
                    ClientData cd = (ClientData)obj;
                        cdlist.Add(cd);
                };

            foreach (ClientData cd in cdlist)
                try { cd.client.GetStream().Write(data, 0, data.Length); }
                catch { };
        }

        private static float GetLengthAB(double alat, double alon, double blat, double blon)
        {
            double D2R = Math.PI / 180;
            double dDistance = Double.MinValue;
            double dLat1InRad = alat * D2R;
            double dLong1InRad = alon * D2R;
            double dLat2InRad = blat * D2R;
            double dLong2InRad = blon * D2R;

            double dLongitude = dLong2InRad - dLong1InRad;
            double dLatitude = dLat2InRad - dLat1InRad;

            double a = Math.Pow(Math.Sin(dLatitude / 2.0), 2.0) +
                       Math.Cos(dLat1InRad) * Math.Cos(dLat2InRad) *
                       Math.Pow(Math.Sin(dLongitude / 2.0), 2.0);

            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));

            const double kEarthRadiusKms = 6378137.0000;
            dDistance = kEarthRadiusKms * c;

            return (float)Math.Round(dDistance);
        }


        public string GetPacketText_APRS(DateTime dt, string callfrom, string callto, double lat, double lon, double speed, double heading, double altitude, string icon, string comment)
        {
            string mn_icon = icon.Replace("/", "").Replace(@"\", "").Trim();
            string ll_deli = icon.IndexOf(@"\") == 0 ? @"\" : "/";
            return
                callfrom.ToUpper() + ">" + callto + ":@" + // Position without timestamp + APRS message
                dt.ToUniversalTime().ToString("HHmmss") + "z" +
                Math.Truncate(lat).ToString("00") + ((lat - Math.Truncate(lat)) * 60).ToString("00.00").Replace(",", ".") + "N" + // Lat
                ll_deli +
                Math.Truncate(lon).ToString("000") + ((lon - Math.Truncate(lon)) * 60).ToString("00.00").Replace(",", ".") + "E" + // Lon
                mn_icon +
                Math.Truncate(heading).ToString("000") + "/" + Math.Truncate(speed / 1.852).ToString("000") + " " +  // knots
                //"/A=" + Math.Truncate(altitude * 3.28084).ToString("00000") + " " + // feets
                " " + comment + "\r\n";
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
       
        private static string GetPacketText_OpenGPSNET_HTTPReq(string imei, double lat, double lon, double speed, double heading, double altitude)
        {
            Random rnd = new Random();

            if (lat == 0) lat = 55.54404 + (0.5 - rnd.NextDouble()) / 10;
            if (lon == 0) lon = 37.55860 + (0.5 - rnd.NextDouble()) / 10;
            if (speed < 0) speed = 10; // kmph
            if (heading < 0) heading = rnd.Next(0, 359);
            if (altitude < 0) altitude = 251;

            //http://www.opengps.net/configure.php
            return
                "&imei=" + imei + "&data=" +
                DateTime.UtcNow.ToString("HHmmss") + ".000," +
                Math.Truncate(lat).ToString("00") + ((lat - Math.Truncate(lat)) * 60).ToString("00.0000").Replace(",", ".") + "N," + // Lat
                Math.Truncate(lon).ToString("000") + ((lon - Math.Truncate(lon)) * 60).ToString("00.0000").Replace(",", ".") + "E," + // Lon
                "2.6," + // HDOP
                altitude.ToString("0.0").Replace(",", ".") + "," + // altitude
                "3," + // 0 - noFix, 2-2D,3-3D
                heading.ToString("000.00").Replace(",", ".") + "," + //heading
                speed.ToString("0.0").Replace(",", ".") + "," + // kmph
                (speed / 1.852).ToString("0.0").Replace(",", ".") + "," + // knots
                DateTime.UtcNow.ToString("ddMMyy") + "," + // date
                "12" // sat count
                ;
            ;
        }

        private static string GetPacketText_TK102B_Normal(DateTime dt, string imei, double lat, double lon, double speed, double heading, double altitude)
        {
            Random rnd = new Random();

            if (lat == 0) lat = 55.54404 + (0.5 - rnd.NextDouble()) / 10;
            if (lon == 0) lon = 37.55860 + (0.5 - rnd.NextDouble()) / 10;
            if (speed < 0) speed = 10; // kmph
            if (heading < 0) heading = rnd.Next(0, 359);
            if (altitude < 0) altitude = 251;
            return
                dt.ToString("yyMMddHHmmss") + "," + //Serial no.(year, month, date, hour, minute, second )
                "0," + // Authorized phone no.
                "GPRMC," + // begin GPRMC sentence
                dt.ToString("HHmmss") + ".000,A," + // Time
                Math.Truncate(lat).ToString("00") + ((lat - Math.Truncate(lat)) * 60).ToString("00.0000").Replace(",", ".") + ",N," + // Lat
                Math.Truncate(lon).ToString("000") + ((lon - Math.Truncate(lon)) * 60).ToString("00.0000").Replace(",", ".") + ",E," + // Lon
                (speed / 1.852).ToString("0.00").Replace(",", ".") + "," +//Speed in knots
                heading.ToString("0").Replace(",", ".") + "," +//heading
                dt.ToString("ddMMyy") + ",,,A*62," +// Date
                "F," +//F=GPS signal is full, if it indicate " L ", means GPS signal is low
                "imei:" + imei + "," + //imei
                // CRC
                "05," +// GPS fix (03..10)
                altitude.ToString("0.0").Replace(",", ".") //altitude
                //",F:3.79V,0"//0-tracker not charged,1-charged
                // ",122,13990,310,01,0AB0,345A" //
            ;

            // lat: 5722.5915 -> 57 + (22.5915 / 60) = 57.376525
        }

        private static void SendTCP(string IP, int Port, string data)
        {
            try
            {
                TcpClient tc = new TcpClient();
                tc.Connect(IP, Port);
                byte[] buf = System.Text.Encoding.GetEncoding(1251).GetBytes(data);
                tc.GetStream().Write(buf, 0, buf.Length);
                tc.Close();
            }
            catch (Exception ex) { throw ex; };
        }        

        private static bool IsConnected(TcpClient Client)
        {
            if (!Client.Connected) return false;
            if (Client.Client.Poll(0, SelectMode.SelectRead))
            {
                byte[] buff = new byte[1];
                try
                {
                    if (Client.Client.Receive(buff, SocketFlags.Peek) == 0)
                        return false;
                }
                catch
                {
                    return false;
                };
            };
            return true;
        }

        private static double DateTimeToUnixTimestamp(DateTime dateTime)
        {
            return (dateTime - new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc)).TotalSeconds;
        }

        private static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        public static string HeadingToText(int hdg)
        {
            int d = (int)Math.Round(hdg / 22.5);
            switch (d)
            {
                case 0: return "N";
                case 1: return "NNE";
                case 2: return "NE";
                case 3: return "NEE";
                case 4: return "E";
                case 5: return "SEE";
                case 6: return "SE";
                case 7: return "SSE";
                case 8: return "S";
                case 9: return "SSW";
                case 10: return "SW";
                case 11: return "SWW";
                case 12: return "W";
                case 13: return "NWW";
                case 14: return "NW";
                case 15: return "NNW";
                case 16: return "N";
                default: return "";
            };
        }

        private class ClientData
        {
            public byte state; // 0 - undefined; 1 - listen (AIS); 2 - gpsgate; 3 - mapmytracks; 4 - APRS; 5 - FRS (GPSGate by TCP); 6 - listen (APRS)
            public Thread thread;
            public TcpClient client;
            public DateTime connected;
            public ulong id;
            public Stream stream;

            public ClientData(Thread thread, TcpClient client, ulong clientID)
            {
                this.id = clientID;
                this.connected = DateTime.UtcNow;
                this.state = 0;
                this.thread = thread;
                this.client = client;
                this.stream = client.GetStream();
            }

            public string user = "unknown";
            public double[] lastFixYX = new double[] { 0, 0, 0 };

            public ClientAPRSFilter filter = null;

            public string SetFilter(string filter)
            {
                this.filter = new ClientAPRSFilter(filter);
                return this.filter.ToString();
            }
        }

        public class ClientAPRSFilter
        {
            private string filter = "";
            public int inRadiusKM = -1;
            public double inLat = 0.0;
            public double inLon = 0.0;
            public int maxStaticObjectsCount = -1;
            public string[] allowStartsWith = new string[0];
            public string[] allowEndsWith = new string[0];
            public string[] allowFullName = new string[0];
            public string[] allowEvents = new string[0];
            public string[] denyStartsWith = new string[0];
            public string[] denyEndsWith = new string[0];
            public string[] denyFullName = new string[0];
            public string[] denyEvents = new string[0];

            public ClientAPRSFilter(string filter)
            {
                this.filter = filter;
                Init();
            }

            private void Init()
            {
                string ffparsed = "";
                Match m = Regex.Match(filter, @"r/([\d\/]+)");
                if (m.Success)
                {
                    string[] rc = m.Groups[1].Value.Split(new char[] { '/' }, 2);
                    if (rc.Length > 2)
                    {
                        inLat = double.Parse(rc[0], System.Globalization.CultureInfo.InvariantCulture);
                        inLon = double.Parse(rc[1], System.Globalization.CultureInfo.InvariantCulture);
                        inRadiusKM = int.Parse(rc[2]);
                        if (rc.Length > 3) maxStaticObjectsCount = int.Parse(rc[1]);
                        ffparsed += m.ToString() + " ";
                    };
                };
                m = Regex.Match(filter, @"\+sw/([A-Z\d/\-]+)");
                if (m.Success)
                {
                    allowStartsWith = m.Groups[1].Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    ffparsed += m.ToString() + " ";
                };
                m = Regex.Match(filter, @"\+ew/([A-Z\d/\-]+)");
                if (m.Success)
                {
                    allowEndsWith = m.Groups[1].Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    ffparsed += m.ToString() + " ";
                };                
                m = Regex.Match(filter, @"\+fn/([A-Z\d/\-]+)");
                if (m.Success)
                {
                    allowFullName = m.Groups[1].Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    ffparsed += m.ToString() + " ";
                };
                m = Regex.Match(filter, @"\-sw/([A-Z\d/\-]+)");
                if (m.Success)
                {
                    denyStartsWith = m.Groups[1].Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    ffparsed += m.ToString() + " ";
                };
                m = Regex.Match(filter, @"\-ew/([A-Z\d/\-]+)");
                if (m.Success)
                {
                    denyEndsWith = m.Groups[1].Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    ffparsed += m.ToString() + " ";
                };
                m = Regex.Match(filter, @"\-fn/([A-Z\d/\-]+)");
                if (m.Success)
                {
                    denyFullName = m.Groups[1].Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    ffparsed += m.ToString() + " ";
                };

                m = Regex.Match(filter, @"\+ev/([A-Z\d/\-]+)");
                if (m.Success)
                {
                    allowEvents = m.Groups[1].Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    ffparsed += m.ToString() + " ";
                };
                m = Regex.Match(filter, @"\-ev/([A-Z\d/\-]+)");
                if (m.Success)
                {
                    denyEvents = m.Groups[1].Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    ffparsed += m.ToString() + " ";
                };

                filter = ffparsed.Trim();
            }

            public bool PassName(string name)
            {
                if ((name == null) || (name == "")) return true;
                if (filter == "") return true;
                name = name.ToUpper();
                bool pass = true;
                if ((allowStartsWith != null) && (allowStartsWith.Length > 0))
                {
                    pass = false;
                    foreach (string sw in allowStartsWith)
                        if (name.StartsWith(sw)) return true;
                };
                if ((allowEndsWith != null) && (allowEndsWith.Length > 0))
                {
                    pass = false;
                    foreach (string ew in allowEndsWith)
                        if (name.EndsWith(ew)) return true;
                };
                if ((allowFullName != null) && (allowFullName.Length > 0))
                {
                    pass = false;
                    foreach (string fn in allowFullName)
                        if (name == fn)
                            return true;
                };
                //
                if ((denyStartsWith != null) && (denyStartsWith.Length > 0))
                    foreach (string sw in denyStartsWith)
                        if (name.StartsWith(sw)) return false;
                if ((denyEndsWith != null) && (denyEndsWith.Length > 0))
                    foreach (string ew in denyEndsWith)
                        if (name.EndsWith(ew)) return false;
                if ((denyFullName != null) && (denyFullName.Length > 0))
                    foreach (string fn in denyFullName)
                        if (name == fn)
                            return false;
                return pass;
            }

            public override string ToString()
            {
                return filter;
            }
        }
    }


    public class Buddie
    {
        public static Regex BuddieNameRegex = new Regex("^([A-Z0-9]{3,9})$");
        public static Regex BuddieCallSignRegex = new Regex(@"^([A-Z0-9\-]{3,9})$");
        public static string symbolAny = "/*/</=/>/C/F/M/P/U/X/Y/Z/[/a/b/e/f/j/k/p/s/u/v\\O\\j\\k\\u\\v/0/1/2/3/4/5/6/7/8/9/'/O";
        public static int symbolAnyLength = 40;

        internal static ulong _id = 0;
        private ulong _ID = 0;
        internal ulong ID
        {
            get { return _ID; }
            set
            {
                _ID = value;
                if (_ID == 0)
                {
                    IconSymbol = "//";
                    return;
                }
                else if (Buddie.IsNullIcon(IconSymbol))
                    IconSymbol = Buddie.symbolAny.Substring((((int)_ID - 1) % Buddie.symbolAnyLength) * 2, 2);
            }
        }

        public static bool IsNullIcon(string symbol)
        {
            return (symbol == null) || (symbol == String.Empty) || (symbol == "//");
        }

        public byte source; // 0 - unknown; 1 - GPSGate Format; 2 - MapMyTracks Format; 3 - APRS; 4 - FRS; 5 - everytime; 6 - static
        public string name;
        public double lat;
        public double lon;
        public short speed;
        public short course;

        public DateTime last;
        public bool green;

        private string aAIS = "";
        private byte[] aAISNMEA = null;
        private string bAIS = "";
        private byte[] bAISNMEA = null;

        public string AIS
        {
            get
            {
                return green ? bAIS : aAIS;
            }
        }
        public byte[] AISNMEA
        {
            get
            {
                return green ? bAISNMEA : aAISNMEA;
            }
        }

        public string APRS = "";
        public byte[] APRSData = null;

        public string FRPOS = "";
        public byte[] FRPOSData = null;

        public string IconSymbol = "//";

        public string parsedComment = "";
        public string Comment
        {
            get
            {
                if ((parsedComment != null) && (parsedComment != String.Empty)) return parsedComment;
                return "";
            }
            set
            {
                parsedComment = value;
            }
        }
        public string Status = "";

        public string lastPacket = "";

        public bool PositionIsValid
        {
            get { return (lat != 0) && (lon != 0); }
        }

        public Buddie(byte source, string name, double lat, double lon, short speed, short course)
        {
            this.source = source;
            this.name = name;
            this.lat = lat;
            this.lon = lon;
            this.speed = speed;
            this.course = course;
            this.last = DateTime.UtcNow;
            this.green = false;
        }
        

        internal void SetAPRS()
        {
            if (this.source == 3)
            {
                if (((this.parsedComment == null) || (this.parsedComment == String.Empty)) && (this.Comment != null))
                {
                    this.APRS = this.APRS.Insert(this.APRS.Length - 2, " " + this.Comment);
                    this.APRSData = Encoding.ASCII.GetBytes(this.APRS);
                };
                return;
            };

            APRS =
                name + ">APRS,TCPIP*:=" + // Position without timestamp + APRS message
                Math.Truncate(lat).ToString("00") + ((lat - Math.Truncate(lat)) * 60).ToString("00.00").Replace(",", ".") +
                (lat > 0 ? "N" : "S") +
                IconSymbol[0] +
                Math.Truncate(lon).ToString("000") + ((lon - Math.Truncate(lon)) * 60).ToString("00.00").Replace(",", ".") +
                (lon > 0 ? "E" : "W") +
                IconSymbol[1] +
                course.ToString("000") + "/" + Math.Truncate(speed / 1.852).ToString("000") +
                ((this.Comment != null) && (this.Comment != String.Empty) ? " " + this.Comment : "") +
                "\r\n";
            APRSData = Encoding.ASCII.GetBytes(APRS);
        }
        
        public override string ToString()
        {
            return String.Format("{0} at {1}, {2} {3} {4}, {5}", new object[] { name, source, lat, lon, speed, course });
        }

        public static int Hash(string name)
        {
            string upname = name == null ? "" : name;
            int stophere = upname.IndexOf("-");
            if (stophere > 0) upname = upname.Substring(0, stophere);
            while (upname.Length < 9) upname += " ";

            int hash = 0x2017;
            int i = 0;
            while (i < 9)
            {
                hash ^= (int)(upname.Substring(i, 1))[0] << 16;
                hash ^= (int)(upname.Substring(i + 1, 1))[0] << 8;
                hash ^= (int)(upname.Substring(i + 2, 1))[0];
                i += 3;
            };
            return hash & 0x7FFFFF;
        }

        public static uint MMSI(string name)
        {
            string upname = name == null ? "" : name;
            while (upname.Length < 9) upname += " ";
            int hash = 2017;
            int i = 0;
            while (i < 9)
            {
                hash ^= (int)(upname.Substring(i, 1))[0] << 16;
                hash ^= (int)(upname.Substring(i + 1, 1))[0] << 8;
                hash ^= (int)(upname.Substring(i + 2, 1))[0];
                i += 3;
            };
            return (uint)(hash & 0xFFFFFF);
        }

        public static void CopyData(Buddie copyFrom, Buddie copyTo)
        {
            if ((copyTo.source != 3) && (!Buddie.IsNullIcon(copyFrom.IconSymbol)))
                copyTo.IconSymbol = copyFrom.IconSymbol;

            if (Buddie.IsNullIcon(copyTo.IconSymbol))
                copyTo.IconSymbol = copyFrom.IconSymbol;

            if ((copyTo.parsedComment == null) || (copyTo.parsedComment == String.Empty))
            {
                copyTo.parsedComment = copyFrom.parsedComment;
                if ((copyTo.source == 3) && (copyTo.parsedComment != null) && (copyTo.parsedComment != String.Empty))
                {
                    copyTo.APRS = copyTo.APRS.Insert(copyTo.APRS.Length - 2, " " + copyTo.Comment);
                    copyTo.APRSData = Encoding.ASCII.GetBytes(copyTo.APRS);
                };
            };

            copyTo.ID = copyFrom.ID;
            copyTo.Status = copyFrom.Status;
        }
    }


    // APRS
    public class APRSData
    {
        public static int CallsignChecksum(string callsign)
        {
            if (callsign == null) return 99999;
            if (callsign.Length == 0) return 99999;
            if (callsign.Length > 10) return 99999;

            int stophere = callsign.IndexOf("-");
            if (stophere > 0) callsign = callsign.Substring(0, stophere);
            string realcall = callsign.ToUpper();
            while (realcall.Length < 10) realcall += " ";

            // initialize hash 
            int hash = 0x73e2;
            int i = 0;
            int len = realcall.Length;

            // hash callsign two bytes at a time 
            while (i < len)
            {
                hash ^= (int)(realcall.Substring(i, 1))[0] << 8;
                hash ^= (int)(realcall.Substring(i + 1, 1))[0];
                i += 2;
            }
            // mask off the high bit so number is always positive 
            return hash & 0x7fff;
        }

        public static Buddie ParseAPRSPacket(string line)
        {
            if (line.IndexOf("#") == 0) return null; // comment packet

            // Valid APRS?
            int fChr = line.IndexOf(">");
            if (fChr <= 1) return null;  // invalid packet
            int sChr = line.IndexOf(":");
            if (sChr < fChr) return null;  // invalid packet

            string callsign = line.Substring(0, fChr);
            string pckroute = line.Substring(fChr + 1, sChr - fChr - 1);
            string packet = line.Substring(sChr);

            if (packet.Length < 2) return null; // invalid packet

            Buddie b = new Buddie(3, callsign, 0, 0, 0, 0);
            b.lastPacket = line;
            b.APRS = line + "\r\n";
            b.APRSData = Encoding.ASCII.GetBytes(b.APRS);


            switch (packet[1])
            {
                /* Object */
                case ';':
                    int sk0 = Math.Max(packet.IndexOf("*", 2, 10), packet.IndexOf("_", 2, 10));
                    if (sk0 < 0) return null;
                    string obj_name = packet.Substring(2, sk0 - 2).Trim();
                    if (packet.IndexOf("*") > 0)
                        return ParseAPRSPacket(obj_name + ">" + pckroute + ":@" + packet.Substring(sk0 + 1)); // set object name as callsign and packet as position
                    break;

                /* Item Report Format */
                case ')':
                    int sk1 = Math.Max(packet.IndexOf("!", 2, 10), packet.IndexOf("_", 2, 10));
                    if (sk1 < 0) return null;
                    string rep_name = packet.Substring(2, sk1 - 2).Trim();
                    if (packet.IndexOf("!") > 0)
                        return ParseAPRSPacket(rep_name + ">" + pckroute + ":@" + packet.Substring(sk1 + 1)); // set object name as callsign and packet as position
                    break;

                /* Positions Reports */
                case '!': // Positions with no time, no APRS                
                case '=': // Position with no time, but APRS
                case '/': // Position with time, no APRS
                case '@': // Position with time and APRS
                    {
                        string pos = packet.Substring(2);
                        if (pos[0] == '!') break; // Raw Weather Data

                        DateTime received = DateTime.UtcNow;
                        if (pos[0] != '/') // not compressed data firsts
                        {
                            switch (packet[8])
                            {
                                case 'z': // zulu ddHHmm time
                                    received = new DateTime(DateTime.Now.Year, DateTime.Now.Month, int.Parse(packet.Substring(2, 2)),
                                    int.Parse(packet.Substring(4, 2)), int.Parse(packet.Substring(6, 2)), 0, DateTimeKind.Utc);
                                    pos = packet.Substring(9);
                                    break;
                                case '/': // local ddHHmm time
                                    received = new DateTime(DateTime.Now.Year, DateTime.Now.Month, int.Parse(packet.Substring(2, 2)),
                                    int.Parse(packet.Substring(4, 2)), int.Parse(packet.Substring(6, 2)), 0, DateTimeKind.Local);
                                    pos = packet.Substring(9);
                                    break;
                                case 'h': // HHmmss time
                                    received = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day,
                                    int.Parse(packet.Substring(2, 2)), int.Parse(packet.Substring(4, 2)), int.Parse(packet.Substring(6, 2)), DateTimeKind.Local);
                                    pos = packet.Substring(9);
                                    break;
                            };
                        };

                        string aftertext = "";
                        char prim_or_sec = '/';
                        char symbol = '>';

                        if (pos[0] == '/') // compressed data YYYYXXXXcsT
                        {
                            string yyyy = pos.Substring(1, 4);
                            b.lat = 90 - (((byte)yyyy[0] - 33) * Math.Pow(91, 3) + ((byte)yyyy[1] - 33) * Math.Pow(91, 2) + ((byte)yyyy[2] - 33) * 91 + ((byte)yyyy[3] - 33)) / 380926;
                            string xxxx = pos.Substring(5, 4);
                            b.lon = -180 + (((byte)xxxx[0] - 33) * Math.Pow(91, 3) + ((byte)xxxx[1] - 33) * Math.Pow(91, 2) + ((byte)xxxx[2] - 33) * 91 + ((byte)xxxx[3] - 33)) / 190463;
                            symbol = pos[9];
                            string cmpv = pos.Substring(10, 2);
                            int addIfWeather = 0;
                            if (cmpv[0] == '_') // with weather report
                            {
                                symbol = '_';
                                cmpv = pos.Substring(11, 2);
                                addIfWeather = 1;
                            };
                            if (cmpv[0] != ' ') // ' ' - no data
                            {
                                int cmpt = ((byte)pos[12 + addIfWeather] - 33);
                                if (((cmpt & 0x18) == 0x18) && (cmpv[0] != '{') && (cmpv[0] != '|')) // RMC sentence with course & speed
                                {
                                    b.course = (short)(((byte)cmpv[0] - 33) * 4);
                                    b.speed = (short)(((int)Math.Pow(1.08, ((byte)cmpv[1] - 33)) - 1) * 1.852);
                                };
                            };
                            aftertext = pos.Substring(13 + addIfWeather);
                            b.IconSymbol = "/" + symbol.ToString();
                        }
                        else // not compressed
                        {
                            if (pos.Substring(0, 18).Contains(" ")) return null; // nearest degree

                            b.lat = double.Parse(pos.Substring(2, 5), System.Globalization.CultureInfo.InvariantCulture);
                            b.lat = double.Parse(pos.Substring(0, 2), System.Globalization.CultureInfo.InvariantCulture) + b.lat / 60;
                            if (pos[7] == 'S') b.lat *= -1;

                            b.lon = double.Parse(pos.Substring(12, 5), System.Globalization.CultureInfo.InvariantCulture);
                            b.lon = double.Parse(pos.Substring(9, 3), System.Globalization.CultureInfo.InvariantCulture) + b.lon / 60;
                            if (pos[17] == 'W') b.lon *= -1;

                            prim_or_sec = pos[8];
                            symbol = pos[18];
                            aftertext = pos.Substring(19);

                            b.IconSymbol = prim_or_sec.ToString() + symbol.ToString();
                        };

                        // course/speed or course/speed/bearing/NRQ
                        if ((symbol != '_') && (aftertext.Length >= 7) && (aftertext[3] == '/')) // course/speed 000/000
                        {
                            short.TryParse(aftertext.Substring(0, 3), out b.course);
                            short.TryParse(aftertext.Substring(4, 3), out b.speed);
                            aftertext = aftertext.Remove(0, 7);
                        };

                        b.Comment = aftertext.Trim();

                    };
                    break;
                /* All Other */
                default:
                    //
                    break;
            };
            return b;
        }
    }
}
