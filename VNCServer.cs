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
    private static readonly System.Threading.Lock _stream_lock = new();

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

    private static void VNCSendVersion(NetworkStream stream)
    {
        byte[] msg = System.Text.Encoding.ASCII.GetBytes("RFB 003.008\n");
        stream.Write(msg, 0, msg.Length);

        // wait for reply, ignoring what it is
        byte[] buffer = new byte[1];
        buffer[0] = 0;
        while(buffer[0] != '\n')
        {
            if (stream.Read(buffer, 0, 1) == 0)
                return;
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
        stream.Read(buffer, 0, 1);

        byte[] reply = new byte[4];
        reply[3] = 0;  // OK
        stream.Write(reply, 0, reply.Length);
    }

    private static GraphicalFrame ResizeFrame(GraphicalFrame in_, int new_width, int new_height)
    {
        GraphicalFrame out_ = new();
        out_.width = new_width;
        out_.height = new_height;
        out_.rgb_pixels = new byte[new_width * new_height * 3];

        for(int y=0; y<Math.Min(new_height, in_.height); y++)
        {
            int in_offset = y * in_.width * 3;
            int out_offset = y * new_height * 3;
            for(int x=0; x<Math.Min(new_height, in_.height); x++)
            {
                int x_offset = x * 3;
                out_.rgb_pixels[out_offset + x_offset + 0] = in_.rgb_pixels[in_offset + x_offset + 0];
                out_.rgb_pixels[out_offset + x_offset + 1] = in_.rgb_pixels[in_offset + x_offset + 1];
                out_.rgb_pixels[out_offset + x_offset + 2] = in_.rgb_pixels[in_offset + x_offset + 2];
            }
        }

        return out_;
    }

    private static void VNCClientServerInit(NetworkStream stream, VNCServer vnc)
    {
        byte[] shared = new byte[1];
        stream.Read(shared, 0, 1);

        var example = vnc.GetFrame();
        if (vnc._compatible)
            example = ResizeFrame(example, 640, 400);
        byte[] reply = new byte[24];
        reply[0] = (byte)(example.width >> 8);
        reply[1] = (byte)(example.width & 255);
        reply[2] = (byte)(example.height >> 8);
        reply[3] = (byte)(example.height & 255);
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

    private static void VNCWaitForEvent(NetworkStream stream)
    {
        stream.ReadTimeout = 1000 / 15;  // 15 fps

        byte[] type = new byte[1];
        if (stream.Read(type, 0, 1) == 0)
            return;

        stream.ReadTimeout = 250;  // sane(?) timeout

        if (type[0] == 0)  // SetPixelFormat
        {
            byte[] temp = new byte[3 + 16];
            stream.Read(temp, 0, temp.Length);
        }
        else if (type[0] == 2)  // SetEncodings
        {
            byte[] temp = new byte[3];
            stream.Read(temp, 0, temp.Length);
            ushort no_encodings = (ushort)((temp[1] << 8) | temp[2]);
            byte[] encodings = new byte[no_encodings * 4];
            stream.Read(encodings, 0, encodings.Length);
        }
        else if (type[0] == 3)  // FramebufferUpdateRequest
        {
            byte[] buffer = new byte[5];
            stream.Read(buffer, 0, buffer.Length);
            // TODO
        }
        else if (type[0] == 4)  // KeyEvent
        {
            byte[] buffer = new byte[7];
            stream.Read(buffer, 0, buffer.Length);
            // TODO
        }
        else if (type[0] == 5)  // PointerEvent
        {
            byte[] buffer = new byte[5];
            stream.Read(buffer, 0, buffer.Length);
            // TODO
        }
        else if (type[0] == 6)  // ClientCutText
        {
            byte[] buffer = new byte[7];
            stream.Read(buffer, 0, buffer.Length);
            uint n_to_read = (uint)((buffer[3] << 24) | (buffer[4] << 16) | (buffer[5] << 8) | buffer[6]);
            byte[] temp = new byte[n_to_read];
            stream.Read(temp, 0, temp.Length);
        }
    }

    private static void VNCSendFrame(NetworkStream stream, VNCServer vs)
    {
        var frame = vs.GetFrame();

        if (!vs._compatible)
        {
            byte[] resize = new byte[5];
            resize[0] = 15;  // ResizeFrameBuffer
            resize[1] = (byte)(frame.width >> 8);  // width
            resize[2] = (byte)frame.width;
            resize[3] = (byte)(frame.height >> 8);  // height
            resize[4] = (byte)frame.height;
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
        update[8] = (byte)(frame.width >> 8);  // width
        update[9] = (byte)frame.width;
        update[10] = (byte)(frame.height >> 8);  // height
        update[11] = (byte)frame.height;
        update[12] = 0;
        update[13] = 0;
        update[14] = 0;
        update[15] = 0;
        stream.Write(update, 0, update.Length);
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

    public static void VNCThread(object o_parameters)
    {
        VNCServerThreadParameters parameters = (VNCServerThreadParameters)o_parameters;
        TcpListener tcp_listener = new TcpListener(IPAddress.Parse("0.0.0.0"), parameters.port);
        tcp_listener.Start();
        Console.WriteLine("VNC server started");

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

                for(;;)
                {
                    VNCWaitForEvent(stream);

                    VNCSendFrame(stream, parameters.vs);
                }
            }
            catch(SocketException e)
            {
                Console.WriteLine($"VNCServer socket exception: {e.ToString()}");
            }
            catch(Exception e)
            {
                Console.WriteLine($"VNCServer exception: {e.ToString()}");
            }

            client.Close();
        }
    }
}
