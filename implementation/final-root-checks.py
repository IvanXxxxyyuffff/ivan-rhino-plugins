from pathlib import Path
import sys, json, hashlib, shutil, runpy, os, datetime

R = Path(__file__).resolve().parent
C = R / 'MODIFIED_FILE'
V = R.parent / 'VERIFICATION.txt'
gate = runpy.run_path(str(R / 'native-gate.py'))
run = gate['run']
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()

if sys.argv[1] == 'rollback':
    T = R / 'ROLLBACK_TEST_FINAL'
    shutil.copytree(C, T, dirs_exist_ok=True, ignore=shutil.ignore_patterns('bin', 'obj'))
    run('ROLLBACK-FINAL-restore', [r'C:\Program Files\Git\bin\bash.exe', str(R.parent / 'ROLLBACK.sh').replace('\\','/'), str(T).replace('\\','/')])
    m = json.loads((R / 'source-latest.json').read_text(encoding='utf-8-sig'))
    mismatches = [x['relative'] for x in m['files'] if sha(T / x['relative']) != x['sha256']]
    assert not mismatches, mismatches
    result = {'restored_source_files': len(m['files']), 'hash_mismatches': mismatches, 'candidate_kept_modified': sha(C/'src/VapeVolume/UI/VolumeDialog.cs') != sha(T/'src/VapeVolume/UI/VolumeDialog.cs')}
    assert result['candidate_kept_modified']
    run('ROLLBACK-FINAL-build-center', [r'C:\zcode_tools\dotnet\dotnet.exe', 'build', str(T/'stripe/center/Center.csproj'), '-c', 'Release', '--nologo', '-v', 'minimal', '-t:Rebuild'])
    exe = T/'stripe/out/center/IVAN-CENTER.exe'
    run('ROLLBACK-FINAL-uitest', [str(exe), '/uitest'])
    for ext in ['.log','.png']:
        shutil.copy2(Path(os.environ['TEMP']) / ('IVANCENTER-uitest'+ext), R/('ROLLBACK-FINAL-uitest'+ext))
    literal = (R/'ROLLBACK-FINAL-uitest.log').read_text(encoding='utf-8-sig')
    run('ROLLBACK-FINAL-center-full', [str(R/'NativeCenterAudit.exe'), str(exe), str(R/'ROLLBACK-FINAL-center-full.png')])
    result.update(uitest_literal=literal, passed=True)
    (R/'rollback-final-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    with V.open('a',encoding='utf-8') as f: f.write('\nFINAL_ROLLBACK_HASHES_AND_LITERAL_REPORT\n'+json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(result,ensure_ascii=False,indent=2))

elif sys.argv[1] == 'installed':
    payload = C/'stripe/center/payload'
    installed = Path(os.environ['LOCALAPPDATA'])/'IVAN/plugins'
    entries = []
    for p in installed.rglob('*'):
        if not p.is_file() or p.suffix not in ['.rhp','.dll','.json']: continue
        key = p.relative_to(installed).parts[0]
        rel = str(p.relative_to(installed))
        ver = 7 if p.relative_to(installed).parts[1].lower() in ('rh7','rhino7') else 8
        if p.suffix == '.rhp':
            fn = key + ('-Rhino' if key == 'VapeVolume' else '-rh') + str(ver) + '.rhp'
        else: fn = p.name
        src = payload/fn
        if not src.exists(): continue
        entries.append({'installed_path':str(p),'payload_path':str(src),'sha256':sha(p),'matches':sha(p)==sha(src)})
    assert entries and all(x['matches'] for x in entries), entries
    assert {Path(x['installed_path']).relative_to(installed).parts[0] for x in entries} == {'VoronoiTexture','StripeOnSurface','HalftoneDots','RadialDots','VapeVolume'}
    result = {'passed':True,'matched_installed_files':len(entries),'files':entries}
    (R/'installed-final-audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    with V.open('a',encoding='utf-8') as f:f.write('\nFINAL_INSTALLED_PAYLOAD_HASHES\n'+json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print('INSTALLED_ALL_FIVE_PLUGINS_MATCH_PAYLOAD',len(entries))
