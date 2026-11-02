using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OmniroLauncher;

/// <summary>Запрос информации о сервере по стандартному SA-MP query-протоколу (UDP).</summary>
public static class SampQuery
{
    public static async Task<ServerInfo?> QueryAsync(string host, int port, int timeoutMs = 2000)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            var addr = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (addr == null) return null;

            var ip = addr.GetAddressBytes();
            var pkt = new byte[11];
            pkt[0] = (byte)'S'; pkt[1] = (byte)'A'; pkt[2] = (byte)'M'; pkt[3] = (byte)'P';
            Array.Copy(ip, 0, pkt, 4, 4);
            pkt[8] = (byte)(port & 0xFF);
            pkt[9] = (byte)((port >> 8) & 0xFF);
            pkt[10] = (byte)'i';

            using var udp = new UdpClient();
            var sw = Stopwatch.StartNew();
            await udp.SendAsync(pkt, pkt.Length, new IPEndPoint(addr, port));

            var recv = udp.ReceiveAsync();
            if (await Task.WhenAny(recv, Task.Delay(timeoutMs)) != recv) return null;
            sw.Stop();

            var data = recv.Result.Buffer;
            // 11 байт заголовка + пароль(1) + игроки(2) + максимум(2) + длина имени(4)
            if (data.Length < 20) return null;

            int o = 11;
            o += 1;
            int players = BitConverter.ToUInt16(data, o); o += 2;
            int max = BitConverter.ToUInt16(data, o); o += 2;
            int len = BitConverter.ToInt32(data, o); o += 4;

            string name = "";
            if (len > 0 && o + len <= data.Length)
                name = Encoding.GetEncoding(1251).GetString(data, o, len);

            return new ServerInfo(players, max, name, (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            return null;
        }
    }
}
