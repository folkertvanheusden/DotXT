parity_lookup = [ False ] * 256

for v in range(0, 256):
    count = 0

    for i in range(0, 8):
        count += (v & (1 << i)) != 0

    parity_lookup[v] = (count & 1) == 0

def parity(v):
    return parity_lookup[v]

def flags_add_sub_cp(is_sub: bool, carry: bool, val1: int, val2: int):
    org_value = val2

    val2 += 1 if carry else 0

    if is_sub:
        result = val1 - val2

    else:
        result = val1 + val2

    flag_h = ((val1 & 0x10) ^ (org_value & 0x10) ^ (result & 0x10)) == 0x10

    flag_c = (result & 0x100) != 0

    before_sign = val1 & 0x80
    value_sign = org_value & 0x80
    after_sign = result & 0x80
    flag_o = after_sign != before_sign and ((before_sign != value_sign and is_sub) or (before_sign == value_sign and not is_sub))

    result &= 0xff

    flag_z = result == 0
    flag_s = after_sign == 0x80

    flags = (1 if flag_c else 0) + (16 if flag_h else 0) + (2048 if flag_o else 0) + (64 if flag_z else 0) + (128 if flag_s else 0)

    if parity(result):
        flags += 4

    return (result, flags)

def flags_cmp(carry, al, val):
    (result, flags) = flags_add_sub_cp(True, False, al, val)

    return flags

def flags_add_sub_cp16(is_sub: bool, carry: bool, val1: int, val2: int):
    org_value = val2

    val2 += 1 if carry else 0

    if is_sub:
        result = val1 - val2

    else:
        result = val1 + val2

    flag_h = ((val1 & 0x10) ^ (org_value & 0x10) ^ (result & 0x10)) == 0x10

    flag_c = (result & 0x10000) != 0

    before_sign = val1 & 0x8000
    value_sign = org_value & 0x8000
    after_sign = result & 0x8000
    flag_o = after_sign != before_sign and ((before_sign != value_sign and is_sub) or (before_sign == value_sign and not is_sub))

    result &= 0xffff

    flag_z = result == 0
    flag_s = after_sign == 0x8000

    flags = (1 if flag_c else 0) + (16 if flag_h else 0) + (2048 if flag_o else 0) + (64 if flag_z else 0) + (128 if flag_s else 0)

    if parity(result & 0xff):
        flags += 4

    return (result, flags)

def flags_cmp16(carry, ax, val):
    (result, flags) = flags_add_sub_cp16(True, False, ax, val)

    return flags

def _flags_logic(value: int, is16b: bool):
    flag_o = False
    flag_c = False
    flag_z = value == 0
    flag_s = ((value & 0x8000) == 0x8000) if is16b else ((value & 0x80) == 0x80)
    flag_p = parity(value & 0xff)

    flags = (1 if flag_c else 0) + (2048 if flag_o else 0) + (64 if flag_z else 0) + (128 if flag_s else 0) + (4 if flag_p else 0)

    return flags

def flags_or(val1: int, val2: int, is16b: bool):
    result = val1 | val2

    return (result, _flags_logic(result, is16b))

def flags_and(val1: int, val2: int, is16b: bool):
    result = val1 & val2

    return (result, _flags_logic(result, is16b))

def flags_xor(val1: int, val2: int, is16b: bool):
    result = val1 ^ val2

    return (result, _flags_logic(result, is16b))

def flags_rcl(val: int, count: int, carry: int, width: int, set_flag_o: bool):
    check_bit = 32768 if width == 16 else 128

    for i in range(0, count):
        b7 = True if val & check_bit else False
        val <<= 1
        val |= carry
        carry = b7
        val &= 0xff if width == 8 else 65535

    flag_o = False
    mask = ~0

    if set_flag_o:
        flag_o = carry ^ (True if val & check_bit else False)
    else:
        mask = ~2048

    flags = (1 if carry else 0) + (2048 if flag_o else 0)

    return (val, flags, mask & 0xffff)

def flags_rcr(val: int, count: int, carry: int, width: int, set_flag_o: bool):
    check_bit = 32768 if width == 16 else 128

    for i in range(0, count):
        b0 = val & 1
        val >>= 1
        val |= check_bit if carry else 0
        carry = b0
        val &= 0xff if width == 8 else 65535

    flag_o = False
    mask = ~0

    if set_flag_o:
        check_bit2 = 16384 if width == 16 else 64
        flag_o = (True if val & check_bit2 else 0) ^ (True if val & check_bit else False)

    else:
        mask = ~2048

    flags = (1 if carry else 0) + (2048 if flag_o else 0)

    return (val, flags, mask & 0xffff)

def flags_rol(val: int, count: int, carry: int, width: int, set_flag_o: bool):
    check_bit = 32768 if width == 16 else 128

    for i in range(0, count):
        carry = True if val & check_bit else False
        val <<= 1
        val |= carry
        val &= 0xff if width == 8 else 65535

    flag_o = False
    mask = ~0

    if set_flag_o:
        flag_o = carry ^ (True if val & check_bit else False)
    else:
        mask = ~2048

    flags = (1 if carry else 0) + (2048 if flag_o else 0)

    return (val, flags, mask & 0xffff)

def flags_ror(val: int, count: int, carry: int, width: int, set_flag_o: bool):
    check_bit = 32768 if width == 16 else 128

    for i in range(0, count):
        carry = val & 1
        val >>= 1
        val |= check_bit if carry else 0
        val &= 0xff if width == 8 else 65535

    flag_o = False
    mask = ~0

    if set_flag_o:
        check_bit2 = 16384 if width == 16 else 64
        flag_o = (True if val & check_bit2 else 0) ^ (True if val & check_bit else False)

    else:
        mask = ~2048

    flags = (1 if carry else 0) + (2048 if flag_o else 0)

    return (val, flags, mask & 0xffff)

def flags_sal(val: int, count: int, carry: int, width: int, set_flag_o: bool):
    check_bit = 32768 if width == 16 else 128

    before = val

    for i in range(0, count):
        carry = True if val & check_bit else False
        val <<= 1
        val &= 0xff if width == 8 else 65535

    flag_s = flag_z = flag_p = flag_o = False
    mask = ~0

    if set_flag_o:
        check_bit2 = 16384 if width == 16 else 64
        flag_o = (True if before & check_bit2 else 0) ^ (True if before & check_bit else False)

    else:
        mask = ~(2048 | 16)

    if count >= 1:
        flag_s = True if val & check_bit else 0
        flag_z = val == 0
        flag_p = parity(val & 255)

    flags = (1 if carry else 0) + (2048 if flag_o else 0) + (64 if flag_z else 0) + (128 if flag_s else 0) + (4 if flag_p else 0)

    return (val, flags, mask & 0xffff)

def flags_sar(val: int, count: int, carry: int, width: int, set_flag_o: bool):
    check_bit = 32768 if width == 16 else 128

    add_bit = check_bit if (val & check_bit) != 0 else 0

    for i in range(0, count):
        carry = True if val & 1 else False
        val >>= 1
        val |= add_bit

    flag_s = flag_z = flag_p = flag_o = False
    mask = ~(2048 | 16)

    if count >= 1:
        flag_s = True if val & check_bit else 0
        flag_z = val == 0
        flag_p = parity(val & 255)

    flags = (1 if carry else 0) + (2048 if flag_o else 0) + (64 if flag_z else 0) + (128 if flag_s else 0) + (4 if flag_p else 0)

    return (val, flags, mask & 0xffff)

def flags_inc_dec(carry: bool, al: int, is_sub: bool):
    (result, flags) = flags_add_sub_cp(is_sub, False, al, 1)

    flags &= ~1

    return flags

def flags_inc_dec16(carry: bool, ax: int, is_sub: bool):
    (result, flags) = flags_add_sub_cp16(is_sub, False, ax, 1)

    flags &= ~1

    return flags
