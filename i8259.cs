// programmable interrupt controller (PIC)
class pic8259
{
    private int _int_offset = 8;  // TODO updaten bij ICW (OCW?) en dan XT::Tick() de juiste vector
    private byte _irr = 0;  // which irqs are requested
    private byte _isr = 0;  // ...and which are allowed (see imr)
    private byte _imr = 255;  // all irqs masked (disabled)
    private bool _auto_eoi = false;
    private byte _eoi_type = 0;
    private byte _eoi_mask = 0;  // which bits to ignore as EOI was not done
    private int _irq_request_level = 7;  // default value? TODO
    private bool _read_irr = false;
    private bool _has_slave = false;
    private int _int_in_service = -1;  // used by EOI

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
        byte pending_ints = (byte)(_irr & (255 ^ _imr));
        byte temp = pending_ints;

        // any previous interrupts not acked via (auto-)EOI yet?
        // them mask them off
        pending_ints &= (byte)(~_eoi_mask);

	if (temp != 0)
		Log.DoLog($"i8259 pending interrupts: {temp:X2}, after EOI-masking ({_eoi_mask:X2}): {pending_ints:X2}");

        return pending_ints;
    }

    public void RequestInterruptPIC(int interrupt_nr)
    {
        byte mask = (byte)(1 << interrupt_nr);
        _irr |= mask;

        Log.DoLog($"i8259 interrupt {interrupt_nr} requested, pending interrupts: {GetPendingInterrupts()}, mask: {GetInterruptMask():x2}");
    }

    public void ClearPendingInterrupt(int interrupt_nr)
    {
        Log.DoLog($"i8259 interrupt {interrupt_nr} cleared");

        byte bit = (byte)(255 ^ (1 << interrupt_nr));
        _isr &= bit;
        _irr &= bit;
    }

    public void SetIRQBeingServiced(int interrupt_nr)
    {
        if (_int_in_service != -1)
            Log.DoLog($"i8259: interrupt {_int_in_service} was not acked before {interrupt_nr} went in service");

        _int_in_service = interrupt_nr - _int_offset;
        _eoi_mask |= (byte)(1 << interrupt_nr);

        Log.DoLog($"i8259: EOI mask is now {_eoi_mask:X2} by {interrupt_nr}");
    }

    public (byte, bool) In(ushort addr)
    {
        byte rc = 0;

        if (addr == 0x0020)
        {
            if (_read_irr)
                rc = _irr;
            else
                rc = _isr;
        }
        else if (addr == 0x0021)
        {
            rc = _imr;
        }

        Log.DoLog($"i8259 IN: read addr {addr:X4}: {rc:X2}");

        return (rc, false);
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

            _has_slave = (value & 2) == 0;

            if (_in_init)  // ICW
            {
                Log.DoLog($"i8259 OUT is init (start ICW)");

                _ii_icw2 = false;
                _ii_icw3 = false;
                _ii_icw4 = false;
                _ii_icw4_req = (value & 1) == 1;

                if (_int_in_service != -1)
                    Log.DoLog($"i8259 implicit EOI of {_int_in_service}");

                _isr = 0;
                _imr = 0;  // TODO 255?

                _eoi_mask = 0;
                _int_in_service  = -1;
            }
            else  // OCW 2/3
            {
                if ((value & 8) == 8)  // OCW3
                {
                    Log.DoLog($"i8259 OUT: OCW3");
                    _read_irr = (value & 1) == 1;
                }
                else  // OCW2
                {
                    Log.DoLog($"i8259 OUT: OCW2");
                    _irq_request_level = value & 7;

                    if (((value >> 5) & 1) == 1)  // EOI set (in OCW2)?
                    {
                        Log.DoLog($"i8259 EOI of {_int_in_service}, level: {_irq_request_level}");

                        if (_int_in_service == -1)
                            Log.DoLog($"i8259 EOI with no int in service?");
                        else
                        {
                            _eoi_mask &= (byte)~(1 << _int_in_service);
                            _int_in_service  = -1;
                        }
                    }
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
                    _int_offset = value;
                }
                else if (_ii_icw3 == false && _has_slave)
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
