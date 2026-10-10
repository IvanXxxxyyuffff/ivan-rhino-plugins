#!/usr/bin/env python3
"""Full RT_DIALOG / RT_DIALOGEX template parser.
Extracts control id + class + caption text for every item.
Usage: python parse_dialog.py <rhp> 
"""
import struct, sys
import pefile


def read_sz(data, p):
    s = []
    while True:
        if p + 1 >= len(data):
            return ''.join(s), p + 2
        w = data[p:p+2]
        if w == b'\x00\x00':
            return ''.join(s), p + 2
        s.append(w.decode('utf-16-le', 'replace'))
        p += 2


def align4(p):
    return (p + 3) & ~3


def parse(data):
    res = []
    dlgver, sig = struct.unpack_from('<HH', data, 0)
    is_ex = (dlgver == 1 and sig == 0xFFFF)
    p = 0
    if is_ex:
        helpid, exstyle, style, cditems = struct.unpack_from('<IIII', data, 4)[:4] if False else (None, None, None, None)
        helpid = struct.unpack_from('<I', data, 4)[0]
        exstyle = struct.unpack_from('<I', data, 8)[0]
        style = struct.unpack_from('<I', data, 12)[0]
        cditems = struct.unpack_from('<H', data, 16)[0]
        x, y, cx, cy = struct.unpack_from('<hhhh', data, 18)
        p = 26
        res.append(f"DIALOGEX helpID={helpid} style=0x{style:08x} exstyle=0x{exstyle:08x} "
                   f"items={cditems} rect=({x},{y},{cx},{cy})")
    else:
        style, exstyle, cditems = struct.unpack_from('<IIH', data, 0)
        x, y, cx, cy = struct.unpack_from('<hhhh', data, 10)
        p = 18
        res.append(f"DIALOG style=0x{style:08x} exstyle=0x{exstyle:08x} "
                   f"items={cditems} rect=({x},{y},{cx},{cy})")

    def skip_menu_or_class(p, label):
        v = struct.unpack_from('<H', data, p)[0]
        if v == 0x0000:
            return p + 2, f"{label}=<none>"
        if v == 0xFFFF:
            ordv = struct.unpack_from('<H', data, p + 2)[0]
            return p + 4, f"{label}=#{ordv}"
        s, np = read_sz(data, p)
        return np, f"{label}={s!r}"

    p, m = skip_menu_or_class(p, 'menu')
    res.append("  " + m)
    p, m = skip_menu_or_class(p, 'class')
    res.append("  " + m)
    title, p = read_sz(data, p)
    res.append(f"  title={title!r}")
    if is_ex:
        pointsize, weight = struct.unpack_from('<HH', data, p)
        italic = data[p+4]
        charset = data[p+5]
        p += 6
        face, p = read_sz(data, p)
        res.append(f"  font={face!r} pt={pointsize} weight={weight} italic={italic} charset={charset}")
    p = align4(p)
    res.append("  --- items ---")
    for i in range(cditems):
        if is_ex:
            helpid = struct.unpack_from('<I', data, p)[0]; p += 4
            exstyle = struct.unpack_from('<I', data, p)[0]; p += 4
            style = struct.unpack_from('<I', data, p)[0]; p += 4
        else:
            helpid = 0
            style = struct.unpack_from('<I', data, p)[0]; p += 4
            exstyle = struct.unpack_from('<I', data, p)[0]; p += 4
        x, y, cx, cy = struct.unpack_from('<hhhh', data, p); p += 8
        if is_ex:
            cid = struct.unpack_from('<I', data, p)[0]; p += 4
        else:
            cid = struct.unpack_from('<H', data, p)[0]; p += 2
        # class
        v = struct.unpack_from('<H', data, p)[0]
        if v == 0xFFFF:
            ordv = struct.unpack_from('<H', data, p + 2)[0]
            cls = f"#{ordv}"
            p += 4
        else:
            cls, p = read_sz(data, p)
        # title
        t = struct.unpack_from('<H', data, p)[0]
        if t == 0xFFFF:
            ordv = struct.unpack_from('<H', data, p + 2)[0]
            title = f"#{ordv}"
            p += 4
        else:
            title, p = read_sz(data, p)
        # creation data
        cd = struct.unpack_from('<H', data, p)[0]
        p += 2
        if cd:
            p += cd
        p = align4(p)
        res.append(f"   [{i:2}] id={cid:<6} class={cls:<22} rect=({x},{y},{cx},{cy}) "
                   f"style=0x{style:08x} text={title!r}")
    return res


def main():
    path = sys.argv[1]
    pe = pefile.PE(path, fast_load=False)
    for t in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        tname = pefile.RESOURCE_TYPE.get(t.struct.Id, str(t.struct.Id))
        for r in getattr(t, 'directory', []).entries:
            for l in getattr(r, 'directory', []).entries:
                d = l.data.struct
                blob = pe.get_data(d.OffsetToData, d.Size)
                if tname == 'RT_DIALOG':
                    print(f"\n########## RT_DIALOG id={r.struct.Id} lang=0x{l.struct.Id:04x} ##########")
                    for line in parse(blob):
                        print(line)
                elif tname == 'RT_STRING':
                    print(f"\n########## RT_STRING block id={r.struct.Id} lang=0x{l.struct.Id:04x} ##########")
                    p = 0
                    idx = 0
                    while p + 1 < len(blob):
                        ln = struct.unpack_from('<H', blob, p)[0]
                        p += 2
                        s = blob[p:p + ln * 2].decode('utf-16-le', 'replace')
                        p += ln * 2
                        if s.strip():
                            base = (r.struct.Id - 1) * 16
                            print(f"   strid={base + idx:<6} {s!r}")
                        idx += 1


if __name__ == '__main__':
    main()
