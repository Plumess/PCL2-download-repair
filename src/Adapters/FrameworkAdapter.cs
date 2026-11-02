using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Expression=System.Linq.Expressions.Expression;
using System.Runtime.CompilerServices;
using System.Windows;
using HarmonyLib;

namespace PclRepair {
 internal static class FrameworkAdapter {
  const string Restriction="PCL 不再提供 MC 下载功能";
  static readonly Dictionary<MethodBase,string> Plans=new Dictionary<MethodBase,string>();
  static readonly Dictionary<int,MethodInfo> Installers=new Dictionary<int,MethodInfo>();
  static MethodInfo packInstall;
  static Func<object,Delegate> installCallback,saveCallback;
  sealed class RowCallbacks {internal Delegate Install,Save;}
  static readonly ConditionalWeakTable<object,RowCallbacks> RowHandlers=new ConditionalWeakTable<object,RowCallbacks>();
  internal static void Install(Harmony harmony) {
   packInstall=Entry.Method("PCL.ModModpack","ModpackInstall",3);
   if(!Entry.ContainsText(packInstall,Restriction)) {
    if(!PatchProcessor.GetOriginalInstructions(packInstall).Any(c=>c.operand is MethodInfo&&((MethodInfo)c.operand).Name.StartsWith("InstallPack")))
     throw new NotSupportedException("未知的整合包安装结构");
    Entry.Log("Native unrestricted launcher: no feature patches required");return;
   }
   var specs=new[]{new{Key=0,Name="InstallPackCurseForge",Count=5},new{Key=1,Name="InstallPackHMCL",Count=3},new{Key=2,Name="InstallPackMMC",Count=3},new{Key=3,Name="InstallPackMCBBS",Count=4},new{Key=4,Name="InstallPackModrinth",Count=5},new{Key=9,Name="InstallPackLauncherPack",Count=3},new{Key=-1,Name="InstallPackCompress",Count=2}};
   foreach(var s in specs){var m=Entry.Method("PCL.ModModpack",s.Name,s.Count);if(m.ReturnType!=packInstall.ReturnType)throw new NotSupportedException("整合包返回类型改变");Installers.Add(s.Key,m);}
   Add(packInstall,"pack");
   Add(Entry.Method("PCL.ModModpack","ModpackInstall",0),"manual");
   Add(Entry.Method("PCL.PageDownloadInstall","LoaderInit",0),"loader");
   Add(Entry.Type("PCL.PageDownloadLeft").GetConstructor(Type.EmptyTypes),"navigation");
   Add(Entry.Method("PCL.PageDownloadResourceDetail","InitializeStackResourceVersions",1),"rows");
   NativeImport.Validate();
   // Perform structural preflight for the whole feature set before detouring.
   foreach(var pair in Plans) {
    var original=PatchProcessor.GetOriginalInstructions(pair.Key);
    var dm=new DynamicMethod("preflight",typeof(void),Type.EmptyTypes,true);
    Transform(original,dm.GetILGenerator(),pair.Key).ToList();
   }
   var resourceType=Entry.Type("PCL.PageResource").GetProperty("PageType",Entry.All);
   if(resourceType==null||!resourceType.PropertyType.IsEnum||!Enum.IsDefined(resourceType.PropertyType,"ModPack"))throw new NotSupportedException("资源分类接口改变");
   var rowMethod=PatchProcessor.GetOriginalInstructions(Entry.Method("PCL.PageDownloadResourceDetail","InitializeStackResourceVersions",1)).Select(c=>c.operand as MethodInfo).First(m=>m!=null&&m.Name=="ToListItem");
   Type rowHandler=rowMethod.GetParameters()[0].ParameterType;
   Type saveHandler=rowMethod.GetParameters()[1].ParameterType;
   if(!typeof(Delegate).IsAssignableFrom(rowHandler)||!typeof(Delegate).IsAssignableFrom(saveHandler))throw new NotSupportedException("资源列表的事件类型改变");
   installCallback=CallbackFactory(rowHandler,Entry.Method("PCL.PageDownloadResourceDetail","Install_Click",2));
   saveCallback=CallbackFactory(saveHandler,Entry.Method("PCL.PageDownloadResourceDetail","Save_Click",2));
   foreach(var pair in Plans) harmony.Patch(pair.Key,transpiler:new HarmonyMethod(typeof(FrameworkAdapter),"Transform"));
   harmony.Patch(resourceType.GetSetMethod(true),postfix:new HarmonyMethod(typeof(FrameworkAdapter),"ImportButton"));
   // The original startup/empty-instance entry points are restored by replacing
   // only their download-specific disabled constant branches.
   EntryPatches.Install(harmony);
   var modify=Entry.Pcl.GetType("PCL.PageDownloadModify",false);
   if(modify!=null) {
    var matches=modify.GetMethods(Entry.All|BindingFlags.DeclaredOnly).Where(m=>!m.ContainsGenericParameters&&m.GetMethodBody()!=null&&HasCardVisibility(m)).ToArray();
    if(matches.Length!=1)throw new NotSupportedException("版本修改页面结构改变");
    harmony.Patch(matches[0],postfix:new HarmonyMethod(typeof(FrameworkAdapter),"ModifyCard"));
   }
   Entry.Log("Framework feature group installed: vanilla, navigation, manual/drop/online packs, modify and launch entries");
  }
  static void Add(MethodBase m,string plan){if(m==null)throw new MissingMethodException(plan);Plans.Add(m,plan);}
  static bool HasCardVisibility(MethodInfo method) {
   var il=PatchProcessor.GetOriginalInstructions(method);
   return Enumerable.Range(0,Math.Max(0,il.Count-2)).Any(i=>Named(il[i],"get_CardMinecraft")&&il[i+1].opcode==OpCodes.Ldc_I4_2&&Named(il[i+2],"set_Visibility"));
  }
  static int Local(CodeInstruction c,bool store) {
   if(c.opcode==(store?OpCodes.Stloc_0:OpCodes.Ldloc_0))return 0;
   if(c.opcode==(store?OpCodes.Stloc_1:OpCodes.Ldloc_1))return 1;
   if(c.opcode==(store?OpCodes.Stloc_2:OpCodes.Ldloc_2))return 2;
   if(c.opcode==(store?OpCodes.Stloc_3:OpCodes.Ldloc_3))return 3;
   if(c.opcode==(store?OpCodes.Stloc:OpCodes.Ldloc)||c.opcode==(store?OpCodes.Stloc_S:OpCodes.Ldloc_S))return c.operand is LocalBuilder?((LocalBuilder)c.operand).LocalIndex:Convert.ToInt32(c.operand);
   return -1;
  }
  static CodeInstruction LoadLocal(int index){return new CodeInstruction(OpCodes.Ldloc,index);}
  static bool Named(CodeInstruction c,string name){return c.operand is MethodBase&&((MethodBase)c.operand).Name==name;}
  static bool Text(CodeInstruction c,string text){return c.opcode==OpCodes.Ldstr&&((string)c.operand).Contains(text);}
  public static IEnumerable<CodeInstruction> Transform(IEnumerable<CodeInstruction> instructions,ILGenerator generator,MethodBase __originalMethod) {
   var il=instructions.ToList();string plan=Plans[__originalMethod];
   if(plan=="manual") {
    if(!il.Any(c=>Text(c,Restriction)))throw new NotSupportedException("手动导入结构改变");
    return new[]{new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(FrameworkAdapter),"ManualImport")),new CodeInstruction(OpCodes.Ret)};
   }
   if(plan=="navigation") {
    int init=il.FindIndex(c=>Named(c,"InitializeComponent"));
    if(init<0||!il.Skip(init).Any(c=>Named(c,"get_ItemInstall"))||!il.Skip(init).Any(c=>Named(c,"get_LabResource")))throw new NotSupportedException("下载导航结构改变");
    var tail=il.Skip(init+1).ToList();var known=new[]{"get_ItemInstall","get_LabResource","get_ItemMod","set_Visibility","set_Checked"};
    if(tail.Any(c=>c.operand is MethodBase&&!known.Contains(((MethodBase)c.operand).Name))||tail.Any(c=>c.operand is FieldInfo&&((FieldInfo)c.operand).Name!="PageID")||tail.Any(c=>c.labels.Count>0||c.blocks.Count>0)||tail.Count(c=>Named(c,"set_Visibility"))!=2||tail.Count(c=>Named(c,"set_Checked"))!=1)throw new NotSupportedException("下载导航增加了未知逻辑");
    return il.Take(init+1).Concat(new[]{new CodeInstruction(OpCodes.Ret)});
   }
   if(plan=="loader") {
    int start=il.FindIndex(c=>Text(c,"关闭下载"));
    int end=il.FindIndex(start+1,c=>c.opcode==OpCodes.Newobj&&c.operand is ConstructorInfo&&((ConstructorInfo)c.operand).DeclaringType.Name=="LoaderTask`2");
    var field=Entry.Type("PCL.ModDownload").GetField("DlClientListLoader",Entry.All);
    if(start<0||end<start||field==null||field.FieldType!=((ConstructorInfo)il[end].operand).DeclaringType)throw new NotSupportedException("原版下载加载器结构改变");
    il[start].opcode=OpCodes.Ldsfld;il[start].operand=field;
    // Labels internal to the removed disabled-loader construction are removed
    // together; no branch outside that expression may target them.
    var removedLabels=new HashSet<Label>(il.Skip(start+1).Take(end-start).SelectMany(c=>c.labels));
    if(il.Take(start).Concat(il.Skip(end+1)).Any(c=>c.operand is Label&&removedLabels.Contains((Label)c.operand)))throw new NotSupportedException("加载器存在外部跳转");
    il.RemoveRange(start+1,end-start);return il;
   }
   if(plan=="rows") {
    int call=il.FindIndex(c=>Named(c,"ToListItem"));
    var method=call<0?null:il[call].operand as MethodInfo;
    if(method==null||method.GetParameters().Length!=3||method.GetParameters()[2].ParameterType!=typeof(bool)||il.Count(c=>Named(c,"ToListItem"))!=1)throw new NotSupportedException("在线整合包项目结构改变");
    var injected=new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(FrameworkAdapter),"BuildRow")),new CodeInstruction(OpCodes.Castclass,method.ReturnType)};
    injected[0].labels.AddRange(il[call].labels);injected[0].blocks.AddRange(il[call].blocks);
    il.RemoveAt(call);il.InsertRange(call,injected);return il;
   }
   if(plan=="pack") {
    int guard=il.FindIndex(c=>Text(c,Restriction));
    if(guard<0||guard+2>=il.Count||il[guard+1].opcode!=OpCodes.Newobj||il[guard+2].opcode!=OpCodes.Throw||il.Count(c=>Text(c,Restriction))!=1)throw new NotSupportedException("整合包限制结构改变");
    int open=il.FindIndex(c=>Named(c,"OpenZip"));int archive=open<0?-1:Local(il[open+1],true);
    var bases=Enumerable.Range(0,guard-1).Where(i=>il[i].opcode==OpCodes.Ldstr&&(string)il[i].operand==""&&Local(il[i+1],true)>=0).ToArray();
    var kinds=Enumerable.Range(0,guard-1).Where(i=>il[i].opcode==OpCodes.Ldc_I4_M1&&Local(il[i+1],true)>=0).ToArray();
    if(archive<0||bases.Length!=1||kinds.Length!=1)throw new NotSupportedException("整合包局部变量无法唯一识别");
    int folder=Local(il[bases[0]+1],true),kind=Local(il[kinds[0]+1],true);
    for(int i=1;i<guard;i++)if(Local(il[i],true)==kind) {
     int? value=Constant(il[i-1]);if(!value.HasValue||!Installers.ContainsKey(value.Value))throw new NotSupportedException("整合包识别出现新类型，需要更新适配器");
    }
    var locals=__originalMethod.GetMethodBody().LocalVariables;
    if(locals[archive].LocalType.FullName!="System.IO.Compression.ZipArchive"||locals[folder].LocalType!=typeof(string)||locals[kind].LocalType!=typeof(int))throw new NotSupportedException("整合包变量类型不匹配");
    var result=generator.DeclareLocal(((MethodInfo)__originalMethod).ReturnType);Label done=generator.DefineLabel();
    var body=new List<CodeInstruction>{new CodeInstruction(OpCodes.Ldarg_0),LoadLocal(archive),LoadLocal(folder),LoadLocal(kind),new CodeInstruction(OpCodes.Ldarg_1),new CodeInstruction(OpCodes.Ldarg_2),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(FrameworkAdapter),"Dispatch")),new CodeInstruction(OpCodes.Castclass,((MethodInfo)__originalMethod).ReturnType),new CodeInstruction(OpCodes.Stloc,result),new CodeInstruction(OpCodes.Leave,done)};
    body[0].labels.AddRange(il[guard].labels);body[0].blocks.AddRange(il[guard].blocks);
    il.RemoveRange(guard,3);il.InsertRange(guard,body);
    var load=new CodeInstruction(OpCodes.Ldloc,result);load.labels.Add(done);il.Add(load);il.Add(new CodeInstruction(OpCodes.Ret));return il;
   }
   throw new NotSupportedException(plan);
  }
  public static object Dispatch(string path,object archive,string folder,int kind,string instance,string logo) {
   MethodInfo m;if(!Installers.TryGetValue(kind,out m))throw new NotSupportedException("此整合包格式需要更新下载适配器");
   var all=new object[]{path,archive,folder,instance,logo};
   Entry.Log("Native modpack dispatch: "+m.Name);
   return Entry.Invoke(m,null,all.Take(m.GetParameters().Length).ToArray());
  }
  public static object BuildRow(object version,object primary,object secondary,bool bad,object page) {
   var method=version.GetType().GetMethods(Entry.All).Single(m=>m.Name=="ToListItem"&&m.GetParameters().Length==3);
   bool pack=Entry.Get(page,"TargetResourceType").ToString()=="ModPack";
   if(pack) {
    var handlers=RowHandlers.GetValue(page,p=>new RowCallbacks{Install=installCallback(p),Save=saveCallback(p)});
    primary=handlers.Install;secondary=handlers.Save;
   }
   return Entry.Invoke(method,version,primary,secondary,bad);
  }
  public static void ImportButton(object __instance) {
   ((UIElement)Entry.Get(__instance,"BtnSearchInstallModPack")).Visibility=Entry.Get(__instance,"PageType").ToString()=="ModPack"?Visibility.Visible:Visibility.Collapsed;
  }
  public static void ModifyCard(object __instance){((UIElement)Entry.Get(__instance,"CardMinecraft")).Visibility=Visibility.Visible;}
  public static void ManualImport(){NativeImport.Open(packInstall);}
  static Func<object,Delegate> CallbackFactory(Type handler,MethodInfo method) {
   // VB's relaxed AddressOf conversion can downcast the sender to MyListItem.
   // Compile that conversion explicitly, once, rather than compiling per row.
   var invoke=handler.GetMethod("Invoke");var signature=invoke.GetParameters();var target=method.GetParameters();
   if(signature.Length!=target.Length||invoke.ReturnType!=method.ReturnType)throw new NotSupportedException("点击回调签名改变");
   var owner=Expression.Parameter(typeof(object),"owner");
   var parameters=signature.Select(p=>Expression.Parameter(p.ParameterType,p.Name)).ToArray();
   var arguments=parameters.Select((p,i)=>(Expression)Expression.Convert(p,target[i].ParameterType)).ToArray();
   var call=Expression.Call(Expression.Convert(owner,method.DeclaringType),method,arguments);
   var callback=Expression.Lambda(handler,call,parameters);
   return Expression.Lambda<Func<object,Delegate>>(Expression.Convert(callback,typeof(Delegate)),owner).Compile();
  }
  static int? Constant(CodeInstruction c) {
   if(c.opcode==OpCodes.Ldc_I4_M1)return -1;
   if(c.opcode==OpCodes.Ldc_I4_0)return 0;if(c.opcode==OpCodes.Ldc_I4_1)return 1;if(c.opcode==OpCodes.Ldc_I4_2)return 2;if(c.opcode==OpCodes.Ldc_I4_3)return 3;if(c.opcode==OpCodes.Ldc_I4_4)return 4;
   if(c.opcode==OpCodes.Ldc_I4_5)return 5;if(c.opcode==OpCodes.Ldc_I4_6)return 6;if(c.opcode==OpCodes.Ldc_I4_7)return 7;if(c.opcode==OpCodes.Ldc_I4_8)return 8;
   if(c.opcode==OpCodes.Ldc_I4||c.opcode==OpCodes.Ldc_I4_S)return Convert.ToInt32(c.operand);return null;
  }
 }
}
