// programmable interrupt controller (PIC)
class pic8259
{
    private int _int_offset = 8;  // TODO updaten bij ICW (OCW?) en dan XT::Tick() de juiste vector
    private byte _irr = 0;  // which irqs are requested
    private byte _isr = 0;  // ...and which are in service
    private byte _imr = 255;  // all irqs masked (disabled)
    private bool _auto_eoi = false;
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

    public byte GetPendingInterrupt()
    {
        if (_irr == 0 || _int_in_service != -1)
            return 255;

        for(byte i=0; i<8; i++)
        {
            byte mask = (byte)(1 << i);
            if ((_irr & mask) == mask /* requested? */ && (_isr & mask) == 0 /* not in service? */ && (_imr & mask) == 0 /* not masked off? */)
            {
                Log.DoLog($"i8259 pending interrupt: {i:X2}, (irr: {_irr:X2}, isr: {_isr:X2}, imr: {_imr:X2})", true);
                return i;
            }
        }

        // can/should not happen
        return 255;
    }

    public int GetInterruptLevel()
    {
        return _irq_request_level;
    }

    public void RequestInterruptPIC(int interrupt_nr)
    {
        byte mask = (byte)(1 << interrupt_nr);
        _irr |= mask;

        Log.DoLog($"i8259 interrupt {interrupt_nr} requested (irr: {_irr:X2}, imr: {_imr:X2})", true);
    }

    public void SetIRQBeingServiced(int interrupt_nr)
    {
        if (_int_in_service != -1)
            Log.DoLog($"i8259: interrupt {_int_in_service} was not acked before {interrupt_nr} went in service", true);

        _int_in_service = interrupt_nr;
        byte mask = (byte)(1 << interrupt_nr);
        if ((_irr & mask) == 0)
            Log.DoLog($"i8259: interrupt {interrupt_nr} was not requested", true);
        if ((_isr & mask) == mask)
            Log.DoLog($"i8259: interrupt {interrupt_nr} was already in service", true);
        if ((_imr & mask) == mask)
            Log.DoLog($"i8259: interrupt {interrupt_nr} was masked off", true);
        _isr |= mask;

        //Log.DoLog($"i8259: EOI mask is now {_isr:X2} by setting {interrupt_nr} in service", true);
    }

    public (byte, bool) In(ushort addr)
    {
        byte rc = 0;

        if (addr == 0x0020)
        {
            Log.DoLog($"i8259 IN: read status register IRR: {_read_irr} (irr: {_irr:X2}, isr: {_isr:X2})", true);
            if (_read_irr)
                rc = _irr;
            else
                rc = _isr;
        }
        else if (addr == 0x0021)
        {
            rc = _imr;
        }

        Log.DoLog($"i8259 IN: read addr {addr:X4}: {rc:X2}", true);

        return (rc, false);
    }

    public bool Out(ushort addr, byte value)
    {
        Log.DoLog($"i8259 OUT port {addr:X2} value {value:X2}", true);

        if (addr == 0x0020)
        {
            _in_init = (value & 16) == 16;

            _has_slave = (value & 2) == 0;

            if (_in_init)  // ICW
            {
                Log.DoLog($"i8259 OUT is init (start ICW)", true);

                _ii_icw2 = false;
                _ii_icw3 = false;
                _ii_icw4 = false;
                _ii_icw4_req = (value & 1) == 1;

                if (_int_in_service != -1)
                    Log.DoLog($"i8259 implicit EOI of {_int_in_service}", true);

                _imr = 0;  // TODO 255?
                _isr = 0;
                _irr = 0;

                _int_in_service  = -1;
            }
            else  // OCW 2/3
            {
                if ((value & 8) == 8)  // OCW3
                {
                    Log.DoLog($"i8259 OUT: OCW3", true);
                    _read_irr = (value & 3) == 2;
                }
                else  // OCW2
                {
                    Log.DoLog($"i8259 OUT: OCW2", true);
                    _irq_request_level = value & 7;

                    // EOI
                    if (((value >> 5) & 1) == 1)  // EOI set (in OCW2)?
                    {
                        if ((value & 0x60) == 0x60)  // ack a certain level
                        {
                            int i = value & 7;
                            Log.DoLog($"i8259 EOI of {i}, level: {_irq_request_level}", true);

                            byte mask = (byte)~(1 << i);
                            _irr &= mask;
                            _isr &= mask;
                            if (i == _int_in_service)
                                _int_in_service = -1;
                        }
                        else
                        {
                            Log.DoLog($"i8259 EOI of {_int_in_service}, level: {_irq_request_level}", true);

                            if (_int_in_service == -1)
                                Log.DoLog($"i8259 EOI with no int in service?", true);
                            else
                            {
                                byte mask = (byte)~(1 << _int_in_service);
                                _irr &= mask;
                                _isr &= mask;
                                _int_in_service = -1;
                            }
                        }
                    }

                    Log.DoLog($"i8259 set level to: {_irq_request_level}", true);
                }
            }
        }
        else if (addr == 0x0021)
        {
            if (_in_init)
            {
                if (_ii_icw2 == false)
                {
                    Log.DoLog($"i8259 OUT: is ICW2", true);

                    _ii_icw2 = true;
                    if (value != 0x00 && value != 0x08)
                        Log.DoLog($"i8259 OUT: ICW2 assigned strange value: 0x{value:X2}", true);
                    _int_offset = value;
                }
                else if (_ii_icw3 == false && _has_slave)
                {
                    Log.DoLog($"i8259 OUT: is ICW3", true);

                    _ii_icw3 = true;

                    // ignore value: slave-devices are not supported in this emulator

                    if (_ii_icw4_req == false)
                    {
                        _in_init = false;
                        Log.DoLog($"i8259 OUT: end of ICW", true);
                    }
                }
                else if (_ii_icw4 == false)
                {
                    Log.DoLog($"i8259 OUT: is ICW4", true);

                    _ii_icw4 = true;
                    _in_init = false;
                    bool new_auto_eoi = (value & 2) == 2;
                    if (new_auto_eoi != _auto_eoi)
                    {
                        Log.DoLog($"i8259 OUT: _auto_eoi is now {new_auto_eoi}", true);
                        _auto_eoi = new_auto_eoi;
                    }
                }
            }
            else
            {
                Log.DoLog($"i8259 OUT: is OCW1, value {value:X2}", true);
                _imr = value;
            }
        }
        else
        {
            Log.DoLog($"i8259 OUT has no port {addr:X2}", true);
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
