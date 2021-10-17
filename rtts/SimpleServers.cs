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

namespace SimpleServers
{
    /// <summary>
    ///     Основные методы и свойства сервера
    /// </summary>
    public class SimpleServer
    {        
        public delegate bool ValidData(string proto, string data);
        public delegate bool ValidData2(TcpClient client, ulong clientID, string data);
        public delegate void ValidClient(TcpClient client, ulong clientID);
    }

    

    /// <summary>
    ///     Простейший TCP-сервер
    /// </summary>
    public class SimpleTCPServer
    {
        private Thread mainThread = null;
        private TcpListener mainListener = null;
        private IPAddress ListenIP = IPAddress.Any;
        private int ListenPort = 5000;
        private bool isRunning = false;

        public SimpleTCPServer() { }
        public SimpleTCPServer(int Port) { this.ListenPort = Port; }
        public SimpleTCPServer(IPAddress IP, int Port) { this.ListenIP = IP; this.ListenPort = Port; }

        public bool Running { get { return isRunning; } }
        public IPAddress ServerIP { get { return ListenIP; } }
        public int ServerPort { get { return ListenPort; } set { ListenPort = value; } }

        public void Dispose() { Stop(); }
        ~SimpleTCPServer() { Dispose(); }

        public virtual void Start()
        {
            if (isRunning) throw new Exception("Server Already Running!");

            isRunning = true;
            mainThread = new Thread(MainThread);
            mainThread.Start();
        }

        private void MainThread()
        {
            mainListener = new TcpListener(this.ListenIP, this.ListenPort);
            mainListener.Start();
            while (isRunning)
            {
                try
                {
                    GetClient(mainListener.AcceptTcpClient());
                }
                catch { };
                Thread.Sleep(1);
            };
        }

        public virtual void Stop()
        {
            if (!isRunning) return;

            isRunning = false;

            if (mainListener != null) mainListener.Stop();
            mainListener = null;

            mainThread.Join();
            mainThread = null;
        }

        public virtual void GetClient(TcpClient Client)
        {
            Client.Close();
        }
    }

    /// <summary>
    ///     Простейший TCP-сервер, который принимает текст и закрывает соединение
    /// </summary>
    public class SimpleTextTCPServer : SimpleTCPServer
    {
        public SimpleTextTCPServer() : base() { }
        public SimpleTextTCPServer(int Port) : base(Port) { }
        public SimpleTextTCPServer(IPAddress IP, int Port) : base(IP, Port) { }

        public override void GetClient(TcpClient Client)
        {
            string Request = "";
            byte[] Buffer = new byte[4096];
            int Count;

            while ((Count = Client.GetStream().Read(Buffer, 0, Buffer.Length)) > 0)
            {
                Request += Encoding.ASCII.GetString(Buffer, 0, Count);
                if (Request.IndexOf("\r\n\r\n") >= 0 || Request.Length > 4096) { break; };
            };

            ReceiveDataValid(Client, 0,  Request);
            Client.Close();
        }

        public virtual bool ReceiveDataValid(TcpClient Client, ulong clientID, string Request)
        {
            if (OnDataValid2 != null)
                return OnDataValid2(Client, clientID, Request);
            if (OnDataValid != null)
                return OnDataValid("tcp://" + Client.Client.RemoteEndPoint.ToString() + "/", Request);
            return false;
        }

        public SimpleServer.ValidData OnDataValid = null;
        public SimpleServer.ValidData2 OnDataValid2 = null;
    }    

    /// <summary>
    ///     Простейший TCP-сервер, который принимает текст и держит соединение
    /// </summary>
    public class SimpleTNCTCPServer : SimpleTextTCPServer
    {
        private class CSData
        {
            public Thread thread;
            public TcpClient client;
            public UInt64 cNo;
            private Stream str;

            public Stream stream { get { return str; } }

            public CSData(Thread thread, TcpClient client, UInt64 cNo)
            {
                this.thread = thread;
                this.client = client;
                this.cNo = cNo;
                this.str = client.GetStream();
            }
        }
        private List<CSData> ClientStack = new List<CSData>();
        private UInt64 cCounter = 0;
        private byte MaxThreadStack = 100;
        public SimpleServer.ValidClient OnConnect;
        public SimpleServer.ValidClient OnDisconnect;

        public SimpleTNCTCPServer() : base() { }
        public SimpleTNCTCPServer(int Port) : base(Port) { }
        public SimpleTNCTCPServer(IPAddress IP, int Port) : base(IP, Port) { }

        public override void Stop()
        {
            base.Stop();

            for (int i = 0; i < ClientStack.Count; i++)
            {
                ClientStack[i].client.Close();
                ClientStack[i].thread.Abort();
                ClientStack[i].thread.Join();
            };
            ClientStack.Clear();
        }

        public override void GetClient(TcpClient Client)
        {
            if (ClientStack.Count == MaxThreadStack)
            {
                ClientStack[0].client.Client.Close();
                ClientStack[0].thread.Abort();
                ClientStack[0].thread.Join();
                ClientStack.RemoveAt(0);
            };
            
            CSData cd = new CSData(new Thread(ClientThread), Client, ++cCounter);
            ClientStack.Add(cd);
            cd.thread.Start(cd);

            if (OnConnect != null) OnConnect(Client, cd.cNo);
        }

        private void ClientThread(object param)
        {
            CSData cd = (CSData)param;

            byte[] byteBuffer = new byte[65536]; // 64kBytes
            string textBuffer = "";
            int readed = 0;
            int canRead = 0;            

            while (Running && cd.thread.IsAlive && IsConnected(cd.client))
            {
                try { canRead = cd.client.Client.Available; } catch { break; };

                while (canRead > 0)
                {
                    try { readed = cd.stream.Read(byteBuffer, 0, byteBuffer.Length > canRead ? canRead : byteBuffer.Length); } catch { break; };
                    if (readed == 0) continue;

                    textBuffer += Encoding.GetEncoding(1251).GetString(byteBuffer, 0, readed);
                    int pos = textBuffer.IndexOf("\n");
                    if (pos < 0) continue;

                    string t2snd = textBuffer.Substring(0, pos).Replace("\r", "").Trim();
                    textBuffer = textBuffer.Remove(0, pos + 1);
                    if (!ReceiveDataValid(cd.client, cd.cNo, t2snd)) { };
                    canRead -= readed;
                };
                Thread.Sleep(100);                
            };

            cd.client.Close();
            if (OnDisconnect != null) OnDisconnect(cd.client, cd.cNo);

            for (int i = ClientStack.Count - 1; i >= 0; i--)
                if (ClientStack[i].cNo == cd.cNo)
                    ClientStack.RemoveAt(i);

            cd.thread.Abort();
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
    }

    /// <summary>
    ///     Простейший UDP-сервер
    /// </summary>
    public class SimpleUDPServer
    {
        private Thread mainThread = null;
        Socket udpSocket = null;
        private IPAddress ListenIP = IPAddress.Any;
        private int ListenPort = 5000;
        private bool isRunning = false;

        public SimpleUDPServer() { }
        public SimpleUDPServer(int Port) { this.ListenPort = Port; }
        public SimpleUDPServer(IPAddress IP, int Port) { this.ListenIP = IP; this.ListenPort = Port; }

        public bool Running { get { return isRunning; } }
        public IPAddress ServerIP { get { return ListenIP; } }
        public int ServerPort { get { return ListenPort; } }

        public void Dispose() { Stop(); }
        ~SimpleUDPServer() { Dispose(); }


        public void Start()
        {
            if (isRunning) throw new Exception("Server Already Running!");

            isRunning = true;
            mainThread = new Thread(MainThread);
            mainThread.Start();
        }


        public void MainThread()
        {
            udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint ipep = new IPEndPoint(this.ListenIP, this.ListenPort);
            udpSocket.Bind(ipep);

            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            EndPoint Remote = (EndPoint)(sender);

            //byte[] data1 = Encoding.ASCII.GetBytes("Hello");
            //udpSocket.SendTo(data1, data1.Length, SocketFlags.None, new IPEndPoint(IPAddress.Parse("127.0.0.1"), this.ListenPort));

            while (isRunning)
            {
                try
                {
                    byte[] data = new byte[4096];
                    int recv = udpSocket.ReceiveFrom(data, ref Remote);
                    if (recv > 0) ReceiveBuff(Remote, data, recv);
                }
                catch (Exception ex)
                { 
                };
                Thread.Sleep(1);
            };
        }

        public void Stop()
        {
            if (!isRunning) return;
            isRunning = false;

            udpSocket.Close();
            mainThread.Join();

            udpSocket = null;
            mainThread = null;
        }


        public virtual void ReceiveBuff(EndPoint Client, byte[] data, int length)
        {
        }
    }

    /// <summary>
    ///     Простейший UDP-сервер, который принимает текст
    /// </summary>
    public class SimpleTextUDPServer : SimpleUDPServer
    {
        public SimpleTextUDPServer() : base() { }
        public SimpleTextUDPServer(int Port) : base(Port) { }
        public SimpleTextUDPServer(IPAddress IP, int Port) : base(IP, Port) { }

        public override void ReceiveBuff(EndPoint Client, byte[] data, int length)
        {
            string Request = System.Text.Encoding.GetEncoding(1251).GetString(data, 0, length);
            ReceiveData(Client, Request);
        }

        public void ReceiveData(EndPoint Client, string Request)
        {
            if (OnData2 != null) OnData2(null, 0, Request);
            if (OnData != null) OnData("udp://" + Client.ToString() + "/", Request);
            return;
        }

        public SimpleServer.ValidData OnData = null;
        public SimpleServer.ValidData2 OnData2 = null;
    }

    /// <summary>
    ///     Простейший HTTP-сервер
    /// </summary>
    public class SimpleHttpServer : SimpleTextTCPServer
    {
        public SimpleHttpServer() : base(80) { }
        public SimpleHttpServer(int Port) : base(Port) { }
        public SimpleHttpServer(IPAddress IP, int Port) : base(IP, Port) { }

        // Отправка страницы с ошибкой
        public virtual void HttpClientSendError(TcpClient Client, int Code)
        {
            // Получаем строку вида "200 OK"
            // HttpStatusCode хранит в себе все статус-коды HTTP/1.1
            string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            // Код простой HTML-странички
            string Html = "<html><body><h1>" + CodeStr + "</h1></body></html>";
            // Необходимые заголовки: ответ сервера, тип и длина содержимого. После двух пустых строк - само содержимое
            string Str = "HTTP/1.1 " + CodeStr + "\r\nContent-type: text/html\r\nContent-Length:" + Html.Length.ToString() + "\r\n\r\n" + Html;
            // Приведем строку к виду массива байт
            byte[] Buffer = Encoding.ASCII.GetBytes(Str);
            // Отправим его клиенту
            Client.GetStream().Write(Buffer, 0, Buffer.Length);
            // Закроем соединение
            Client.Close();
        }

        public override bool ReceiveDataValid(TcpClient Client, ulong clientID, string Request)
        {
            HttpClientSendError(Client, 501);
            return false;
        }
    }
}
