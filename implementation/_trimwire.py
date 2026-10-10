# -*- coding: utf-8 -*-
from pathlib import Path
p = Path(r'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\surfaceunify\src\SurfaceUnifyCore.cs')
t = p.read_text(encoding='utf-8-sig')
pairs = [
("""                LoopData domain = outer;
                if (s.Extend > 1e-9)
                {
                    var big = EnlargeLoop(outer, rimPlane, s.Extend);""",
 """                LoopData domain = outer;
                double extWant = s.AllowTrim ? Math.Max(0.0, s.Extend) : 0.0;
                if (extWant > 1e-9)
                {
                    var big = EnlargeLoop(outer, rimPlane, extWant);"""),
("""                        "边界外扩 {0:0.##} → 已按原边界修剪（多余部分剪掉，边界贴齐）", s.Extend));""",
 """                        "边界外扩 {0:0.##} → 已按原边界修剪（多余部分剪掉，边界贴齐）", extWant));"""),
]
for a, b in pairs:
    ok = a in t
    print(('OK  ' if ok else 'MISS'), a.split('\n')[0][:60])
    if ok:
        t = t.replace(a, b, 1)
p.write_text(t, encoding='utf-8')
print('written')
