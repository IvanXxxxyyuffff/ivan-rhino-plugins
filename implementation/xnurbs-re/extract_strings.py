#!/usr/bin/env python3
"""Extract ASCII + UTF-16LE strings from a binary, with dedup and offset.
Usage: python extract_strings.py <file> <out.txt> [minlen]
Also does a keyword histogram pass for algorithm hints.
"""
import sys, re, collections

def strings_from(data, minlen=5):
    out = []
    # ASCII
    for m in re.finditer(rb'[\x20-\x7e]{%d,}' % minlen, data):
        out.append((m.start(), 'A', m.group().decode('latin1')))
    # UTF-16LE (printable ascii chars followed by 00)
    pat = re.compile((rb'(?:[\x20-\x7e]\x00){%d,}' % minlen))
    for m in pat.finditer(data):
        try:
            s = m.group().decode('utf-16-le')
        except Exception:
            continue
        out.append((m.start(), 'W', s))
    out.sort(key=lambda t: t[0])
    return out


def main():
    path, outp = sys.argv[1], sys.argv[2]
    minlen = int(sys.argv[3]) if len(sys.argv) > 3 else 5
    data = open(path, 'rb').read()
    ss = strings_from(data, minlen)
    seen = set()
    lines = []
    for off, kind, s in ss:
        key = (kind, s)
        if key in seen:
            continue
        seen.add(key)
        lines.append(f"{off:08x} {kind} {s}")
    with open(outp, 'w', encoding='utf-8', errors='replace') as f:
        f.write("\n".join(lines) + "\n")
    print(f"[{path}] strings={len(lines)} (raw={len(ss)}) -> {outp}")


if __name__ == "__main__":
    main()
