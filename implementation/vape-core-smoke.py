# -*- coding: utf-8 -*-
import Rhino
import System
import io
import json
import os
import traceback
import clr
clr.AddReference('System.Windows.Forms')
from System.Windows.Forms import Timer

R = r'E:\IVAN-LiquidGlass-preview\implementation'
doc = Rhino.RhinoDoc.ActiveDoc
System.IO.File.WriteAllText(os.path.join(R, 'vape-core-smoke-started.txt'), 'STARTED')
result = {'command': '_VapeVolumeSelfTest', 'objectsBefore': int(doc.Objects.Count), 'objectsAfter': None}
try:
    if doc.Objects.Count != 0:
        raise RuntimeError('Own temporary document must be empty')
    result['commandReturn'] = bool(Rhino.RhinoApp.RunScript('_VapeVolumeSelfTest', False))
    result['literal'] = unicode(System.IO.File.ReadAllText(r'D:\UserData\Desktop\VapeVolume自检结果.txt'))
    result['objectsAfter'] = int(doc.Objects.Count)
    result['B_skipped'] = u'B 布尔差集失败，跳过' in result['literal']
    result['A_C_D_passed'] = result['literal'].count(u'→ 通过') == 3
    result['E_passed'] = u'结果：自动更新成功' in result['literal']
    result['F_passed'] = u'规则全部正确：是' in result['literal']
    result['done'] = u'DONE' in result['literal']
except:
    result['exception'] = unicode(traceback.format_exc())
finally:
    with io.open(os.path.join(R, 'vape-core-smoke-result.json'), 'w', encoding='utf-8') as f:
        f.write(unicode(json.dumps(result, ensure_ascii=False, indent=2)))
    # This process and its initially empty document are owned synthetic fixtures.
    # Preserve the real residual count, then discard the temporary document only.
    def finish_owned_test(sender, args):
        _exit_timer.Stop()
        doc.Modified = False
        Rhino.RhinoApp.Exit()
    _exit_timer = Timer()
    _exit_timer.Interval = 300
    _exit_timer.Tick += finish_owned_test
    _exit_timer.Start()
