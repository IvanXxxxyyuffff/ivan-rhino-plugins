from pathlib import Path
import json, hashlib, subprocess, zipfile, sys, html
sys.stdout.reconfigure(encoding='utf-8')
r=Path(__file__).resolve().parent
shots=[('01-launcher-light.png','主界面 · 浅色'),('02-launcher-dark.png','主界面 · 深色'),('03-panel-stripe.png','表面条纹'),('04-panel-halftone.png','参数化阵列纹理'),('05-panel-voronoi.png','泰森多边形纹'),('06-panel-radialdots.png','径向渐变圆点'),('07-panel-vape.png','烟油容量'),('08-icon-family.png','插件图标家族')]
cards=[]
for name,title in shots:
    assert (r/name).exists(),name
    cards.append(f'<section><h2>{title}</h2><a href="{name}" target="_blank"><img src="{name}" alt="{title}"></a></section>')
gallery='''<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>IVAN CENTER UI 效果总览</title><style>*{box-sizing:border-box}body{margin:0;padding:32px;background:#e4edf4;color:#26384a;font:14px "Segoe UI","Microsoft YaHei",sans-serif}main{max-width:1200px;margin:auto}header{display:flex;gap:16px;align-items:center;justify-content:space-between;margin-bottom:30px}h1{font-size:28px;margin:0}p{color:#788b9c;font-size:12px}a{color:#3479c6;text-decoration:none}header>a{padding:12px 20px;background:#fff9;border-radius:12px}section{margin:28px 0}h2{font-size:16px;font-weight:550;margin:0 0 12px}img{width:100%;height:auto;border-radius:20px;display:block;box-shadow:0 10px 40px #314d6720}footer{font-size:12px;color:#72879a;margin:30px 0}@media(max-width:600px){body{padding:16px}header{display:block}header>a{display:inline-block;margin:12px 0}h1{font-size:23px}}</style></head><body><main><header><div><h1>IVAN CENTER · UI 效果总览</h1><p>主界面、五个插件面板、统一液态玻璃图标；点击图片查看原尺寸。</p></div><a href="index.html">打开交互预览 →</a></header>'''+''.join(cards)+'''<footer>独立 UI 预览，未连接 Rhino 或执行安装。面板可滚动，包含既有参数；左侧造型仅为示意，不是几何计算结果。</footer></main></body></html>'''
(r/'gallery.html').write_text(gallery,encoding='utf-8')
transaction=json.loads((r/'transaction-verification.json').read_text(encoding='utf-8'))
temporary=r/'diff-reconstruction';temporary.mkdir(exist_ok=True)
(temporary/'index.html').write_bytes((r/'index.baseline.html').read_bytes())
git=r'C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\git\cmd\git.exe'
cmd=[git,'-c','core.autocrlf=false','apply',str(r/'UI-PREVIEW.patch')]
out=subprocess.run(cmd,cwd=temporary,capture_output=True,text=True,encoding='utf-8')
assert out.returncode==0,(out.returncode,out.stderr)
assert (temporary/'index.html').read_bytes()==(r/'index.html').read_bytes()
with (r/'VERIFICATION.txt').open('a',encoding='utf-8') as f:
    f.write('\nPREVIEW-ONLY TRANSACTION\nChanged field: index.html script[src=art.js] added before app.js.\n')
    for record in transaction['records']:
        f.write('\n'+record['label']+'\nCOMMAND '+json.dumps(record['command'],ensure_ascii=False)+'\nSTDOUT '+repr(record['stdout'])+'\nSTDERR '+repr(record['stderr'])+'\nEXIT '+str(record['exit'])+'\nSHA256 '+record.get('sha256','n/a')+'\n')
    f.write('\nPATCH_RECONSTRUCTION\nCOMMAND '+json.dumps(cmd)+'\nSTDOUT '+repr(out.stdout)+'\nSTDERR '+repr(out.stderr)+'\nEXIT '+str(out.returncode)+'\nRECONSTRUCTED_BYTES_EQUAL_MODIFIED True\n')
    f.write('\nObserved retries: initial shell helper setup refresh error; specified session file absent (matched SQLite task index instead); GBK output encoding error repaired with UTF-8; source filename StripeCore.cs absent (read StripePattern.cs); first browser readiness timeout (candidate script not yet saved, interrupted process exit=1); first render mismatched CSS classes/repeated handler fixed; optional background preview server command was rejected before execution (no server claimed started); first finalize-preview.py exit=1, stdout empty, stderr AssertionError at byte-comparison line 19, repaired git patch reconstruction command with core.autocrlf=false.\n')
    f.write('\nOriginal snapshot: concurrent changes observed in eight C:/zcode_build files. This preview and its workers wrote only the separate preview directory. Those concurrent changes were not reverted. The original installer UI and shared theme retained their initial hashes.\n')
    f.write('\nCURRENT_SCOPE UI preview only. No actual plugin compile/install or Rhino geometry self-tests requested/executed at this confirmation stage.\n')
    f.write('\nROLE_HASHES\n')
    for name in ['index.html','UI-PREVIEW.patch','ROLLBACK.sh','styles.css','app.js','panels-data.js','icons.js','art.js']:
        f.write(name+' '+hashlib.sha256((r/name).read_bytes()).hexdigest()+'\n')
# Reopen the four roles and confirm final field is still applied.
for name in ['index.html','UI-PREVIEW.patch','VERIFICATION.txt','ROLLBACK.sh']:
    raw=(r/name).read_bytes();assert raw
    print('REOPENED',str(r/name),'bytes='+str(len(raw)))
assert 'src="art.js"' in (r/'index.html').read_text(encoding='utf-8')
report=json.loads((r/'browser-verification.json').read_text(encoding='utf-8'))
assert all(c['ok'] for c in report['checks'])
print('BROWSER_CHECKS',len(report['checks']),'all_passed=True')
bundle=r/'IVAN-LiquidGlass-preview.zip'
names=['index.html','gallery.html','styles.css','app.js','panels-data.js','icons.js','art.js','UI-PREVIEW.patch','VERIFICATION.txt','ROLLBACK.sh','control-map.json','browser-verification.json','transaction-verification.json']+[n for n,_ in shots]+['detail-'+k+'.png' for k in ['stripe','halftone','voronoi','radialdots','vape']]
with zipfile.ZipFile(bundle,'w',zipfile.ZIP_DEFLATED) as z:
    for name in names:z.write(r/name,name)
    for p in (r/'icons').glob('*.svg'):z.write(p,'icons/'+p.name)
with zipfile.ZipFile(bundle) as z:assert z.testzip() is None
print('ZIP_VERIFIED',str(bundle),'bytes='+str(bundle.stat().st_size))
checkpoint={'ACTIVE_OBJECT':str(r/'index.html'),'LAST_CONFIRMED_RESULT':f"{len(report['checks'])} browser checks passed; main + 5 panel screenshots; preview-only rollback and diff reconstruction verified",'NEXT_EXECUTABLE_ACTION':'Await human UI design confirmation before changing production UI','INPUT_PATHS':[str(r/'index.html'),str(r/'gallery.html')],'ACCEPTANCE_EVENT':'UI preview rendered and reopened; actual plugin functions were not connected or deployed','status':'awaiting_design_confirmation'}
(r/'checkpoint.json').write_text(json.dumps(checkpoint,ensure_ascii=False,indent=2),encoding='utf-8')
