param([switch]$VerifyRegression)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Compile the actual settings page against isolated layout stubs. Listing's
# column/CurHeight behavior follows read-only IL inspection of 1.7.4995 rev309.
# This tests layout policy, not native rendering or touchscreen behavior.
$stubs = @'
namespace UnityEngine {
 public struct Vector2 {public float x,y;}
 public struct Rect {public float x,y,width,height;public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}}
 public static class Mathf {public static float Max(float a,float b)=>System.Math.Max(a,b);public static float Abs(float a)=>System.Math.Abs(a);public static float Clamp(float v,float a,float b)=>System.Math.Clamp(v,a,b);}
 public static class GUIUtility {public static string systemCopyBuffer;}
}
namespace Verse {
 public class ModContentPack {}
 public class Mod {public Mod(ModContentPack c){}public T GetSettings<T>() where T:new()=>new T();public virtual string SettingsCategory()=>"";public virtual void DoSettingsWindowContents(UnityEngine.Rect r){}}
 public class ModSettings {public virtual void ExposeData(){}}
 public static class Translation {public static string Translate(this string s)=>s;}
 public enum LoadSaveMode {PostLoadInit}
 public static class Scribe {public static LoadSaveMode mode;}
 public static class Scribe_Values {public static void Look<T>(ref T v,string k,T d){}}
 public static class Prefs {public static float UIScale;}
 public class CameraMapConfig {public Range sizeRange;}
 public struct Range {public float min,max;}
 public class CameraDriver {public CameraMapConfig config;}
 public static class Find {public static CameraDriver CameraDriver;}
 public static class Widgets {
  public static UnityEngine.Rect View,Viewport;
  public static void BeginScrollView(UnityEngine.Rect r,ref UnityEngine.Vector2 s,UnityEngine.Rect v){Viewport=r;View=v;}
  public static void EndScrollView(){}
 }
 public class Listing_Standard {
  public static Listing_Standard Last;
  public static float RowHeight=24f;
  public bool maxOneColumn;
  public float CurHeight;
  public int Columns,Sliders;
  public readonly System.Collections.Generic.List<string> Controls=new System.Collections.Generic.List<string>();
  UnityEngine.Rect rect;
  public void Begin(UnityEngine.Rect r){rect=r;CurHeight=0;Last=this;}
  public void End(){}
  void Add(string key,float height){if(!maxOneColumn&&CurHeight+height>rect.height){Columns++;CurHeight=0;}Controls.Add(key);CurHeight+=height+2;}
  public void CheckboxLabeled(string key,ref bool value,string tip){Add(key,RowHeight);}
  public void Label(string key){Add(key,RowHeight);}
  public float Slider(float value,float lo,float hi){Sliders++;Add("slider",22);return value;}
  public bool ButtonText(string key){Add(key,30);return false;}
  public void Gap(){CurHeight+=12;}
 }
}
namespace RimTouch.TouchPlatform {
 public static class TouchSource {public static string Status=>"test";}
 public static class TouchInputProof {public static bool RuntimeDebugLogEnabled;public static string CopyText=>"";public static void SetRuntimeDebugLog(bool v){RuntimeDebugLogEnabled=v;}}
 public class ZoomRangeLease {public bool Update(float a,float b,bool e,float c,float d,out float x,out float y){x=a;y=b;return false;}}
}
namespace RimTouch {
 public static class RimTouchBootstrap {public static bool UiScaleHookReady=true;}
 public static class SettingsLayoutTests {
  static void Assert(bool v,string message){if(!v)throw new System.Exception(message);}
  public static void Run(){
   foreach(float rowHeight in new[]{24f,42f,64f}) {
    Verse.Listing_Standard.RowHeight=rowHeight;
    var mod=new RimTouchMod(new Verse.ModContentPack());
    float measured=0;
    for(int pass=0;pass<5;pass++) {
     mod.DoSettingsWindowContents(new UnityEngine.Rect(0,0,600,480));
     var listing=Verse.Listing_Standard.Last;
     Assert(listing.Columns==0,"Settings wrapped into clipped columns, pass "+pass);
     Assert(listing.Sliders==9,"A settings slider disappeared");
     Assert(listing.Controls.Exists(k=>k.StartsWith("RimTouch.PanInertiaStrength")),"Missing inertia settings");
     Assert(listing.Controls.Contains("RimTouch.ResetZoomRange"),"Missing last settings control");
     Assert(listing.CurHeight>480,"Complete settings should overflow this viewport");
     if(pass>0) {
      Assert(System.Math.Abs(listing.CurHeight-measured)<.01f,"Height shrank between GUI passes");
      Assert(System.Math.Abs(Verse.Widgets.View.height-listing.CurHeight)<.01f,"Scroll extent omits content");
      float maxScroll=Verse.Widgets.View.height-Verse.Widgets.Viewport.height;
      Assert(System.Math.Abs(listing.CurHeight-maxScroll-480)<.01f,"Bottom control is unreachable");
     }
     measured=listing.CurHeight;
    }
   }
  }
 }
}
'@
$source = [IO.File]::ReadAllText((Join-Path $root 'Source/RimTouchMod.cs'))
if ($VerifyRegression) { $source = $source.Replace('listing.maxOneColumn = true;', '') }
$body = $source -replace '(?m)^using [^;]+;\r?\n',''
Add-Type -WarningAction SilentlyContinue -TypeDefinition ("using UnityEngine; using Verse; using System.Runtime.CompilerServices; using RimTouch.TouchPlatform;`n" + $stubs + $body)
if ($VerifyRegression) {
    try { [RimTouch.SettingsLayoutTests]::Run() }
    catch {
        if ($_.Exception.ToString().Contains('Settings wrapped into clipped columns')) {
            'PASS negative control: removing single-column policy reproduces clipped-column failure'
            exit 0
        }
        throw
    }
    throw 'Regression test did not detect the old layout failure'
}
[RimTouch.SettingsLayoutTests]::Run()
'PASS settings layout: single column, all 9 sliders, bottom control reachable, stable full height across GUI passes and row heights'
