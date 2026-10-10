# 给 10 份 PanelTheme.cs（shared + 8 插件 src + stripe\center + patchfill）+ iconmake\Program.cs
# 加同一个图标分支：kind = patchfill（朱橙 #C2603A）。
# 规矩（DESIGN-CONSISTENCY §8）：方法体逐 token 一致；10 份 PanelTheme md5 必须一致。
import re
import sys
from pathlib import Path

ROOT = Path(r'E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE')

THEMES = [
    ROOT / 'shared' / 'PanelTheme.cs',
    ROOT / 'voronoi' / 'src' / 'PanelTheme.cs',
    ROOT / 'stripe' / 'src' / 'PanelTheme.cs',
    ROOT / 'stripe' / 'center' / 'PanelTheme.cs',
    ROOT / 'halftone' / 'src' / 'PanelTheme.cs',
    ROOT / 'radialdots' / 'src' / 'PanelTheme.cs',
    ROOT / 'diamondfacet' / 'src' / 'PanelTheme.cs',
    ROOT / 'waterripple' / 'src' / 'PanelTheme.cs',
    ROOT / 'surfaceunify' / 'src' / 'PanelTheme.cs',
    ROOT / 'patchfill' / 'src' / 'PanelTheme.cs',
]
ICONMAKE = ROOT / 'iconmake' / 'Program.cs'

DISPATCH_ANCHOR = ('else if (string.Equals(kind, "unify", StringComparison.OrdinalIgnoreCase)) '
                   'GlassDrawUnify(g, side, accent);')
DISPATCH_NEW = DISPATCH_ANCHOR + ('\n                    else if (string.Equals(kind, "patchfill", '
                                  'StringComparison.OrdinalIgnoreCase)) GlassDrawPatchFill(g, side, accent);')

ACCENT_ANCHOR = ('if (string.Equals(kind, "unify", StringComparison.OrdinalIgnoreCase)) '
                 'return Color.FromArgb(255, 168, 69, 111);')
ACCENT_NEW = ACCENT_ANCHOR + ('\n            if (string.Equals(kind, "patchfill", StringComparison.OrdinalIgnoreCase)) '
                              'return Color.FromArgb(255, 194, 96, 58);')

METHOD = '''        /// <summary>多边补面：五边形边界环 + 环内一张补出来的曲面（5 个角点 + 两条 iso 肋线）</summary>
        private static void GlassDrawPatchFill(Graphics g, int size, Color accent)
        {
            float s = size;
            float cx = s * 0.5f, cy = s * 0.5f;
            float R = s * 0.335f;
            float stroke = Math.Max(1.0f, s * 0.062f);
            var ring = new PointF[5];
            for (int i = 0; i < 5; i++)
            {
                double a = -Math.PI / 2 + i * 2.0 * Math.PI / 5.0;
                ring[i] = new PointF(cx + R * (float)Math.Cos(a), cy + R * (float)Math.Sin(a));
            }
            var inner = new PointF[5];
            for (int i = 0; i < 5; i++)
            {
                double a = -Math.PI / 2 + i * 2.0 * Math.PI / 5.0 + 0.10;
                inner[i] = new PointF(cx + R * 0.68f * (float)Math.Cos(a), cy + R * 0.68f * (float)Math.Sin(a) - s * 0.015f);
            }
            using (var patch = new GraphicsPath())
            {
                patch.StartFigure();
                patch.AddClosedCurve(inner, 0.55f);
                var box = new RectangleF(cx - R, cy - R, R * 2f, R * 2f);
                using (LinearGradientBrush fill = new LinearGradientBrush(box,
                    GlassBlend(accent, Color.White, 0.46f), GlassBlend(accent, Color.Black, 0.18f), 90f))
                    g.FillPath(fill, patch);
                using (Pen rib = new Pen(Color.FromArgb(150, 14, 26, 38), Math.Max(0.42f, s * 0.020f)))
                {
                    rib.StartCap = LineCap.Round;
                    rib.EndCap = LineCap.Round;
                    g.DrawLine(rib, cx - R * 0.30f, cy - R * 0.30f, cx - R * 0.22f, cy + R * 0.34f);
                    g.DrawLine(rib, cx + R * 0.30f, cy - R * 0.30f, cx + R * 0.24f, cy + R * 0.34f);
                }
                using (Pen dark = new Pen(Color.FromArgb(196, 14, 26, 38), stroke + Math.Max(0.7f, s * 0.028f)))
                using (Pen main = new Pen(accent, stroke))
                {
                    dark.LineJoin = LineJoin.Round;
                    main.LineJoin = LineJoin.Round;
                    g.DrawPath(dark, patch);
                    g.DrawPath(main, patch);
                }
            }
            using (var loop = new GraphicsPath())
            {
                loop.StartFigure();
                loop.AddPolygon(ring);
                using (Pen dark = new Pen(Color.FromArgb(200, 14, 26, 38), stroke * 0.70f + Math.Max(0.5f, s * 0.020f)))
                using (Pen main = new Pen(GlassBlend(accent, Color.Black, 0.28f), stroke * 0.70f))
                {
                    dark.LineJoin = LineJoin.Round;
                    main.LineJoin = LineJoin.Round;
                    g.DrawPath(dark, loop);
                    g.DrawPath(main, loop);
                }
                using (var dot = new SolidBrush(GlassBlend(accent, Color.Black, 0.34f)))
                {
                    float d = Math.Max(1.4f, s * 0.10f);
                    for (int i = 0; i < 5; i++)
                        g.FillEllipse(dot, ring[i].X - d * 0.5f, ring[i].Y - d * 0.5f, d, d);
                }
            }
            using (Pen hi = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(0.6f, s * 0.020f)))
            {
                hi.StartCap = LineCap.Round;
                hi.EndCap = LineCap.Round;
                g.DrawLine(hi, cx - R * 0.26f, cy - R * 0.34f, cx + R * 0.10f, cy - R * 0.44f);
            }
        }
'''


def method_span(text):
    """GlassDrawPatchFill 方法体的 [start, end) 区间（花括号配平，不用猜缩进）。"""
    key = 'private static void GlassDrawPatchFill(Graphics g, int size, Color accent)'
    i = text.find(key)
    if i < 0:
        raise SystemExit('GlassDrawPatchFill not found')
    # 往回吃到行首（含 /// 注释行）
    line_start = text.rfind('\n', 0, i) + 1
    doc_start = text.rfind('\n', 0, line_start)
    j = text.find('{', i)
    depth = 0
    k = j
    while k < len(text):
        ch = text[k]
        if ch == '{':
            depth += 1
        elif ch == '}':
            depth -= 1
            if depth == 0:
                return doc_start + 1, k + 2      # 含换行
        k += 1
    raise SystemExit('unbalanced braces')


def patch(path):
    t = path.read_text(encoding='utf-8-sig')
    if 'GlassDrawPatchFill' in t:
        print('  already patched: %s' % path.name)
        return
    if t.count(DISPATCH_ANCHOR) != 1:
        raise SystemExit('dispatch anchor x%d in %s' % (t.count(DISPATCH_ANCHOR), path))
    if t.count(ACCENT_ANCHOR) != 1:
        raise SystemExit('accent anchor x%d in %s' % (t.count(ACCENT_ANCHOR), path))
    t = t.replace(DISPATCH_ANCHOR, DISPATCH_NEW)
    t = t.replace(ACCENT_ANCHOR, ACCENT_NEW)
    # 插到 GlassDrawUnify 方法之后
    a, b = span_of(t, 'private static void GlassDrawUnify(Graphics g, int size, Color accent)')
    t = t[:b] + METHOD + t[b:]
    path.write_text(t, encoding='utf-8')
    print('  patched: %s' % path)


def span_of(text, key):
    i = text.find(key)
    if i < 0:
        raise SystemExit('not found: ' + key)
    j = text.find('{', i)
    depth = 0
    k = j
    while k < len(text):
        ch = text[k]
        if ch == '{':
            depth += 1
        elif ch == '}':
            depth -= 1
            if depth == 0:
                return i, k + 2
        k += 1
    raise SystemExit('unbalanced braces in ' + key)


def norm_body(path):
    t = path.read_text(encoding='utf-8-sig')
    a, b = method_span(t)
    return re.sub(r'\s+', ' ', t[a:b]).strip()


if __name__ == '__main__':
    print('=== 加 patchfill 图标分支 ===')
    for p in THEMES + [ICONMAKE]:
        if not p.exists():
            raise SystemExit('missing: %s' % p)
        patch(p)

    print('=== 复验 ===')
    import hashlib
    digests = {}
    for p in THEMES:
        h = hashlib.md5(p.read_bytes()).hexdigest()
        digests.setdefault(h, []).append(p)
    for h, ps in digests.items():
        print('  md5 %s : %d 份' % (h, len(ps)))
    if len(digests) != 1:
        print('  !! PanelTheme 副本不一致')
        for h, ps in digests.items():
            for p in ps:
                print('     %s %s' % (h, p))
        sys.exit(1)
    base = norm_body(THEMES[0])
    same = True
    for p in THEMES[1:] + [ICONMAKE]:
        if norm_body(p) != base:
            print('  !! 方法体不一致: %s' % p)
            same = False
    print('  GlassDrawPatchFill 方法体逐 token 一致:', same)
    if not same:
        sys.exit(1)
    print('OK 10 份 PanelTheme + iconmake 已同步')
