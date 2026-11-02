using System;
using System.IO;
using System.Reflection;

internal static class StartupHook {
 public static void Initialize() {
  Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS",Environment.GetEnvironmentVariable("PCL_REPAIR_PREVIOUS_HOOKS"));
  Environment.SetEnvironmentVariable("PCL_REPAIR_PREVIOUS_HOOKS",null);
  string home=Path.Combine(AppContext.BaseDirectory,"PCL2-download-repair");
  try {
   string version=File.ReadAllText(Path.Combine(home,"runtime","current.txt")).Trim();
   if(version.Length==0||version.IndexOfAny(new[]{'/', '\\', ':'})>=0||version=="."||version=="..")throw new InvalidDataException("Invalid version");
   string dir=Path.Combine(home,"runtime","versions",version,"modern");
   AppDomain.CurrentDomain.AssemblyResolve+=(sender,eventArgs)=>{
    var name=new AssemblyName(eventArgs.Name);
    if(name.Name!="0Harmony"&&name.Name!="PclRepair.Adapter")return null;
    string file=Path.Combine(dir,name.Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;
   };
   Assembly.LoadFrom(Path.Combine(dir,"0Harmony.dll"));
   Assembly a=Assembly.LoadFrom(Path.Combine(dir,"PclRepair.Adapter.dll"));
   a.GetType("PclRepair.Entry",true).GetMethod("Start").Invoke(null,new object[]{home});
  }catch(Exception ex){try{string logs=Path.Combine(home,".local","logs");Directory.CreateDirectory(logs);File.AppendAllText(Path.Combine(logs,"bootstrap.log"),DateTime.UtcNow.ToString("o")+" "+ex.GetBaseException()+Environment.NewLine);}catch{}Rollback(home);}
 }
 static void Rollback(string home){try{string root=Path.Combine(home,"runtime"),current=Path.Combine(root,"current.txt"),previous=Path.Combine(root,"previous.txt");if(!File.Exists(previous))return;string old=File.ReadAllText(previous).Trim(),bad=File.ReadAllText(current).Trim();Version parsed;if(old==bad||!Version.TryParse(old,out parsed)||old!=parsed.ToString()||!Directory.Exists(Path.Combine(root,"versions",old)))return;string state=Path.Combine(home,".local","state");Directory.CreateDirectory(state);File.AppendAllText(Path.Combine(state,"rejected.txt"),bad+Environment.NewLine);string temp=current+".rollback.tmp";File.WriteAllText(temp,old);File.Replace(temp,current,null);}catch{}}
}
