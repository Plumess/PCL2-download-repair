using System;
using System.IO;
using System.Reflection;
using System.Windows;

[assembly: AssemblyTitle("PCL2-download-repair")]
[assembly: AssemblyProduct("PCL2-download-repair")]
[assembly: AssemblyVersion(PclRepair.BuildInfo.AdapterVersion)]
namespace PclRepair.Tool {
 internal static class Program {
  [STAThread] static int Main(string[] args) {
   if(args.Length>=2&&(args[0]=="--install"||args[0]=="--uninstall"||args[0]=="--inspect")) {
    try {var target=InstallEngine.Identify(args[1]);if(target==null)return 2;if(args[0]!="--inspect")InstallEngine.Apply(target,args[0]=="--uninstall");return 0;}catch(Exception ex){try{File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"installer-error.log"),ex.ToString());}catch{}return 1;}
   }
   new Application().Run(new RepairWindow());return 0;
  }
 }
}
