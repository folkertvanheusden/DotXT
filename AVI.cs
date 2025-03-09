class AVI
{
    private Thread _thread = null;
    private FileStream _stream = null;
    private int _width = 0;
    private int _height = 0;
    private Display _d = null;

    public AVI(string filename, int fps, Display d)
    {
        _d = d;
        _width = d.GetWidth();
        _height = d.GetHeight();

        _stream = new FileStream(filename, FileMode.Create, FileAccess.Write);

        Write(RIFFHeader());

        byte[] main_avi_header = GenMainAVIHeader(fps, _width, _height);

        byte[] stream_header = GenStreamHeader(fps);
        byte[] stream_format = GenStreamFormat(_width, _height);
        byte[] stream_list = GenList(new char[] { 's', 't', 'r', 'l' }, stream_header.Concat(stream_format).ToArray());

        Write(GenList(new char[] { 'h', 'd', 'r', 'l' }, main_avi_header.Concat(stream_list).ToArray()));

        _thread = new Thread(AVI.AVIStreamer);
        _thread.Name = "avi-streamer-thread";
        _thread.Start(this);
    }

    public void Close()
    {
        _stream.Close();

        _thread.Join();
    }

    public static void AVIStreamer(object o)
    {
        AVI avi = (AVI)o;

        for(;;)
        {
            long start_ts = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            avi.PushFrame();

            long end_ts = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            Thread.Sleep((int)(1000 / 15 - (end_ts - start_ts)));  // 15 is hardcoded fps TODO
        }
    }

    private void Write(byte [] what)
    {
        _stream.Write(what, 0, what.Length);
    }

    public void PushFrame()
    {
        byte[] frame = EncodeFrame(_d.GetFrame());
        byte[] content_list = GenList(new char[] { 'm', 'o', 'v', 'i' }, frame);
        Write(content_list);
    }

    private byte[] EncodeFrame(GraphicalFrame g)
    {
        List<byte> @out = new();

        for(int y=g.height - 1; y >= 0; y--) {
            int in_o = y * g.width * 3;
            int run_count = 0;
            int prev_pv = 0x10000;
            for(int x=0; x<g.width; x++) {
                int in_o2 = in_o + x * 3;
                int pv = ((g.rgb_pixels[in_o2 + 0] << 8) & 0x1f000) | (g.rgb_pixels[in_o2 + 2] >> 3) | ((g.rgb_pixels[in_o2 + 2] << 5) & 0x7e0);

                if (x == 0)
                {
                    prev_pv = pv;
                    run_count = 1;
                }
                else if (prev_pv != pv || run_count == 255)
                {
                    @out.Add((byte)run_count);
                    @out.Add((byte)(prev_pv >> 8));
                    @out.Add((byte)prev_pv);

                    prev_pv = pv;
                    run_count = 1;
                }
                else
                {
                    run_count++;
                }
            }
            if (run_count > 0)
            {
                @out.Add((byte)run_count);
                @out.Add((byte)(prev_pv >> 8));
                @out.Add((byte)prev_pv);
            }
        }

        return GenChunk(new char[] { '0', '0', 'd', 'b' }, @out.ToArray());
    }

    private void PutWORD(ref byte[] to, int offset, uint what)
    {
        to[offset + 0] = (byte)(what);
        to[offset + 1] = (byte)(what >> 8);
    }

    private void PutDWORD(ref byte[] to, int offset, uint what)
    {
        to[offset + 0] = (byte)(what);
        to[offset + 1] = (byte)(what >> 8);
        to[offset + 2] = (byte)(what >> 16);
        to[offset + 3] = (byte)(what >> 24);
    }

    private byte[] RIFFHeader()
    {
        byte [] @out = new byte[12];
        @out[0] = (byte)'R';
        @out[1] = (byte)'I';
        @out[2] = (byte)'F';
        @out[3] = (byte)'F';
        @out[8] = (byte)'A';
        @out[9] = (byte)'V';
        @out[10] = (byte)'I';
        @out[11] = (byte)' ';
        return @out;
    }

    private byte[] GenChunk(char[] fourcc, byte[] payload)
    {
        byte [] @out = new byte[8 + payload.Length];
        @out[0] = (byte)fourcc[0];
        @out[1] = (byte)fourcc[1];
        @out[2] = (byte)fourcc[2];
        @out[3] = (byte)fourcc[3];
        PutDWORD(ref @out, 4, (uint)payload.Length);
        Array.Copy(payload, 0, @out, 8, payload.Length);
        return @out;
    }

    private byte[] GenList(char[] fourcc, byte[] payload)
    {
        byte [] @out = new byte[12 + (payload != null ? payload.Length : 0)];
        @out[0] = (byte)'L';
        @out[1] = (byte)'I';
        @out[2] = (byte)'S';
        @out[3] = (byte)'T';
        if (payload != null)
            PutDWORD(ref @out, 4, (uint)(payload.Length + 4));
        @out[8] = (byte)fourcc[0];
        @out[9] = (byte)fourcc[1];
        @out[10] = (byte)fourcc[2];
        @out[11] = (byte)fourcc[3];
        if (payload != null)
            Array.Copy(payload, 0, @out, 12, payload.Length);
        return @out;
    }

    private byte[] GenMainAVIHeader(int fps, int width, int height)
    {
        byte[] @out = new byte[14 * 4];
        PutDWORD(ref @out, 0, (uint)(1000000 / fps));
        PutDWORD(ref @out, 4, (uint)(fps * width * height * 3));  // bytes per second
        PutDWORD(ref @out, 8, 4);  // padding
        PutDWORD(ref @out, 12, 0);  // flags
        PutDWORD(ref @out, 16, 0);  // total number of frames
        PutDWORD(ref @out, 20, 0);  // initial frames
        PutDWORD(ref @out, 24, 1);  // number of streams
        PutDWORD(ref @out, 28, 0);  // suggested buffer size
        PutDWORD(ref @out, 32, (uint)width);
        PutDWORD(ref @out, 36, (uint)height);
        return GenChunk(new char[] { 'a', 'v', 'i', 'h' }, @out);
    }

    private byte[] GenStreamHeader(int fps)
    {
        byte[] @out = new byte[12 * 4 + 4 * 2];
        @out[0] = (byte)'v';  // type
        @out[1] = (byte)'i';
        @out[2] = (byte)'d';
        @out[3] = (byte)'s';
        @out[4] = (byte)'M';  // codec (16 bits MRLE)
        @out[5] = (byte)'R';
        @out[6] = (byte)'L';
        @out[7] = (byte)'E';
        PutDWORD(ref @out, 8, 0);  // flags
        PutWORD(ref @out, 12, 0);  // prio
        PutWORD(ref @out, 14, 0);  // language
        PutDWORD(ref @out, 16, 0);  // initial frames
        PutDWORD(ref @out, 20, 1);  // scale
        PutDWORD(ref @out, 24, (uint)fps);  // rate
        PutDWORD(ref @out, 28, 0);  // start
        PutDWORD(ref @out, 32, 0);  // length
        PutDWORD(ref @out, 36, 0);  // suggested buffer size
        PutDWORD(ref @out, 40, 0);  // quality
        PutDWORD(ref @out, 44, 4);  // sample size
        // RECT rcFrame
        return GenChunk(new char[] { 's', 't', 'r', 'h' }, @out);
    }

    private byte[] GenStreamFormat(int width, int height)
    {
        byte[] @out = new byte[44];
        PutDWORD(ref @out, 0, 44);  // structure size
        PutDWORD(ref @out, 4, (uint)width);
        PutDWORD(ref @out, 8, (uint)height);
        PutDWORD(ref @out, 12, 1);  // planes
        PutDWORD(ref @out, 14, 16);  // bits per pixel
        @out[16] = (byte)'M';  // codec (16 bits MRLE)
        @out[17] = (byte)'R';
        @out[18] = (byte)'L';
        @out[19] = (byte)'E';
        PutDWORD(ref @out, 20, 0);  // size of image
        PutDWORD(ref @out, 24, 1);  // pixels per meter, x
        PutDWORD(ref @out, 28, 1);  // pixels per meter, y
        PutDWORD(ref @out, 32, 0);
        PutDWORD(ref @out, 36, 0);

        return GenChunk(new char[] { 's', 't', 'r', 'f' }, @out);
    }
}
