using System;
using System.IO;
using System.Reflection;

[assembly: AssemblyVersion("0.3.0.0")]
namespace PclRepair.Bootstrap {
    // Loaded by the CLR for this application's config only. No registry or
    // machine-wide environment settings are used.
    public sealed class Manager : AppDomainManager {
        public override void InitializeNewDomain(AppDomainSetup setup) {
            setup.TargetFrameworkName = ".NETFramework,Version=v4.8";
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            AppContext.SetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", false);
            AppContext.SetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", false);
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.SystemDefault;
            string home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PCL2-download-repair");
            try {
                string version = File.ReadAllText(Path.Combine(home, "runtime", "current.txt")).Trim();
                if (version.Length == 0 || version.IndexOfAny(new[] {'/', '\\', ':'}) >= 0 || version == "." || version == "..")
                    throw new InvalidDataException("Invalid adapter version");
                string folder = Path.Combine(home, "runtime", "versions", version, "framework");
                AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
                    string name = new AssemblyName(e.Name).Name;
                    if (name != "0Harmony" && name != "PclRepair.Adapter") return null;
                    string file = Path.Combine(folder, name + ".dll");
                    return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                };
                Assembly.LoadFrom(Path.Combine(folder, "0Harmony.dll"));
                Assembly adapter = Assembly.LoadFrom(Path.Combine(folder, "PclRepair.Adapter.dll"));
                adapter.GetType("PclRepair.Entry", true).GetMethod("Start").Invoke(null, new object[] {home});
            } catch (Exception error) {
                try {
                    string logs = Path.Combine(home, ".local", "logs"); Directory.CreateDirectory(logs);
                    File.AppendAllText(Path.Combine(logs, "bootstrap.log"), DateTime.UtcNow.ToString("o") + " " + error.GetBaseException() + Environment.NewLine);
                } catch { }
                Rollback(home);
                // A broken adapter must not prevent the original launcher opening.
            }
        }
        static void Rollback(string home) {
            try {
                string root=Path.Combine(home,"runtime"), current=Path.Combine(root,"current.txt"), previous=Path.Combine(root,"previous.txt");
                if(!File.Exists(previous))return;
                string old=File.ReadAllText(previous).Trim(),bad=File.ReadAllText(current).Trim();Version parsed;
                if(old==bad||!Version.TryParse(old,out parsed)||old!=parsed.ToString()||!Directory.Exists(Path.Combine(root,"versions",old)))return;
                string state=Path.Combine(home,".local","state");Directory.CreateDirectory(state);File.AppendAllText(Path.Combine(state,"rejected.txt"),bad+Environment.NewLine);
                string temp=current+".rollback.tmp";File.WriteAllText(temp,old);File.Replace(temp,current,null);
            }catch{}
        }
    }
}
