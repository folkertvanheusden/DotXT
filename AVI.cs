class AVI
{
    FileStream stream = null;

    public AVI(string filename, int fps, int width, int height)
    {
        stream = new FileStream(filename, FileMode.Create, FileAccess.Write);

        Write(RIFFHeader());

        byte[] main_avi_header = GenMainAVIHeader(fps, width, height);

        byte[] stream_header = GenStreamHeader(fps);
        byte[] stream_format = GenStreamFormat(width, height);
        byte[] stream_list = GenList(new char[] { 's', 't', 'r', 'l' }, stream_header.Concat(stream_format).ToArray());

        Write(GenList(new char[] { 'h', 'd', 'r', 'l' }, main_avi_header.Concat(stream_list).ToArray()));

        for(int i=0; i<100; i++)
        {
            byte[] frame = GenerateFakeFrame(width, height, i);
            byte[] content_list = GenList(new char[] { 'm', 'o', 'v', 'i' }, frame);
            Write(content_list);
        }
    }

    public void Close()
    {
        stream.Close();
    }

    private void Write(byte [] what)
    {
        stream.Write(what, 0, what.Length);
    }

    private byte[] GenerateFakeFrame(int width, int height, int offset)
    {
        int n = width * height * 3;
        byte[] @out = new byte[n];
        for(int i=0; i<n; i++)
            @out[i] = (byte)(i + offset);

        return GenChunk(new char[] { '0', '0', 'd', 'b' }, @out);
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
        @out[4] = (byte)'R';  // codec (24b RGB)
        @out[5] = (byte)'G';
        @out[6] = (byte)'B';
        @out[7] = (byte)' ';
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
        PutDWORD(ref @out, 14, 24);  // bits per pixel
        PutDWORD(ref @out, 16, 0);  // BI_RGB == 0x0000, 
        PutDWORD(ref @out, 20, 0);  // size of image
        PutDWORD(ref @out, 24, 1);  // pixels per meter, x
        PutDWORD(ref @out, 28, 1);  // pixels per meter, y
        PutDWORD(ref @out, 32, 0);
        PutDWORD(ref @out, 36, 0);

        return GenChunk(new char[] { 's', 't', 'r', 'f' }, @out);
    }
}
