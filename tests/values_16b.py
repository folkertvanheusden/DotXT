b16_values = set((0, 13, 15, 16, 255, 256, 255 + 15, 256 + 15, 255 + 16, 256 + 16, 0x01ff, 32767, 32768, 0xff00, 0xff10, 0xffff))

def get_pairs_16b():
    for a in b16_values:
        for b in b16_values:
            yield (a, b)
