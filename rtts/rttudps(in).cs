using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace rtts
{
    public class UDPUploadServer : SimpleServers.SimpleUDPServer
    {
        public TriggerServer.onReceivePacket onPacket;

        public UDPUploadServer(int port): base (port) {}

        public override void ReceiveBuff(System.Net.EndPoint Client, byte[] data, int length)
        {
            if (length == 0) return;
            if (data.Length < 16) return; // no valid length

            int startIndex = 0;
            while (startIndex < length)
            {      
                if(data[startIndex] == 0) return;
                RTTPacket p = RTTPacket.FromBytes(data, startIndex);
                if (!p.valid) return;
                ReceivePacket(p);
                startIndex += p.packet_length;
            };
        }

        public void ReceivePacket(RTTPacket pkt)
        {            
            //Console.WriteLine(pkt.packet_text);
            if ((pkt.ptype == "01") || (pkt.ptype == "02"))
                ReceivePacket(pkt, PT0102.FromJSON(pkt.datatext));
        }

        public void ReceivePacket(RTTPacket pkt, PT0102 data)
        {
            if (onPacket != null) onPacket(pkt, data, 0);
            //Console.WriteLine(pkt.datatext);
        }
    }    
}
