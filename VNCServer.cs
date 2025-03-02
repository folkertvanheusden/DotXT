using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;


internal struct VNCServerThreadParameters
{
    public VNCServer vs { get; set; }
    public int port { get; set; }
};

class VNCServer: GraphicalConsole
{
    private Thread _thread = null;
    private Keyboard _kb = null;
    private int _listen_port = 5900;
    private bool _compatible = false;
    private static int compatible_width = 640;
    private static int compatible_height = 400;

    public VNCServer(Keyboard kb, int port, bool compatible)
    {
        _kb = kb;
        _listen_port = port;
        _compatible = compatible;

        VNCServerThreadParameters parameters = new();
        parameters.vs = this;
        parameters.port = port;

        _thread = new Thread(VNCServer.VNCThread);
        _thread.Name = "vnc-server-thread";
        _thread.Start(parameters);
    }

    public void PushChar(uint c, bool press)
    {
        if (_kb == null)
            return;

        Dictionary<uint, byte []> key_map = new() {
                { 0xff1b, new byte[] { 0x01 } },  // escape
                { 0xff0d, new byte[] { 0x1c } },  // enter
                { 0xff08, new byte[] { 0x0e } },  // backspace
                { 0xff09, new byte[] { 0x0f } },  // tab
                { 0xffe1, new byte[] { 0x2a } },  // left shift
                { 0xffe3, new byte[] { 0x1d } },  // left control
                { 0xffe9, new byte[] { 0x38 } },  // left alt
                { 0xffbe, new byte[] { 0x3b } },  // F1
                { 0xffbf, new byte[] { 0x3c } },  // F2
                { 0xffc0, new byte[] { 0x3d } },  // F3
                { 0xffc1, new byte[] { 0x3e } },  // F4
                { 0xffc2, new byte[] { 0x3f } },  // F5
                { 0xffc3, new byte[] { 0x40 } },  // F6
                { 0xffc4, new byte[] { 0x41 } },  // F7
                { 0xffc5, new byte[] { 0x42 } },  // F8
                { 0xffc6, new byte[] { 0x43 } },  // F9
                { 0xffc7, new byte[] { 0x44 } },  // F10
                { 0x31, new byte[] { 0x02 } },  // 1
                { 0x32, new byte[] { 0x03 } },
                { 0x33, new byte[] { 0x04 } },
                { 0x34, new byte[] { 0x05 } },
                { 0x35, new byte[] { 0x06 } },
                { 0x36, new byte[] { 0x07 } },
                { 0x37, new byte[] { 0x08 } },
                { 0x38, new byte[] { 0x09 } },
                { 0x39, new byte[] { 0x0a } },  // 9
                { 0x30, new byte[] { 0x0b } },  // 0
                { 0x41, new byte[] { 0x1e } },  // A
                { 0x42, new byte[] { 0x30 } },
                { 0x43, new byte[] { 0x2e } },
                { 0x44, new byte[] { 0x20 } },
                { 0x45, new byte[] { 0x12 } },
                { 0x46, new byte[] { 0x21 } },
                { 0x47, new byte[] { 0x22 } },
                { 0x48, new byte[] { 0x23 } },
                { 0x49, new byte[] { 0x17 } },
                { 0x4a, new byte[] { 0x24 } },
                { 0x4b, new byte[] { 0x25 } },
                { 0x4c, new byte[] { 0x26 } },
                { 0x4d, new byte[] { 0x32 } },
                { 0x4e, new byte[] { 0x31 } },
                { 0x4f, new byte[] { 0x18 } },
                { 0x50, new byte[] { 0x19 } },
                { 0x51, new byte[] { 0x10 } },
                { 0x52, new byte[] { 0x13 } },
                { 0x53, new byte[] { 0x1f } },
                { 0x54, new byte[] { 0x14 } },
                { 0x55, new byte[] { 0x16 } },
                { 0x56, new byte[] { 0x2f } },
                { 0x57, new byte[] { 0x11 } },
                { 0x58, new byte[] { 0x2d } },
                { 0x59, new byte[] { 0x15 } },
                { 0x5a, new byte[] { 0x2c } },  // Z
                { 0x61, new byte[] { 0x1e } },  // a
                { 0x62, new byte[] { 0x30 } },
                { 0x63, new byte[] { 0x2e } },
                { 0x64, new byte[] { 0x20 } },
                { 0x65, new byte[] { 0x12 } },
                { 0x66, new byte[] { 0x21 } },
                { 0x67, new byte[] { 0x22 } },
                { 0x68, new byte[] { 0x23 } },
                { 0x69, new byte[] { 0x17 } },
                { 0x6a, new byte[] { 0x24 } },
                { 0x6b, new byte[] { 0x25 } },
                { 0x6c, new byte[] { 0x26 } },
                { 0x6d, new byte[] { 0x32 } },
                { 0x6e, new byte[] { 0x31 } },
                { 0x6f, new byte[] { 0x18 } },
                { 0x70, new byte[] { 0x19 } },
                { 0x71, new byte[] { 0x10 } },
                { 0x72, new byte[] { 0x13 } },
                { 0x73, new byte[] { 0x1f } },
                { 0x74, new byte[] { 0x14 } },
                { 0x75, new byte[] { 0x16 } },
                { 0x76, new byte[] { 0x2f } },
                { 0x77, new byte[] { 0x11 } },
                { 0x78, new byte[] { 0x2d } },
                { 0x79, new byte[] { 0x15 } },
                { 0x7a, new byte[] { 0x2c } },  // z
                { 0x20, new byte[] { 0x39 } },  // space
                { 0x2e, new byte[] { 0x34 } },  // .
                { 0x2d, new byte[] { 0x0c } },  // -
                { 0x5f, new byte[] { 0x0c } },  // _
                { 0x3a, new byte[] { 0x27 } },  // :
                { 0x2f, new byte[] { 0x35 } },  // /
                { 0x2a, new byte[] { 0x09 } },  // *  (shift)
                { 0x26, new byte[] { 0x08 } },  // &  (shift)
                { 0x5c, new byte[] { 0x2b } },  // \
                { 0x7c, new byte[] { 0x2b } },  // |  (shift)
                { 0xff54, new byte[] { 0x50 } },  // cursor down
                { 0xff52, new byte[] { 0x48 } },  // cursor up
                { 0xff51, new byte[] { 0x4b } },  // cursor left
                { 0xff53, new byte[] { 0x4d } },  // cursor right
                { 0xff50, new byte[] { 0x47 } },  // home
                { 0xff57, new byte[] { 0x4f } },  // end
        };

        if (key_map.ContainsKey(c))
        {
            var messages = key_map[c];
            for(int i=0; i<messages.Length; i++)
                _kb.PushKeyboardScancode(press ? messages[i] : (messages[i] | 0x80));
        }
    }

    private static void VNCSendVersion(NetworkStream stream)
    {
        byte[] msg = System.Text.Encoding.ASCII.GetBytes("RFB 003.008\n");
        stream.Write(msg, 0, msg.Length);

        // wait for reply, ignoring what it is
        byte[] buffer = new byte[1];
        buffer[0] = 0;
        while(buffer[0] != '\n')
        {
            stream.ReadExactly(buffer);
        }
    }

    private static void VNCSecurityHandshake(NetworkStream stream)
    {
        byte[] list = new byte[2];
        list[0] = 1;  // 1
        list[1] = 1;  // None
        stream.Write(list, 0, list.Length);

        // receive reply with choice, ignoring choice
        byte[] buffer = new byte[1];
        stream.ReadExactly(buffer);

        byte[] reply = new byte[4];
        reply[3] = 0;  // OK
        stream.Write(reply, 0, reply.Length);
    }

    private static void VNCClientServerInit(NetworkStream stream, VNCServer vnc)
    {
        byte[] shared = new byte[1];
        stream.ReadExactly(shared);

        var example = vnc.GetFrame();
        int width = vnc._compatible ? compatible_width : example.width;
        int height = vnc._compatible ? compatible_height : example.height;
        byte[] reply = new byte[24];
        reply[0] = (byte)(width >> 8);
        reply[1] = (byte)(width & 255);
        reply[2] = (byte)(height >> 8);
        reply[3] = (byte)(height & 255);
        reply[4] = 32;  // bits per pixel
        reply[5] = 32;  // depth
        reply[6] = 1;  // big endian
        reply[7] = 1;  // true color
        reply[8] = 0;  // red max
        reply[9] = 255;  // red max
        reply[10] = 0;  // green max
        reply[11] = 255;  // green max
        reply[12] = 0;  // blue max
        reply[13] = 255;  // blue max
        reply[14] = 16;  // red shift
        reply[15] = 8;  // green shift
        reply[16] = 0;  // blue shift
        reply[17] = reply[18] = reply[19] = 0;  // padding
        string name = "DotXT";
        reply[20] = (byte)(name.Length >> 24);
        reply[21] = (byte)(name.Length >> 16);
        reply[22] = (byte)(name.Length >>  8);
        reply[23] = (byte)name.Length;
        stream.Write(reply, 0, reply.Length);
        byte[] name_bytes = System.Text.Encoding.ASCII.GetBytes(name);
        stream.Write(name_bytes, 0, name_bytes.Length);
    }

    private static void VNCWaitForEvent(NetworkStream stream, VNCServer vnc)
    {
        stream.ReadTimeout = 1000 / 15;  // 15 fps

        byte[] type = new byte[1];
        try
        {
            stream.ReadExactly(type);
        }
        catch(System.IO.IOException e)
        {
            return;
        }

        stream.ReadTimeout = 1000;  // sane(?) timeout

        Log.DoLog($"Client message {type[0]} received", LogLevel.TRACE);

        if (type[0] == 0)  // SetPixelFormat
        {
            byte[] temp = new byte[3 + 16];
            stream.ReadExactly(temp);
        }
        else if (type[0] == 2)  // SetEncodings
        {
            byte[] temp = new byte[3];
            stream.ReadExactly(temp);
            ushort no_encodings = (ushort)((temp[1] << 8) | temp[2]);
            Log.DoLog($"retrieve {no_encodings} encodings", LogLevel.TRACE);
            byte[] encodings = new byte[no_encodings * 4];
            stream.ReadExactly(encodings);
        }
        else if (type[0] == 3)  // FramebufferUpdateRequest
        {
            byte[] buffer = new byte[9];
            stream.ReadExactly(buffer);
            // TODO
        }
        else if (type[0] == 4)  // KeyEvent
        {
            byte[] buffer = new byte[7];
            stream.ReadExactly(buffer);
            uint vnc_scan_code = (uint)((buffer[3] << 24) | (buffer[4] << 16) | (buffer[5] << 8) | buffer[6]);
            Log.DoLog($"Key {buffer[0]} {vnc_scan_code:x04}", LogLevel.DEBUG);
            vnc.PushChar(vnc_scan_code, buffer[0] != 0);
        }
        else if (type[0] == 5)  // PointerEvent
        {
            byte[] buffer = new byte[5];
            stream.ReadExactly(buffer);
            // TODO
        }
        else if (type[0] == 6)  // ClientCutText
        {
            byte[] buffer = new byte[7];
            stream.ReadExactly(buffer);
            uint n_to_read = (uint)((buffer[3] << 24) | (buffer[4] << 16) | (buffer[5] << 8) | buffer[6]);
            byte[] temp = new byte[n_to_read];
            stream.ReadExactly(temp);
        }
        else
        {
            Log.DoLog($"Client message {type[0]} not understood", LogLevel.WARNING);
        }
    }

    private static void VNCSendFrame(NetworkStream stream, VNCServer vs)
    {
        var frame = vs.GetFrame();

        int width = vs._compatible ? compatible_width : frame.width;
        int height = vs._compatible ? compatible_height : frame.height;

        if (!vs._compatible)
        {
            byte[] resize = new byte[5];
            resize[0] = 15;  // ResizeFrameBuffer
            resize[1] = (byte)(width >> 8);  // width
            resize[2] = (byte)width;
            resize[3] = (byte)(height >> 8);  // height
            resize[4] = (byte)height;
            stream.Write(resize, 0, resize.Length);
        }

        byte[] update = new byte[4 + 12];
        update[0] = 0;  // FrameBufferUpdate
        update[1] = 0;  // padding
        update[2] = 0;  // 1 rectangle
        update[3] = 1;
        update[4] = 0;  // x pos
        update[5] = 0;
        update[6] = 0;  // y pos
        update[7] = 0;
        update[8] = (byte)(width >> 8);  // width
        update[9] = (byte)width;
        update[10] = (byte)(height >> 8);  // height
        update[11] = (byte)height;
        update[12] = 0;
        update[13] = 0;
        update[14] = 0;
        update[15] = 0;
        stream.Write(update, 0, update.Length);

        if (vs._compatible)
        {
            byte[] buffer = new byte[width * height * 4];
            for(int y=0; y<Math.Min(height, frame.height); y++)
            {
                int in_offset = y * frame.width * 3;
                int out_offset = y * width * 4;
                for(int x=0; x<Math.Min(width, frame.width); x++)
                {
                    int i_offset = in_offset + x * 3;
                    int o_offset = out_offset + x * 4;
                    buffer[o_offset + 3] = 255;
                    buffer[o_offset + 2] = frame.rgb_pixels[i_offset + 0];
                    buffer[o_offset + 1] = frame.rgb_pixels[i_offset + 1];
                    buffer[o_offset + 0] = frame.rgb_pixels[i_offset + 2];
                }
            }
            stream.Write(buffer, 0, buffer.Length);
        }
        else
        {
            byte[] buffer = new byte[frame.width * frame.height * 4];
            for(int i=0; i<frame.width * frame.height; i++)
            {
                int o_offset = i * 4;
                int i_offset = i * 3;
                buffer[o_offset + 3] = 255;
                buffer[o_offset + 2] = frame.rgb_pixels[i_offset + 0];
                buffer[o_offset + 1] = frame.rgb_pixels[i_offset + 1];
                buffer[o_offset + 0] = frame.rgb_pixels[i_offset + 2];
            }
            stream.Write(buffer, 0, buffer.Length);
        }
    }

    public static void VNCThread(object o_parameters)
    {
        VNCServerThreadParameters parameters = (VNCServerThreadParameters)o_parameters;
        TcpListener tcp_listener = new TcpListener(IPAddress.Parse("0.0.0.0"), parameters.port);
        tcp_listener.Start();
        Console.WriteLine($"VNC server started on port {parameters.port}");

        for(;;)
        {
            TcpClient client = tcp_listener.AcceptTcpClient();
            Console.WriteLine("Connected to VNC client");
            NetworkStream stream = client.GetStream();

            try
            {
                VNCSendVersion(stream);
                VNCSecurityHandshake(stream);
                VNCClientServerInit(stream, parameters.vs);

                Console.WriteLine("Starting graphics transmission");
                parameters.vs.Redraw();
                ulong version = 0;
                for(;;)
                {
                    ulong new_version = parameters.vs.GetFrameVersion();
                    if (new_version != version)
                    {
                        version = new_version;

                        VNCSendFrame(stream, parameters.vs);
                    }

                    VNCWaitForEvent(stream, parameters.vs);
                }
            }
            catch(SocketException e)
            {
                Log.DoLog($"VNCServer socket exception: {e.ToString()}", LogLevel.INFO);
            }
            catch(Exception e)
            {
                Log.DoLog($"VNCServer exception: {e.ToString()}", LogLevel.WARNING);
            }

            Console.WriteLine("VNC session ended", LogLevel.DEBUG);

            client.Close();
        }
    }
}
