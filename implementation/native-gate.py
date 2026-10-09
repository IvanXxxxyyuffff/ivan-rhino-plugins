from pathlib import Path
import subprocess,json,sys,datetime,hashlib,shutil
R=Path(__file__).resolve().parent; C=R/'MODIFIED_FILE'; V=R.parent/'VERIFICATION.txt'; D=r'C:\zcode_tools\dotnet\dotnet.exe'
def run(label,argv,cwd=None):
 print('START '+label,flush=True)
 cp=R/'checkpoint.json'; state=json.loads(cp.read_text(encoding='utf-8-sig'))
 state.update(LAST_CONFIRMED_RESULT='Starting '+label,NEXT_EXECUTABLE_ACTION={'command':[sys.executable,'-X','utf8',str(__file__)]+sys.argv[1:]}, pending=label)
 cp.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf-8')
 p=subprocess.run(argv,cwd=cwd or str(C),capture_output=True,text=True,encoding='utf-8',errors='replace')
 event=dict(label=label,command=argv,cwd=str(cwd or C),exit=p.returncode,stdout=p.stdout,stderr=p.stderr,time=datetime.datetime.now().isoformat())
 with (R/'native-run.jsonl').open('a',encoding='utf-8') as f: f.write(json.dumps(event,ensure_ascii=False)+'\n')
 with V.open('a',encoding='utf-8') as f: f.write('\nNATIVE_COMMAND '+json.dumps(argv,ensure_ascii=False)+'\nLABEL '+label+'\nCWD '+str(cwd or C)+'\nSTDOUT:\n'+p.stdout+'\nSTDERR:\n'+p.stderr+'\nEXIT '+str(p.returncode)+'\n')
 (R/(label+'.log')).write_text(p.stdout+'\n[STDERR]\n'+p.stderr,encoding='utf-8')
 print(p.stdout[-3500:]); print(p.stderr[-1500:]); print('EXIT '+label+' '+str(p.returncode),flush=True)
 if p.returncode: sys.exit(p.returncode)
 return event
if __name__=='__main__':
 action=sys.argv[1]
 if action=='build-panels':
  jobs=[('voronoi8','voronoi/Rhino8/VoronoiTexture.csproj'),('voronoi7','voronoi/Rhino7/VoronoiTexture.csproj'),('stripe8','stripe/Rhino8/StripeOnSurface.csproj'),('stripe7','stripe/Rhino7/StripeOnSurface.csproj'),('halftone8','halftone/Rhino8/HalftoneDots.csproj'),('halftone7','halftone/Rhino7/HalftoneDots.csproj'),('radialdots8','radialdots/Rhino8/RadialDots.csproj'),('radialdots7','radialdots/Rhino7/RadialDots.csproj'),('meshfix8','meshfix/Rhino8/MeshFix.csproj'),('meshfix7','meshfix/Rhino7/MeshFix.csproj'),('diamondfacet8','diamondfacet/Rhino8/DiamondFacet.csproj'),('diamondfacet7','diamondfacet/Rhino7/DiamondFacet.csproj')]
  for tag,p in jobs: run('build-'+tag,[D,'build',str(C/p),'-c','Release','--nologo','-v','minimal'])
 elif action=='build-vape-center':
  run('build-vape',[D,'build',str(C/'src/VapeVolume/VapeVolume.csproj'),'-c','Release','--nologo','-v','minimal'])
  P=C/'stripe/center/payload'
  for folder,name in [('voronoi','VoronoiTexture'),('stripe','StripeOnSurface'),('halftone','HalftoneDots'),('radialdots','RadialDots'),('meshfix','MeshFix'),('diamondfacet','DiamondFacet')]:
   for ver in [7,8]: shutil.copy2(C/folder/'out'/('rhino'+str(ver))/(name+'.rhp'),P/(name+'-rh'+str(ver)+'.rhp'))
   for suffix in ['.dll','.deps.json','.runtimeconfig.json']:
    src=C/folder/'out/rhino8'/(name+suffix)
    if src.exists(): shutil.copy2(src,P/(name+suffix))
  for ver,tfm in [(7,'net48'),(8,'net7.0-windows')]: shutil.copy2(C/'src/VapeVolume/bin/Release'/tfm/'VapeVolume.rhp',P/('VapeVolume-Rhino'+str(ver)+'.rhp'))
  for suffix in ['.dll','.deps.json','.runtimeconfig.json']:
   src=C/'src/VapeVolume/bin/Release/net7.0-windows'/('VapeVolume'+suffix)
   if src.exists(): shutil.copy2(src,P/('VapeVolume'+suffix))
  print('PAYLOAD_REFRESHED original resource filenames retained',flush=True)
  run('build-center',[D,'build',str(C/'stripe/center/Center.csproj'),'-c','Release','--nologo','-v','minimal'])
 elif action=='uitest':
  exe=Path(sys.argv[2]); label=sys.argv[3]; run(label,[str(exe),'/uitest'])
  import os
  for suffix in ['.log','.png']:
   src=Path(os.environ['TEMP'])/('IVANCENTER-uitest'+suffix)
   if src.exists(): shutil.copy2(src,R/(label+suffix))
  print((R/(label+'.log')).read_text(encoding='utf-8-sig'),flush=True)
