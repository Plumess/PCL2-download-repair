using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace PclRepair {
 internal static class NativeImport {
  static MethodInfo dialog,callback;
  static Type closure;
  static FieldInfo fileField;
  static object filter;
  internal static void Validate() {
   var manual=Entry.Method("PCL.ModModpack","ModpackInstall",0);
   closure=manual.GetMethodBody().LocalVariables.Select(v=>v.LocalType).Single(t=>t.DeclaringType==manual.DeclaringType);
   var install=Entry.Method("PCL.ModModpack","ModpackInstall",3);
   callback=closure.GetMethods(Entry.All|BindingFlags.DeclaredOnly).Single(m=>m.ReturnType==typeof(void)&&m.GetParameters().Length==0&&m.GetMethodBody()!=null&&PatchProcessor.GetOriginalInstructions(m).Any(c=>object.Equals(c.operand,install)));
   var fields=PatchProcessor.GetOriginalInstructions(callback).Where(c=>c.operand is FieldInfo&&((FieldInfo)c.operand).DeclaringType==closure&&((FieldInfo)c.operand).FieldType==typeof(string)).Select(c=>(FieldInfo)c.operand).Distinct().ToArray();
   if(fields.Length!=1)throw new NotSupportedException("整合包回调文件字段改变");fileField=fields[0];
   dialog=PatchProcessor.GetOriginalInstructions(Entry.Method("PCL.ModMinecraft","McSkinSelect",0)).Select(c=>c.operand as MethodInfo).First(m=>m!=null&&m.Name=="SelectFile");
   var args=dialog.GetParameters();
   if(args.Length!=4||args[0].ParameterType!=typeof(string)||args[1].ParameterType!=typeof(bool))throw new NotSupportedException("原生文件选择接口改变");
   Type union=Nullable.GetUnderlyingType(args[3].ParameterType);
   Type alternatives=union.GetGenericArguments()[0];
   var convert=union.GetMethods(Entry.All).Single(m=>m.Name=="op_Implicit"&&m.ReturnType==union&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==alternatives);
   Type tuple=convert.GetParameters()[0].ParameterType.GetGenericArguments()[0];
   Array array=Array.CreateInstance(tuple,1);
   array.SetValue(Activator.CreateInstance(tuple,new object[]{new[]{"rar","zip","mrpack"},"整合包文件"}),0);
   filter=convert.Invoke(null,new object[]{array});
  }
  internal static void Open(MethodInfo ignored) {
   var files=(IEnumerable)Entry.Invoke(dialog,null,"选择整合包压缩文件",false,null,filter);
   string path=files.Cast<string>().FirstOrDefault();if(string.IsNullOrEmpty(path))return;
   object state=Activator.CreateInstance(closure,true);fileField.SetValue(state,path);
   var action=(Action)Delegate.CreateDelegate(typeof(Action),state,callback);
   var thread=new Thread(()=>action()){IsBackground=true,Name="Modpack import"};thread.Start();
  }
 }
}
