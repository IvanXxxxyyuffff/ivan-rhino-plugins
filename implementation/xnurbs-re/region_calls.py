#!/usr/bin/env python3
"""For a given source code region, list ALL direct call/jmp targets and
bucket them, so we learn exactly which statically-linked library regions
the code calls into.
Usage: python region_calls.py <pe> <src_lo_hex> <src_hi_hex>
"""
import sys, collections
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_OP_IMM

MASK64 = (1 << 64) - 1


def main():
    path = sys.argv[1]
    lo, hi = int(sys.argv[2], 16), int(sys.argv[3], 16)
    pe = pefile.PE(path, fast_load=False)
    base = pe.OPTIONAL_HEADER.ImageBase
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True

    tgts = collections.Counter()
    # per 256KB bucket for finer resolution
    fine = collections.Counter()
    step = 0x10000
    for rva in range(lo, hi, step):
        data = pe.get_data(rva, step)
        if not data:
            continue
        for ins in md.disasm(data, base + rva):
            if ins.mnemonic not in ('call', 'jmp'):
                continue
            if not ins.operands:
                continue
            op = ins.operands[0]
            if op.type != CS_OP_IMM:
                continue
            t = (op.imm & MASK64) - base
            if not (0 <= t < pe.OPTIONAL_HEADER.SizeOfImage):
                continue
            if lo <= t < hi:
                continue  # internal
            tgts[t // (1 << 20)] += 1
            fine[t // (256 * 1024)] += 1

    total = sum(tgts.values())
    print(f"## external direct call/jmp targets from [0x{lo:x},0x{hi:x}) "
          f"= {total}")
    print("## per 1MB bucket (1MB index, count, addr range)")
    for k in sorted(tgts):
        print(f"   {k:3} MB  0x{k<<20:08x}-0x{((k+1)<<20)-1:08x} : {tgts[k]}")
    print("\n## per 256KB bucket (nonzero only)")
    for k in sorted(fine):
        print(f"   0x{k<<18:08x} : {fine[k]}")


if __name__ == '__main__':
    main()
