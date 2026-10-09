from pathlib import Path
import runpy, json, shutil
R=Path(__file__).resolve().parent
run=runpy.run_path(str(R/'native-gate.py'))['run']
script='/runscript="_-New _None _Enter _-RunPythonScript '+str(R/'vape-core-smoke.py')+' _Enter"'
results={}
for label,exe in [('BASELINE-LATEST',R/'baseline-latest-IVAN-CENTER.exe'),('MODIFIED-FINAL',R/'MODIFIED_FILE/stripe/out/center/IVAN-CENTER.exe')]:
    run(label+'-VapeCore-install',[str(exe),'/silent'])
    run(label+'-VapeCore-command',['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(R/'run-vape-core-process.ps1')])
    p=R/'vape-core-smoke-result.json'
    j=json.loads(p.read_text(encoding='utf-8-sig'))
    shutil.copy2(p,R/(label+'-VapeCore.json'))
    (R/(label+'-VapeVolumeSelfTest.txt')).write_text(j.get('literal',''),encoding='utf-8')
    assert j.get('commandReturn') and j.get('objectsBefore')==0,j
    assert all(j.get(k) for k in ['A_C_D_passed','E_passed','F_passed','done']),j
    results[label]=j
    with (R.parent/'VERIFICATION.txt').open('a',encoding='utf-8') as f:f.write('\n'+label+' VAPE ALGORITHM LITERAL AND CLEANUP\n'+json.dumps(j,ensure_ascii=False,indent=2)+'\n')
assert results['BASELINE-LATEST']['literal']==results['MODIFIED-FINAL']['literal'],'Vape core baseline/modified literal differs'
assert results['BASELINE-LATEST']['objectsAfter']==results['MODIFIED-FINAL']['objectsAfter']
result={'baseline_modified_literal_equal':True,'B_skipped_in_both':results['BASELINE-LATEST']['B_skipped'],'remaining_A_C_D_E_F_passed':True,'selftest_residual_objects_both':results['BASELINE-LATEST']['objectsAfter'],'own_temporary_documents_discarded_after_recording':True}
(R/'vape-core-comparison.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False,indent=2))
