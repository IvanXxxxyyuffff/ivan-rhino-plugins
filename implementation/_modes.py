# -*- coding: utf-8 -*-
"""模式整理：0=自动(Patch if AllowTrim else Coons)、1=强制 Coons、2=强制爬行（实验）；回退只试 Coons"""
from pathlib import Path
p = Path(r'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\surfaceunify\src\SurfaceUnifyCore.cs')
t = p.read_text(encoding='utf-8-sig')
pairs = [
("            SurfaceUnifyResult r2 = GenerateOnce(target, s, r1.Wrapped ? 2 : 1, out rep2);",
 "            SurfaceUnifyResult r2 = GenerateOnce(target, s, 1, out rep2);      // 另一种：纯 Coons + 射线"),
("""            bool wrapped = mode == 1 ? true : (mode == 2 ? false : false);""",
 """            bool wrapped = mode == 2;"""),
("            if (s.AllowTrim && !wrapped)",
 "            if (s.AllowTrim && mode == 0)"),
("""        /// <summary>mode：0 = 按面积比自动；1 = 强制爬行；2 = 强制 Coons + 射线</summary>""",
 """        /// <summary>mode：0 = 自动（允许修剪时走 Patch，否则 Coons + 射线）；1 = 强制 Coons + 射线；2 = 强制爬行（实验，未完成）</summary>"""),
]
for a, b in pairs:
    ok = a in t
    print(('OK  ' if ok else 'MISS'), a.strip()[:64])
    if ok:
        t = t.replace(a, b, 1)
p.write_text(t, encoding='utf-8')
print('written')
