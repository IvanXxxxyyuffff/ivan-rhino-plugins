#!/usr/bin/env python3
"""Scan the XNurbs-own code region for RIP-relative loads of float/double
constants and dump them. Reveals tolerances, degrees, weights, defaults.
Usage: python consts.py <pe> <lo_hex> <hi_hex>
"""
import sys, struct, collections
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

MASK64 = (1 << 64) - 1


def main():
    path, lo, hi = sys.argv[1], int(sys.argv[2], 16), int(sys.argv[3], 16)
    pe = pefile.PE(path, fast_load=False)
    base = pe.OPTIONAL_HEADER.ImageBase
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True

    found = collections.OrderedDict()
    step = 0x1000
    for rva in range(lo, hi, step):
        data = pe.get_data(rva, step)
        if not data:
            continue
        for ins in md.disasm(data, base + rva):
            if ins.mnemonic not in ('movsd', 'movss', 'movapd', 'movaps',
                                    'movddup', 'movdqa', 'movups', 'movupd',
                                    'cmpss', 'cmpsd', 'addsd', 'mulsd',
                                    'divsd', 'subsd', 'addss', 'mulss'):
                continue
            for op in ins.operands:
                if op.type != 3:  # X86_OP_MEM
                    continue
                m = op.mem
                if m.base != 0x29 or m.index != 0:  # X86_REG_RIP == 0x29
                    continue
                tgt_va = ins.address + ins.size + m.disp
                trva = tgt_va - base
                if not (0 <= trva < pe.OPTIONAL_HEADER.SizeOfImage):
                    continue
                try:
                    raw = pe.get_data(trva, 8)
                except Exception:
                    continue
                if len(raw) < 8:
                    continue
                d = struct.unpack('<d', raw)[0]
                f = struct.unpack('<f', raw[:4])[0]
                # keep only "interesting" values: finite, not denormal,
                # not obviously a pointer/garbage
                def interesting(v):
                    if v != v:
                        return False
                    if abs(v) == 0:
                        return True
                    if abs(v) > 1e12 or abs(v) < 1e-14:
                        return False
                    return True
                if interesting(d) or interesting(f):
                    key = (ins.mnemonic, d if ins.mnemonic.endswith('d')
                           or ins.mnemonic in ('movsd', 'addsd', 'mulsd',
                                               'divsd', 'subsd', 'cmpsd',
                                               'movddup') else f)
                    found.setdefault((ins.mnemonic, d, f, trva), 0)
                    found[(ins.mnemonic, d, f, trva)] += 1

    # collapse: show unique (mnemonic, double, float) with one example site
    uniq = {}
    for (mn, d, f, trva), cnt in found.items():
        uniq.setdefault((mn, d, f), [trva, 0])
        uniq[(mn, d, f)][1] += cnt

    rows = sorted(uniq.items(), key=lambda kv: (kv[0][0], abs(kv[0][1])))
    print(f"## distinct RIP-relative float/double constants in "
          f"[0x{lo:x},0x{hi:x}) : {len(rows)}")
    print(f"{'mnemonic':10} {'as_double':>22} {'as_float':>16} {'site':>10} n")
    for (mn, d, f), (site, cnt) in rows:
        print(f"{mn:10} {d:>22.12g} {f:>16.7g} 0x{site:08x} {cnt}")


if __name__ == '__main__':
    main()
