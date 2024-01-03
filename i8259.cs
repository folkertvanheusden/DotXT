// programmable interrupt controller (PIC)
class pic8259
{
    private int _int_offset = 0;  // TODO updaten bij ICW (OCW?) en dan XT::Tick() de juiste vector
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
        {
            Log.DoLog($"i8259 interrupt {interrupt_nr} out of range");
            return;
        }

        byte mask = Bit(interrupt_nr);

        _irr |= mask;

        if ((_imr & mask) == 0)
        {
            _isr |= mask;
            Log.DoLog($"i8259 interrupt {interrupt_nr} requested");
        }
        else
        {
            Log.DoLog($"i8259 interrupt {interrupt_nr} masked off ({_imr:X2})");
        }
    }

    public void ClearPendingInterrupt(int interrupt_nr)
    {
        if (interrupt_nr < _int_offset || interrupt_nr >= _int_offset + 8)
        {
            Log.DoLog($"i8259 interrupt {interrupt_nr} out of range");
            return;
        }

        Log.DoLog($"i8259 interrupt {interrupt_nr} cleared");

        byte bit = (byte)(255 ^ Bit(interrupt_nr));
        _isr &= bit;
        _irr &= bit;
    }

    public (byte, bool) In(ushort addr)
    {
        Log.DoLog($"i8259 IN: read addr {addr:X4}");

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
        Log.DoLog($"i8259 OUT port {addr:X2} value {value:X2}");

        if (addr == 0x0020)
        {
            _in_init = (value & 16) == 16;

            if (_in_init)  // ICW
            {
                _ii_icw2 = false;
                _ii_icw3 = false;
                _ii_icw4 = false;
                _ii_icw4_req = (value & 1) == 1;

                Log.DoLog($"i8259 OUT: is init (start ICW)");
            }
            else  // OCW 2/3
            {
                if ((value & 8) == 8)  // OCW3
                {
                    _read_irr = (value & 1) == 1;
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
                if (_ii_icw2 == false)
                {
                    Log.DoLog($"i8259 OUT: is ICW2");

                    _ii_icw2 = true;
                    if (value != 0x00 && value != 0x08)
                        Log.DoLog($"i8259 OUT: ICW2 assigned strange value: 0x{value:X2}");
                }
                else if (_ii_icw3 == false)
                {
                    Log.DoLog($"i8259 OUT: is ICW3");

                    _ii_icw3 = true;

                    // ignore value: slave-devices are not supported in this emulator

                    if (_ii_icw4_req == false)
                    {
                        _in_init = false;
                        Log.DoLog($"i8259 OUT: end of ICW");
                    }
                }
                else if (_ii_icw4 == false)
                {
                    Log.DoLog($"i8259 OUT: is ICW4");

                    _ii_icw4 = true;
                    _in_init = false;
                    _auto_eoi = (value & 2) == 2;
                }
            }
            else
            {
                Log.DoLog($"i8259 OUT: is OCW1, value {value:X2}");
                _imr = value;
            }
        }
        else
        {
            Log.DoLog($"i8259 OUT has no port {addr:X2}");
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
