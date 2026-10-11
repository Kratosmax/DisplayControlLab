import ctypes,http.server,json,os,subprocess,sys,threading,time,uuid,zipfile
import xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];VERSION=ET.parse(ROOT/'Directory.Build.props').findtext('PropertyGroup/Version');OUT=ROOT/'temp/validation/updates/release-smoke';OUT.mkdir(parents=True,exist_ok=True);REPORT={'channels':[],'visibleWindows':False,'hardwareWrites':0,'baselineNote':'Isolated predecessor fixture; actual official 0.2.0 has no updater and requires manual transition'}
def save(): (OUT/'results.json').write_text(json.dumps(REPORT,ensure_ascii=False,indent=2),encoding='utf-8')
class Proxy(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  data=self.server.payload
  self.send_response(200);self.send_header('Content-Length',str(len(data)));self.end_headers();self.wfile.write(data)
 def log_message(self,*args):pass

def run(exe,args,env,timeout=120):
 p=subprocess.run([str(exe),*map(str,args)],capture_output=True,encoding='utf-8',env=env,creationflags=subprocess.CREATE_NO_WINDOW,timeout=timeout)
 if p.returncode:raise RuntimeError('Command failed '+str(args)+': '+p.stderr[:1000])
 return p.stdout

def start_ticks(process):
 stamps=[ctypes.c_uint64() for _ in range(4)]
 kernel=ctypes.windll.kernel32;kernel.GetProcessTimes.argtypes=[ctypes.c_void_p,*[ctypes.POINTER(ctypes.c_uint64)]*4]
 if not kernel.GetProcessTimes(ctypes.c_void_p(int(process._handle)),*[ctypes.byref(s) for s in stamps]):raise RuntimeError('GetProcessTimes failed')
 return stamps[0].value+504911232000000000
try:
 for channel in ['Lite','Full']:
  case={'channel':channel};REPORT['channels'].append(case);save()
  fixture=OUT/(channel+'-'+uuid.uuid4().hex);fixture.mkdir();target=fixture/'install'
  package=ROOT/'temp/release'/('DisplayControlLab-'+VERSION+'-'+channel+'.zip');manifest=ROOT/'temp/release'/('update-'+channel.lower()+'.json')
  with zipfile.ZipFile(package) as z:z.extractall(target)
  marker=json.loads((target/'update-install.json').read_text(encoding='utf-8'));marker['version']='0.2.0';(target/'update-install.json').write_text(json.dumps(marker),encoding='utf-8')
  data=fixture/'user-data';data.mkdir();settings=data/'update-settings';settings.mkdir()
  server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Proxy);server.payload=package.read_bytes()
  thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
  (settings/('33333333333333333333333333333333.json')).write_text(json.dumps({'id':'33333333333333333333333333333333','httpProxy':'http://127.0.0.1:'+str(server.server_port),'routes':[{'baseUrl':'http://release-test.invalid','priority':10,'isDirect':False},{'baseUrl':'','priority':0,'isDirect':True}]}),encoding='utf-8')
  sentinel=data/'preserved-user.txt';sentinel.write_text('preserve')
  env=os.environ.copy();env['DISPLAYCONTROL_DATA']=str(data)
  if channel=='Full':env['DOTNET_ROOT']=str(fixture/'no-runtime');env['DOTNET_MULTILEVEL_LOOKUP']='0'
  exe=target/'DisplayControl.Controller.exe'
  run(exe,['--update-inspect',package,manifest],env);case['hostActualZipInspection']=True;save()
  prepared=json.loads(run(exe,['--update-prepare',manifest],env));case['hostDownloadedVerifiedPrepared']=True;case['launcherOutsideInstall']=not Path(prepared['LauncherPath']).is_relative_to(target);save()
  server.shutdown();server.server_close()
  sleeper=subprocess.Popen([sys.executable,'-c','import time;time.sleep(2)'],creationflags=subprocess.CREATE_NO_WINDOW)
  ticks=start_ticks(sleeper)
  updater_args=['--package',prepared['PackagePath'],'--manifest',prepared['ManifestPath'],'--target',str(target),'--pid',str(sleeper.pid),'--started',str(ticks),'--entry','DisplayControl.Controller.exe','--verify-startup','true']
  case['updaterOutput']=run(Path(prepared['LauncherPath']),updater_args,env)
  sleeper.wait(timeout=10)
  for _ in range(100):
   if not (target/'.displaycontrol-update').exists() and not (target/'update-result.json').exists():break
   time.sleep(0.1)
  assert json.loads((target/'update-install.json').read_text(encoding='utf-8'))['version']==VERSION
  assert not (target/'.displaycontrol-update').exists()
  assert sentinel.read_text()=='preserve'
  case['externalWaitReplaceRestartStartupCleanup']=True;case['userDataPreserved']=True;save()
except Exception as e:REPORT['error']=str(e);save();raise
save();print(json.dumps(REPORT,ensure_ascii=False,indent=2))
