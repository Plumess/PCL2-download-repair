using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Windows;
using HarmonyLib;

[assembly: AssemblyVersion(PclRepair.BuildInfo.AdapterVersion)]

namespace PclRepair {
 public static class Entry {
  internal static string Home;
  internal static Assembly Pcl;
  internal static readonly BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
  static readonly object Gate=new object();
  static bool attempted;
  static Harmony startupHook;
  internal static string Failure;
  [ThreadStatic] static HashSet<string> resolving;
  public static void Start(string home) {
   Home=home;
   AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
   // WPF raises Startup before loading StartupUri. Wait until the original
   // Startup handlers finish initializing native identity/settings, then attach
   // before the main window is constructed. Never detour a WPF constructor:
   // replaying its initialization can alias private event-handler keys.
   startupHook=new Harmony("PCL2-download-repair.startup");
   startupHook.Patch(typeof(Application).GetMethod("OnStartup",BindingFlags.Instance|BindingFlags.NonPublic),postfix:new HarmonyMethod(typeof(Entry),"ApplicationStarted"));
   if(Application.Current!=null&&Application.Current.MainWindow!=null&&Application.Current.MainWindow.IsLoaded)ApplicationStarted(Application.Current);
   Log("Bootstrap attached to original entry; waiting for native Startup completion, pid="+System.Diagnostics.Process.GetCurrentProcess().Id);
  }
  static void ApplicationStarted(Application __instance){
   if(__instance.GetType().Assembly.GetType("PCL.FormMain",false)==null)return;
   SafeConsider(__instance.GetType().Assembly);
  }
  internal static void DetachStartupHook(){
   if(startupHook==null)return;
   try{startupHook.UnpatchAll(startupHook.Id);startupHook=null;}catch(Exception e){Log("Startup hook cleanup: "+e.GetBaseException().Message);}
  }
  static void SafeConsider(Assembly assembly){try{Consider(assembly);}catch(Exception ex){Log("Assembly adapter unavailable: "+ex.GetBaseException().Message);}}
  static Assembly Resolve(object sender,ResolveEventArgs e) {
   var requested=new AssemblyName(e.Name);
   if(Pcl==null)return null;
   var existing=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>Matches(requested,a.GetName()));
   if(existing!=null)return existing;
   if(resolving==null)resolving=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   if(!resolving.Add(e.Name))return null;
   try{
    using(var stream=Pcl.GetManifestResourceStream("PCL.Resources.resources")) {
     if(stream==null)return null;
     using(var reader=new ResourceReader(stream)){
      var entries=reader.GetEnumerator();
      while(entries.MoveNext()){
       if(!string.Equals(entries.Key as string,requested.Name,StringComparison.Ordinal))continue;
       var bytes=entries.Value as byte[];if(bytes==null)return null;
       // Early patching must retain the launcher's embedded dependencies,
       // including Registry shims used to read its existing identity/settings.
       var loaded=Assembly.Load(bytes);
       if(!Matches(requested,loaded.GetName()))throw new System.IO.FileLoadException("Embedded dependency identity mismatch: "+requested.Name);
       return loaded;
      }
     }
    }return null;
   }finally{resolving.Remove(e.Name);}
  }
  static bool Matches(AssemblyName requested,AssemblyName actual){
   return requested.Name==actual.Name&&(requested.Version==null||requested.Version==actual.Version)&&string.Equals(requested.CultureName??"",actual.CultureName??"",StringComparison.OrdinalIgnoreCase)&&(requested.GetPublicKeyToken()??new byte[0]).SequenceEqual(actual.GetPublicKeyToken()??new byte[0]);
  }
  static void Consider(Assembly assembly) {
   if(assembly.IsDynamic || assembly.GetType("PCL.FormMain",false)==null)return;
   lock(Gate) {
    if(attempted)return;attempted=true;Pcl=assembly;
    var harmony=new Harmony("PCL2-download-repair");
    try {
     if(assembly.GetType("PCL.RestrictedFeaturePolicy",false)!=null)CeAdapter.Install(harmony);
     else FrameworkAdapter.Install(harmony);
     Log("Adapter ready: "+assembly.GetName().Version+"; repair "+BuildInfo.AdapterVersion+"; EXE on disk unchanged");
    }catch(Exception ex) {
     harmony.UnpatchAll(harmony.Id);Failure=ex.GetBaseException().Message;
     Log("Adapter rolled back: "+ex);
     UpdateClient.Rollback();
    }
    NativeUi.Register();
   }
  }
  internal static Type Type(string name){return Pcl.GetType(name,true);}
  internal static MethodInfo Method(string type,string name,int count) {
   var found=Type(type).GetMethods(All).Where(m=>m.Name==name&&m.GetParameters().Length==count).ToArray();
   if(found.Length!=1)throw new MissingMethodException(type,name+" ("+count+")");return found[0];
  }
  internal static object Get(object target,string name) {
   for(var t=target.GetType();t!=null;t=t.BaseType) {
    var p=t.GetProperty(name,All|BindingFlags.DeclaredOnly);if(p!=null)return p.GetValue(target,null);
    var f=t.GetField(name,All|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(target);
   }throw new MissingMemberException(target.GetType().FullName,name);
  }
  internal static object Invoke(MethodInfo m,object target,params object[] args) {
   try{return m.Invoke(target,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}
  }
  internal static bool ContainsText(MethodBase m,string text) {
   return PatchProcessor.GetOriginalInstructions(m).Any(c=>c.opcode==System.Reflection.Emit.OpCodes.Ldstr && ((string)c.operand).Contains(text));
  }
  internal static void Log(string message) {
   try {var dir=Path.Combine(Home,".local","logs");Directory.CreateDirectory(dir);var path=Path.Combine(dir,"runtime.log");lock(Gate){if(File.Exists(path)&&new FileInfo(path).Length>1024*1024){if(File.Exists(path+".previous"))File.Delete(path+".previous");File.Move(path,path+".previous");}File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);}}catch{}
  }
 }
}
