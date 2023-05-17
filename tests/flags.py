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

def _flags_logic(value: int):
    flag_o = False
    flag_c = False
    flag_z = value == 0
    flag_s = (value & 0x80) == 0x80
    flag_p = parity(value)

    flags = (1 if flag_c else 0) + (2048 if flag_o else 0) + (64 if flag_z else 0) + (128 if flag_s else 0) + (4 if flag_p else 0)

    return flags

def flags_or(val1: int, val2: int):
    result = val1 | val2

    return (result, _flags_logic(result))

def flags_and(val1: int, val2: int):
    result = val1 & val2

    return (result, _flags_logic(result))

def flags_xor(val1: int, val2: int):
    result = val1 ^ val2

    return (result, _flags_logic(result))

def flags_rcl(val: int, count: int, carry: int):
    for i in range(0, count):
        b7 = True if val & 128 else False
        val <<= 1
        val |= carry
        carry = b7
        val &= 0xff

    flag_o = False
    mask = ~0

    if count == 1:
        flag_o = carry ^ (True if val & 128 else False)
    else:
        mask = ~2048

    flags = (1 if carry else 0) + (2048 if flag_o else 0)

    return (val, flags, mask)

def flags_rcr(val: int, count: int, carry: int):
    for i in range(0, count):
        b0 = val & 1
        val >>= 1
        val |= 128 if carry else 0
        carry = b0
        val &= 0xff

    flag_o = False
    mask = ~0

    if count == 1:
        flag_o = (True if val & 64 else 0) ^ (True if val & 128 else False)
    else:
        mask = ~2048

    flags = (1 if carry else 0) + (2048 if flag_o else 0)

    return (val, flags, mask)

def flags_rol(val: int, count: int, carry: int):
    for i in range(0, count):
        carry = True if val & 128 else False
        val <<= 1
        val |= carry
        val &= 0xff

    flag_o = False
    mask = ~0

    if count == 1:
        flag_o = carry ^ (True if val & 128 else False)
    else:
        mask = ~2048

    flags = (1 if carry else 0) + (2048 if flag_o else 0)

    return (val, flags, mask)

def flags_ror(val: int, count: int, carry: int):
    for i in range(0, count):
        carry = val & 1
        val >>= 1
        val |= 128 if carry else 0
        val &= 0xff

    flag_o = False
    mask = ~0

    if count == 1:
        flag_o = (True if val & 64 else 0) ^ (True if val & 128 else False)
    else:
        mask = ~2048

    flags = (1 if carry else 0) + (2048 if flag_o else 0)

    return (val, flags, mask)
