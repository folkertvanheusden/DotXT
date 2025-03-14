using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;


internal struct HTTPServerThreadParameters
{
    public HTTPServer hs { get; set; }
    public int port { get; set; }
};

class HTTPServer: GraphicalConsole
{
    private Thread _thread = null;
    private Keyboard _kb = null;
    private int _listen_port = 8080;
    private readonly System.Threading.Lock _stream_lock = new();

    public HTTPServer(Keyboard kb, int port)
    {
        _kb = kb;
        _listen_port = port;

        HTTPServerThreadParameters parameters = new();
        parameters.hs = this;
        parameters.port = port;

        _thread = new Thread(HTTPServer.HTTPThread);
        _thread.Name = "http-server-thread";
        _thread.Start(parameters);
    }

    public static void PushLine(NetworkStream stream, string what)
    {
        byte[] msg = System.Text.Encoding.ASCII.GetBytes(what + "\r\n");
        stream.Write(msg, 0, msg.Length);
    }

    public static void HTTPThread(object o_parameters)
    {
        HTTPServerThreadParameters parameters = (HTTPServerThreadParameters)o_parameters;
        TcpListener tcp_listener = new TcpListener(IPAddress.Parse("0.0.0.0"), parameters.port);
        tcp_listener.Start();
        Log.Cnsl($"HTTP server started on port {parameters.port}");

        for(;;)
        {
            TcpClient client = tcp_listener.AcceptTcpClient();
            Log.Cnsl("Connected to HTTP client");
            NetworkStream stream = client.GetStream();

            try
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
                if (request.Length == 3)
                {
                    if (request[0] == "GET" && request[1] == "/frame.cgi")
                    {
                        Log.Cnsl($"Requested: {request[1]} - 200");

                        PushLine(stream, "HTTP/1.0 200 All good");
                        PushLine(stream, "Server: DotXT");
                        PushLine(stream, "Content-Type: image/bmp");
                        PushLine(stream, "");

                        byte [] data = parameters.hs.GetBmp();
                        stream.Write(data, 0, data.Length);
                    }
                    else if (request[0] == "GET" && request[1] == "/stream.cgi")
                    {
                        Log.Cnsl($"Requested: {request[1]} - 200");

                        PushLine(stream, "HTTP/1.0 200 All good");
                        PushLine(stream, "Server: DotXT");
                        PushLine(stream, "Cache-Control: no-cache");
                        PushLine(stream, "Pragma: no-cache");
                        PushLine(stream, "Expires: Thu, 01 Dec 1994 16:00:00 GMT");
                        PushLine(stream, "Content-Type: multipart/x-mixed-replace; boundary=--myboundary");
                        PushLine(stream, "");

                        int version = 0;
                        for(;;)
                        {
                            int new_version = parameters.hs.GetFrameVersion();
                            if (new_version != version)
                            {
                                version = new_version;

                                byte [] data = parameters.hs.GetBmp();

                                PushLine(stream, "--myboundary");
                                PushLine(stream, "Content-Type: image/bmp");
                                PushLine(stream, $"Content-Length: {data.Length}");
                                PushLine(stream, "");
                                stream.Write(data, 0, data.Length);
                            }

                            Thread.Sleep(1000 / 15);
                        }
                    }
                    else
                    {
                        Log.Cnsl($"Requested: {request[1]} - 404");

                        PushLine(stream, $"HTTP/1.0 404 {request[1]} not found");
                        PushLine(stream, "Server: DotXT");
                        PushLine(stream, "");
                    }
                }
                else
                {
                        Log.Cnsl(headers);
                }
            }
            catch(SocketException e)
            {
                Log.Cnsl($"HTTPServer socket exception: {e.ToString()}");
            }
            catch(Exception e)
            {
                Log.Cnsl($"HTTPServer exception: {e.ToString()}");
            }

            client.Close();
        }
    }
}
