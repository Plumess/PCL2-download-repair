using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shell;
using ShapePath=System.Windows.Shapes.Path;

namespace PclRepair.Tool {
 internal sealed class RepairWindow : Window {
  static readonly Brush Accent=BrushOf("#19888B"),Ink=BrushOf("#203A40"),Muted=BrushOf("#64797D");
  TextBlock title,body,details,stateLabel;
  Border symbolBox;
  ShapePath symbol,maximizeGlyph;
  ProgressBar progress;
  Button retry,pick,undo,closeButton,maximizeButton;
  bool busy,canFinish;string selected;
  internal RepairWindow(){
   Title="PCL2-download-repair";Width=640;Height=488;MinWidth=560;MinHeight=440;
   WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;
   Background=BrushOf("#F3F8F7");Foreground=Ink;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;
   UseLayoutRounding=true;SnapsToDevicePixels=true;TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
   WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=54,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(12),UseAeroCaptionButtons=false});
   Resources.Add(typeof(Button),ButtonStyle(Brushes.White,Ink,BrushOf("#D7E4E3"),BrushOf("#EAF3F1"),Ink));
   var frame=new Border{BorderBrush=BrushOf("#D9E5E3"),BorderThickness=new Thickness(1),Background=Background,CornerRadius=new CornerRadius(12)};
   var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(54)});layout.RowDefinitions.Add(new RowDefinition());frame.Child=layout;Content=frame;layout.Children.Add(TitleBar());
   var content=new Grid{Margin=new Thickness(30,18,30,24)};content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});content.RowDefinitions.Add(new RowDefinition());content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Grid.SetRow(content,1);layout.Children.Add(content);
   var heading=new StackPanel{Margin=new Thickness(0,0,0,22)};heading.Children.Add(new TextBlock{Text="一次修复，照常使用",FontSize=26,FontWeight=FontWeights.SemiBold,Foreground=Ink});heading.Children.Add(new TextBlock{Text="保留熟悉的 PCL，继续用原来的图标打开",FontSize=13,Foreground=Muted,Margin=new Thickness(0,8,0,0)});content.Children.Add(heading);
   var card=new Border{CornerRadius=new CornerRadius(12),Background=Brushes.White,BorderBrush=BrushOf("#E1EBE8"),BorderThickness=new Thickness(1),Padding=new Thickness(22)};Grid.SetRow(card,1);content.Children.Add(card);
   var cardGrid=new Grid();cardGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});cardGrid.RowDefinitions.Add(new RowDefinition());cardGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});card.Child=cardGrid;
   var status=new Grid();status.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});status.ColumnDefinitions.Add(new ColumnDefinition());cardGrid.Children.Add(status);
   symbol=new ShapePath{Data=Geometry.Parse("M8,2 L8,12 M3,7 L8,12 L13,7 M2,12 L2,16 L14,16 L14,12"),Stroke=Accent,StrokeThickness=1.8,Width=24,Height=24,Stretch=Stretch.Uniform,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
   symbolBox=new Border{Width=44,Height=44,CornerRadius=new CornerRadius(12),Background=BrushOf("#E6F4F0"),Child=symbol};status.Children.Add(symbolBox);
   var labels=new StackPanel{Margin=new Thickness(14,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(labels,1);status.Children.Add(labels);stateLabel=new TextBlock{Text="正在准备",FontSize=11,Foreground=Accent,Margin=new Thickness(0,0,0,3)};labels.Children.Add(stateLabel);title=new TextBlock{Text="正在寻找 PCL",FontSize=19,FontWeight=FontWeights.SemiBold,Foreground=Ink,TextWrapping=TextWrapping.Wrap};labels.Children.Add(title);
   var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,16,0,14)};Grid.SetRow(scroll,1);cardGrid.Children.Add(scroll);var text=new StackPanel();scroll.Content=text;
   body=new TextBlock{Text="正在识别启动器，请稍候。",TextWrapping=TextWrapping.Wrap,LineHeight=23,Foreground=BrushOf("#4B6368")};text.Children.Add(body);details=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,LineHeight=20,Foreground=Muted,Margin=new Thickness(0,12,0,0)};text.Children.Add(details);
   progress=new ProgressBar{Height=4,IsIndeterminate=true,Foreground=Accent,Background=BrushOf("#E9F1EE"),BorderThickness=new Thickness(0)};Grid.SetRow(progress,2);cardGrid.Children.Add(progress);
   var footer=new Grid{Margin=new Thickness(0,20,0,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Grid.SetRow(footer,2);content.Children.Add(footer);
   undo=Button("恢复原状",()=>Run(true));undo.Style=ButtonStyle(Brushes.Transparent,Muted,Brushes.Transparent,BrushOf("#E6EFEC"),Ink);undo.HorizontalAlignment=HorizontalAlignment.Left;undo.Padding=new Thickness(8,9,8,9);footer.Children.Add(undo);
   var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);footer.Children.Add(actions);pick=Button("选择 PCL",Choose);pick.Margin=new Thickness(0,0,10,0);actions.Children.Add(pick);retry=Button("重新检查",()=>{if(canFinish)Close();else Run(false);});retry.Style=ButtonStyle(Accent,Brushes.White,Accent,BrushOf("#14777A"),Brushes.White);retry.MinWidth=100;actions.Children.Add(retry);
   Loaded+=(s,e)=>Run(false);Closing+=(s,e)=>{if(busy)e.Cancel=true;};
   StateChanged+=(s,e)=>{maximizeGlyph.Data=Geometry.Parse(WindowState==WindowState.Maximized?"M4,1 L11,1 L11,8 M1,4 L8,4 L8,11 L1,11 Z":"M1,1 L11,1 L11,11 L1,11 Z");maximizeButton.ToolTip=WindowState==WindowState.Maximized?"还原":"最大化";frame.CornerRadius=new CornerRadius(WindowState==WindowState.Maximized?0:12);};
  }
  static Brush BrushOf(string hex){var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);brush.Freeze();return brush;}
  UIElement TitleBar(){
   var bar=new Grid{Margin=new Thickness(16,0,8,0)};bar.ColumnDefinitions.Add(new ColumnDefinition());bar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var branding=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};bar.Children.Add(branding);
   branding.Children.Add(new ShapePath{Data=Geometry.Parse("M9,1 L9,11 M4,6 L9,11 L14,6 M2,12 L2,17 L16,17 L16,12"),Stroke=Accent,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=18,Height=18,Stretch=Stretch.Uniform,Margin=new Thickness(3,0,10,0)});
   branding.Children.Add(new TextBlock{Text="PCL2-download-repair",FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center});branding.Children.Add(new Border{CornerRadius=new CornerRadius(5),Background=BrushOf("#E5EEEB"),Padding=new Thickness(6,2,6,2),Margin=new Thickness(9,0,0,0),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text="v"+BuildInfo.AdapterVersion,FontSize=10,Foreground=Muted}});
   var controls=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(controls,1);bar.Children.Add(controls);controls.Children.Add(CaptionButton("最小化","M1,6 L11,6",()=>WindowState=WindowState.Minimized,false));maximizeButton=CaptionButton("最大化","M1,1 L11,1 L11,11 L1,11 Z",()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized,false);maximizeGlyph=(ShapePath)maximizeButton.Content;controls.Children.Add(maximizeButton);closeButton=CaptionButton("关闭","M1,1 L11,11 M11,1 L1,11",Close,true);controls.Children.Add(closeButton);return bar;
  }
  Button CaptionButton(string name,string geometry,Action action,bool close){
   var b=new Button{Width=36,Height=32,Padding=new Thickness(8),Margin=new Thickness(2,0,0,0),ToolTip=name,Style=ButtonStyle(Brushes.Transparent,Muted,Brushes.Transparent,close?BrushOf("#FAE8E7"):BrushOf("#E6EFEC"),close?BrushOf("#C74D49"):Ink)};var path=new ShapePath{Data=Geometry.Parse(geometry),Width=12,Height=12,Stretch=Stretch.Uniform,StrokeThickness=1.4,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};path.SetBinding(ShapePath.StrokeProperty,new Binding("Foreground"){Source=b});b.Content=path;AutomationProperties.SetName(b,name);WindowChrome.SetIsHitTestVisibleInChrome(b,true);b.Click+=(s,e)=>action();return b;
  }
  static Style ButtonStyle(Brush background,Brush foreground,Brush borderBrush,Brush hover,Brush hoverText){
   var style=new Style(typeof(Button));style.Setters.Add(new Setter(Control.BackgroundProperty,background));style.Setters.Add(new Setter(Control.ForegroundProperty,foreground));style.Setters.Add(new Setter(Control.BorderBrushProperty,borderBrush));style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));style.Setters.Add(new Setter(Control.FontSizeProperty,13.0));
   var border=new FrameworkElementFactory(typeof(Border));border.Name="Chrome";border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));foreach(var binding in new[]{new{To=Border.BackgroundProperty,From="Background"},new{To=Border.BorderBrushProperty,From="BorderBrush"},new{To=Border.BorderThicknessProperty,From="BorderThickness"},new{To=Border.PaddingProperty,From="Padding"}})border.SetBinding(binding.To,new Binding(binding.From){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);content.SetValue(ContentPresenter.RecognizesAccessKeyProperty,true);border.AppendChild(content);var template=new ControlTemplate(typeof(Button)){VisualTree=border};
   var over=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};over.Setters.Add(new Setter(Control.BackgroundProperty,hover));over.Setters.Add(new Setter(Control.ForegroundProperty,hoverText));template.Triggers.Add(over);var pressed=new Trigger{Property=System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,Value=true};pressed.Setters.Add(new Setter(UIElement.OpacityProperty,0.72,"Chrome"));template.Triggers.Add(pressed);var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.42,"Chrome"));template.Triggers.Add(disabled);var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Chrome"));focus.Setters.Add(new Setter(Border.BorderThicknessProperty,new Thickness(2),"Chrome"));template.Triggers.Add(focus);style.Setters.Add(new Setter(Control.TemplateProperty,template));return style;
  }
  static Button Button(string text,Action action){var b=new Button{Content=text,Padding=new Thickness(17,10,17,10),MinWidth=90};b.Click+=(s,e)=>action();return b;}
  void SetState(string state,string heading,string message,string context){
   bool working=state=="working",success=state=="success"||state=="removed";canFinish=success;title.Text=heading;body.Text=message;details.Text=context;details.Visibility=string.IsNullOrEmpty(context)?Visibility.Collapsed:Visibility.Visible;stateLabel.Text=working?"自动识别与接入":success?"可以继续使用 PCL":"还需要一步";var color=state=="error"?BrushOf("#BC7B25"):Accent;stateLabel.Foreground=color;symbol.Stroke=color;symbolBox.Background=state=="error"?BrushOf("#FFF3DE"):BrushOf("#E6F4F0");symbol.Data=Geometry.Parse(success?"M2,9 L6,13 L15,3":state=="error"?"M8,2 L8,10 M8,14 L8,14.3":"M8,2 L8,12 M3,7 L8,12 L13,7 M2,12 L2,16 L14,16 L14,12");progress.Visibility=working?Visibility.Visible:Visibility.Collapsed;retry.Content=success?"完成":"重新检查";
  }
  void Choose(){var dialog=new Microsoft.Win32.OpenFileDialog{Title="选择你平时用来打开 PCL 的程序",Filter="PCL 程序 (*.exe)|*.exe"};if(dialog.ShowDialog(this)==true){selected=dialog.FileName;Run(false);}}
  async void Run(bool remove){
   if(busy)return;busy=true;retry.IsEnabled=pick.IsEnabled=undo.IsEnabled=closeButton.IsEnabled=false;SetState("working",remove?"正在恢复原状":"正在接入修复","正在识别启动器并检查组件，请稍候。","");
   try{
    var targets=selected==null?InstallEngine.Find(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)):new System.Collections.Generic.List<Target>{InstallEngine.Identify(selected)};
    if(targets.Count==0||targets.Contains(null)){SetState("error","还没找到你的 PCL","把本工具文件夹放在 PCL 程序旁边，或点“选择 PCL”找到原来的启动器。","");return;}
    var results=new System.Collections.Generic.List<string>();int failures=0;
    foreach(var target in targets){details.Text=target.ToString();details.Visibility=Visibility.Visible;try{await Task.Run(()=>InstallEngine.Apply(target,remove));results.Add(target.ToString());}catch(Exception ex){failures++;results.Add(target+"\n"+ex.Message);}}
    if(failures>0)SetState("error","还有一步需要处理","请按下面的提示处理，然后点“重新检查”。",string.Join("\n\n",results));else SetState(remove?"removed":"success",remove?"已恢复原状":"已完成接入",remove?"继续打开原来的 PCL 即可。游戏目录、账号和启动设置保持原样。":"以后继续打开原来的 PCL 图标，修复组件会自动加载。游戏目录、账号和启动设置保持原样。",string.Join("\n",results));
   }catch(Exception ex){SetState("error","需要你帮一下忙",ex.Message,"");}finally{busy=false;retry.IsEnabled=pick.IsEnabled=undo.IsEnabled=closeButton.IsEnabled=true;}
  }
 }
}
