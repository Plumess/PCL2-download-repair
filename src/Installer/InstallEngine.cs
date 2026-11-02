using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PclRepair.Tool {
 internal sealed class Target {
  public string Path; public bool Modern; public string Version;
  public override string ToString(){return System.IO.Path.GetFileName(Path)+" · "+(Modern?"CE": "正式版／快照版")+" "+Version;}
 }
 internal static class InstallEngine {
  const string Identity="PclRepair.Bootstrap, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null";
  const string PrivatePath="PCL2-download-repair\\runtime\\bootstrap";
  static readonly XNamespace Binding="urn:schemas-microsoft-com:asm.v1";
  internal static string Hash(string path){using(var f=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
  internal static Target Identify(string path) {
   if(!File.Exists(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return null;
   var v=FileVersionInfo.GetVersionInfo(path);string info=(v.ProductName+" "+v.FileDescription+" "+v.OriginalFilename).ToLowerInvariant();
   if(info.Contains("repair")||info.Contains("恢复")||!(info.Contains("plain craft")||info.Contains("pcl2")||info.Contains("pcl ce")))return null;
   bool modern=false;
   try {var a=AssemblyName.GetAssemblyName(path);if(a.Name!="Plain Craft Launcher 2")return null;}
   catch(BadImageFormatException){modern=true;}
   if(modern) {
    using(var f=File.OpenRead(path))using(var r=new BinaryReader(f)) {
     if(r.ReadUInt16()!=0x5a4d)return null;f.Position=0x3c;int offset=r.ReadInt32();if(offset<0||offset>f.Length-6)return null;f.Position=offset;if(r.ReadUInt32()!=0x4550)return null;
     ushort machine=r.ReadUInt16();if(machine!=0x8664)throw new NotSupportedException("目前的 CE 引导组件只支持 x64，请不要用于 ARM64 程序。");
    }
   }
   return new Target{Path=System.IO.Path.GetFullPath(path),Modern=modern,Version=v.FileVersion};
  }
  internal static List<Target> Find(string toolFolder) {
   var roots=new[]{toolFolder,System.IO.Path.GetDirectoryName(toolFolder),Environment.CurrentDirectory}.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
   var hits=new List<Target>();
   foreach(var root in roots)foreach(var file in Directory.GetFiles(root,"*.exe")) {
    try{var t=Identify(file);if(t!=null&&!hits.Any(x=>string.Equals(x.Path,t.Path,StringComparison.OrdinalIgnoreCase)))hits.Add(t);}catch{ }
   }return hits;
  }
  static void EnsureClosed(Target target) {
   foreach(var p in Process.GetProcesses())using(p) {
    string path;try{path=p.MainModule.FileName;}catch{continue;}
    if(!string.Equals(path,target.Path,StringComparison.OrdinalIgnoreCase))continue;
    if(!p.CloseMainWindow()||!p.WaitForExit(8000))throw new InvalidOperationException("请先关闭 "+System.IO.Path.GetFileName(target.Path)+"，再点“重试”。不用关闭 Minecraft 游戏。");
   }
  }
  static string Home(Target t){return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(t.Path),"PCL2-download-repair");}
  static void AtomicBytes(string path,byte[] data) {
   Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllBytes(temp,data);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  static void Text(string path,string text){AtomicBytes(path,new UTF8Encoding(false).GetBytes(text));}
  static void Payload(string home) {
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("PclRepair.Payload")) {
    if(stream==null)throw new FileNotFoundException("修复工具不完整，请重新下载。");
    using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))foreach(var entry in zip.Entries) {
     string relative=entry.FullName.Replace('/',System.IO.Path.DirectorySeparatorChar);
     string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(home,relative));
     if(!path.StartsWith(System.IO.Path.GetFullPath(home)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("组件路径无效");
     if(!relative.StartsWith("runtime"+System.IO.Path.DirectorySeparatorChar,StringComparison.Ordinal)||entry.Length>16*1024*1024)throw new InvalidDataException("组件包不正确");
     using(var source=entry.Open())using(var memory=new MemoryStream()) {
      source.CopyTo(memory);byte[] bytes=memory.ToArray();bool equal=false;
      if(File.Exists(path))using(var h=SHA256.Create())equal=Hash(path)==BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
      if(!equal)AtomicBytes(path,bytes);
     }
    }
   }
   string pointer=System.IO.Path.Combine(home,"runtime","current.txt");
   bool usable=false;
   if(File.Exists(pointer)) {
    string version=File.ReadAllText(pointer).Trim();Version parsed;
    if(Version.TryParse(version,out parsed)&&version==parsed.ToString()&&parsed>=new Version(BuildInfo.AdapterVersion)) {
     string directory=System.IO.Path.Combine(home,"runtime","versions",version);
     usable=new[]{"framework/PclRepair.Adapter.dll","framework/0Harmony.dll","modern/PclRepair.Adapter.dll","modern/0Harmony.dll"}.All(f=>File.Exists(System.IO.Path.Combine(directory,f)));
    }
   }
   if(!usable)Text(pointer,BuildInfo.AdapterVersion);
  }
  static XDocument LoadConfig(string path) {
   if(!File.Exists(path))return new XDocument(new XElement("configuration"));
   var settings=new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null};
   using(var r=System.Xml.XmlReader.Create(path,settings))return XDocument.Load(r,LoadOptions.PreserveWhitespace);
  }
  static XElement Child(XElement parent,XName name){var e=parent.Element(name);if(e==null){e=new XElement(name);parent.Add(e);}return e;}
  static byte[] XmlBytes(XDocument xml){using(var m=new MemoryStream()){xml.Save(m);return m.ToArray();}}
  static void SaveFramework(Target target,string home,bool remove) {
   string config=target.Path+".config";var xml=LoadConfig(config);
   if(xml.Root==null||xml.Root.Name!="configuration")throw new InvalidDataException("PCL 的现有配置格式不正确，未覆盖它。");
   var runtime=remove?xml.Root.Element("runtime"):Child(xml.Root,"runtime");if(runtime==null)return;
   var manager=runtime.Element("appDomainManagerAssembly");
   if(manager!=null&&!((string)manager.Attribute("value")??"").StartsWith("PclRepair.Bootstrap,",StringComparison.Ordinal))throw new InvalidOperationException("此 PCL 已安装另一个启动扩展。为避免覆盖，请先恢复那个扩展。");
   if(!remove) {
    if(manager==null)runtime.Add(manager=new XElement("appDomainManagerAssembly"));manager.SetAttributeValue("value",Identity);
    Child(runtime,"appDomainManagerType").SetAttributeValue("value","PclRepair.Bootstrap.Manager");
   }else {if(manager==null)return;manager.Remove();var type=runtime.Element("appDomainManagerType");if(type!=null)type.Remove();}
   var bindings=remove?runtime.Element(Binding+"assemblyBinding"):Child(runtime,Binding+"assemblyBinding");
   if(bindings!=null) {
    var probing=remove?bindings.Element(Binding+"probing"):Child(bindings,Binding+"probing");
    if(probing!=null) {
     var paths=((string)probing.Attribute("privatePath")??"").Split(';').Where(s=>s.Length>0&&!s.Replace('/','\\').Equals(PrivatePath,StringComparison.OrdinalIgnoreCase)).ToList();
     if(!remove)paths.Add(PrivatePath);
     if(paths.Count==0)probing.Remove();else probing.SetAttributeValue("privatePath",string.Join(";",paths));
    }
    if(!bindings.HasElements)bindings.Remove();
   }
   if(!runtime.HasElements)runtime.Remove();
   if(remove&&!xml.Root.HasElements&&!xml.Root.HasAttributes){File.Delete(config);return;}
   if(!remove&&File.Exists(config)) {
    string backup=System.IO.Path.Combine(home,".local","config-backups",System.IO.Path.GetFileName(config)+".before");
    if(!File.Exists(backup)){Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));File.Copy(config,backup);}
   }
   AtomicBytes(config,XmlBytes(xml));
  }
  static void SaveModern(Target target,string home,bool remove) {
   string root=System.IO.Path.GetDirectoryName(target.Path),bridge=System.IO.Path.Combine(root,"hostfxr.dll");
   string source=System.IO.Path.Combine(home,"runtime","bootstrap","hostfxr-x64.dll");
   string ownership=System.IO.Path.Combine(home,".local","state","bridge.sha256");
   string registrations=System.IO.Path.Combine(home,"runtime","modern-targets.txt");
   var targets=File.Exists(registrations)?File.ReadAllLines(registrations).Where(s=>s.Length>0).ToList():new List<string>();
   targets.RemoveAll(s=>s.Equals(System.IO.Path.GetFileName(target.Path),StringComparison.OrdinalIgnoreCase));
   if(remove) {
    Text(registrations,string.Join("\n",targets));
    if(targets.Count==0&&File.Exists(bridge)&&File.Exists(ownership)&&Hash(bridge)==File.ReadAllText(ownership).Trim()){File.Delete(bridge);File.Delete(ownership);}
    return;
   }
   if(File.Exists(bridge)&&Hash(bridge)!=Hash(source)&&(!File.Exists(ownership)||Hash(bridge)!=File.ReadAllText(ownership).Trim()))throw new InvalidOperationException("PCL 旁边已有其他 hostfxr.dll，未覆盖。请先检查是否安装过其他扩展或自带运行时。");
   if(!File.Exists(bridge)||Hash(bridge)!=Hash(source))AtomicBytes(bridge,File.ReadAllBytes(source));
   Text(ownership,Hash(bridge));targets.Add(System.IO.Path.GetFileName(target.Path));Text(registrations,string.Join("\n",targets));
  }
  internal static string Apply(Target target,bool remove) {
   EnsureClosed(target);string before=Hash(target.Path),home=Home(target);
   // Validate configuration conflicts before deploying any persistent entry.
   if(!target.Modern&&!remove){var config=LoadConfig(target.Path+".config");var rt=config.Root==null?null:config.Root.Element("runtime");var m=rt==null?null:rt.Element("appDomainManagerAssembly");if(m!=null&&!((string)m.Attribute("value")??"").StartsWith("PclRepair.Bootstrap,",StringComparison.Ordinal))throw new InvalidOperationException("已有其他启动扩展，未覆盖。");}
   if(!remove)Payload(home);
   if(target.Modern)SaveModern(target,home,remove);else SaveFramework(target,home,remove);
   if(Hash(target.Path)!=before)throw new IOException("修复过程中 PCL 自己发生了更新，请等更新完成后重试。");
   Directory.CreateDirectory(System.IO.Path.Combine(home,".local","logs"));
   File.AppendAllText(System.IO.Path.Combine(home,".local","logs","installer.log"),DateTime.UtcNow.ToString("o")+" "+(remove?"Removed":"Installed")+" "+target+" exeSha256="+before+Environment.NewLine);
   return remove?"已恢复原状，继续使用原来的 PCL 即可。":"已接入修复组件。以后照常打开原来的 PCL，无需再运行本工具。";
  }
 }
}
