#!/usr/bin/env python3
"""Deep BFS from exports recording which address buckets are reached.
Answers: does XNurbs's own API reach the statically-linked MKL / TetGen code?
Usage: python bfs_deep.py <pe> <budget>
"""
import sys, collections
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_OP_IMM

MASK64 = (1 << 64) - 1
BUCKET = 1 << 20  # 1 MB


def main():
    path = sys.argv[1]
    budget = int(sys.argv[2]) if len(sys.argv) > 2 else 200000
    pe = pefile.PE(path, fast_load=False)
    base = pe.OPTIONAL_HEADER.ImageBase
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True

    exports = [(s.name.decode(), s.address)
               for s in pe.DIRECTORY_ENTRY_EXPORT.symbols if s.name]

    seen = set()
    q = collections.deque(a for _, a in exports)
    # pre-scan buckets
    hist = collections.Counter()
    edges = collections.Counter()
    scanned = 0
    while q and scanned < budget:
        rva = q.popleft()
        if rva in seen or rva <= 0 or rva >= pe.OPTIONAL_HEADER.SizeOfImage:
            continue
        seen.add(rva)
        scanned += 1
        hist[rva // BUCKET] += 1
        try:
            data = pe.get_data(rva, 0x200)
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
                        edges[(rva // BUCKET, t // BUCKET)] += 1
                        if t not in seen:
                            q.append(t)
            if ins.mnemonic == 'ret':
                break

    print(f"## deep BFS: scanned={scanned} distinct={len(seen)} "
          f"(budget {budget}, {'EXHAUSTED' if scanned >= budget else 'complete'})")
    print(f"## reached code per 1MB bucket")
    for k in sorted(hist):
        print(f"   {k:3} MB (0x{k*BUCKET:08x}) : {hist[k]}")
    print("\n## cross-bucket edges (from -> to : count), sorted")
    for (a, b), c in edges.most_common(40):
        print(f"   {a:3} MB -> {b:3} MB : {c}")


if __name__ == '__main__':
    main()
