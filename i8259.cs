// programmable interrupt controller (PIC)
class i8259: Device
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

    private byte _icw1 = 0;
    private bool _in_init = false;
    private bool _ii_icw2 = false;
    private byte _icw2 = 0;
    private bool _ii_icw3 = false;
    private byte _icw3 = 0;
    private bool _ii_icw4 = false;
    private byte _icw4 = 0;
    private bool _ii_icw4_req = false;
    private byte _ocw2 = 0;
    private byte _ocw3 = 0;

    public i8259()
    {
    }

    public override int GetIRQNumber()
    {
        return -1;
    }

    public override String GetName()
    {
        return "i8259";
    }

    public override List<string> GetState()
    {
        List<string> @out = new();
        @out.Add($"IRR: {_irr:X2}, ISR: {_isr:X2}, IMR: {_imr:X2}, int in serice: {_int_in_service}");
        @out.Add($"auto eoi: {_auto_eoi}, request level: {_irq_request_level}");
        @out.Add($"read irr: {_read_irr}");
        @out.Add($"in init: {_in_init}, icw1: {_icw1:X2}, icw2: {_ii_icw2}/{_icw2:X2}, ic3w: {_ii_icw3}/{_icw3:X2}, icw4: {_ii_icw4}/{_icw4:X2}, icw4 req: {_ii_icw4_req}");
        @out.Add($"ocw2: {_ocw2:X2}, ocw3: {_ocw3:X2}");
        return @out;
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x0020] = this;
        mappings[0x0021] = this;
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
                Log.DoLog($"i8259 pending interrupt: {i:X2}, (irr: {_irr:X2}, isr: {_isr:X2}, imr: {_imr:X2})", LogLevel.TRACE);
                return i;
            }
        }

        // can/should not happen
        Log.DoLog($"i8259 this should not happen", LogLevel.ERROR);
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

        Log.DoLog($"i8259 interrupt {interrupt_nr} requested (irr: {_irr:X2}, isr: {_isr:X2}, imr: {_imr:X2})", LogLevel.TRACE);
    }

    public void SetIRQBeingServiced(int interrupt_nr)
    {
        if (_auto_eoi == false)
        {
            if (_int_in_service != -1)
                Log.DoLog($"i8259: interrupt {_int_in_service} was not acked before {interrupt_nr} went in service", LogLevel.DEBUG);

            _int_in_service = interrupt_nr;
            byte mask = (byte)(1 << interrupt_nr);
            if ((_irr & mask) == 0)
                Log.DoLog($"i8259: interrupt {interrupt_nr} was not requested", LogLevel.DEBUG);
            if ((_isr & mask) == mask)
                Log.DoLog($"i8259: interrupt {interrupt_nr} was already in service", LogLevel.DEBUG);
            if ((_imr & mask) == mask)
                Log.DoLog($"i8259: interrupt {interrupt_nr} was masked off", LogLevel.DEBUG);
            _isr |= mask;
        }
        else
        {
            byte mask = (byte)~(1 << interrupt_nr);
            _irr &= mask;
            _isr &= mask;
            _int_in_service = -1;
        }
    }

    public override byte IO_Read(ushort addr)
    {
        byte rc = 0;

        if (addr == 0x0020)
        {
            Log.DoLog($"i8259 IN: read status register IRR: {_read_irr} (irr: {_irr:X2}, isr: {_isr:X2})", LogLevel.TRACE);
            if (_read_irr)
                rc = _irr;
            else
                rc = _isr;
        }
        else if (addr == 0x0021)
        {
            rc = _imr;
        }

        Log.DoLog($"i8259 IN: read addr {addr:X4}: {rc:X2}", LogLevel.TRACE);

        return rc;
    }

    public override bool IO_Write(ushort addr, byte value)
    {
        Log.DoLog($"i8259 OUT port {addr:X2} value {value:X2}", LogLevel.TRACE);

        if (addr == 0x0020)
        {
            _in_init = (value & 16) == 16;
            _has_slave = (value & 2) == 0;
            _icw1 = value;

            if (_in_init)  // ICW
            {
                Log.DoLog($"i8259 OUT is init (start ICW)", LogLevel.TRACE);

                _ii_icw2 = false;
                _ii_icw3 = false;
                _ii_icw4 = false;
                _ii_icw4_req = (value & 1) == 1;

                if (_int_in_service != -1)
                    Log.DoLog($"i8259 implicit EOI of {_int_in_service}", LogLevel.TRACE);

                _imr = 0;  // TODO 255?
                _isr = 0;
                _irr = 0;

                _int_in_service  = -1;
            }
            else  // OCW 2/3
            {
                if ((value & 8) == 8)  // OCW3
                {
                    Log.DoLog($"i8259 OUT: OCW3", LogLevel.TRACE);
                    _read_irr = (value & 3) == 2;
                    _ocw3 = value;
                }
                else  // OCW2
                {
                    Log.DoLog($"i8259 OUT: OCW2", LogLevel.TRACE);
                    _irq_request_level = value & 7;
                    _ocw2 = value;

                    // EOI
                    if (((value >> 5) & 1) == 1)  // EOI set (in OCW2)?
                    {
                        if ((value & 0x60) == 0x60)  // ack a certain level
                        {
                            int i = value & 7;
                            Log.DoLog($"i8259 EOI of {i}, level: {_irq_request_level}", LogLevel.TRACE);

                            byte mask = (byte)~(1 << i);
                            _irr &= mask;
                            _isr &= mask;
                            if (i == _int_in_service)
                                _int_in_service = -1;
                        }
                        else
                        {
                            Log.DoLog($"i8259 EOI of {_int_in_service}, level: {_irq_request_level}", LogLevel.TRACE);

                            if (_int_in_service == -1)
                                Log.DoLog($"i8259 EOI with no int in service?", LogLevel.DEBUG);
                            else
                            {
                                byte mask = (byte)~(1 << _int_in_service);
                                _irr &= mask;
                                _isr &= mask;
                                _int_in_service = -1;
                            }
                        }
                    }

                    Log.DoLog($"i8259 set level to: {_irq_request_level}", LogLevel.TRACE);
                }
            }
        }
        else if (addr == 0x0021)
        {
            if (_in_init)
            {
                if (_ii_icw2 == false)
                {
                    Log.DoLog($"i8259 OUT: is ICW2", LogLevel.TRACE);
                    _icw2 = value;
                    _ii_icw2 = true;
                    if (value != 0x00 && value != 0x08)
                        Log.DoLog($"i8259 OUT: ICW2 assigned strange value: 0x{value:X2}", LogLevel.DEBUG);
                    _int_offset = value;
                }
                else if (_ii_icw3 == false && _has_slave)
                {
                    Log.DoLog($"i8259 OUT: is ICW3", LogLevel.TRACE);

                    _ii_icw3 = true;
                    _icw3 = value;

                    // ignore value: slave-devices are not supported in this emulator

                    if (_ii_icw4_req == false)
                    {
                        _in_init = false;
                        Log.DoLog($"i8259 OUT: end of ICW", LogLevel.TRACE);
                    }
                }
                else if (_ii_icw4 == false)
                {
                    Log.DoLog($"i8259 OUT: is ICW4", LogLevel.TRACE);

                    _ii_icw4 = true;
                    _icw4 = value;
                    _in_init = false;
                    bool new_auto_eoi = (value & 2) == 2;
                    if (new_auto_eoi != _auto_eoi)
                    {
                        Log.DoLog($"i8259 OUT: _auto_eoi is now {new_auto_eoi}", LogLevel.TRACE);
                        _auto_eoi = new_auto_eoi;
                    }
                }
            }
            else
            {
                Log.DoLog($"i8259 OUT: is OCW1, value {value:X2}", LogLevel.TRACE);
                _imr = value;
            }
        }
        else
        {
            Log.DoLog($"i8259 OUT has no port {addr:X2}", LogLevel.ERROR);
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

    public override List<Tuple<uint, int> > GetAddressList()
    {
        return new() { };
    }

    public override void WriteByte(uint offset, byte value)
    {
    }

    public override byte ReadByte(uint offset)
    {
        return 0xee;
    }
}
