#!/usr/bin/env python3
"""Read-only PE recon for XNurbsRhino.rhp and xnkernel.dll.
Emits: headers, sections+entropy, exports, imports, CLR flag, TLS, resources.
Usage: python recon_pe.py <path> <outdir>
"""
import sys, os, math, json, collections

import pefile


def entropy(data):
    if not data:
        return 0.0
    cnt = collections.Counter(data)
    n = len(data)
    ent = 0.0
    for c in cnt.values():
        p = c / n
        ent -= p * math.log2(p)
    return round(ent, 4)


def main():
    path = sys.argv[1]
    outdir = sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    base = os.path.splitext(os.path.basename(path))[0]

    pe = pefile.PE(path, fast_load=False)
    out = []
    w = out.append

    w(f"# PE RECON: {path}")
    w(f"size_bytes: {os.path.getsize(path)}")
    w(f"machine: {hex(pe.FILE_HEADER.Machine)}  "
      f"(0x8664=x64, 0x14c=x86)")
    w(f"timestamp: {pe.FILE_HEADER.TimeDateStamp}  "
      f"({__import__('datetime').datetime.utcfromtimestamp(pe.FILE_HEADER.TimeDateStamp)} UTC)")
    w(f"characteristics: {hex(pe.FILE_HEADER.Characteristics)}")
    w(f"is_dll: {pe.is_dll()}  is_exe: {pe.is_exe()}")
    w(f"subsystem: {pe.OPTIONAL_HEADER.Subsystem}")
    w(f"image_base: {hex(pe.OPTIONAL_HEADER.ImageBase)}")
    w(f"entry_point(RVA): {hex(pe.OPTIONAL_HEADER.AddressOfEntryPoint)}")
    w(f"dll_characteristics: {hex(pe.OPTIONAL_HEADER.DllCharacteristics)}")
    dc = pe.OPTIONAL_HEADER.DllCharacteristics
    w("  flags: " + ", ".join(
        n for n, b in [("HIGH_ENTROPY_VA", 0x20), ("DYNAMIC_BASE", 0x40),
                       ("FORCE_INTEGRITY", 0x80), ("NX_COMPAT", 0x100),
                       ("NO_ISOLATION", 0x200), ("NO_SEH", 0x400),
                       ("NO_BIND", 0x800), ("APPCONTAINER", 0x1000),
                       ("WDM_DRIVER", 0x2000), ("GUARD_CF", 0x4000),
                       ("TERMINAL_SERVER_AWARE", 0x8000)] if dc & b))
    w("")

    # --- CLR detection ---
    w("## CLR / MANAGED DETECTION")
    clr = None
    try:
        dd = pe.OPTIONAL_HEADER.DATA_DIRECTORY
        clr = dd[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR']]
    except Exception as e:
        w(f"  com descriptor lookup failed: {e}")
    if clr is not None:
        w(f"  COM_DESCRIPTOR dir: RVA={hex(clr.VirtualAddress)} size={clr.Size}")
        w(f"  => IS_MANAGED: {clr.VirtualAddress != 0 and clr.Size != 0}")
    w("")

    # --- Sections ---
    w("## SECTIONS")
    w(f"{'name':10} {'VA':>10} {'VSize':>10} {'RawPtr':>10} {'RawSize':>10} "
      f"{'Entropy':>8} {'Flags':>10}")
    for s in pe.sections:
        nm = s.Name.rstrip(b'\x00').decode('latin1')
        try:
            data = s.get_data()
            ent = entropy(data)
        except Exception:
            ent = -1
        w(f"{nm:10} {hex(s.VirtualAddress):>10} {hex(s.Misc_VirtualSize):>10} "
          f"{hex(s.PointerToRawData):>10} {hex(s.SizeOfRawData):>10} "
          f"{ent:>8} {hex(s.Characteristics):>10}")
    w("")

    # --- Exports ---
    w("## EXPORTS")
    try:
        exp = pe.DIRECTORY_ENTRY_EXPORT
        w(f"  dll_name: {exp.name.decode('latin1') if exp.name else None}")
        w(f"  ordinal_base: {exp.struct.Base}")
        syms = exp.symbols
        w(f"  export_count: {len(syms)}")
        named = [s for s in syms if s.name]
        w(f"  named_count: {len(named)}")
        w(f"  {'ord':>5} {'RVA':>10} {'name'}")
        for s in syms:
            nm = s.name.decode('latin1') if s.name else f"ORDINAL_{s.ordinal}"
            w(f"  {s.ordinal:>5} {hex(s.address):>10} {nm}")
    except AttributeError:
        w("  (no export directory)")
    w("")

    # --- Imports ---
    w("## IMPORTS")
    try:
        for entry in pe.DIRECTORY_ENTRY_IMPORT:
            dll = entry.dll.decode('latin1')
            names = []
            for imp in entry.imports:
                if imp.name:
                    names.append(imp.name.decode('latin1'))
                else:
                    names.append(f"ORD_{imp.ordinal}")
            w(f"  {dll}  ({len(names)} imports)")
            for n in sorted(names):
                w(f"      {n}")
    except AttributeError:
        w("  (no import directory)")
    w("")

    # --- Delay imports ---
    w("## DELAY IMPORTS")
    try:
        for entry in pe.DIRECTORY_ENTRY_DELAY_IMPORT:
            dll = entry.dll.decode('latin1')
            names = [i.name.decode('latin1') if i.name else f"ORD_{i.ordinal}"
                     for i in entry.imports]
            w(f"  {dll}  ({len(names)})")
            for n in sorted(names):
                w(f"      {n}")
    except AttributeError:
        w("  (none)")
    w("")

    # --- Debug / TLS / Resources / version info ---
    w("## MISC")
    try:
        for d in pe.DIRECTORY_ENTRY_DEBUG:
            w(f"  DEBUG entry type={d.struct.Type} size={d.struct.SizeOfData} "
              f"ptr={hex(d.struct.PointerToRawData)}")
            if d.struct.Type == 2:  # CODEVIEW
                try:
                    w(f"    codeview: {d.entry.PdbFileName}")
                except Exception:
                    pass
    except AttributeError:
        w("  (no debug dir)")
    try:
        ti = pe.DIRECTORY_ENTRY_TLS
        w(f"  TLS present: callbacks={ti.struct.AddressOfCallBacks}")
    except AttributeError:
        w("  (no TLS)")
    try:
        for fi in pe.FileInfo:
            for f in fi:
                if f.Key == b'StringFileInfo':
                    for st in f.StringTable:
                        for k, v in st.entries.items():
                            w(f"  versioninfo {k.decode('latin1')} = {v.decode('latin1')}")
    except Exception:
        pass
    # resources
    try:
        types = {}
        for r in pe.DIRECTORY_ENTRY_RESOURCE.entries:
            tn = pefile.RESOURCE_TYPE.get(r.struct.Id, str(r.struct.Id)) if r.struct.Id else (r.name.string.decode() if r.name else '?')
            types[tn] = types.get(tn, 0) + 1
        w(f"  resources: {types}")
    except AttributeError:
        w("  (no resources)")
    w("")

    # --- Overlay ---
    last = max(s.PointerToRawData + s.SizeOfRawData for s in pe.sections)
    fsz = os.path.getsize(path)
    w(f"## OVERLAY: overlay_bytes={fsz - last} (file={fsz}, last_section_end={last})")
    if fsz - last > 0:
        w(f"  overlay_entropy={entropy(open(path,'rb').read()[last:])}")

    txt = "\n".join(out)
    op = os.path.join(outdir, f"{base}.recon.txt")
    with open(op, "w", encoding="utf-8") as fh:
        fh.write(txt + "\n")
    print(txt)
    print(f"\n[written] {op}")


if __name__ == "__main__":
    main()
