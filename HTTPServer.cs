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
    private static readonly System.Threading.Lock _stream_lock = new();

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

    public static byte[] GraphicalFrameToBmp(GraphicalFrame g)
    {
        int out_len = g.width * g.height * 3 + 2 + 12 + 40;
        byte [] out_ = new byte[out_len];

        int offset = 0;
        out_[offset++] = (byte)'B';
        out_[offset++] = (byte)'M';
        out_[offset++] = (byte)out_len;  // file size in bytes
        out_[offset++] = (byte)(out_len >> 8);
        out_[offset++] = (byte)(out_len >> 16);
        out_[offset++] = (byte)(out_len >> 24);
        out_[offset++] = 0x00;  // reserved
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 54;  // offset of start (2 + 12 + 40)
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        //assert(offset == 0x0e);
        out_[offset++] = 40;  // header size
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.width;
        out_[offset++] = (byte)(g.width >> 8);
        out_[offset++] = (byte)(g.width >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.height;
        out_[offset++] = (byte)(g.height >> 8);
        out_[offset++] = (byte)(g.height >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = 0x01;  // color planes
        out_[offset++] = 0x00;
        out_[offset++] = 24;  // bits per pixel
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // compression method
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // image size
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.width;
        out_[offset++] = (byte)(g.width >> 8);
        out_[offset++] = (byte)(g.width >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.height;
        out_[offset++] = (byte)(g.height >> 8);
        out_[offset++] = (byte)(g.height >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // color count
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // important colors
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;

        for(int y=g.height - 1; y >= 0; y--) {
            int in_o = y * g.width * 3;
            for(int x=0; x<g.width; x++) {
                int in_o2 = in_o + x * 3;
                out_[offset++] = g.rgb_pixels[in_o2 + 2];
                out_[offset++] = g.rgb_pixels[in_o2 + 1];
                out_[offset++] = g.rgb_pixels[in_o2 + 0];
            }
        }

        return out_;
    }

    public static void HTTPThread(object o_parameters)
    {
        HTTPServerThreadParameters parameters = (HTTPServerThreadParameters)o_parameters;
        TcpListener tcp_listener = new TcpListener(IPAddress.Parse("0.0.0.0"), parameters.port);
        tcp_listener.Start();
        Console.WriteLine("HTTP server started");

        for(;;)
        {
            TcpClient client = tcp_listener.AcceptTcpClient();
            Console.WriteLine("Connected to HTTP client");
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
                        Console.WriteLine($"Requested: {request[1]} - 200");

                        PushLine(stream, "HTTP/1.0 200 All good");
                        PushLine(stream, "Server: DotXT");
                        PushLine(stream, "Content-Type: image/bmp");
                        PushLine(stream, "");

                        GraphicalFrame frame = parameters.hs.GetFrame();
                        byte [] data = GraphicalFrameToBmp(frame);
                        stream.Write(data, 0, data.Length);
                    }
                    else if (request[0] == "GET" && request[1] == "/stream.cgi")
                    {
                        Console.WriteLine($"Requested: {request[1]} - 200");

                        PushLine(stream, "HTTP/1.0 200 All good");
                        PushLine(stream, "Server: DotXT");
                        PushLine(stream, "Cache-Control: no-cache");
                        PushLine(stream, "Pragma: no-cache");
                        PushLine(stream, "Expires: Thu, 01 Dec 1994 16:00:00 GMT");
                        PushLine(stream, "Content-Type: multipart/x-mixed-replace; boundary=--myboundary");
                        PushLine(stream, "");

                        ulong version = 0;
                        for(;;)
                        {
                            ulong new_version = parameters.hs.GetFrameVersion();
                            if (new_version != version)
                            {
                                version = new_version;

                                GraphicalFrame frame = parameters.hs.GetFrame();
                                byte [] data = GraphicalFrameToBmp(frame);

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
                        Console.WriteLine($"Requested: {request[1]} - 404");

                        PushLine(stream, $"HTTP/1.0 404 {request[1]} not found");
                        PushLine(stream, "Server: DotXT");
                        PushLine(stream, "");
                    }
                }
                else
                {
                        Console.WriteLine(headers);
                }
            }
            catch(SocketException e)
            {
                Console.WriteLine($"HTTPServer socket exception: {e.ToString()}");
            }
            catch(Exception e)
            {
                Console.WriteLine($"HTTPServer exception: {e.ToString()}");
            }

            client.Close();
        }
    }
}
