using DotXT;

class State8086
{
    public byte ah { get; set; }
    public byte al { get; set; }
    public byte bh { get; set; }
    public byte bl { get; set; }
    public byte ch { get; set; }
    public byte cl { get; set; }
    public byte dh { get; set; }
    public byte dl { get; set; }

    public ushort si { get; set; }
    public ushort di { get; set; }
    public ushort bp { get; set; }
    public ushort sp { get; set; }

    public ushort ip { get; set; }

    public ushort cs { get; set; }
    public ushort ds { get; set; }
    public ushort es { get; set; }
    public ushort ss { get; set; }

    // replace by an Optional-type when available
    public ushort segment_override { get; set; }
    public bool segment_override_set { get; set; }

    public ushort flags { get; set; }

    public bool in_hlt { get; set; }
    public bool inhibit_interrupts { get; set; }  // for 1 instruction after loading segment registers

    public bool rep { get; set; }
    public bool rep_do_nothing { get; set; }
    public RepMode rep_mode { get; set; }
    public ushort rep_addr { get; set; }
    public byte rep_opcode { get; set; }

    public long clock { get; set; }

    public int crash_counter { get; set; }

    public long GetClock()
    {
        return clock;
    }

    public void SetIP(ushort cs_in, ushort ip_in)
    {
        Log.DoLog($"Set CS/IP to {cs:X4}:{ip:X4}", LogLevel.DEBUG);

        cs = cs_in;
        ip = ip_in;
    }

    public void FixFlags()
    {
        flags &= 0b1111111111010101;
        flags |= 2;  // bit 1 is always set
        flags |= 0xf000;  // upper 4 bits are always 1
    }

    public ushort GetAX()
    {
        return (ushort)((ah << 8) | al);
    }

    public void SetAX(ushort v)
    {
        ah = (byte)(v >> 8);
        al = (byte)v;
    }

    public ushort GetBX()
    {
        return (ushort)((bh << 8) | bl);
    }

    public void SetBX(ushort v)
    {
        bh = (byte)(v >> 8);
        bl = (byte)v;
    }

    public ushort GetCX()
    {
        return (ushort)((ch << 8) | cl);
    }

    public void SetCX(ushort v)
    {
        ch = (byte)(v >> 8);
        cl = (byte)v;
    }

    public ushort GetDX()
    {
        return (ushort)((dh << 8) | dl);
    }

    public void SetDX(ushort v)
    {
        dh = (byte)(v >> 8);
        dl = (byte)v;
    }

    public void SetSS(ushort v)
    {
        ss = v;
    }

    public void SetCS(ushort v)
    {
        cs = v;
    }

    public void SetDS(ushort v)
    {
        ds = v;
    }

    public void SetES(ushort v)
    {
        es = v;
    }

    public void SetSP(ushort v)
    {
        sp = v;
    }

    public void SetBP(ushort v)
    {
        bp = v;
    }

    public void SetSI(ushort v)
    {
        si = v;
    }

    public void SetDI(ushort v)
    {
        di = v;
    }

    public void SetIP(ushort v)
    {
        ip = v;
    }

    public void SetFlags(ushort v)
    {
        flags = v;
    }

    public ushort GetSS()
    {
        return ss;
    }

    public ushort GetCS()
    {
        return cs;
    }

    public ushort GetDS()
    {
        return ds;
    }

    public ushort GetES()
    {
        return es;
    }

    public ushort GetSP()
    {
        return sp;
    }

    public ushort GetBP()
    {
        return bp;
    }

    public ushort GetSI()
    {
        return si;
    }

    public ushort GetDI()
    {
        return di;
    }

    public ushort GetIP()
    {
        return ip;
    }

    public ushort GetFlags()
    {
        return flags;
    }

    public void SetZSPFlags(byte v)
    {
        SetFlagZ(v == 0);
        SetFlagS((v & 0x80) == 0x80);
        SetFlagP(v);
    }

    public void ClearFlagBit(int bit)
    {
        flags &= (ushort)(ushort.MaxValue ^ (1 << bit));
    }

    public void SetFlagBit(int bit)
    {
        flags |= (ushort)(1 << bit);
    }

    public void SetFlag(int bit, bool state)
    {
        if (state)
            SetFlagBit(bit);
        else
            ClearFlagBit(bit);
    }

    public bool GetFlag(int bit)
    {
        return (flags & (1 << bit)) != 0;
    }

    public void SetFlagC(bool state)
    {
        SetFlag(0, state);
    }

    public bool GetFlagC()
    {
        return GetFlag(0);
    }

    public void SetFlagP(byte v)
    {
        int count = 0;

        while (v != 0)
        {
            count++;

            v &= (byte)(v - 1);
        }

        SetFlag(2, (count & 1) == 0);
    }

    public bool GetFlagP()
    {
        return GetFlag(2);
    }

    public void SetFlagA(bool state)
    {
        SetFlag(4, state);
    }

    public bool GetFlagA()
    {
        return GetFlag(4);
    }

    public void SetFlagZ(bool state)
    {
        SetFlag(6, state);
    }

    public bool GetFlagZ()
    {
        return GetFlag(6);
    }

    public void SetFlagS(bool state)
    {
        SetFlag(7, state);
    }

    public bool GetFlagS()
    {
        return GetFlag(7);
    }

    public void SetFlagT(bool state)
    {
        SetFlag(8, state);
    }

    public bool GetFlagT()
    {
        return GetFlag(8);
    }

    public void SetFlagI(bool state)
    {
        SetFlag(9, state);
    }

    public bool GetFlagI()
    {
        return GetFlag(9);
    }

    public void SetFlagD(bool state)
    {
        SetFlag(10, state);
    }

    public bool GetFlagD()
    {
        return GetFlag(10);
    }

    public void SetFlagO(bool state)
    {
        SetFlag(11, state);
    }

    public bool GetFlagO()
    {
        return GetFlag(11);
    }
};
