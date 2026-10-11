import json,os,subprocess,uuid,winreg
import xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];VERSION=ET.parse(ROOT/'Directory.Build.props').findtext('PropertyGroup/Version');OUT=ROOT/'temp/validation/updates/installers';OUT.mkdir(parents=True,exist_ok=True)
report={'tests':[],'visibleWindows':False,'hardwareWrites':0}
def save(): (OUT/'results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
for channel in ['Lite','Full']:
 key='Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DisplayControlLab.'+channel+'_is1'
 try:
  with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key):raise RuntimeError('Existing install registry entry; refuse to replace: '+channel)
 except FileNotFoundError:pass
 target=OUT/(channel+'-'+uuid.uuid4().hex);installer=ROOT/'temp/release'/('DisplayControlLab-'+VERSION+'-'+channel+'-Setup.exe')
 env=os.environ.copy();native_temp=OUT/('native-'+channel);native_temp.mkdir(exist_ok=True);env['TEMP']=env['TMP']=str(native_temp);env['DISPLAYCONTROL_DATA']=str(OUT/('data-'+uuid.uuid4().hex))
 test={'channel':channel,'target':str(target)};report['tests'].append(test);save()
 try:
  p=subprocess.run([str(installer),'/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/NOICONS','/DIR='+str(target),'/LOG='+str(OUT/(channel+'-install.log'))],env=env,creationflags=subprocess.CREATE_NO_WINDOW,timeout=120)
  test['installExit']=p.returncode;assert p.returncode==0
  marker=json.loads((target/'update-install.json').read_text(encoding='utf-8'));assert marker['version']==VERSION and marker['channel']==channel
  if channel=='Full':
   config=json.loads((target/'DisplayControl.Controller.runtimeconfig.json').read_text(encoding='utf-8'));assert 'includedFrameworks' in config['runtimeOptions'];test['selfContainedIdentity']=True
   env['DOTNET_ROOT']=str(target/'no-global-runtime');env['DOTNET_MULTILEVEL_LOOKUP']='0'
  p=subprocess.run([str(target/'DisplayControl.Controller.exe'),'--update-verify-startup'],capture_output=True,encoding='utf-8',env=env,creationflags=subprocess.CREATE_NO_WINDOW,timeout=40)
  assert p.returncode==0 and json.loads(p.stdout)['version']==VERSION;test['nativeEntryExit']=p.returncode;test['identityVerified']=True;save()
 finally:
  if (target/'unins000.exe').exists():
   p=subprocess.run([str(target/'unins000.exe'),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LOG='+str(OUT/(channel+'-uninstall.log'))],env=env,creationflags=subprocess.CREATE_NO_WINDOW,timeout=120)
   test['uninstallExit']=p.returncode;assert p.returncode==0
   try:
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key):raise RuntimeError('Uninstall registry remains')
   except FileNotFoundError:test['registryCleaned']=True
  save()
print(json.dumps(report,ensure_ascii=False,indent=2))
