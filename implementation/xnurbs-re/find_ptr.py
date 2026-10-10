#!/usr/bin/env python3
"""Find 8-byte pointers anywhere in the image that point at a target VA.
Then report which .text code references those pointer slots (RIP-relative).
This chases indirect string access (message tables).
Usage: python find_ptr.py <pe> <target_va_hex> [more_targets...]
"""
import sys, struct, collections
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

MASK64 = (1 << 64) - 1


def main():
    path = sys.argv[1]
    targets = set(int(x, 16) for x in sys.argv[2:])
    pe = pefile.PE(path, fast_load=False)
    base = pe.OPTIONAL_HEADER.ImageBase
    blob = open(path, 'rb').read()

    # map VA -> file offset for each section
    def va_to_off(va):
        rva = va - base
        for s in pe.sections:
            if s.VirtualAddress <= rva < s.VirtualAddress + max(
                    s.Misc_VirtualSize, s.SizeOfRawData):
                return s.PointerToRawData + (rva - s.VirtualAddress)
        return None

    # scan the whole file for 8-byte little-endian pointers to any target
    slots = collections.defaultdict(list)
    tset = targets
    # build a fast lookup of target -> True
    for off in range(0, len(blob) - 8):
        v = struct.unpack_from('<Q', blob, off)[0]
        if v in tset:
            # convert file offset back to VA
            va = None
            for s in pe.sections:
                if s.PointerToRawData <= off < s.PointerToRawData + s.SizeOfRawData:
                    va = base + s.VirtualAddress + (off - s.PointerToRawData)
                    break
            if va is not None:
                slots[va].append(v)
    print(f"## pointer slots found: {len(slots)}")
    for va, vs in sorted(slots.items()):
        print(f"   slot VA 0x{va:x} -> 0x{vs[0]:x}")

    if not slots:
        return
    # xref those slots from .text
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    sec = [s for s in pe.sections if s.Name.rstrip(b'\x00') == b'.text'][0]
    lo, hi = sec.VirtualAddress, sec.VirtualAddress + sec.Misc_VirtualSize
    slotset = set(slots)
    hits = []
    for rva in range(lo, hi, 0x10000):
        data = pe.get_data(rva, 0x10000)
        if not data:
            continue
        for ins in md.disasm(data, base + rva):
            for op in ins.operands:
                if op.type == 3 and op.mem.base == 0x29:
                    tva = ins.address + ins.size + op.mem.disp
                    if tva in slotset:
                        hits.append((ins.address - base, ins.mnemonic, ins.op_str, tva))
    print(f"\n## .text references to those slots: {len(hits)}")
    for rva, mn, ops, tva in hits[:40]:
        print(f"   site 0x{rva:08x}  {mn} {ops}  (slot 0x{tva:x})")
    if hits:
        rv = [h[0] for h in hits]
        b = collections.Counter(r // (1 << 20) for r in rv)
        print("   sites per 1MB bucket: " + ", ".join(f"{k}MB:{b[k]}" for k in sorted(b)))


if __name__ == '__main__':
    main()
