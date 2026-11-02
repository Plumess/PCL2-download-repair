using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;

namespace PclRepair {
 internal static class CeAdapter {
  static ConstructorInfo archiveConstructor;
  internal static void Install(Harmony harmony) {
   Type policy=Entry.Type("PCL.RestrictedFeaturePolicy");
   MethodInfo check=policy.GetMethod("Check",Entry.All,null,new[]{typeof(string),typeof(bool)},null);
   MethodInfo restricted=policy.GetProperty("IsRestricted",Entry.All).GetGetMethod(true);
   if(check==null||!check.IsStatic||check.ReturnType!=typeof(bool)||!restricted.IsStatic||restricted.ReturnType!=typeof(bool))throw new NotSupportedException("CE 下载策略接口改变");
   // Audit every use of this download policy within PCL before enabling it.
   // Unknown feature identifiers require a maintainer to extend the adapter.
   string[] allowed={"minecraft-install","minecraft-download","minecraft-client-download","minecraft-client-save","minecraft-loader-install","minecraft-repair","minecraft-reset","minecraft-launch-missing-files","modpack-install","online-modpack-install","auto-modpack-install"};
   int uses=0;
   foreach(var type in Entry.Pcl.GetTypes())foreach(var method in type.GetMethods(Entry.All|BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(type.GetConstructors(Entry.All))) {
    if(method.GetMethodBody()==null||method.ContainsGenericParameters)continue;
    var il=PatchProcessor.GetOriginalInstructions(method);
    if(!il.Any(c=>object.Equals(c.operand,check)||object.Equals(c.operand,restricted)||c.operand is MethodInfo&&((MethodInfo)c.operand).DeclaringType==policy&&((MethodInfo)c.operand).Name=="ThrowIfRestricted"))continue;
    uses++;
    var tokens=il.Where(c=>c.opcode==System.Reflection.Emit.OpCodes.Ldstr).Select(c=>(string)c.operand).Where(s=>s.StartsWith("minecraft-")||s.EndsWith("modpack-install")).ToArray();
    if(tokens.Any(s=>!allowed.Contains(s)))throw new NotSupportedException("CE 添加了新的受限动作，需要更新适配器");
    Entry.Log("CE policy caller: "+type.FullName+"."+method.Name);
   }
   if(uses==0)throw new NotSupportedException("CE 下载策略没有可验证的调用者");
   var pack=Entry.Type("PCL.ModModpack").GetMethods(Entry.All).Single(m=>m.Name=="ModpackInstall"&&m.GetParameters().Length>=3);
   var constructors=PatchProcessor.GetOriginalInstructions(pack).Where(c=>c.opcode==OpCodes.Newobj&&c.operand is ConstructorInfo).Select(c=>(ConstructorInfo)c.operand).Where(c=>c.DeclaringType.FullName=="System.IO.Compression.ZipArchive"&&c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType==typeof(Stream)).ToArray();
   if(constructors.Length!=1)throw new NotSupportedException("CE 整合包压缩流接口改变，需要更新适配器");archiveConstructor=constructors[0];
   harmony.Patch(pack,transpiler:new HarmonyMethod(typeof(CeAdapter),"ArchiveOpen"));
   harmony.Patch(check,prefix:new HarmonyMethod(typeof(CeAdapter),"Check"));
   harmony.Patch(restricted,prefix:new HarmonyMethod(typeof(CeAdapter),"Restricted"));
   if(!(bool)Entry.Invoke(check,null,"modpack-install",false)||(bool)Entry.Invoke(restricted,null))throw new InvalidOperationException("CE 策略修复未生效");
   Entry.Log("CE download policy installed, audited callers="+uses);
  }
  public static bool Check(string feature,ref bool __result) {
   switch(feature) {
    case "minecraft-install":case "minecraft-download":case "minecraft-client-download":case "minecraft-client-save":case "minecraft-loader-install":case "minecraft-repair":case "minecraft-reset":case "minecraft-launch-missing-files":case "modpack-install":case "online-modpack-install":case "auto-modpack-install":__result=true;return false;
    default:return true;
   }
  }
  public static bool Restricted(ref bool __result){__result=false;return false;}
  public static IEnumerable<CodeInstruction> ArchiveOpen(IEnumerable<CodeInstruction> instructions) {
   foreach(var c in instructions) {
    if(c.opcode==OpCodes.Newobj&&object.Equals(c.operand,archiveConstructor)) {
     var call=new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CeAdapter),"OpenArchive"));call.labels.AddRange(c.labels);call.blocks.AddRange(c.blocks);yield return call;yield return new CodeInstruction(OpCodes.Castclass,archiveConstructor.DeclaringType);
    }else yield return c;
   }
  }
  public static object OpenArchive(Stream stream) {
   try{return archiveConstructor.Invoke(new object[]{stream});}
   catch(TargetInvocationException ex){stream.Dispose();System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException??ex).Throw();throw;}
   catch{stream.Dispose();throw;}
  }
 }
}
