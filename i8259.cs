// programmable interrupt controller (PIC)
class pic8259
{
    private int _int_offset = 8;
    private byte _irr = 0;  // which irqs are requested
    private byte _isr = 0;  // ...and which are allowed (see imr)
    private byte _imr = 255;  // all irqs masked (disabled)
    private bool _auto_eoi = false;
    private byte _eoi_type = 0;
    private int _irq_request_level = 7;  // default value? TODO
    private bool _read_irr = false;

    private bool _in_init = false;
    private bool _ii_icw2 = false;
    private bool _ii_icw3 = false;
    private bool _ii_icw4 = false;
    private bool _ii_icw4_req = false;

    public pic8259()
    {
    }

    public byte GetPendingInterrupts()
    {
        return _isr;
    }

    public byte Bit(int interrupt_nr)
    {
        return (byte)(1 << (interrupt_nr - _int_offset));
    }

    public void RequestInterrupt(int interrupt_nr)
    {
        if (interrupt_nr < _int_offset || interrupt_nr >= _int_offset + 8)
            return;

        byte bit = Bit(interrupt_nr);

        _irr |= bit;

        if ((_imr & bit) == bit)
            _isr |= bit;
    }

    public void ClearPendingInterrupt(int interrupt_nr)
    {
        if (interrupt_nr < _int_offset || interrupt_nr >= _int_offset + 8)
            return;

        byte bit = (byte)(255 ^ Bit(interrupt_nr));
        _isr &= bit;
        _irr &= bit;
    }

    public (byte, bool) In(ushort addr)
    {
        Log.DoLog($"8259 IN: read addr {addr:X4}");

        if (addr == 0x0020)
        {
            if (_read_irr)
                return (_irr, false);

            return (_isr, false);
        }
        else if (addr == 0x0021)
        {
            return (_imr, false);
        }

        return (0, false);
    }

    public void Tick()
    {
    }

    public bool Out(ushort addr, byte value)
    {
        Log.DoLog($"8259 OUT port {addr} value {value:X2}");

        if (addr == 0x0020)
        {
            _in_init = (value & 16) == 16;

            if (_in_init)  // ICW
            {
                _ii_icw2 = false;
                _ii_icw3 = false;
                _ii_icw4 = false;
                _ii_icw4_req = (value & 1) == 1;

                Log.DoLog($"8259 OUT: is init");
            }
            else  // OCW 2/3
            {
                if ((value & 8) == 8)  // OCW3
                {
                }
                else  // OCW2
                {
                    _irq_request_level = value & 7;
                    _eoi_type = (byte)(value >> 5);
                }
            }
        }
        else if (addr == 0x0021)
        {
            if (_in_init)
            {
                Log.DoLog($"8259 OUT: is ICW");

                if (_ii_icw2 == false)
                {
                    _ii_icw2 = true;
                    if (value != 0)
                        Log.DoLog($"8259 OUT: ICW2 should be 0x00, not 0x{value:X2}");
                }
                else if (_ii_icw3 == false)
                {
                    _ii_icw3 = true;
                    if (value != 0)  // slaves are not supported in this emulator
                        Log.DoLog($"8259 OUT: ICW3 should be 0x00, not 0x{value:X2}");

                    _read_irr = (value & 1) == 1;

                    if (_ii_icw4_req == false)
                        _in_init = false;
                }
                else if (_ii_icw4 == false)
                {
                    _ii_icw4 = true;
                    _in_init = false;
                    _auto_eoi = (value & 2) == 2;
                }
            }
            else
            {
                Log.DoLog($"8259 OUT: is OCW1, value {value:X2}");
                _imr = value;
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
        return _imr;
    }
}
