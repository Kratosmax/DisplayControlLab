using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using MonitorInspector;
using MonitorInspector.Updates;

string root=Path.GetFullPath(args[0]), evidence=Path.GetFullPath(args[1]);Directory.CreateDirectory(evidence);
Environment.SetEnvironmentVariable("DISPLAYCONTROL_DATA",Path.Combine(evidence,"data-"+Guid.NewGuid().ToString("N")));
var checks=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
void Reject(Action act,string name){try{act();}catch{checks.Add(name);return;}throw new Exception("未拒绝："+name);}
async Task RejectAsync(Func<Task> act,string name){try{await act();}catch{checks.Add(name);return;}throw new Exception("未拒绝："+name);}
var settings=new UpdateSettings{Routes=[new("https://route.test/",5),new("https://route.test",5),new("https://other.test/github",5),new("",1,true)]};
var normalized=settings.Normalize();Check(normalized.Routes.Count==3,"线路去重与结尾斜杠规范化");
var url=new Uri(UpdateTrust.Repository+"/releases/latest/download/update.json");
Check(normalized.Candidates(url).Select(x=>x.Name).SequenceEqual(new[]{"route.test","other.test","GitHub 直连"}),"同级稳定顺序与直连回退");
Check(normalized.Candidates(url).ElementAt(1).Uri.AbsoluteUri.StartsWith("https://other.test/github/https://github.com/"),"带路径前缀拼接");
Check(new UpdateSettings{Routes=[]}.Normalize().Routes.Single().IsDirect,"缺失直连补回");
Check(normalized.Candidates(new Uri("https://example.org/test")).Single().Uri.Host=="example.org","非GitHub不改写");
Reject(()=>new UpdateSettings{Routes=[new("",0,true)]}.Normalize(),"全部禁用拒绝");
foreach(var value in new[]{"https://u:p@proxy.test","https://proxy.test?q=x","https://proxy.test#x","file:///x"})Reject(()=>new UpdateSettings{Routes=[new(value,5)]}.Normalize(),"拒绝非法前缀 "+value.Split(':')[0]);
foreach(var value in new[]{"socks5://localhost:1","https://localhost:1","http://u:p@localhost:1","http://localhost:1/a"})Reject(()=>new UpdateSettings{HttpProxy=value}.Normalize(),"拒绝不支持网络代理 "+value.Split(':')[0]);
Check(new UpdateSettings{HttpProxy="http://127.0.0.1:7890/"}.Normalize().HttpProxy=="http://127.0.0.1:7890","HTTP authority规范化");
normalized.Save();Check(UpdateSettings.Load().Routes.Count==3,"规范化设置原子持久化");
using var rsa=RSA.Create(2048);string publicKey=rsa.ExportSubjectPublicKeyInfoPem();
UpdateManifest Make(string notes="current notes",string channel="Lite")=>new(){Version=UpdateIdentity.CurrentVersion.ToString(3),Channel=channel,DownloadUrl=UpdateTrust.Repository+"/releases/download/v"+UpdateIdentity.CurrentVersion.ToString(3)+"/DisplayControlLab-"+UpdateIdentity.CurrentVersion.ToString(3)+"-"+channel+".zip",Size=5,Sha256=new string('a',64),ReleaseNotes=notes};
var unsigned=Make();string sig=UpdateManifestCodec.Sign(unsigned,rsa.ExportPkcs8PrivateKeyPem());
string Signed(UpdateManifest m)=>UpdateManifestCodec.Serialize(new(){Version=m.Version,Channel=m.Channel,DownloadUrl=m.DownloadUrl,Size=m.Size,Sha256=m.Sha256,ReleaseNotes=m.ReleaseNotes,Signature=sig});
string signed=Signed(unsigned);Check(UpdateManifestCodec.ParseAndVerify(signed,publicKey,"Lite").Version.ToString(3)==UpdateIdentity.CurrentVersion.ToString(3),"清单RSA签名验证");
Reject(()=>UpdateManifestCodec.ParseAndVerify(Signed(Make("changed notes")),publicKey,"Lite"),"公告也包含在签名载荷中");
Reject(()=>UpdateManifestCodec.ParseAndVerify(signed,publicKey,"Full"),"跨通道清单拒绝");
foreach(string path in new[]{"../a","/a","C:/a","a/../b","a./b","a /b","NUL.txt","a:stream",".displaycontrol-update/file","a//b"})Reject(()=>UpdatePackageValidator.NormalizeRelativePath(path),"拒绝不安全ZIP路径 "+path);
var handler=new Handler((request,index)=>new HttpResponseMessage(index==0?HttpStatusCode.BadGateway:HttpStatusCode.OK){Content=new StringContent(signed)});
using(var network=new UpdateNetwork(normalized,handler)){var got=await network.Fetch(url,65536,json=>UpdateManifestCodec.ParseAndVerify(json,publicKey,"Lite"),CancellationToken.None);Check(got.Channel=="Lite"&&handler.Calls.Count==2,"第一线路HTTP失败后验证下一线路签名");}
var forged=new Handler((r,i)=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(i==0?"<html>error</html>":signed)});
using(var network=new UpdateNetwork(normalized,forged)){await network.Fetch(url,65536,json=>UpdateManifestCodec.ParseAndVerify(json,publicKey,"Lite"),CancellationToken.None);Check(forged.Calls.Count==2,"伪造HTML拒绝并切换线路");}
var redirect=new Handler((r,i)=>{var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("https://untrusted.test/");return response;});
using(var network=new UpdateNetwork(new(),redirect))await RejectAsync(async()=>{await network.Fetch(url,65536,s=>s,CancellationToken.None);},"发送重定向前执行allowlist");
using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();var h=new Handler((r,i)=>new(HttpStatusCode.OK));using var network=new UpdateNetwork(normalized,h);await RejectAsync(async()=>{await network.Fetch(url,65536,s=>s,cancellation.Token);},"用户取消不继续轮询");Check(h.Calls.Count==0,"取消前零HTTP请求");}
if(args.Length>2){
 string package=Path.GetFullPath(args[2]);string channel=args.Length>3?args[3]:"Lite";
 var info=new UpdateInfo(UpdateIdentity.CurrentVersion,channel,new Uri(UpdateTrust.Repository+"/releases/download/v"+UpdateIdentity.CurrentVersion.ToString(3)+"/DisplayControlLab-"+UpdateIdentity.CurrentVersion.ToString(3)+"-"+channel+".zip"),new FileInfo(package).Length,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))),"notes","test");
 var validated=await UpdatePackageValidator.ValidateAsync(package,info);Check(validated.Metadata.Version==UpdateIdentity.CurrentVersion.ToString(3),"真实压缩流入口版本、通道、必需文件预检");
 await RejectAsync(async()=>{await UpdatePackageValidator.ValidateAsync(package,info with{Sha256=new string('0',64)});},"坏哈希拒绝");
 await RejectAsync(async()=>{await UpdatePackageValidator.ValidateAsync(package,info with{Channel=channel=="Lite"?"Full":"Lite"});},"真实ZIP跨通道拒绝");
 await RejectAsync(async()=>{await UpdatePackageValidator.ValidateAsync(package,info with{Version=new Version(UpdateIdentity.CurrentVersion.Major,UpdateIdentity.CurrentVersion.Minor,UpdateIdentity.CurrentVersion.Build+1)});},"真实程序集与清单版本不符拒绝");
 using(var proxy=new LocalProxy(signed,File.ReadAllBytes(package))) {
  var proxied=new UpdateSettings{HttpProxy=proxy.Address,Routes=[new("http://first.invalid",10),new("http://second.invalid",9),new("",0,true)]};
  using var network=new UpdateNetwork(proxied);
  await network.Fetch(url,65536,json=>UpdateManifestCodec.ParseAndVerify(json,publicKey,"Lite"),CancellationToken.None);
  await network.Download(info,Path.Combine(evidence,"http-proxy-"+Guid.NewGuid().ToString("N")),null,CancellationToken.None);
  Check(proxy.Requests.Count==3&&proxy.Requests.ElementAt(0).Contains("first.invalid/https://github.com")&&proxy.Requests.ElementAt(1).Contains("second.invalid/https://github.com"),"真实HTTP代理覆盖清单故障转移与ZIP下载");
 }
 string folder=Path.Combine(evidence,"install-"+Guid.NewGuid().ToString("N"));ZipFile.ExtractToDirectory(package,folder);
 var marker=new UpdatePackageMetadata{Version="0.2.0",Channel=channel,Files=["obsolete-software.dll"]};File.WriteAllText(Path.Combine(folder,"obsolete-software.dll"),"old");File.WriteAllText(Path.Combine(folder,"update-install.json"),JsonSerializer.Serialize(marker));
 using(var lease=UpdateIdentity.Lease(folder,false))await RejectAsync(async()=>{await UpdateInstaller.Install(package,folder,info);},"运行中工具文件租约阻止替换");
 string untouched=Path.Combine(folder,"user-unowned.txt");File.WriteAllText(untouched,"keep");
 string locked=Path.Combine(folder,"DisplayControl.Reader.exe");byte[] original=File.ReadAllBytes(Path.Combine(folder,"update-install.json"));
 using(var held=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None))await RejectAsync(async()=>{await UpdateInstaller.Install(package,folder,info);},"真实文件占用触发替换失败与回滚");
 Check(File.ReadAllBytes(Path.Combine(folder,"update-install.json")).SequenceEqual(original)&&!Directory.Exists(Path.Combine(folder,".displaycontrol-update")),"失败回滚原安装身份并清理事务");
 await UpdateInstaller.Install(package,folder,info);Check(UpdateIdentity.Read(folder).Version==UpdateIdentity.CurrentVersion.ToString(3),"实际stage备份替换成功");
 Check(File.ReadAllText(untouched)=="keep","非软件文件不覆盖");Check(!File.Exists(Path.Combine(folder,"obsolete-software.dll")),"更新仅清理原软件清单中已废弃的文件");
 Check(UpdateIdentity.Startup(folder)?.Contains("更新完成")==true&&!Directory.Exists(Path.Combine(folder,".displaycontrol-update")),"下一次启动确认成功并清理备份");
 string transaction=Path.Combine(folder,".displaycontrol-update");Directory.CreateDirectory(Path.Combine(transaction,"backup"));
 File.WriteAllText(Path.Combine(folder,"README.md"),"interrupted new");File.WriteAllText(Path.Combine(transaction,"backup/README.md"),"original readme");
 File.WriteAllText(Path.Combine(transaction,"transaction.json"),JsonSerializer.Serialize(new UpdateTransaction{Phase="replacing",Changes=[new("README.md",true)]}));
 Check(UpdateIdentity.Startup(folder)?.Contains("恢复原版本")==true&&File.ReadAllText(Path.Combine(folder,"README.md"))=="original readme","模拟进程中断后启动恢复备份");
 string downloadFolder=Path.Combine(evidence,"download-"+Guid.NewGuid().ToString("N"));
 var bytes=File.ReadAllBytes(package);var downloader=new Handler((r,i)=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
 var progress=new List<int>();using(var network=new UpdateNetwork(new(),downloader)){string downloaded=await network.Download(info,downloadFolder,new InlineProgress(p=>{if(p.Percent is int n)progress.Add(n);}),CancellationToken.None);Check(File.Exists(downloaded)&&!File.Exists(Path.Combine(downloadFolder,"package.download")),"流关闭后校验并原子改名，避免占用错误");}
 Check(progress.Last()==100&&progress.Take(progress.Count-1).All(n=>n<100)&&progress.SequenceEqual(progress.Order()),"校验前不显示100%，进度单调");
 var badDownload=new Handler((r,i)=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[bytes.Length])});
 using(var network=new UpdateNetwork(new(),badDownload))await RejectAsync(async()=>{await network.Download(info,downloadFolder,null,CancellationToken.None);},"下载错误哈希拒绝");Check(!File.Exists(Path.Combine(downloadFolder,"package.download")),"下载校验失败临时文件释放并删除");
}
File.WriteAllText(Path.Combine(evidence,"update-tests.json"),JsonSerializer.Serialize(new{count=checks.Count,passed=checks,hardwareWrites=0},Packages.Json));Console.WriteLine(checks.Count+" 项更新专项通过");
class Handler(Func<HttpRequestMessage,int,HttpResponseMessage> respond):HttpMessageHandler{public List<Uri> Calls{get;}=[];protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){cancellationToken.ThrowIfCancellationRequested();Calls.Add(request.RequestUri!);return Task.FromResult(respond(request,Calls.Count-1));}}
class InlineProgress(Action<UpdateProgress> action):IProgress<UpdateProgress>{public void Report(UpdateProgress value)=>action(value);}

class LocalProxy:IDisposable {
 readonly TcpListener listener=new(IPAddress.Loopback,0);readonly CancellationTokenSource cancellation=new();readonly Task serving;
 readonly string manifest;readonly byte[] package;
 public System.Collections.Concurrent.ConcurrentQueue<string> Requests{get;}=new();
 public string Address{get;}
 public LocalProxy(string manifest,byte[] package){this.manifest=manifest;this.package=package;listener.Start();Address="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;serving=Serve();}
 async Task Serve(){try{while(!cancellation.IsCancellationRequested){using var client=await listener.AcceptTcpClientAsync(cancellation.Token);await using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,4096,true);string request=await reader.ReadLineAsync(cancellation.Token)??"";while(!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellation.Token))){}Requests.Enqueue(request);byte[] body=Requests.Count==1?Encoding.UTF8.GetBytes("bad gateway"):request.Contains(".zip")?package:Encoding.UTF8.GetBytes(manifest);string status=Requests.Count==1?"502 Bad Gateway":"200 OK";byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+"\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");await stream.WriteAsync(header,cancellation.Token);await stream.WriteAsync(body,cancellation.Token);}}catch(OperationCanceledException){}catch(SocketException)when(cancellation.IsCancellationRequested){}}
 public void Dispose(){cancellation.Cancel();listener.Stop();serving.GetAwaiter().GetResult();cancellation.Dispose();}
}
