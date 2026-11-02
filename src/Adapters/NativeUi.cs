using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace PclRepair {
 internal static class NativeUi {
  static bool ready;
  internal static void Register() {
   EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.LoadedEvent,new RoutedEventHandler(Loaded),true);
   if(Application.Current!=null&&Application.Current.MainWindow!=null&&Application.Current.MainWindow.IsLoaded&&Application.Current.MainWindow.GetType().FullName=="PCL.FormMain")MainReady(Application.Current.MainWindow);
  }
  static void Loaded(object sender,RoutedEventArgs e) {
   if(ready||sender.GetType().FullName!="PCL.FormMain"||!ReferenceEquals(sender,e.OriginalSource))return;
   MainReady((FrameworkElement)sender);
  }
  static void MainReady(FrameworkElement window){
   if(ready)return;ready=true;Entry.DetachStartupHook();
   window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>{
    Entry.Log("Native main window ready");
    if(Entry.Failure!=null)Hint("下载功能需要新的适配，请运行新版修复工具。",true);
    UpdateClient.Start();
   }));
  }
  internal static void Hint(string text,bool error) {
   try {
    var original=Entry.Pcl.GetType("PCL.ModMain",false);
    MethodInfo hint=original==null?null:original.GetMethods(Entry.All).FirstOrDefault(m=>m.Name=="Hint"&&m.GetParameters().Length==3&&m.GetParameters()[0].ParameterType==typeof(string));
    if(hint!=null){var p=hint.GetParameters();Entry.Invoke(hint,null,text,Enum.ToObject(p[1].ParameterType,error?2:0),true);return;}
    var ce=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("PCL.Core.UI.HintService",false)).FirstOrDefault(t=>t!=null);
    if(ce==null)return;hint=ce.GetMethods(Entry.All).First(m=>m.Name=="Hint"&&m.GetParameters()[0].ParameterType==typeof(string));
    var values=hint.GetParameters().Select(p=>p.HasDefaultValue?p.DefaultValue:(p.ParameterType.IsValueType?Activator.CreateInstance(p.ParameterType):null)).ToArray();values[0]=text;
    Entry.Invoke(hint,null,values);
   }catch(Exception ex){Entry.Log("Native hint unavailable: "+ex.GetBaseException().Message);}
  }
 }
}
