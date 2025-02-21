internal class FlipFlop
{
    bool state = false;

    public bool get_state()
    {
        bool rc = state;
        state = !state;
        return rc;
    }

    public void reset()
    {
        state = false;
    }
}

internal class b16buffer
{
    ushort _value;
    FlipFlop _f;

    public b16buffer(FlipFlop f)
    {
        _f = f;
    }

    public void Put(byte v)
    {
        bool low_high = _f.get_state();

        if (low_high)
        {
            _value &= 0xff;
            _value |= (ushort)(v << 8);
        }
        else
        {
            _value &= 0xff00;
            _value |= v;
        }
    }

    public ushort GetValue()
    {
        return _value;
    }

    public void SetValue(ushort v)
    {
        _value = v;
    }

    public byte Get()
    {
        bool low_high = _f.get_state();

        if (low_high)
            return (byte)(_value >> 8);

        return (byte)(_value & 0xff);
    }
}

internal class i8237
{
    byte [] _channel_page = new byte[4];
    b16buffer [] _channel_address_register = new b16buffer[4];
    b16buffer [] _channel_word_count = new b16buffer[4];
    byte _command;
    bool [] _channel_mask = new bool[4];
    bool [] _reached_tc = new bool[4];
    byte [] _channel_mode = new byte[4];
    FlipFlop _ff = new();
    bool _dma_enabled = true;
    Bus _b;

    public i8237(Bus b)
    {
        for(int i=0; i<4; i++) {
            _channel_address_register[i] = new b16buffer(_ff);
            _channel_word_count[i] = new b16buffer(_ff);
            _reached_tc[i] = false;
        }

        _b = b;
    }

    public void TickChannel0()
    {
        // RAM refresh
        _channel_address_register[0].SetValue((ushort)(_channel_address_register[0].GetValue() + 1));

        ushort count = _channel_word_count[0].GetValue();
        count--;
        _channel_word_count[0].SetValue(count);

        if (count == 0xffff)
            _reached_tc[0] = true;
    }

    public (byte, bool) In(ushort addr)
    {
        byte v = 0;

        if (addr == 0 || addr == 2 || addr == 4 || addr == 6)
        {
            v = _channel_address_register[addr / 2].Get();
        }
        else if (addr == 1 || addr == 3 || addr == 5 || addr == 7)
        {
            v = _channel_word_count[addr / 2].Get();
        }
        else if (addr == 8)  // status register
        {
            Log.DoLog($"i8237_IN: read status register", true);

            for(int i=0; i<4; i++)
            {
                if (_reached_tc[i])
                {
                    _reached_tc[i] = false;
                    v |= (byte)(1 << i);
                }
            }
        }

        Log.DoLog($"i8237_IN: {addr:X4} {v:X2}", true);

        return (v, false);
    }

    void reset_masks(bool state)
    {
        for(int i=0; i<4; i++)
            _channel_mask[i] = state;
    }

    public bool Out(ushort addr, byte value)
    {
        Log.DoLog($"i8237_OUT: addr {addr:X4} value {value:X2}", true);

        if (addr == 0 || addr == 2 || addr == 4 || addr == 6)
        {
            _channel_address_register[addr / 2].Put(value);
            Log.DoLog($"i8237 set channel {addr / 2} to address {_channel_address_register[addr / 2].GetValue():X04}", true);
        }
        else if (addr == 1 || addr == 3 || addr == 5 || addr == 7)
        {
            _channel_word_count[addr / 2].Put(value);
            Log.DoLog($"i8237 set channel {addr / 2} to count {_channel_word_count[addr / 2].GetValue()}", true);
            _reached_tc[addr / 2] = false;
        }
        else if (addr == 8)
        {
            _command = value;
            _dma_enabled = (_command & 4) == 0;
        }
        else if (addr == 0x0a)  // mask
            _channel_mask[value & 3] = (value & 4) == 4;  // dreq enable/disable
        else if (addr == 0x0b)  // mode register
        {
            _channel_mode[value & 3] = value;
            string [] type = new string[] { "controller self test", "read transfer", "write transfer", "invalid" };
            string [] mode = new string[] { "on demand", "block", "single", "cascade" };
            for(int i=0; i<4; i++)
                _reached_tc[i] = false;
            string extra = "";
            if ((value & 16) == 16)
                extra += ", auto init";
            if ((value & 32) == 32)
                extra += ", decrement address";
            else
                extra += ", increment address";
            Log.DoLog($"i8237 mode register channel {value & 3}: {value:X02} {type[(value >> 2) & 3]}, {mode[(value >> 6) & 3]}{extra}", true);
        }
        else if (addr == 0x0c)  // reset flipflop
            _ff.reset();
        else if (addr == 0x0d)  // master reset
        {
            Log.DoLog($"i8237_IN: MASTER RESET", true);
            reset_masks(true);
            _ff.reset();
            for(int i=0; i<4; i++)
                _reached_tc[i] = false;
        }
        else if (addr == 0x0e)  // reset masks
        {
            reset_masks(false);
        }
        else if (addr == 0x0f)  // multiple mask
        {
            for(int i=0; i<4; i++)
                _channel_mask[i] = (value & (1 << i)) != 0;
        }
        else if (addr == 0x87)
        {
            _channel_page[0] = (byte)(value & 0x0f);
        }
        else if (addr == 0x83)
        {
            _channel_page[1] = (byte)(value & 0x0f);
        }
        else if (addr == 0x81)
        {
            _channel_page[2] = (byte)(value & 0x0f);
        }
        else if (addr == 0x82)
        {
            _channel_page[3] = (byte)(value & 0x0f);
        }

        return false;
    }

    // used by devices (floppy etc) to send data to memory
    public bool SendToChannel(int channel, byte value)
    {
        if (_dma_enabled == false)
        {
            Log.DoLog($"i8237 SendToChannel channel {channel} value {value:X2}: dma not enabled", true);
            return false;
        }

        if (_channel_mask[channel])
        {
            Log.DoLog($"i8237 SendToChannel channel {channel} value {value:X2}: channel masked", true);
            return false;
        }

        if (_reached_tc[channel])
        {
            Log.DoLog($"i8237 SendToChannel channel {channel} value {value:X2}: reached tc, channel address {_channel_address_register[channel].GetValue():X04}", true);
            return false;
        }

        ushort addr = _channel_address_register[channel].GetValue();
        uint full_addr = (uint)((_channel_page[channel] << 16) | addr);
        addr++;
        _channel_address_register[channel].SetValue(addr);

        _b.WriteByte(full_addr, value);

        ushort count = _channel_word_count[channel].GetValue();
        count--;
        if (count == 0xffff)
        {
            Log.DoLog($"i8237 SendToChannel channel {channel} count has reached -1, set tc, address {full_addr:X06}", true);
            _reached_tc[channel] = true;
        }

        _channel_word_count[channel].SetValue(count);

        return true;
    }
}
