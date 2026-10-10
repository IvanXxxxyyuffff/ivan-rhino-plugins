import re
from pathlib import Path

root = Path(r'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE')
theme = root / 'shared' / 'PanelTheme.cs'
icon = root / 'iconmake' / 'Program.cs'

def span(text, needle='private static void GlassDrawUnify'):
    i = text.find(needle)
    if i < 0:
        raise SystemExit('not found in ' + needle)
    j = text.find('\n        }\n', i)
    if j < 0:
        raise SystemExit('end not found')
    return i, j + len('\n        }\n')

tt = theme.read_text(encoding='utf-8-sig')
body = tt[span(tt)[0]:span(tt)[1]]

it = icon.read_text(encoding='utf-8-sig')
i0, i1 = span(it)
old = it[i0:i1]
new = it[:i0] + body + it[i1:]
icon.write_text(new, encoding='utf-8')
print('iconmake GlassDrawUnify replaced: old %d chars -> new %d chars' % (len(old), len(body)))

# 复验
def grab(p):
    t = Path(p).read_text(encoding='utf-8-sig')
    s = span(t)
    return re.sub(r'\s+', ' ', t[s[0]:s[1]]).strip()

print('theme == iconmake :', grab(theme) == grab(icon))
