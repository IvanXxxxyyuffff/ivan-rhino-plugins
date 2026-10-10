#!/usr/bin/env python3
"""Find all instructions in .text that reference a given target address
via RIP-relative operand. Locates the code region that "owns" a string.
Usage: python xref_addr.py <pe> <target_va_hex> [max_hits]
"""
import sys, collections
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

MASK64 = (1 << 64) - 1


def main():
    path = sys.argv[1]
    target = int(sys.argv[2], 16)
    maxhits = int(sys.argv[3]) if len(sys.argv) > 3 else 50
    pe = pefile.PE(path, fast_load=False)
    base = pe.OPTIONAL_HEADER.ImageBase
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True

    hits = []
    sec = None
    for s in pe.sections:
        if s.Name.rstrip(b'\x00') == b'.text':
            sec = s
    lo = sec.VirtualAddress
    hi = lo + sec.Misc_VirtualSize
    step = 0x10000
    for rva in range(lo, hi, step):
        data = pe.get_data(rva, step)
        if not data:
            continue
        for ins in md.disasm(data, base + rva):
            for op in ins.operands:
                if op.type == 3 and op.mem.base == 0x29:
                    tva = ins.address + ins.size + op.mem.disp
                    if tva == target:
                        hits.append((ins.address - base, ins.mnemonic, ins.op_str))
    print(f"## references to VA 0x{target:x} (rva 0x{target-base:x}) : {len(hits)}")
    for rva, mn, ops in hits[:maxhits]:
        print(f"   site rva 0x{rva:08x}  {mn} {ops}")
    if hits:
        rv = [h[0] for h in hits]
        print(f"   site rva range: 0x{min(rv):x} .. 0x{max(rv):x}")
        b = collections.Counter(r // (1 << 20) for r in rv)
        print("   sites per 1MB bucket: " + ", ".join(f"{k}MB:{b[k]}" for k in sorted(b)))


if __name__ == '__main__':
    main()
