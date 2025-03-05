using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;


internal struct RTSPServerThreadParameters
{
    public RTSPServer hs { get; set; }
    public int port { get; set; }
};

class RTSPServer: GraphicalConsole
{
    private Thread _thread = null;
    private Adlib _adlib = null;
    private int _listen_port = 5540;
    private readonly System.Threading.Lock _stream_lock = new();

    public RTSPServer(Adlib adlib, int port)
    {
        _adlib = adlib;
        _listen_port = port;

        RTSPServerThreadParameters parameters = new();
        parameters.hs = this;
        parameters.port = port;

        _thread = new Thread(RTSPServer.RTSPThread);
        _thread.Name = "rtsp-server-thread";
        _thread.Start(parameters);
    }

    public static void PushLine(NetworkStream stream, string what)
    {
        byte[] msg = System.Text.Encoding.ASCII.GetBytes(what + "\r\n");
        stream.Write(msg, 0, msg.Length);
    }

    public static void RTSPThread(object o_parameters)
    {
        RTSPServerThreadParameters parameters = (RTSPServerThreadParameters)o_parameters;
        TcpListener tcp_listener = new TcpListener(IPAddress.Parse("0.0.0.0"), parameters.port);
        tcp_listener.Start();
        Log.Cnsl($"RTSP server started on port {parameters.port}");

        for(;;)
        {
            TcpClient client = tcp_listener.AcceptTcpClient();
            Log.Cnsl("Connected to RTSP client");
            NetworkStream stream = client.GetStream();

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

                    // Console.WriteLine("---");
                    // Console.WriteLine(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000);
                    // Console.WriteLine(headers);

                    string [] lines = headers.Split("\r\n");
                    string [] request = lines[0].Split(" ");
                    if (request.Length == 3)
                    {
                        if (request[0] == "OPTIONS")
                        {
                            Log.DoLog("RTSP OPTIONS", LogLevel.TRACE);

                            PushLine(stream, "RTSP/1.0 200 All good");
                            PushLine(stream, "CSeq: 1");
                            PushLine(stream, "Public: DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE");
                            PushLine(stream, "");
                        }
                        else if (request[0] == "DESCRIBE")
                        {
                            Log.DoLog("RTSP DESCRIBE", LogLevel.TRACE);

                            string sdp = "m=audio 0 RTP/AVP 11\r\n" +  // 11=L16, audio, 1 channel, 44100
                                         "a=rtpmap:11\r\n" +
                                         "a=AvgBitRate:integer;88200\r\n" +
                                         "a=StreamName:string;\"DotXT\"\r\n";
                            byte[] sdp_msg = System.Text.Encoding.ASCII.GetBytes(sdp);

                            PushLine(stream, "RTSP/1.0 200 All good");
                            PushLine(stream, "CSeq: 2");
                            PushLine(stream, $"Content-Base: {request[1]}");
                            PushLine(stream, "Content-Type: application/sdp");
                            PushLine(stream, $"Content-Length: {sdp_msg.Length}");
                            PushLine(stream, "");
                        }
                        else if (request[0] == "SETUP")
                        {
                            PushLine(stream, "RTSP/1.0 200 OK");
                            PushLine(stream, "CSeq: 3");
                            PushLine(stream, "Session: 12345678");
                            PushLine(stream, "");
                        }
                        else if (request[0] == "PLAY")
                        {
                            PushLine(stream, "RTSP/1.0 200 OK");
                            PushLine(stream, "CSeq: 4");
                            PushLine(stream, "Session: 12345678");
                            PushLine(stream, "");

// UDP stream (RTP)
                        }
                        else
                        {
                            Log.Cnsl($"Requested: {request[1]} - 404");

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
    }
}
