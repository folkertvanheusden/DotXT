def get_pairs_16b():
    in_ = (0, 13, 15, 16, 255, 256, 255 + 15, 256 + 15, 255 + 16, 256 + 16, 0x01ff, 32767, 32768, 0xff00, 0xff10)

    work = set(in_)

    for a in work:
        for b in work:
            yield (a, b)
