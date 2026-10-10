#!/usr/bin/env python3
"""Parse .pdata RUNTIME_FUNCTION table -> function boundaries + sizes.
Also: BFS call-graph probe from exports to bound XNurbs's own code extent.
Usage: python funcs.py <pe>
"""
import sys, collections, struct
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_OP_IMM

MASK64 = (1 << 64) - 1


def parse_pdata(pe):
    sec = None
    for s in pe.sections:
        if s.Name.rstrip(b'\x00') == b'.pdata':
            sec = s
            break
    if sec is None:
        return []
    data = sec.get_data()
    base = pe.OPTIONAL_HEADER.ImageBase
    funcs = []
    for off in range(0, len(data) - 11, 12):
        begin, end, unwind = struct.unpack_from('<III', data, off)
        if begin == 0 and end == 0:
            continue
        funcs.append((begin, end, unwind))
    funcs.sort()
    return funcs


def main():
    path = sys.argv[1]
    pe = pefile.PE(path, fast_load=False)
    base = pe.OPTIONAL_HEADER.ImageBase
    funcs = parse_pdata(pe)
    print(f"## .pdata function count = {len(funcs)}")
    sizes = [(e - b, b, e) for b, e, u in funcs if e > b]
    sizes.sort(reverse=True)
    print("## 40 largest functions (size, rva_begin, rva_end)")
    for sz, b, e in sizes[:40]:
        print(f"   {sz:8} 0x{b:08x} .. 0x{e:08x}")

    # region histogram: functions per 2MB bucket
    hist = collections.Counter()
    for b, e, u in funcs:
        hist[b // (2 * 1024 * 1024)] += 1
    print("\n## function count per 2MB bucket (MB -> count)")
    for k in sorted(hist):
        print(f"   {k*2:3} MB : {hist[k]}")

    # BFS from exports
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    exports = []
    for s in pe.DIRECTORY_ENTRY_EXPORT.symbols:
        if s.name:
            exports.append((s.name.decode(), s.address))
    print(f"\n## BFS call-graph probe from {len(exports)} exports")
    seen = set()
    q = collections.deque()
    for n, a in exports:
        q.append(a)
    lo, hi = 1 << 62, 0
    called_ext = collections.Counter()
    budget = 3000
    while q and budget > 0:
        rva = q.popleft()
        if rva in seen or rva <= 0 or rva >= pe.OPTIONAL_HEADER.SizeOfImage:
            continue
        seen.add(rva)
        budget -= 1
        lo = min(lo, rva)
        hi = max(hi, rva)
        try:
            data = pe.get_data(rva, 0x400)
        except Exception:
            continue
        if not data:
            continue
        for ins in md.disasm(data, base + rva):
            if ins.mnemonic in ('call', 'jmp') and ins.operands:
                op = ins.operands[0]
                if op.type == CS_OP_IMM:
                    t = (op.imm & MASK64) - base
                    if 0 <= t < pe.OPTIONAL_HEADER.SizeOfImage:
                        if t not in seen:
                            q.append(t)
                    else:
                        called_ext[t] += 1
            if ins.mnemonic == 'ret':
                break
    print(f"   reached {len(seen)} distinct code RVAs (budget {budget} left)")
    print(f"   address extent: 0x{lo:x} .. 0x{hi:x}  "
          f"(span {(hi-lo)/1048576:.2f} MB)")
    print(f"   external-call count={sum(called_ext.values())}")
    # bucket the reached RVAs
    hist2 = collections.Counter()
    for r in seen:
        hist2[r // (2 * 1024 * 1024)] += 1
    print("   reached RVAs per 2MB bucket:")
    for k in sorted(hist2):
        print(f"     {k*2:3} MB : {hist2[k]}")


if __name__ == '__main__':
    main()
