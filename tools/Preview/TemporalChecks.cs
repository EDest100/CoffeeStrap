// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.UI.Utility;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static ManualMotionTime? _manualTime;
    private static readonly List<object> TemporalRecordings=[];
    private sealed class ManualMotionTime : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(int milliseconds) => _ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }
    private static void StepFrame(int milliseconds=16)
    { _manualTime?.Advance(milliseconds); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void RawClick(Window window,Point point)
    { window.MouseMove(point,RawInputModifiers.None);window.MouseDown(point,MouseButton.Left,RawInputModifiers.None);window.MouseUp(point,MouseButton.Left,RawInputModifiers.None); }
    private static void SaveTemporal(Window window,string name)
    {
        using var frame=window.GetLastRenderedFrame()!;
        var folder=Path.Combine(_output,"temporal-frames");Directory.CreateDirectory(folder);
        var filename="temporal-frames/"+name+".png";
        frame.Save(Path.Combine(folder,name+".png"),PngBitmapEncoderOptions.Default);
        Captures.Add(new{fileName=filename,theme="Dark",width=frame.PixelSize.Width,height=frame.PixelSize.Height,actualViewType=window.GetType().FullName,kind="temporal"});
        string stream=name.StartsWith("group-close")?"group-collapse":name.StartsWith("sidebar-open")?"sidebar-reflow":"hover-reversal";
        TemporalRecordings.Add(new{stream,file=filename,elapsedMilliseconds=(_manualTime?.GetTimestamp()??0)/TimeSpan.TicksPerMillisecond});
    }
    private static void VerifyTemporalRegressions()
    {
        var original=JsonSerializer.Serialize(App.Settings.Prop);
        _manualTime=new ManualMotionTime();CoffeeMotion.TimeSource=_manualTime;
        App.Settings.Prop.DisableAnimations=false;App.RemoveAnimationSettings();App.Settings.Prop.Theme=Theme.Dark;
        Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var body=new StackPanel{Spacing=12};
        for(int i=0;i<3;i++)body.Children.Add(new OptionControl{Header="Dependent setting "+(i+1),Description="Visible content remains intact while the group closes.",InnerContent=new ToggleSwitch()});
        var group=new CardExpander{Header="Feature group",Description="Expand and collapse",Content=body,IsExpanded=true};
        var fixture=new Window{Width=760,Height=740,Content=new StackPanel{Margin=new Thickness(20),Children={group,new TextBlock{Text="Content after the group"}}}};
        fixture.Show();RunFor(320);
        var reveal=group.GetVisualDescendants().OfType<AnimatedReveal>().Single();
        var header=group.GetVisualDescendants().OfType<ToggleButton>().First(b=>b.Name=="HeaderButton");
        double open=reveal.RevealedHeight;
        var collapse=new List<object>();var collapseHeights=new List<double>();var clock=Stopwatch.StartNew();
        SaveTemporal(fixture,"group-close-start");RawClick(fixture,At(header,fixture));
        for(int i=0;i<10;i++)
        {
            StepFrame();double h=reveal.RevealedHeight;collapseHeights.Add(h);
            collapse.Add(new{frame=i,elapsed=clock.ElapsedMilliseconds,height=h,alpha=reveal.Opacity,contentHeight=reveal.Child!.Bounds.Height});
            Check(h<.1||reveal.Opacity>0,"group children remain visible before the collapse endpoint "+i);
            SaveTemporal(fixture,"group-close-"+i.ToString("D2"));
        }
        WaitFor(()=>!reveal.HasActiveMotion);
        SaveTemporal(fixture,"group-close-end");
        File.WriteAllText(Path.Combine(_output,"collapse-samples.json"),JsonSerializer.Serialize(collapse,new JsonSerializerOptions{WriteIndented=true}));
        Check(collapseHeights.Any(h=>h>1&&h<open-1),"group collapse renders actual intermediate clipped heights");
        Check(reveal.RevealedHeight<.1&&reveal.Opacity==0&&!reveal.IsEnabled,"collapsed group finishes at zero and excludes hidden keyboard controls");
        Check(collapseHeights.Where(h=>h>1&&h<open-1).Any()&&reveal.Child!.Bounds.Height>=open-1,"child layout stays at its natural height while clipping changes");
        var reversal=new List<object>();
        for(int i=0;i<8;i++)
        {
            double before=reveal.RevealedHeight;var inputClock=Stopwatch.StartNew();RawClick(fixture,At(header,fixture));
            double after=reveal.RevealedHeight;
            double allowed=reveal.NaturalHeight*Math.Min(1,3*inputClock.Elapsed.TotalMilliseconds/240)+2;
            Check(Math.Abs(after-before)<=allowed,"rapid group reversal remains bounded by elapsed frame time "+i,new{before,after,elapsed=inputClock.Elapsed.TotalMilliseconds,allowed});
            StepFrame(22);reversal.Add(new{frame=i,expanded=group.IsExpanded,height=reveal.RevealedHeight});
        }
        group.IsExpanded=true;WaitFor(()=>!reveal.HasActiveMotion);Pump();
        double beforeGrowth=reveal.RevealedHeight;
        body.Children.Add(new OptionControl{Header="Added dependent setting",Description="The expanded height follows content changes.",InnerContent=new ToggleSwitch()});
        var growth=new List<double>();
        for(int i=0;i<14;i++){StepFrame();growth.Add(reveal.RevealedHeight);}
        WaitFor(()=>!reveal.HasActiveMotion);
        Check(reveal.RevealedHeight>beforeGrowth+30&&growth.Any(h=>h>beforeGrowth+1&&h<reveal.RevealedHeight-1),"expanded group animates content-dependent height changes");
        fixture.Width=600;RunFor(80);WaitFor(()=>!reveal.HasActiveMotion);Check(reveal.RevealedHeight>=reveal.NaturalHeight-1,"expanded group remeasures after a width change");
        fixture.Hide();

        var w=new SettingsWindow(false);w.Show();var vm=(MainWindowViewModel)w.DataContext!;
        vm.NavigateToIntegrationsCommand.Execute(null);RunFor(350);
        Check(w.FindControl<TextBlock>("PageTitleText")==null,"feature area contains no redundant current-tab heading");
        var masterGroup=w.GetVisualDescendants().OfType<SettingsGroup>().First();var master=(OptionControl)masterGroup.Master!;
        var dependencyCards=masterGroup.GetVisualDescendants().OfType<OptionControl>().Where(c=>c!=master).ToArray();
        RawClick(w,At(master,w,.4,.5));Pump();
        Check(!App.Settings.Prop.EnableActivityTracking&&dependencyCards.Length>=5&&dependencyCards.All(c=>!c.IsEffectivelyEnabled),"master setting retains its existing dependent disabled states");
        RawClick(w,At(master,w,.4,.5));Pump();Check(App.Settings.Prop.EnableActivityTracking,"master setting re-enables through the existing binding");
        var nav=w.FindControl<CoffeeNavigationView>("NavView")!;var items=nav.MenuItems.Where(i=>i.IsVisible&&i.IsEnabled).ToArray();
        var pane=nav.GetVisualDescendants().OfType<Border>().First(b=>b.Name=="PART_Pane");
        var area=nav.GetVisualDescendants().OfType<Border>().First(b=>b.Name=="PART_FeatureArea");
        w.MouseMove(new Point(650,70),RawInputModifiers.None);RunFor(400);nav.IsPaneOpen=false;RunFor(300);
        double compact=nav.CurrentPaneLength;double wide=area.Bounds.Width;
        var widths=new List<double>();var resize=new List<object>();
        SaveTemporal(w,"sidebar-open-start");
        var activationPoint=At(items[1],w,.05,.5);
        w.MouseMove(activationPoint,RawInputModifiers.None);
        for(int i=0;i<12;i++)
        {
            StepFrame();widths.Add(nav.CurrentPaneLength);
            var paneRight=pane.TranslatePoint(new Point(pane.Bounds.Width,0),nav)!.Value.X;
            var areaLeft=area.TranslatePoint(default,nav)!.Value.X;
            Check(paneRight<=areaLeft+.5,"sidebar and features never overlap during expansion "+i);
            resize.Add(new{frame=i,rail=nav.CurrentPaneLength,features=area.Bounds.Width,paneRight,areaLeft});
            SaveTemporal(w,"sidebar-open-"+i.ToString("D2"));
        }
        WaitFor(()=>!nav.HasActiveMotion);
        SaveTemporal(w,"sidebar-open-end");
        File.WriteAllText(Path.Combine(_output,"sidebar-samples.json"),JsonSerializer.Serialize(resize,new JsonSerializerOptions{WriteIndented=true}));
        Check(widths.Any(x=>x>compact+1&&x<nav.OpenPaneLength-1)&&area.Bounds.Width<wide-100,"sidebar renders intermediate feature reflow frames",new{compact,wide,widths,nav.IsPaneOpen,features=area.Bounds.Width,activationPoint,nav.WidthDiagnostics});
        w.MouseMove(At(items[1],w),RawInputModifiers.None);RunFor(250);double hovered=nav.HighlightTarget;
        var itemY=At(items[1],w).Y;
        var paneX=pane.TranslatePoint(default,w)!.Value.X;
        w.MouseMove(new Point(paneX+2,itemY),RawInputModifiers.None);StepFrame();
        Check(Math.Abs(nav.HighlightTarget-hovered)<.1,"navigation gaps preserve hover instead of detouring to selection");
        vm.NavigateToIntegrationsCommand.Execute(null);Pump();Check(Math.Abs(nav.HighlightTarget-hovered)<.1,"selection updates do not fight an active hover highlight");
        var highlightPositions=new List<double>();
        for(int i=0;i<8;i++)
        {
            double before=nav.HighlightPosition;w.MouseMove(At(items[i%2==0?2:1],w),RawInputModifiers.None);
            Check(Math.Abs(nav.HighlightPosition-before)<1,"hover reversal retargets without a position snap "+i);
            StepFrame(20);highlightPositions.Add(nav.HighlightPosition);
            SaveTemporal(w,"highlight-reverse-"+i.ToString("D2"));
        }
        Check(highlightPositions.Distinct().Count()>3,"hover highlight renders intermediate positions across rapid reversals");
        RunFor(300);var edge=pane.TranslatePoint(new Point(pane.Bounds.Width+6,pane.Bounds.Height*.4),w)!.Value;
        w.MouseMove(edge,RawInputModifiers.None);RunFor(120);WaitFor(()=>!nav.HasActiveMotion);
        Check(!nav.IsPaneOpen&&Math.Abs(nav.CurrentPaneLength-nav.CompactPaneLength)<1,"sidebar collapses after leaving its actual boundary by six pixels");
        foreach(double width in new[]{750d,800d,1125d})
        {
            w.Width=width;RunFor(90);nav.IsPaneOpen=true;RunFor(300);
            VerifyCardPanels(w,"inline sidebar at "+width);nav.IsPaneOpen=false;RunFor(280);
        }
        w.Hide();
        var caption=new TitleBar{Title="CoffeeStrap"};var captionWindow=new Window{Width=500,Height=200,Content=caption};captionWindow.Show();RunFor(80);
        var buttons=caption.GetVisualDescendants().OfType<IconButton>().ToArray();
        var minimize=buttons.First(b=>b.Name=="PART_MinimizeButton");var maximize=buttons.First(b=>b.Name=="PART_MaximizeButton");var close=buttons.First(b=>b.Name=="PART_CloseButton");
        Check(WindowDecorationProperties.GetElementRole(maximize)==WindowDecorationsElementRole.MaximizeButton,"custom maximize button supplies Avalonia's native caption role");
        Click(captionWindow,At(maximize,captionWindow));Check(captionWindow.WindowState==WindowState.Maximized,"custom maximize changes window state in the isolated platform");
        Click(captionWindow,At(maximize,captionWindow));Check(captionWindow.WindowState==WindowState.Normal,"custom restore returns window state to normal");
        Click(captionWindow,At(minimize,captionWindow));Check(captionWindow.WindowState==WindowState.Minimized,"custom minimize changes window state in the isolated platform");
        captionWindow.WindowState=WindowState.Normal;bool closed=false;captionWindow.Closed+=(_,_)=>closed=true;
        Click(captionWindow,At(close,captionWindow));Check(closed,"custom close closes an isolated window without production startup");
        File.WriteAllText(Path.Combine(_output,"temporal-checks.json"),JsonSerializer.Serialize(new{collapse,reversal,growth,resize,highlightPositions,nativeWindowsInputVerified=false},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(_output,"temporal-recordings.json"),JsonSerializer.Serialize(TemporalRecordings,new JsonSerializerOptions{WriteIndented=true}));
        App.Settings.Prop=JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(original)!;
        CoffeeMotion.TimeSource=TimeProvider.System;_manualTime=null;
        App.Settings.Prop.DisableAnimations=false;App.RemoveAnimationSettings();
    }
    private static void RenderRevisionSmoke()
    {
        App.Settings.Prop.DisableAnimations=false; App.RemoveAnimationSettings();
        foreach(var theme in new[]{Theme.Dark,Theme.Light})
        {
            App.Settings.Prop.Theme=theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var w=new SettingsWindow(false); w.Show();var vm=(MainWindowViewModel)w.DataContext!;
            vm.NavigateToIntegrationsCommand.Execute(null);Capture(w,theme,"integrations");
            vm.NavigateToBehaviourCommand.Execute(null);Capture(w,theme,"bootstrapper");
            w.FindControl<CoffeeNavigationView>("NavView")!.IsPaneOpen=true;Capture(w,theme,"navigation-open");
            vm.NavigateToAppearanceCommand.Execute(null);Capture(w,theme,"appearance");w.Hide();
        }
    }
}
