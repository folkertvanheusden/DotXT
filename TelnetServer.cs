using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;


internal struct TelnetServerThreadParameters
{
    public TelnetServer ts { get; set; }
    public int port { get; set; }
};

class TelnetServer: TextConsole
{
    private Thread _thread = null;
    private Keyboard _kb = null;
    private int _listen_port = 2300;
    private static readonly System.Threading.Lock _stream_lock = new();
    private NetworkStream _ns = null;
    private UTF8Encoding utf8 = new UTF8Encoding();

    public TelnetServer(Keyboard kb, int port)
    {
        _kb = kb;
        _listen_port = port;

        TelnetServerThreadParameters parameters = new();
        parameters.ts = this;
        parameters.port = port;

        _thread = new Thread(TelnetServer.TelnetThread);
        _thread.Name = "telnet-server-thread";
        _thread.Start(parameters);
    }

    public override void Write(string what)
    {
        lock(_stream_lock)
        {
            if (_ns != null)
            {
                byte [] msg = utf8.GetBytes(what);
                try
                {
                    _ns.Write(msg, 0, msg.Length);
                }
                catch(SocketException e)
                {
                    Console.WriteLine($"TelnetServer socket exception: {e}");
                }
            }
        }
    }

    public void PushChar(byte c)
    {
        if (_kb == null || c == 0 || c >= 127)
            return;

        Console.WriteLine($"PushChar({c})");

        Dictionary<char, byte []> key_map = new() {
                { (char)27, new byte[] { 0x01 } },
                { (char)13, new byte[] { 0x1c } },
                { '1', new byte[] { 0x02 } },
                { '2', new byte[] { 0x03 } },
                { '3', new byte[] { 0x04 } },
                { '4', new byte[] { 0x05 } },
                { '5', new byte[] { 0x06 } },
                { '6', new byte[] { 0x07 } },
                { '7', new byte[] { 0x08 } },
                { '8', new byte[] { 0x09 } },
                { '9', new byte[] { 0x0a } },
                { '0', new byte[] { 0x0b } },
                { 'A', new byte[] { 0x2a, 0x1e, 0x1e | 0x80, 0x2a, 0xaa } },
                { 'B', new byte[] { 0x2a, 0x30, 0x30 | 0x80, 0x2a, 0xaa } },
                { 'C', new byte[] { 0x2a, 0x2e, 0x2e | 0x80, 0x2a, 0xaa } },
                { 'D', new byte[] { 0x2a, 0x20, 0x20 | 0x80, 0x2a, 0xaa } },
                { 'E', new byte[] { 0x2a, 0x12, 0x12 | 0x80, 0x2a, 0xaa } },
                { 'F', new byte[] { 0x2a, 0x21, 0x21 | 0x80, 0x2a, 0xaa } },
                { 'G', new byte[] { 0x2a, 0x22, 0x22 | 0x80, 0x2a, 0xaa } },
                { 'H', new byte[] { 0x2a, 0x23, 0x23 | 0x80, 0x2a, 0xaa } },
                { 'I', new byte[] { 0x2a, 0x17, 0x17 | 0x80, 0x2a, 0xaa } },
                { 'J', new byte[] { 0x2a, 0x24, 0x24 | 0x80, 0x2a, 0xaa } },
                { 'K', new byte[] { 0x2a, 0x25, 0x25 | 0x80, 0x2a, 0xaa } },
                { 'L', new byte[] { 0x2a, 0x26, 0x26 | 0x80, 0x2a, 0xaa } },
                { 'M', new byte[] { 0x2a, 0x32, 0x32 | 0x80, 0x2a, 0xaa } },
                { 'N', new byte[] { 0x2a, 0x31, 0x31 | 0x80, 0x2a, 0xaa } },
                { 'O', new byte[] { 0x2a, 0x18, 0x18 | 0x80, 0x2a, 0xaa } },
                { 'P', new byte[] { 0x2a, 0x19, 0x19 | 0x80, 0x2a, 0xaa } },
                { 'Q', new byte[] { 0x2a, 0x10, 0x10 | 0x80, 0x2a, 0xaa } },
                { 'R', new byte[] { 0x2a, 0x13, 0x13 | 0x80, 0x2a, 0xaa } },
                { 'S', new byte[] { 0x2a, 0x1f, 0x1f | 0x80, 0x2a, 0xaa } },
                { 'T', new byte[] { 0x2a, 0x14, 0x14 | 0x80, 0x2a, 0xaa } },
                { 'U', new byte[] { 0x2a, 0x16, 0x16 | 0x80, 0x2a, 0xaa } },
                { 'V', new byte[] { 0x2a, 0x2f, 0x2f | 0x80, 0x2a, 0xaa } },
                { 'W', new byte[] { 0x2a, 0x11, 0x11 | 0x80, 0x2a, 0xaa } },
                { 'X', new byte[] { 0x2a, 0x2d, 0x2d | 0x80, 0x2a, 0xaa } },
                { 'Y', new byte[] { 0x2a, 0x15, 0x15 | 0x80, 0x2a, 0xaa } },
                { 'Z', new byte[] { 0x2a, 0x2c, 0x2c | 0x80, 0x2a, 0xaa } },
                { 'a', new byte[] { 0x1e } },
                { 'b', new byte[] { 0x30 } },
                { 'c', new byte[] { 0x2e } },
                { 'd', new byte[] { 0x20 } },
                { 'e', new byte[] { 0x12 } },
                { 'f', new byte[] { 0x21 } },
                { 'g', new byte[] { 0x22 } },
                { 'h', new byte[] { 0x23 } },
                { 'i', new byte[] { 0x17 } },
                { 'j', new byte[] { 0x24 } },
                { 'k', new byte[] { 0x25 } },
                { 'l', new byte[] { 0x26 } },
                { 'm', new byte[] { 0x32 } },
                { 'n', new byte[] { 0x31 } },
                { 'o', new byte[] { 0x18 } },
                { 'p', new byte[] { 0x19 } },
                { 'q', new byte[] { 0x10 } },
                { 'r', new byte[] { 0x13 } },
                { 's', new byte[] { 0x1f } },
                { 't', new byte[] { 0x14 } },
                { 'u', new byte[] { 0x16 } },
                { 'v', new byte[] { 0x2f } },
                { 'w', new byte[] { 0x11 } },
                { 'x', new byte[] { 0x2d } },
                { 'y', new byte[] { 0x15 } },
                { 'z', new byte[] { 0x2c } },
                { ' ', new byte[] { 0x39 } },
                { '.', new byte[] { 0x34 } },
                { '-', new byte[] { 0x0c } },
                { '_', new byte[] { 0x2a, 0x0c, 0x0c | 0x80, 0x2a, 0xaa } },
                { (char)8, new byte[] { 0x0e } },
                { (char)9, new byte[] { 0x0f } },
                { ':', new byte[] { 0x2a, 0x27, 0x27 | 0x80, 0x2a, 0xaa } },
        };

        if (key_map.ContainsKey((char)c))
        {
            var messages = key_map[(char)c];
            for(int i=0; i<messages.Length; i++)
                _kb.PushKeyboardScancode(messages[i]);
            if (messages.Length == 1)
                _kb.PushKeyboardScancode(messages[0] ^ 0x80);
        }
    }

    public void SetStream(NetworkStream ns)
    {
        lock(_stream_lock)
        {
            _ns = ns;
            _d.Redraw();
        }
    }

    public static void SetupTelnetSession(NetworkStream stream)
    {
        byte [] dont_auth          = { 0xff, 0xf4, 0x25 };
        byte [] suppress_goahead   = { 0xff, 0xfb, 0x03 };
        byte [] dont_linemode      = { 0xff, 0xfe, 0x22 };
        byte [] dont_new_env       = { 0xff, 0xfe, 0x27 };
        byte [] will_echo          = { 0xff, 0xfb, 0x01 };
        byte [] dont_echo          = { 0xff, 0xfe, 0x01 };
        byte [] noecho             = { 0xff, 0xfd, 0x2d };
        // uint8_t charset[]          = { 0xff, 0xfb, 0x01 };

        stream.Write(dont_auth, 0, dont_auth.Length);
        stream.Write(suppress_goahead, 0, suppress_goahead.Length);
        stream.Write(dont_linemode, 0, dont_linemode.Length);
        stream.Write(dont_new_env, 0, dont_new_env.Length);
        stream.Write(will_echo, 0, will_echo.Length);
        stream.Write(dont_echo, 0, dont_echo.Length);
        stream.Write(noecho, 0, noecho.Length);
    }

    public static void TelnetThread(object o_parameters)
    {
        TelnetServerThreadParameters parameters = (TelnetServerThreadParameters)o_parameters;
        TcpListener tcp_listener = new TcpListener(IPAddress.Parse("0.0.0.0"), parameters.port);
        tcp_listener.Start();
        Console.WriteLine("Telnet server started");

        for(;;)
        {
            TcpClient client = tcp_listener.AcceptTcpClient();
            Console.WriteLine("Connected to telnet client");
            NetworkStream stream = client.GetStream();
            SetupTelnetSession(stream);
            parameters.ts.SetStream(stream);

            byte [] buffer = new byte[1];
            while (stream.Read(buffer, 0, buffer.Length) != 0)
            {
                parameters.ts.PushChar(buffer[0]);
            }

            client.Close();
        }
    }
}
