// programmable interrupt controller (PIC)
class pic8259
{
    private bool _init = false;
    private byte _init_data = 0;
    private byte _ICW1, _ICW2, _ICW3, _ICW4;
    private bool _is_ocw = false;
    private int _ocw_nr = 0;
    private byte _OCW1, _OCW2, _OCW3;

    private int _int_offset = 8;
    private byte _interrupt_mask = 0xff;

    private byte [] _register_cache = new byte[2];

    private byte _pending_interrupts = 0;

    public pic8259()
    {
    }

    private byte GetPendingInterrupts()
    {
        return _pending_interrupts;
    }

    public void SetPendingInterrupt(int interrupt_nr)
    {
        if (interrupt_nr < _int_offset || interrupt_nr >= _int_offset + 8)
            return;

        _pending_interrupts |= (byte)(1 << (interrupt_nr - _int_offset));
    }

    public void ClearPendingInterrupt(int interrupt_nr)
    {
        if (interrupt_nr < _int_offset || interrupt_nr >= _int_offset + 8)
            return;

        _pending_interrupts &= (byte)(255 ^ (byte)(1 << (interrupt_nr - _int_offset)));
    }

    public (byte, bool) In(ushort addr)
    {
        if (_is_ocw)
        {
#if DEBUG
            Log.DoLog($"8259 IN: is ocw {_ocw_nr}, read nr {_ocw_nr}");
#endif

            if (_ocw_nr == 0)
            {
                _ocw_nr++;
                return (_OCW1, false);
            }
            else if (_ocw_nr == 1)
            {
                _ocw_nr++;
                return (_OCW2, false);
            }
            else if (_ocw_nr == 2)
            {
                _ocw_nr++;
                return (GetPendingInterrupts(), false);
            }
            else
            {
                Log.DoLog($"8259 IN: OCW nr is {_ocw_nr}");
                _is_ocw = false;
            }
        }

        Log.DoLog($"8259 IN: read cache for addr {addr:X4}");

        return (_register_cache[addr - 0x0020], false);
    }

    public void Tick()
    {
    }

    public bool Out(ushort addr, byte value)
    {
        Log.DoLog($"8259 OUT port {addr} value {value:X2}");

        _register_cache[addr - 0x0020] = value;

        if (addr == 0x0020)
        {
            if ((value & 128) == 0)
            {
                _init = (value & 16) == 16;

                Log.DoLog($"8259 OUT: is init: {_init}");
            }
            else
            {
                Log.DoLog($"8259 OUT: is OCW, value {value:X2}");

                _is_ocw = true;
                _OCW1 = value;
                _ocw_nr = 0;
            }
        }
        else if (addr == 0x0021)
        {
            if (_init)
            {
                Log.DoLog($"8259 OUT: is ICW, value {value:X2}, {_init_data:X}");

                if ((_init_data & 16) == 16)  // waiting for ICW2
                {
                    _ICW2 = value;

                    _init_data = (byte)(_init_data & ~16);
                }
                else if ((_init_data & 2) == 2)  // waiting for ICW3
                {
                    _ICW3 = value;

                    _init_data = (byte)(_init_data & ~2);
                }
                else if ((_init_data & 1) == 1)  // waiting for ICW4
                {
                    _ICW4 = value;

                    _init_data = (byte)(_init_data & ~1);
                }
                else
                {
                    _init = false;
                }
            }
            else if (_is_ocw)
            {
                Log.DoLog($"8259 OUT: is OCW, value {value:X2}, {_ocw_nr}");

                if (_ocw_nr == 0)
                    _OCW2 = value;
                else if (_ocw_nr == 1)
                    _OCW3 = value;
                else
                {
                    Log.DoLog($"8259 OCW OUT nr is {_ocw_nr}");
                    _is_ocw = false;
                }

                _ocw_nr++;
            }
        }
        else
        {
            Log.DoLog($"8259 OUT has no port {addr:X2}");
        }

        // when reconfiguring the PIC8259, force an interrupt recheck
        return true;
    }

    public int GetInterruptOffset()
    {
        return _int_offset;
    }

    public byte GetInterruptMask()
    {
        return _register_cache[1];
    }
}
