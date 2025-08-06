class Hercules : MDA
{
    private bool _graphics_mode = false;
    private int _read_3ba_count = 0;

    public Hercules(List<EmulatorConsole> consoles) : base(consoles)
    {
        _ram = new byte[65536];
        _gf.rgb_pixels = null;
        _gf.width = 720;
        _gf.height = 350;
        _gf.rgb_pixels = new byte[_gf.width * _gf.height * 3];
    }

    public override String GetName()
    {
        return "Hercules";
    }

    public override bool IsInHSync()
    {
        int pixel = (int)(_clock % 304);
        return pixel < 2 || pixel > 302;  // FIXME
    }

    public override bool IsInVSync()
    {
        int scan_line = GetCurrentScanLine();
        return scan_line < 16 || scan_line >= 216;  // FIXME
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        Log.DoLog("Hercules::RegisterDevice", LogLevel.DEBUG);

        for(ushort port=0x3b0; port<0x3bc; port++)
            mappings[port] = this;
        mappings[0x3bf] = this;
    }

    public override List<Tuple<uint, int> > GetAddressList()
    {
        return new() { new(0xb0000, 0x10000) };
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"Hercules::IO_Write {port:X4} {value:X4}", LogLevel.DEBUG);

        if (port == 0x3bf)
            _graphics_mode = (value & 1) == 1;

        return base.IO_Write(port, value);
    }

    public override byte IO_Read(ushort port)
    {
        Log.DoLog($"Hercules::IO_Read {port:X4}", LogLevel.DEBUG);

        byte rc = base.IO_Read(port);

        if (port == 0x03ba)
        {
            rc &= 0x0f;

            if (++_read_3ba_count >= 31000)
            {
                _read_3ba_count = 0;
                rc |= 0x80;
            }
        }

        return rc;
    }

    public override void WriteByte(uint offset, byte value)
    {
        uint use_offset = (offset - 0xb0000) & 0xffff;
        _ram[use_offset] = value;
        DrawOnConsole(use_offset);
        _gf_version++;
    }

    public override void DrawOnConsole(uint offset)
    {
        if (_graphics_mode)
        {
            offset -= _display_address;
            uint bank = offset / 0x2000;
            uint byte_in_bank = offset % 0x2000;
            uint y_in_bank = byte_in_bank / 90;
            uint y = y_in_bank * 4 + bank;
            uint x_byte = byte_in_bank % 90;
            uint x = x_byte * 8;

            if (y < 348 && x < 720) {
                uint pixel_offset = y * 720 * 3;
                for(int x_use=0; x_use<8; x_use++)
                {
                    byte color = (byte)((_ram[offset] & (1 << (7 - x_use))) != 0 ? 255 : 0);
                    _gf.rgb_pixels[pixel_offset + (x_use + x) * 3 + 0] = color;
                    _gf.rgb_pixels[pixel_offset + (x_use + x) * 3 + 1] = color;
                    _gf.rgb_pixels[pixel_offset + (x_use + x) * 3 + 2] = color;
                }
            }
        }
        else
        {
            offset -= _display_address;
            if (offset >= 80 * 25 * 2)
                return;
            uint y = offset / (80 * 2);
            uint x = (offset % (80 * 2)) / 2;

            uint mask = uint.MaxValue - 1;
            uint char_base_offset = offset & mask;

            byte character = _ram[char_base_offset + 0];
            byte attributes = _ram[char_base_offset + 1];

            EmulateTextDisplay(x, y, character, attributes);

            if (_gf.rgb_pixels != null)
            {
                int char_offset = character * font_descr.height;
                int fg = attributes & 15;
                int bg = (attributes >> 4) & 7;
                for(int yo=0; yo<font_descr.height; yo++)
                {
                    int y_pixel_offset = ((int)y * font_descr.height + yo) * _gf.width * 3;
                    byte line = font_descr.pixels[char_offset + yo];
                    byte bit_mask = 128;
                    for(int xo=0; xo<8; xo++)
                    {
                        int x_pixel_offset = y_pixel_offset + ((int)x * 8 + xo) * 3;
                        bool is_fg = (line & bit_mask) != 0;
                        bit_mask >>= 1;
                        if (is_fg)
                        {
                            _gf.rgb_pixels[x_pixel_offset + 0] = palette[fg][0];
                            _gf.rgb_pixels[x_pixel_offset + 1] = palette[fg][1];
                            _gf.rgb_pixels[x_pixel_offset + 2] = palette[fg][2];
                        }
                        else
                        {
                            _gf.rgb_pixels[x_pixel_offset + 0] = palette[bg][0];
                            _gf.rgb_pixels[x_pixel_offset + 1] = palette[bg][1];
                            _gf.rgb_pixels[x_pixel_offset + 2] = palette[bg][2];
                        }
                    }
                }
            }
        }
    }

    public override void Redraw()
    {
        if (_graphics_mode)
        {
            for(uint i=0; i<90 * 348; i++)
                DrawOnConsole(i);
        }
        else
        {
            for(uint i=0; i<80 * 25 * 2; i += 2)
                DrawOnConsole(i);
        }
    }

    public override byte ReadByte(uint offset)
    {
        return _ram[(offset - 0xb0000) & 0xffff];
    }

    public override int GetCurrentScanLine()
    {
        // 304 cpu cycles per scan line  <-- from CGA, TODO
        // 262 scan lines
        return (int)((_clock / 304) % 262);
    }

    public override bool Ticks()
    {
        return true;
    }

    public override bool Tick(int cycles, long clock)
    {
        _clock = clock;

        int line = GetCurrentScanLine();
        if (line == 200)
            PublishVSync();

        return base.Tick(cycles, clock);
    }
}
