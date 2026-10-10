# -*- coding: utf-8 -*-
from pathlib import Path
p = Path(r'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\surfaceunify\src\SurfaceUnifyCore.cs')
t = p.read_text(encoding='utf-8-sig')
pairs = [
("            bool extendFlag = false;\n", "            bool extendFlag = false;\n            double extWant = 0;\n"),
("                double extWant = s.AllowTrim ? Math.Max(0.0, s.Extend) : 0.0;", "                extWant = s.AllowTrim ? Math.Max(0.0, s.Extend) : 0.0;"),
]
for a, b in pairs:
    print(('OK  ' if a in t else 'MISS'), a.strip()[:60])
    t = t.replace(a, b, 1)
p.write_text(t, encoding='utf-8')
print('written')
