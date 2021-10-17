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
    public class TCPViewerServer
    {
        public static string welcome = String.Format(TriggerServer.RTTHeader, "TCP");

        private int listerPort = 0;
        private SimpleServers.SimpleTNCTCPServer server = null;
        private Hashtable clients = new Hashtable();
        private bool _isActive = false;
        private int Online = 0;
        public int ActiveConnections { get { return Online; }  }
        
        public delegate void onConnectEvent(ClientInfo ci);
        public onConnectEvent onClientConnect;


        public TCPViewerServer(int port) { listerPort = port; }

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
                server.OnDataValid2 += new SimpleServers.SimpleServer.ValidData2(onClientData);
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
            while (_isActive)
            {
                pto++;
                if (pto == 0) // in 15 sec
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
        }

        public void onDisconnect(TcpClient client, ulong clientID)
        {
            Online--;
            Console.ForegroundColor = ConsoleColor.Cyan;
            //Console.WriteLine("& " + clientID.ToString() + " AIS disconnected");

            lock (clients) clients.Remove(clientID);
        }

        public bool onClientData(TcpClient client, ulong clientID, string line)
        {
            if (String.IsNullOrEmpty(line)) return false;
            ClientInfo ci;
            lock (clients) ci = (ClientInfo)clients[clientID];


            Console.WriteLine("A@@>> " + line);
            string[] lines = line.Split(new char[] { '\r', '\n' });
            foreach(string ln in lines)
            {
                byte[] data = System.Text.Encoding.ASCII.GetBytes(ln);
                int startIndex = 0;
                while (startIndex < data.Length)
                {
                    if (data[startIndex] == 0) return true;
                    RTTPacket p = RTTPacket.FromBytes(data, startIndex);
                    if (!p.valid) return true;
                    if (p.ptype == "01")
                    {
                        PT0102 json = PT0102.FromJSON(p.datatext);
                        if (!String.IsNullOrEmpty(json.Filter))
                            ci.rttFilter = new APRSIOServer.ClientAPRSFilter(json.Filter);
                        ci.rttIMEI = json.IMEI;
                        ci.rttID = json.ID;
                        ci.rttEvent = json.Event;
                    };
                    startIndex += p.packet_length;
                };                
            };
            return true;
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

        public void BroadcaseMsg(RTTPacket pkt, PT0102 json)
        {
            ClientInfo[] cls = Clients;
            foreach (ClientInfo ci in cls)
            {
                bool add = true;
                if (String.IsNullOrEmpty(ci.rttEvent))
                {
                    if (ci.rttFilter != null)
                    {
                        int pi = ci.rttFilter.ToString().IndexOf("+");
                        int mi = ci.rttFilter.ToString().IndexOf("-");
                        int ri = ci.rttFilter.ToString().IndexOf("r");

                        if (ri >= 0) add = false;
                        if ((mi >= 0) && (pi < 0)) add = true;
                        if ((pi >= 0) && (mi < 0)) add = false;
                        if ((mi >= 0) && (pi >= 0) && (mi < pi)) add = true;
                        if ((mi >= 0) && (pi >= 0) && (mi > pi)) add = false;
                    };
                }
                else
                {
                    add = false;
                    if ((!String.IsNullOrEmpty(json.Event)) && (ci.rttEvent.ToUpper() == json.Event.ToUpper()))
                        add = true;                    
                };
                if (ci.rttFilter != null)
                {                                        
                    if (ci.rttFilter.inRadiusKM > 0)
                    {
                        float l = GetLengthAB(ci.rttFilter.inLat, ci.rttFilter.inLon, json.Lat, json.Lon);
                        if (l > (ci.rttFilter.inRadiusKM * 1000)) add = false;
                    };
                    if ((ci.rttFilter.allowEndsWith != null) && (ci.rttFilter.allowEndsWith.Length > 0))
                        foreach (string s in ci.rttFilter.allowEndsWith)
                            if (json.ID.EndsWith(s)) add = true;
                    if (json.Event != null)
                        if ((ci.rttFilter.allowEvents != null) && (ci.rttFilter.allowEvents.Length > 0))
                            foreach (string s in ci.rttFilter.allowEvents)
                                if (json.Event.ToUpper() == s.ToUpper()) add = true;
                    if ((ci.rttFilter.allowFullName != null) && (ci.rttFilter.allowFullName.Length > 0))
                        foreach (string s in ci.rttFilter.allowFullName)
                            if (json.ID.ToUpper() == s.ToUpper()) add = true;
                    if ((ci.rttFilter.allowStartsWith != null) && (ci.rttFilter.allowStartsWith.Length > 0))
                        foreach (string s in ci.rttFilter.allowStartsWith)
                            if (json.ID.StartsWith(s)) add = true;
                    //
                    if ((ci.rttFilter.denyEndsWith != null) && (ci.rttFilter.denyEndsWith.Length > 0))
                        foreach (string s in ci.rttFilter.denyEndsWith)
                            if (json.ID.EndsWith(s)) add = false;
                    if (json.Event != null)
                        if ((ci.rttFilter.denyEvents != null) && (ci.rttFilter.denyEvents.Length > 0))
                            foreach (string s in ci.rttFilter.denyEvents)
                                if (json.Event.ToUpper() == s.ToUpper()) add = false;
                    if ((ci.rttFilter.denyFullName != null) && (ci.rttFilter.denyFullName.Length > 0))
                        foreach (string s in ci.rttFilter.denyFullName)
                            if (json.ID.ToUpper() == s.ToUpper()) add = false;
                    if ((ci.rttFilter.denyStartsWith != null) && (ci.rttFilter.denyStartsWith.Length > 0))
                        foreach (string s in ci.rttFilter.denyStartsWith)
                            if (json.ID.StartsWith(s)) add = false;
                };
                if (add)
                    SendMsg(ci, pkt.packet_text);
            };
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

        public void BroadcaseMsg(string msg)
        {
            ClientInfo[] cls = Clients;
            foreach (ClientInfo ci in cls)
            {                
                SendMsg(ci, msg);
            };
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
}
