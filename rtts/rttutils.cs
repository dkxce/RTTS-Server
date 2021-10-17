using System;
using System.Collections.Generic;
using System.Text;

namespace rtts
{
    public class rttutils
    {        
        public static string GenerateLink(string ID, string Event)
        {
            string prefix = "http://127.0.0.1/rtt/qr/";
            byte[] plainTextBytes = (String.IsNullOrEmpty(ID) ? System.Text.Encoding.UTF8.GetBytes("Event:'" + Event + "'") : System.Text.Encoding.UTF8.GetBytes("ID:'" + ID + "',Event:'" + Event + "'"));
            return prefix + System.Convert.ToBase64String(plainTextBytes);

            //public static string Base64Decode(string base64EncodedData) {
            //  var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            //  return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        public static int CSChecksum(string callsign)
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
    }
}
