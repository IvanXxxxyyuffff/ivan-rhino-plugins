#!/usr/bin/env python3
"""Disassemble a region of a PE with capstone, resolving intra-module
call/jmp targets to RVA so the real implementation can be followed.
Usage: python disas.py <pe> <start_rva_hex> <len_hex> [outfile]
       python disas.py <pe> --follow <export_name>
"""
import sys
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_OP_IMM

MASK64 = (1 << 64) - 1


def make(pe):
    base = pe.OPTIONAL_HEADER.ImageBase
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    return md, base


def get_rva_from_va(pe, va):
    return va - pe.OPTIONAL_HEADER.ImageBase


def disas_range(pe, md, base, start_rva, length, label=None, out=None):
    data = pe.get_data(start_rva, length)
    if not data:
        return []
    lines = []
    if label:
        lines.append(f"\n===== {label} @ rva 0x{start_rva:x} len 0x{length:x} =====")
    for ins in md.disasm(data, base + start_rva):
        rva = ins.address - base
        ann = ""
        if ins.mnemonic in ('call', 'jmp') and ins.operands:
            op = ins.operands[0]
            if op.type == CS_OP_IMM:
                tgt = op.imm & MASK64
                trva = tgt - base
                if 0 <= trva < pe.OPTIONAL_HEADER.SizeOfImage:
                    ann = f"   ; -> 0x{trva:x}"
                else:
                    ann = f"   ; -> VA 0x{tgt:x} (external/import)"
        lines.append(f"  {rva:08x}  {ins.mnemonic:<8} {ins.op_str}{ann}")
    if out:
        out.extend(lines)
    return lines


def main():
    path = sys.argv[1]
    pe = pefile.PE(path, fast_load=False)
    md, base = make(pe)

    if sys.argv[2] == '--follow':
        name = sys.argv[3]
        exp = pe.DIRECTORY_ENTRY_EXPORT
        target = None
        for s in exp.symbols:
            if s.name and s.name.decode() == name:
                target = s.address
                break
        if target is None:
            print(f"export {name} not found")
            return
        print(f"### {name} entry RVA = 0x{target:x}")
        seen = set()
        queue = [target]
        budget = 40
        while queue and budget > 0:
            rva = queue.pop(0)
            if rva in seen:
                continue
            seen.add(rva)
            budget -= 1
            lines = disas_range(pe, md, base, rva, 0x60,
                                label=f"chunk @0x{rva:x}")
            print("\n".join(lines))
            # follow direct jmps only (thunk chains)
            data = pe.get_data(rva, 0x60)
            for ins in md.disasm(data, base + rva):
                if ins.mnemonic == 'jmp' and ins.operands:
                    op = ins.operands[0]
                    if op.type == CS_OP_IMM:
                        trva = (op.imm & MASK64) - base
                        if 0 <= trva < pe.OPTIONAL_HEADER.SizeOfImage and trva not in seen:
                            print(f"   >> following jmp to 0x{trva:x}")
                            queue.append(trva)
                if ins.mnemonic == 'ret':
                    break
        return

    start = int(sys.argv[2], 16)
    length = int(sys.argv[3], 16)
    out = []
    disas_range(pe, md, base, start, length, label=sys.argv[4] if len(sys.argv) > 4 else None, out=out)
    txt = "\n".join(out)
    print(txt)


if __name__ == '__main__':
    main()
