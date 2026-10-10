#!/usr/bin/env python3
"""Full .text disassembly of a small PE with inline annotation of
RIP-relative references to: strings (ascii/utf16), IAT imports, data.
Usage: python full_disas.py <pe> <out.txt> [section=.text]
"""
import sys, re, struct
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

MASK64 = (1 << 64) - 1
PRINTABLE_A = re.compile(rb'^[\x20-\x7e]{3,}$')


def find_ascii(pe, rva):
    try:
        data = pe.get_data(rva, 96)
    except Exception:
        return None
    m = re.match(rb'[\x20-\x7e]{3,}', data)
    if m and len(m.group()) >= 3:
        return m.group().decode('latin1')
    return None


def find_utf16(pe, rva):
    try:
        data = pe.get_data(rva, 192)
    except Exception:
        return None
    m = re.match(rb'(?:[\x20-\x7e]\x00){3,}', data)
    if m:
        try:
            return m.group().decode('utf-16-le')
        except Exception:
            return None
    return None


def main():
    path, outp = sys.argv[1], sys.argv[2]
    secname = sys.argv[3] if len(sys.argv) > 3 else '.text'
    pe = pefile.PE(path, fast_load=False)
    base = pe.OPTIONAL_HEADER.ImageBase

    iat = {}
    for entry in pe.DIRECTORY_ENTRY_IMPORT:
        dll = entry.dll.decode()
        for imp in entry.imports:
            nm = imp.name.decode() if imp.name else f"ORD_{imp.ordinal}"
            iat[imp.address] = f"{dll}!{nm}"

    # collect all string start addresses for backward refs
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True

    out = []
    skipped = 0
    for s in pe.sections:
        if s.Name.rstrip(b'\x00') != secname.encode():
            continue
        data = s.get_data()
        pos = 0
        n = len(data)
        while pos < n:
            consumed = False
            for ins in md.disasm(data[pos:], base + s.VirtualAddress + pos):
                consumed = True
                rva = ins.address - base
                ann = ""
                for op in ins.operands:
                    if op.type == 3 and op.mem.base == 0x29:  # RIP-relative
                        tva = ins.address + ins.size + op.mem.disp
                        if tva in iat:
                            ann = f"   ; IMPORT {iat[tva]}"
                            break
                        trva = tva - base
                        if 0 <= trva < pe.OPTIONAL_HEADER.SizeOfImage:
                            s16 = find_utf16(pe, trva)
                            sa = find_ascii(pe, trva)
                            if s16 and (not sa or len(s16) >= 3):
                                ann = f"   ; WSTR {s16!r}"
                            elif sa:
                                ann = f"   ; STR {sa!r}"
                            else:
                                try:
                                    raw = pe.get_data(trva, 8)
                                    d = struct.unpack('<d', raw)[0]
                                    if -1e12 < d < 1e12 and d == d and abs(d) > 1e-14:
                                        ann = f"   ; DBL {d:.12g}"
                                except Exception:
                                    pass
                            break
                out.append(f"  {rva:08x}  {ins.mnemonic:<9} {ins.op_str}{ann}")
                # advance
                pos = (ins.address - base - s.VirtualAddress) + ins.size
            if not consumed:
                out.append(f"  {s.VirtualAddress+pos:08x}  db       0x{data[pos]:02x}"
                           f"   ; (undecodable, resync)")
                skipped += 1
                pos += 1
    with open(outp, 'w', encoding='utf-8') as f:
        f.write("\n".join(out) + "\n")
    print(f"wrote {len(out)} lines ({skipped} resync bytes) -> {outp}")


if __name__ == '__main__':
    main()
