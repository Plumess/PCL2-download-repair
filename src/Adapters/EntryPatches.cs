using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows;
using HarmonyLib;
namespace PclRepair {
 internal static class EntryPatches {
  internal static void Install(Harmony harmony) {
   var refresh=Entry.Method("PCL.PageLaunchLeft","RefreshButtonsUI",0);
   Transform(PatchProcessor.GetOriginalInstructions(refresh)).ToList();
   harmony.Patch(refresh,transpiler:new HarmonyMethod(typeof(EntryPatches),"Transform"));
   var empty=Entry.Method("PCL.PageSelectRight","McInstanceListUI",1);
   Empty(PatchProcessor.GetOriginalInstructions(empty)).ToList();
   harmony.Patch(empty,transpiler:new HarmonyMethod(typeof(EntryPatches),"Empty"));
  }
  public static IEnumerable<CodeInstruction> Transform(IEnumerable<CodeInstruction> instructions) {
   var il=instructions.ToList();
   int index=il.FindIndex(c=>c.operand is MethodInfo&&((MethodInfo)c.operand).Name=="get_McInstanceSelected");
   if(index<0||index+2>=il.Count||(il[index+1].opcode!=OpCodes.Brtrue&&il[index+1].opcode!=OpCodes.Brtrue_S)||il[index+2].opcode!=OpCodes.Ldc_I4_1)throw new NotSupportedException("启动页下载入口结构改变");
   il[index+2].opcode=OpCodes.Call;il[index+2].operand=AccessTools.Method(typeof(EntryPatches),"EmptyInstanceState");return il;
  }
  public static int EmptyInstanceState() {
   var main=Entry.Type("PCL.ModMain").GetField("FrmMain",Entry.All).GetValue(null);
   if(main==null)return 1;
   return ((UIElement)Entry.Get(main,"BtnTitleSelect1")).Visibility==Visibility.Visible?2:1;
  }
  public static IEnumerable<CodeInstruction> Empty(IEnumerable<CodeInstruction> instructions) {
   var il=instructions.ToList();
   int title=il.FindIndex(c=>c.opcode==OpCodes.Ldstr&&(string)c.operand=="无可用版本");
   int button=il.FindIndex(title+1,c=>c.operand is MethodInfo&&((MethodInfo)c.operand).Name=="get_BtnEmptyDownload");
   if(title<0||button<0||il[button+1].opcode!=OpCodes.Ldc_I4_2||!(il[button+2].operand is MethodInfo)||((MethodInfo)il[button+2].operand).Name!="set_Visibility")throw new NotSupportedException("无版本页面下载入口改变");
   il[button+1].opcode=OpCodes.Call;il[button+1].operand=AccessTools.Method(typeof(EntryPatches),"EmptyButtonVisibility");return il;
  }
  public static Visibility EmptyButtonVisibility(){return EmptyInstanceState()==2?Visibility.Visible:Visibility.Collapsed;}
 }
}
