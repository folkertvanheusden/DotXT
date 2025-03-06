using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;


internal struct RTSPServerThreadParameters
{
    public RTSPServer hs { get; set; }
    public int port { get; set; }
    public Adlib adlib { get; set; }
};

internal struct RTSPClientThreadParameters
{
    public TcpClient client { get; set; }
    public Adlib adlib { get; set; }
};

class RTSPServer: GraphicalConsole
{
    private Thread _thread = null;
    private int _listen_port = 5540;
    private readonly System.Threading.Lock _stream_lock = new();

    public RTSPServer(Adlib adlib, int port)
    {
        _listen_port = port;

        RTSPServerThreadParameters parameters = new();
        parameters.hs = this;
        parameters.port = port;
        parameters.adlib = adlib;

        _thread = new Thread(RTSPServer.RTSPThread);
        _thread.Name = "rtsp-server-thread";
        _thread.Start(parameters);
    }

    private static byte[] CreateRTPPacket(uint ssrc, ushort seq_nr, uint t, short [] samples)
    {
        int size = 3 * 4 + samples.Length * 2;
        byte [] rtp_packet = new byte[size];
        rtp_packet[0] |= 128;  // v2
        rtp_packet[1] = 11;  // L16
        rtp_packet[2] = (byte)(seq_nr >> 8);
        rtp_packet[3] = (byte)(seq_nr);
        rtp_packet[4] = (byte)(t >> 24);
        rtp_packet[5] = (byte)(t >> 16);
        rtp_packet[6] = (byte)(t >>  8);
        rtp_packet[7] = (byte)(t);
        rtp_packet[8] = (byte)(ssrc >> 24);
        rtp_packet[9] = (byte)(ssrc >> 16);
        rtp_packet[10] = (byte)(ssrc >>  8);
        rtp_packet[11] = (byte)(ssrc);
        for(int i=0; i<samples.Length; i++)
        {
            ushort s = (ushort)samples[i];
            rtp_packet[12 + i * 2 + 1] = (byte)s;
            rtp_packet[12 + i * 2 + 0] = (byte)(s >> 8);
        }

        return rtp_packet;
    }

    public static void RTPStreamTo(Adlib adlib, IPEndPoint remote_endpoint)
    {
        Log.DoLog($"RTPStreamTo: started pushing audio to {remote_endpoint}", LogLevel.DEBUG);
        Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            uint ssrc = (uint)(new Random().Next(1677216));
            int samples_version = -1;
            ushort seq_nr = 0;
            uint t = 0;
            for(;;)
            {
                var rc = adlib.GetSamples(samples_version);
                samples_version = rc.Item2;
                short [] samples = rc.Item1;

                byte [] packet = CreateRTPPacket(ssrc, seq_nr++, t, samples);
                t += (uint)samples.Length;

                s.SendTo(packet, remote_endpoint);
            }
        }
        catch(SocketException e)
        {
            Log.Cnsl($"RTPStreamTo socket exception: {e.ToString()}");
        }
        catch(Exception e)
        {
            Log.Cnsl($"RTPStreamTo exception: {e.ToString()}");
        }

        s.Close();

        Log.DoLog($"RTPStreamTo: stopped pushing audio to {remote_endpoint}", LogLevel.DEBUG);
    }

    private static void PushLine(NetworkStream stream, string what)
    {
        byte[] msg = System.Text.Encoding.ASCII.GetBytes(what + "\r\n");
        stream.Write(msg, 0, msg.Length);
    }

    private static void SendRTSPOKHeader(NetworkStream stream)
    {
        PushLine(stream, "RTSP/1.0 200 All good");
        PushLine(stream, "Server: DotXT");
    }

    public static void RTSPSessionHandler(object o_parameters)
    {
        RTSPClientThreadParameters parameters = (RTSPClientThreadParameters)o_parameters;
        TcpClient client = parameters.client;
        NetworkStream stream = client.GetStream();
        int port = -1;

        try
        {
            for(;;)
            {
                string headers = "";

                // wait for request headers
                byte [] buffer = new byte[4] { 0, 0, 0, 0 };
                while (stream.Read(buffer, 3, 1) != 0)
                {
                    headers += (char)buffer[3];
                    if (buffer[0] == '\r' && buffer[1] == '\n' && buffer[2] == '\r' && buffer[3] == '\n')
                        break;
                    buffer[0] = buffer[1];
                    buffer[1] = buffer[2];
                    buffer[2] = buffer[3];
                    buffer[3] = 0x00;
                }

                string [] lines = headers.Split("\r\n");
                string [] request = lines[0].Split(" ");

                string cseq = "CSeq: 1";
                foreach(var line in lines)
                {
                    if (line.Length > 6 && line.Substring(0, 5) == "CSeq:")
                    {
                        cseq = line;
                        break;
                    }
                }

                if (request.Length == 3)
                {
                    if (request[0] == "OPTIONS")
                    {
                        Log.DoLog("RTSP OPTIONS", LogLevel.TRACE);

                        SendRTSPOKHeader(stream);
                        PushLine(stream, cseq);
                        PushLine(stream, "Public: DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE");
                        PushLine(stream, "");
                    }
                    else if (request[0] == "DESCRIBE")
                    {
                        Log.DoLog("RTSP DESCRIBE", LogLevel.TRACE);

                        string sdp = "v=0\r\n" +
                                     "m=audio 0 RTP/AVP 11\r\n" +  // 11=L16, audio, 1 channel, 44100
                                     "a=rtpmap:11\r\n" +
                                     "a=AvgBitRate:integer;88200\r\n" +
                                     "a=StreamName:string;\"DotXT\"\r\n" +
                                     "i=DotXT\r\n" +
                                     "s=DotXT\r\n";
                        byte[] sdp_msg = System.Text.Encoding.ASCII.GetBytes(sdp);

                        SendRTSPOKHeader(stream);
                        PushLine(stream, cseq);
                        PushLine(stream, $"Content-Base: {request[1]}");
                        PushLine(stream, "Content-Type: application/sdp");
                        PushLine(stream, $"Content-Length: {sdp_msg.Length}");
                        PushLine(stream, "");
                        stream.Write(sdp_msg);
                    }
                    else if (request[0] == "SETUP")
                    {
                        SendRTSPOKHeader(stream);
                        PushLine(stream, cseq);
                        PushLine(stream, "Session: 12345678");
                        PushLine(stream, "Transport: RTP/AVP/UDP;unicast");
                        PushLine(stream, "");

                        foreach(var line in lines)
                        {
                            if (line.Length > 10 && line.Substring(0, 10) == "Transport:")
                            {
                                int port_string_index = line.IndexOf("client_port=");
                                if (port_string_index != -1)
                                {
                                    string [] port_parts = line.Substring(port_string_index + 12).Split("-");
                                    port = Convert.ToInt32(port_parts[0], 10);
                                    break;
                                }
                            }
                        }

                        if (port == -1)
                            break;

                        Log.DoLog($"RTSP (RTSPSessionHandler): port {port}", LogLevel.DEBUG);
                    }
                    else if (request[0] == "PLAY")
                    {
                        SendRTSPOKHeader(stream);
                        PushLine(stream, cseq);
                        PushLine(stream, "Session: 12345678");
                        PushLine(stream, "");

                        if (port != -1)
                        {
                            IPEndPoint rtsp_remote_endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                            IPEndPoint rtp_remote_endpoint = new IPEndPoint(rtsp_remote_endpoint.Address, port);
                            RTPStreamTo(parameters.adlib, rtp_remote_endpoint);
                        }
                    }
                    else if (request[0] == "TEARDOWN")
                    {
                        // TODO
                    }
                    else
                    {
                        Log.Cnsl($"Requested: {request[1]} - 404");

                        Console.WriteLine("---");
                        Console.WriteLine(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000);
                        Console.WriteLine(headers);

                        PushLine(stream, $"RTSP/1.0 404 {request[1]} not found");
                        PushLine(stream, "Server: DotXT");
                        PushLine(stream, "");
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
        }
        catch(SocketException e)
        {
            Log.Cnsl($"RTSPServer socket exception: {e.ToString()}");
        }
        catch(Exception e)
        {
            Log.Cnsl($"RTSPServer exception: {e.ToString()}");
        }

        client.Close();
    }

    public static void RTSPThread(object o_parameters)
    {
        RTSPServerThreadParameters s_parameters = (RTSPServerThreadParameters)o_parameters;
        TcpListener tcp_listener = new TcpListener(IPAddress.Parse("0.0.0.0"), s_parameters.port);
        tcp_listener.Start();
        Log.Cnsl($"RTSP server started on port {s_parameters.port}");

        for(;;)
        {
            TcpClient client = tcp_listener.AcceptTcpClient();
            Log.Cnsl("Connected to RTSP client");

            RTSPClientThreadParameters c_parameters = new();
            c_parameters.client = client;
            c_parameters.adlib = s_parameters.adlib;

            Thread thread = new Thread(RTSPServer.RTSPSessionHandler);
            thread.Name = "rtsp-sdp-rtp-thread";
            thread.IsBackground = true;
            thread.Start(c_parameters);
        }
    }
}
