#!/usr/bin/env python3
"""Enumerate + dump PE resources (esp. RT_DIALOG) from a DLL.
Usage: python dump_resources.py <pe> <outdir>
Writes <outdir>/<name>.res_<type>_<id>.bin for each leaf, and a manifest.
Also decodes RT_DIALOG templates to readable text (control ids + captions).
"""
import os, struct, sys
import pefile


def iter_res(pe):
    """Yield (type_name, res_name, lang, rva, size, data_rva)."""
    if not hasattr(pe, 'DIRECTORY_ENTRY_RESOURCE'):
        return
    for t in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        tname = (pefile.RESOURCE_TYPE.get(t.struct.Id, str(t.struct.Id))
                 if t.struct.Id else (t.name.string.decode('utf-16-le', 'replace')
                                      if t.name else '?'))
        for r in getattr(t, 'directory', []).entries:
            rname = (str(r.struct.Id) if r.struct.Id
                     else (r.name.string.decode('utf-16-le', 'replace')
                           if r.name else '?'))
            for l in getattr(r, 'directory', []).entries:
                d = l.data.struct
                yield tname, rname, l.struct.Id, d.OffsetToData, d.Size


def decode_dialog(data):
    """Very small RT_DIALOG decoder: extracts the string table + item ids."""
    out = []
    if len(data) < 26:
        return out
    style, exstyle = struct.unpack_from('<II', data, 0)
    out.append(f"    style=0x{style:08x} exstyle=0x{exstyle:08x}")
    # DIALOGEX has version + signature
    off = 8
    dlgver, sig = struct.unpack_from('<HH', data, 0)
    is_ex = (dlgver == 1 and sig == 0xFFFF)
    if is_ex:
        out.append("    DIALOGEX")
        # header: dlgVer(2) sig(2) helpID(4) exStyle(4) style(4) cDlgItems(2)
        #         x y cx cy (4*2) menu(2) windowClass(2) title(var) ...
        cditems = struct.unpack_from('<H', data, 16)[0]
        out.append(f"    cDlgItems={cditems}")
        p = 18 + 8  # x,y,cx,cy
        # menu
        if struct.unpack_from('<H', data, p)[0] == 0:
            p += 2
        else:
            p += 2
            while data[p] != 0 or data[p+1] != 0:
                p += 2
            p += 2
        # class
        if struct.unpack_from('<H', data, p)[0] == 0:
            p += 2
        else:
            p += 2
            while data[p] != 0 or data[p+1] != 0:
                p += 2
            p += 2
        # title
        if struct.unpack_from('<H', data, p)[0] == 0:
            p += 2
        else:
            p += 2
            s = []
            while data[p] != 0 or data[p+1] != 0:
                s.append(data[p:p+2].decode('utf-16-le', 'replace'))
                p += 2
            p += 2
            out.append("    title=" + ''.join(s))
        # creation data + pointsize/weight/italic etc for EX
        p += 2 + 2 + 4 + 2  # pointsize(2) weight(2) italic(1) charset(1) reserved(1)? approx
        return out
    return out


def main():
    path, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    pe = pefile.PE(path, fast_load=False)
    man = []
    for tname, rname, lang, rva, size in iter_res(pe):
        blob = pe.get_data(rva, size)
        fn = f"{tname}_{rname}_{lang}.bin"
        fn = "".join(c if c.isalnum() or c in '._-' else '_' for c in fn)
        with open(os.path.join(outdir, fn), 'wb') as f:
            f.write(blob)
        man.append((tname, rname, lang, rva, size, fn))
    with open(os.path.join(outdir, 'RESOURCE-MANIFEST.txt'), 'w', encoding='utf-8') as f:
        f.write(f"# resources of {path}\n")
        for tname, rname, lang, rva, size, fn in man:
            f.write(f"{tname:20} id={rname:8} lang=0x{lang:04x} rva=0x{rva:x} "
                    f"size={size:8} -> {fn}\n")
    print(f"resources={len(man)} -> {outdir}")
    for m in man:
        print(f"  {m[0]:20} id={m[1]:8} lang=0x{m[2]:04x} size={m[4]:8} {m[5]}")
    # decode dialogs
    for tname, rname, lang, rva, size, fn in man:
        if tname == 'RT_DIALOG':
            print(f"\n--- RT_DIALOG id={rname} lang=0x{lang:04x} ---")
            for line in decode_dialog(pe.get_data(rva, size)):
                print(line)


if __name__ == '__main__':
    main()
