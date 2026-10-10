#!/usr/bin/env python3
"""Find call sites of imported xnkernel.dll functions inside XNurbsRhino.rhp,
and disassemble a window around each so the argument setup is visible.
Usage: python xref_imports.py <pe> <dllname>
"""
import sys, collections
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_OP_IMM, CS_OP_MEM

MASK64 = (1 << 64) - 1


def main():
    path, want = sys.argv[1], sys.argv[2]
    pe = pefile.PE(path, fast_load=False)
    base = pe.OPTIONAL_HEADER.ImageBase

    # IAT slots for the wanted dll
    iat = {}
    for entry in pe.DIRECTORY_ENTRY_IMPORT:
        dll = entry.dll.decode()
        if dll.lower() != want.lower():
            continue
        for imp in entry.imports:
            nm = imp.name.decode() if imp.name else f"ORD_{imp.ordinal}"
            iat[imp.address] = nm
    print(f"## {len(iat)} IAT slots for {want}")

    # build map: absolute VA of IAT slot -> name  (call qword ptr [rip+disp] resolves here)
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True

    # scan .text of the rhp
    for s in pe.sections:
        if s.Name.rstrip(b'\x00') != b'.text':
            continue
        lo = s.VirtualAddress
        hi = lo + s.Misc_VirtualSize
        data = s.get_data()
        hits = collections.defaultdict(list)
        insns = list(md.disasm(data, base + lo))
        # map instruction address -> index
        for i, ins in enumerate(insns):
            if ins.mnemonic not in ('call', 'jmp'):
                continue
            for op in ins.operands:
                if op.type == CS_OP_MEM and op.mem.base == 0x29:  # RIP
                    tgt_va = ins.address + ins.size + op.mem.disp
                    if tgt_va in iat:
                        hits[iat[tgt_va]].append((ins.address - base, i))
        print(f"\n## call sites per imported kernel function")
        for nm in sorted(hits, key=lambda k: -len(hits[k])):
            print(f"   {nm:44} x{len(hits[nm])}  @ "
                  + ", ".join(f"0x{a:x}" for a, _ in hits[nm][:12]))
        # disassemble a window around the 3 busiest
        top = sorted(hits, key=lambda k: -len(hits[k]))[:4]
        for nm in top:
            for rva, i in hits[nm][:2]:
                print(f"\n===== window around call to {nm} @0x{rva:x} =====")
                a = max(0, i - 40)
                b = min(len(insns), i + 6)
                for ins in insns[a:b]:
                    mark = " <<<" if ins.address - base == rva else ""
                    print(f"  {ins.address-base:08x}  {ins.mnemonic:<9} "
                          f"{ins.op_str}{mark}")


if __name__ == '__main__':
    main()
