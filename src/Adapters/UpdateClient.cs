using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PclRepair {
 [DataContract] internal sealed class Channel {
  [DataMember(Name="manifestUrl")] public string Url { get; set; }
  [DataMember(Name="publicKeyXml")] public string PublicKey { get; set; }
  [DataMember(Name="enabled")] public bool Enabled { get; set; }
 }
 [DataContract] internal sealed class Envelope {
  [DataMember(Name="payload")] public string Payload { get; set; }
  [DataMember(Name="signature")] public string Signature { get; set; }
 }
 [DataContract] internal sealed class ReleaseManifest {
  [DataMember(Name="schema")] public int Schema { get; set; }
  [DataMember(Name="version")] public string Version { get; set; }
  [DataMember(Name="minimumBootstrap")] public int MinimumBootstrap { get; set; }
  [DataMember(Name="files")] public List<ReleaseFile> Files { get; set; }
 }
 [DataContract] internal sealed class ReleaseFile {
  [DataMember(Name="path")] public string Path { get; set; }
  [DataMember(Name="url")] public string Url { get; set; }
  [DataMember(Name="sha256")] public string Hash { get; set; }
  [DataMember(Name="size")] public long Size { get; set; }
 }
 internal static class UpdateClient {
  static readonly string[] Allowed={"framework/PclRepair.Adapter.dll","framework/0Harmony.dll","modern/PclRepair.Adapter.dll","modern/0Harmony.dll"};
  internal static T Read<T>(byte[] data) {using(var stream=new MemoryStream(data))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}
  internal static void Start() {
   new Thread(()=>{try{Check();}catch(Exception ex){Entry.Log("Optional update check skipped: "+ex.GetBaseException().Message);}}){IsBackground=true,Name="PCL repair update"}.Start();
  }
  static void Check() {
   string runtime=Path.Combine(Entry.Home,"runtime"),state=Path.Combine(Entry.Home,".local","state");Directory.CreateDirectory(state);
   string config=Path.Combine(runtime,"channel.json");if(!File.Exists(config))return;
   var channel=Read<Channel>(File.ReadAllBytes(config));if(!channel.Enabled||string.IsNullOrWhiteSpace(channel.Url)||string.IsNullOrWhiteSpace(channel.PublicKey))return;
   ValidateHttps(channel.Url);
   string scope;using(var sha=SHA256.Create())scope=Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(Entry.Home).ToUpperInvariant()))).Substring(0,24);
   using(var mutex=new Mutex(false,"Local\\PclRepair.Update."+scope)) {
    bool taken;try{taken=mutex.WaitOne(0);}catch(AbandonedMutexException){taken=true;}if(!taken)return;
    try {
     string exe=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
     string fingerprint=HashFile(exe),seen=Path.Combine(state,"launcher.sha256"),checkedAt=Path.Combine(state,"last-check.txt");
     bool changed=!File.Exists(seen)||File.ReadAllText(seen)!=fingerprint;
     TimeSpan interval=TimeSpan.FromHours(Entry.Failure==null?12:1);
     DateTime last;bool recent=File.Exists(checkedAt)&&DateTime.TryParse(File.ReadAllText(checkedAt),null,System.Globalization.DateTimeStyles.RoundtripKind,out last)&&DateTime.UtcNow-last<interval;
     if(!changed&&recent)return;
     Atomic(checkedAt,Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o")));Atomic(seen,Encoding.UTF8.GetBytes(fingerprint));
     string etagFile=Path.Combine(state,"manifest.etag");string etag=File.Exists(etagFile)?File.ReadAllText(etagFile):null;
     string received;int status;byte[] envelope=Get(channel.Url,512*1024,etag,out received,out status);
     if(status==304)return;
     var manifest=Verify(envelope,channel.PublicKey);
     string current=File.ReadAllText(Path.Combine(runtime,"current.txt")).Trim();
     if(new Version(manifest.Version)<=new Version(current))return;
     string rejected=Path.Combine(state,"rejected.txt");if(File.Exists(rejected)&&File.ReadAllLines(rejected).Contains(manifest.Version))return;
     Install(manifest,runtime,DownloadComponent);
     if(received!=null)Atomic(etagFile,Encoding.UTF8.GetBytes(received));
     Entry.Log("Verified adapter update staged: "+manifest.Version+"; next normal launch will use it");
     if(Entry.Failure!=null&&System.Windows.Application.Current!=null)System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(()=>NativeUi.Hint("新的下载适配已准备好，下次打开 PCL 会自动使用。",false)));
    }finally{mutex.ReleaseMutex();}
   }
  }
  internal static ReleaseManifest Verify(byte[] data,string publicKey) {
   var envelope=Read<Envelope>(data);byte[] payload=Convert.FromBase64String(envelope.Payload),signature=Convert.FromBase64String(envelope.Signature);
   using(var rsa=RSA.Create()){rsa.FromXmlString(publicKey);if(rsa.KeySize<2048||!rsa.VerifyData(payload,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new InvalidDataException("更新签名不正确");}
   var manifest=Read<ReleaseManifest>(payload);Version version;
   if(manifest.Schema!=1||manifest.MinimumBootstrap>BuildInfo.BootstrapGeneration||manifest.MinimumBootstrap<1||!Version.TryParse(manifest.Version,out version)||version.Build<0||version.Revision!=-1||manifest.Version!=version.ToString()||manifest.Version.IndexOfAny(new[]{'/', '\\', ':'})>=0)throw new InvalidDataException("更新格式或引导版本不兼容");
   if(manifest.Files==null||manifest.Files.Count!=Allowed.Length||manifest.Files.Select(f=>f.Path).Distinct(StringComparer.Ordinal).Count()!=Allowed.Length)throw new InvalidDataException("更新组件不完整");
   long total=0;
   foreach(var file in manifest.Files) {
    if(!Allowed.Contains(file.Path)||file.Size<1||file.Size>16*1024*1024||file.Hash==null||file.Hash.Length!=64||file.Hash.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("更新组件内容无效");
    total+=file.Size;ValidateHttps(file.Url);
   }
   if(total>32*1024*1024)throw new InvalidDataException("更新组件过大");return manifest;
  }
  static byte[] DownloadComponent(string url,int limit){string etag;int status;return Get(url,limit,null,out etag,out status);}
  static void Install(ReleaseManifest manifest,string runtime,Func<string,int,byte[]> fetch) {
   string versions=Path.Combine(runtime,"versions"),destination=Path.Combine(versions,manifest.Version),stage=Path.Combine(versions,".pending-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(stage);
   try {
    foreach(var file in manifest.Files) {
     byte[] bytes=fetch(file.Url,(int)file.Size);string hash;using(var sha=SHA256.Create())hash=Hex(sha.ComputeHash(bytes));
     if(bytes.LongLength!=file.Size||!hash.Equals(file.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新组件校验失败");
     string output=Path.Combine(stage,file.Path.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllBytes(output,bytes);
     string name=AssemblyName.GetAssemblyName(output).Name;if(name!=(file.Path.EndsWith("0Harmony.dll")?"0Harmony":"PclRepair.Adapter"))throw new InvalidDataException("更新程序集身份不正确");
    }
    if(Directory.Exists(destination)) {
     if(manifest.Files.Any(f=>!File.Exists(Path.Combine(destination,f.Path))||!HashFile(Path.Combine(destination,f.Path)).Equals(f.Hash,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("本地同名版本内容不同，未覆盖");
    }else Directory.Move(stage,destination);
    string current=Path.Combine(runtime,"current.txt"),previous=Path.Combine(runtime,"previous.txt");
    Atomic(previous,File.ReadAllBytes(current));Atomic(current,Encoding.UTF8.GetBytes(manifest.Version));
    string keepPrevious=File.ReadAllText(previous).Trim();
    foreach(var folder in Directory.GetDirectories(versions)) {
     string name=Path.GetFileName(folder);Version parsed;
     if(name!=manifest.Version&&name!=keepPrevious&&Version.TryParse(name,out parsed)&&name==parsed.ToString())try{RemoveVersion(versions,folder);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
   }finally{if(Directory.Exists(stage))RemoveVersion(versions,stage);}
  }
  static void RemoveVersion(string parent,string directory) {
   string root=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar),target=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
   if(!string.Equals(Path.GetDirectoryName(target),root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("拒绝清理版本目录之外的路径");
   var queue=new Stack<string>();queue.Push(target);
   while(queue.Count>0){string item=queue.Pop();if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("拒绝递归清理链接目录");foreach(var child in Directory.GetDirectories(item))queue.Push(child);}
   Directory.Delete(target,true);
  }
  internal static void Rollback() {
   try {
    string runtime=Path.Combine(Entry.Home,"runtime"),current=Path.Combine(runtime,"current.txt"),previous=Path.Combine(runtime,"previous.txt");if(!File.Exists(previous))return;
    string old=File.ReadAllText(previous).Trim(),bad=File.ReadAllText(current).Trim();if(old==bad||!Directory.Exists(Path.Combine(runtime,"versions",old)))return;
    var state=Path.Combine(Entry.Home,".local","state");Directory.CreateDirectory(state);File.AppendAllText(Path.Combine(state,"rejected.txt"),bad+Environment.NewLine);
    Atomic(current,Encoding.UTF8.GetBytes(old));Entry.Log("Adapter pointer rolled back to "+old+" for next launch");
   }catch(Exception ex){Entry.Log("Rollback: "+ex.GetBaseException().Message);}
  }
  static void ValidateHttps(string url){Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo))throw new InvalidDataException("更新地址必须使用 HTTPS");}
  static byte[] Get(string url,int limit,string etag,out string received,out int status) {
   ValidateHttps(url);var request=(HttpWebRequest)WebRequest.Create(url);request.Timeout=5000;request.ReadWriteTimeout=8000;request.UserAgent="PCL2-download-repair/"+BuildInfo.AdapterVersion;request.AllowAutoRedirect=true;
   if(!string.IsNullOrEmpty(etag))request.Headers[HttpRequestHeader.IfNoneMatch]=etag;
   HttpWebResponse response;
   try{response=(HttpWebResponse)request.GetResponse();}catch(WebException ex){var r=ex.Response as HttpWebResponse;if(r!=null&&r.StatusCode==HttpStatusCode.NotModified){received=null;status=304;r.Dispose();return null;}throw;}
   using(response){status=(int)response.StatusCode;received=response.Headers[HttpResponseHeader.ETag];if(response.ResponseUri.Scheme!="https"||response.ContentLength>limit)throw new InvalidDataException("更新响应无效");using(var input=response.GetResponseStream())using(var memory=new MemoryStream()){byte[] buffer=new byte[32768];int n;while((n=input.Read(buffer,0,buffer.Length))>0){if(memory.Length+n>limit)throw new InvalidDataException("更新响应超过上限");memory.Write(buffer,0,n);}return memory.ToArray();}}
  }
  static string HashFile(string path){using(var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var sha=SHA256.Create())return Hex(sha.ComputeHash(stream));}
  static string Hex(byte[] hash){return BitConverter.ToString(hash).Replace("-","").ToLowerInvariant();}
  internal static void Atomic(string path,byte[] bytes){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
 }
}
