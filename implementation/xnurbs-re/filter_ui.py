#!/usr/bin/env python3
"""Filter UI-looking strings out of the raw string dump.
Keeps: wide or ascii strings that look like human UI text / option names.
Drops: C++ mangled names, DLL/import names, paths, binary junk.
"""
import re, sys

MANGLED = re.compile(r'[?@]|^__|^\.[a-z]|MSVCP|api-ms|\.dll$|^\?\?')
PATHY = re.compile(r'^[A-Za-z]:\\|\.(rhp|dll|exe|pdb|lib|obj|cpp|h)$', re.I)
UI = re.compile(r'^[A-Za-z][A-Za-z0-9 _\-/&().,\'+#%:!\[\]]*$')


def main():
    src, dst = sys.argv[1], sys.argv[2]
    keep = []
    seen = set()
    for line in open(src, encoding='utf-8', errors='replace'):
        parts = line.rstrip('\n').split(' ', 2)
        if len(parts) < 3:
            continue
        off, kind, s = parts
        if MANGLED.search(s) or PATHY.search(s):
            continue
        if len(s) < 3 or len(s) > 90:
            continue
        if not UI.match(s):
            continue
        # must contain at least one letter run of 3+ or a space (UI text)
        if not re.search(r'[A-Za-z]{3,}', s):
            continue
        k = s.strip()
        if k.lower() in seen:
            continue
        seen.add(k.lower())
        keep.append((off, kind, s))
    with open(dst, 'w', encoding='utf-8') as f:
        for off, kind, s in keep:
            f.write(f"{off} {kind} {s}\n")
    print(f"[{src}] ui-ish strings kept={len(keep)} -> {dst}")


if __name__ == '__main__':
    main()
