$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Isolated policy tests compile the actual VelocityHistory, UiScroll, TouchTapRepair,
# UiHitTester, and ButtonInvisibleDraggable patch logic against lightweight stubs.
$stubs = @'
namespace UnityEngine {
 public enum FocusType { Native, Keyboard, Passive }
 public struct Vector2 {
  public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;}
  public static Vector2 zero => new Vector2(); public float sqrMagnitude=>x*x+y*y; public float magnitude=>(float)System.Math.Sqrt(sqrMagnitude); public Vector2 normalized=>this/magnitude;
  public static Vector2 operator +(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y);
  public static Vector2 operator -(Vector2 a,Vector2 b)=>new Vector2(a.x-b.x,a.y-b.y);
  public static Vector2 operator *(Vector2 a,float s)=>new Vector2(a.x*s,a.y*s);
  public static Vector2 operator /(Vector2 a,float s)=>new Vector2(a.x/s,a.y/s);
  public static float Distance(Vector2 a,Vector2 b)=>(a-b).magnitude;
 }
 public struct Vector3 {
  public float x,y,z; public Vector3(float x,float y,float z=0){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero => new Vector3();
  public float sqrMagnitude => x*x+y*y+z*z;
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float s)=>new Vector3(a.x*s,a.y*s,a.z*s);
  public static Vector3 operator /(Vector3 a,float s)=>new Vector3(a.x/s,a.y/s,a.z/s);
 }
 public struct Rect {
  public float x,y,width,height; public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}
  public float xMin=>x;public float yMin=>y;public float xMax=>x+width;public float yMax=>y+height;
  public bool Contains(Vector2 p)=>p.x>=x&&p.x<xMax&&p.y>=y&&p.y<yMax;
 }
 public static class Mathf {
  public static float Max(float a,float b)=>System.Math.Max(a,b);public static int Min(int a,int b)=>System.Math.Min(a,b);
  public static float Min(float a,float b)=>System.Math.Min(a,b);
  public static float Abs(float a)=>System.Math.Abs(a);public static float Clamp(float a,float lo,float hi)=>System.Math.Clamp(a,lo,hi);
  public static float Clamp01(float a)=>Clamp(a,0,1);public static float Exp(float a)=>(float)System.Math.Exp(a);
 }
 public static class Time {public static int frameCount;public static float realtimeSinceStartup;}
 public static class Screen {public static int width=1920,height=1210;}
 public static class Input {
  public static Vector3 mousePosition;
  public static bool mouseDown,mouseUp,mouseHeld;
  public static bool GetMouseButtonDown(int b)=>b==0&&mouseDown;
  public static bool GetMouseButtonUp(int b)=>b==0&&mouseUp;
  public static bool GetMouseButton(int b)=>b==0&&mouseHeld;
 }
 public static class GUI {public static bool enabled=true;}
 public static class GUIUtility {
  public static float scale=1.5f; public static Vector2 offset, groupOffset;
  public static int keyboardControl;
  public static Vector2 ScreenToGUIPoint(Vector2 p)=>(p-offset)/scale;
  public static Vector2 GUIToScreenPoint(Vector2 p)=>offset + groupOffset + p*scale;
  public static int GetControlID(FocusType f, Rect r)=>((int)(r.x*10+groupOffset.x*10)<<16)^((int)(r.y*10+groupOffset.y*10));
 }
 public enum EventType {MouseDown,MouseUp,MouseDrag,Layout,Repaint,Used}
 public class Event {public static Event current;public EventType type;public int button,clickCount;public Vector2 mousePosition;public bool isMouse=>type==EventType.MouseDown||type==EventType.MouseUp||type==EventType.MouseDrag;public void Use(){type=EventType.Used;}}
}
namespace Verse {
 public enum ProgramState { Entry, MapInitializing, Playing }
 public static class Current { public static ProgramState ProgramState = ProgramState.Playing; }
 public class UIRoot {}
 public class Map {}
 public class Letter {}
 public class LetterStack { public System.Collections.Generic.List<Letter> LettersListForReading = new System.Collections.Generic.List<Letter>(); public float LastTopY = 600f; }
 public class Designator {}
 public class DesignatorManager { public Designator SelectedDesignator; public void Deselect(){SelectedDesignator=null;} }
 public class Targeter { public bool IsTargeting; public void StopTargeting(){IsTargeting=false;} }
 public static class GizmoGridDrawer { public static float HeightDrawnRecently; }
 public static class UI {
  public static int screenWidth=1280,screenHeight=807;
  public static UnityEngine.Vector3 UIToMapPosition(UnityEngine.Vector2 p)=>new UnityEngine.Vector3(p.x,0,p.y);
 }
 public class Window {public UnityEngine.Rect windowRect;}
 public class WindowStack {
  public Window currentlyDrawnWindow;
  public readonly System.Collections.Generic.List<Window> windows = new System.Collections.Generic.List<Window>();
  public Window GetWindowAt(UnityEngine.Vector2 p) {
   for(int i=windows.Count-1;i>=0;i--) if(windows[i].windowRect.Contains(p)) return windows[i];
   return null;
  }
 }
 public static class Mouse { public static bool IsOver(UnityEngine.Rect r)=>UnityEngine.Event.current!=null&&r.Contains(UnityEngine.Event.current.mousePosition); }
 public static class Widgets {
  public enum DraggableResult { Idle, Pressed, Dragged, DraggedThenPressed }
  static int activeControl; static UnityEngine.Vector3 mouseStart; static bool dragged;
  public static bool checkboxPainting, checkboxPaintingState;
  public static DraggableResult ButtonInvisibleDraggable(UnityEngine.Rect butRect, bool doMouseoverSound=false) {
   int id = UnityEngine.GUIUtility.GetControlID(UnityEngine.FocusType.Passive, butRect);
   if (UnityEngine.Input.GetMouseButtonDown(0) && Mouse.IsOver(butRect)) { activeControl = id; mouseStart = UnityEngine.Input.mousePosition; dragged = false; }
   if (activeControl == id) {
    if (UnityEngine.Input.GetMouseButtonUp(0)) { activeControl = 0; if (!Mouse.IsOver(butRect)) return DraggableResult.Idle; return !dragged ? DraggableResult.Pressed : DraggableResult.DraggedThenPressed; }
    if (!UnityEngine.Input.GetMouseButton(0)) { activeControl = 0; return DraggableResult.Idle; }
    if (!dragged && (mouseStart - UnityEngine.Input.mousePosition).sqrMagnitude > 36f) { dragged = true; return DraggableResult.Dragged; }
   }
   return DraggableResult.Idle;
  }
  public static DraggableResult PatchedButtonInvisibleDraggable(UnityEngine.Rect rect) {
   DraggableResult res = DraggableResult.Idle;
   if (!RimTouch.WidgetsButtonInvisibleDraggableTouchPatch.Prefix(rect, ref res)) return res;
   res = ButtonInvisibleDraggable(rect, false);
   RimTouch.WidgetsButtonInvisibleDraggableTouchPatch.Postfix(rect, res);
   return res;
  }
  public static void Toggle(UnityEngine.Rect rect, ref bool checkOn, bool paintable=false) {
   DraggableResult r = PatchedButtonInvisibleDraggable(rect);
   if (r == DraggableResult.Pressed) checkOn = !checkOn;
   else if (r == DraggableResult.Dragged && paintable) { checkOn = !checkOn; checkboxPainting = true; checkboxPaintingState = checkOn; }
   if (paintable && Mouse.IsOver(rect) && checkboxPainting && UnityEngine.Input.GetMouseButton(0) && checkOn != checkboxPaintingState) checkOn = checkboxPaintingState;
  }
 }
 public static class Find {
  public static WindowStack WindowStack=new WindowStack();
  public static UIRoot UIRoot;
  public static Map CurrentMap;
  public static RimWorld.ColonistBar ColonistBar;
  public static LetterStack LetterStack=new LetterStack();
  public static DesignatorManager DesignatorManager=new DesignatorManager();
  public static Targeter Targeter=new Targeter();
 }
}
namespace RimWorld {
 public class AlertsReadout { public float AlertsHeight; }
 public class UIRoot_Play : Verse.UIRoot { public AlertsReadout alerts = new AlertsReadout(); }
 public class ColonistBar {
  public struct Entry {}
  public UnityEngine.Rect occupiedRect;
  public bool TryGetEntryAt(UnityEngine.Vector2 pos, out Entry entry) { entry = default(Entry); return occupiedRect.width > 0 && occupiedRect.Contains(pos); }
 }
}
namespace RimWorld.Planet {}
namespace HarmonyLib { public class HarmonyPatch : System.Attribute { public HarmonyPatch(System.Type t, string m, System.Type[] p=null){} } }
namespace RimTouch.TouchPlatform {
 public enum ContactPhase {Began,Moved,Stationary,Ended}
 public struct ContactTouch {public long fingerId;public ContactPhase phase;public UnityEngine.Vector2 position,deltaPosition;}
 public static class TouchSource {public static int Count;public static ContactTouch touch;public static ContactTouch GetTouch(int i)=>touch;}
 public static class TouchInputProof {public static bool IsRecording=>false;}
}
namespace RimTouch {
 internal static class ReflectionGuard {
  public static System.Reflection.FieldInfo Field(System.Type t,string n,bool l)=>null;
  public static bool TryGetField<T>(System.Reflection.FieldInfo f,object o,out T v){v=default(T);return false;}
  public static bool TrySetField(System.Reflection.FieldInfo f,object o,object v)=>false;
 }
 public enum TouchMode {None,OneFinger,SelectionHold,MapPan,TwoFinger}
 public static partial class TouchInputDriver {
  internal static bool TouchModeEnabled=true;static bool ignoreTouchesUntilAllReleased,uiScrollGestureActive,uiScrollInertiaActive,startedOverUi,suppressReleaseTap;
  static bool twoFingerStartedWithVanillaMapTool, worldActive;
  static int suppressUiClickUntilFrame=-1000,suppressWindowCloseUntilFrame=-1000,suppressVanillaMapInputUntilFrame=-1000;
  static long primaryFingerId=1;static UnityEngine.Vector2 startGui,uiScrollLastGui,uiScrollVelocity;
  static UnityEngine.Rect uiScrollActiveRect,uiScrollInertiaRect;
  static float uiScrollInertiaStartTime,lastUiScrollInertiaTime,lastUiScrollSampleTime;
  static int lastUiScrollApplyFrame=-1000,lastUiScrollInertiaApplyFrame=-1000;
  const float MovementDetectionSlopPixels=6,DragSlopPixels=18,MinUiScrollInertiaSpeed=5,MinUiScrollInertiaStartSpeed=50,MaxUiScrollInertiaSpeed=2000,PanInertiaDamping=5;
  static TouchMode mode;
  static bool IsWorldMapActive()=>worldActive;
  static bool IsTwoFingerTapGesture()=>false;
  static void CancelRimWorldDragBox(){}static void CancelWorldDragBox(){}
  public static bool ShouldSuppressUiClickInRect(UnityEngine.Rect r)=>uiScrollGestureActive;
  static bool IsFinite(UnityEngine.Vector2 p)=>true;
  struct ScrollProofEvent {}static ScrollProofEvent CaptureScrollProofEvent()=>new ScrollProofEvent();
  static void RecordScrollDecision(UnityEngine.Rect a,UnityEngine.Rect b,UnityEngine.Vector2 c,UnityEngine.Vector2 d,bool e,bool f,UnityEngine.Vector2 g,ScrollProofEvent i,bool j,ScrollDecision k){}
  static void Assert(bool p,string m){if(!p)throw new System.Exception(m);}
  static void Contact(float x,float y) {TouchPlatform.TouchSource.Count=1;TouchPlatform.TouchSource.touch=new TouchPlatform.ContactTouch {fingerId=1,phase=TouchPlatform.ContactPhase.Moved,position=new UnityEngine.Vector2(x*1.5f,1210-y*1210/807)};UnityEngine.Time.frameCount++;}
  public static void RunTests() {
   var rect=new UnityEngine.Rect(0,40,864,584);var view=new UnityEngine.Rect(0,0,844,972);var scroll=new UnityEngine.Vector2(0,100);
   var w=new Verse.Window();Verse.Find.WindowStack.currentlyDrawnWindow=w;uiTouchStartWindow=w;
   UnityEngine.Input.mousePosition=new UnityEngine.Vector3(1559,961);UnityEngine.GUIUtility.offset=new UnityEngine.Vector2(208,71.33333f);
   UnityEngine.Event.current=new UnityEngine.Event{type=UnityEngine.EventType.Layout,mousePosition=new UnityEngine.Vector2(831.3333f,94.66667f)};
   var local=GuiToLocalGuiPoint(new UnityEngine.Vector2(1039.3333f,166.0686f));
   Assert(UnityEngine.Vector2.Distance(local,UnityEngine.Event.current.mousePosition)<.01f,"1.5x coordinate regression");
   foreach(float scale in new[]{1f,2f}) {UnityEngine.GUIUtility.scale=scale;var a=GuiToLocalGuiPoint(new UnityEngine.Vector2(500,300));var b=GuiToLocalGuiPoint(new UnityEngine.Vector2(520,300));Assert(System.Math.Abs(b.x-a.x-30/scale)<.01f,"scaled displacement");}
   UnityEngine.GUIUtility.scale=1.5f;
   UnityEngine.Input.mousePosition=new UnityEngine.Vector3(300,910);UnityEngine.Event.current.mousePosition=new UnityEngine.Vector2(0,100);
   startGui=new UnityEngine.Vector2(400,300);Contact(421.3333f,303.334f);
   Assert(!ProcessScrollViewTouch(rect,view,ref scroll)&&!uiScrollGestureActive,"horizontal 21/3 stolen");
   Assert(!ShouldAllowWindowDrag(),"viewport candidate also drags window");
   uiScrollCandidateWindow=null;startGui=new UnityEngine.Vector2(928.6667f,84.70165f);Contact(930.6667f,103.376f);
   var edgeRect=new UnityEngine.Rect(10,100,904,488);var edgeView=new UnityEngine.Rect(0,0,888,532);
   UnityEngine.Input.mousePosition=new UnityEngine.Vector3(1396,1055);UnityEngine.Event.current.mousePosition=new UnityEngine.Vector2(752.6667f,19.33334f);
   Assert(!ProcessScrollViewTouch(edgeRect,edgeView,ref scroll)&&ShouldAllowWindowDrag(),"outside edge new gesture stolen");
   w.windowRect=new UnityEngine.Rect(0,-100,900,700);UnityEngine.Event.current.mousePosition.y+=100;
   Assert(!ProcessScrollViewTouch(edgeRect,edgeView,ref scroll),"moving window reclassified edge start");w.windowRect=new UnityEngine.Rect();
   UnityEngine.Input.mousePosition=new UnityEngine.Vector3(300,910);UnityEngine.Event.current.mousePosition=new UnityEngine.Vector2(0,100);
   startGui=new UnityEngine.Vector2(1055,300);Contact(1055,320);Assert(!ProcessScrollViewTouch(rect,view,ref scroll),"scrollbar native path stolen");
   UnityEngine.Input.mousePosition=new UnityEngine.Vector3(300,910);UnityEngine.Event.current.mousePosition=new UnityEngine.Vector2(0,100);
   startGui=new UnityEngine.Vector2(400,300);Contact(402,314);UnityEngine.Event.current.type=UnityEngine.EventType.MouseDown;
   primaryFingerId=-1;Assert(!ProcessScrollViewTouch(rect,view,ref scroll),"uninitialized touch claimed old start");primaryFingerId=1;
   Assert(ProcessScrollViewTouch(rect,view,ref scroll)&&uiScrollGestureActive,"vertical swipe not claimed");
   Assert(UnityEngine.Event.current.type==UnityEngine.EventType.Used,"late MouseDown not consumed");
   Verse.Find.WindowStack.currentlyDrawnWindow=new Verse.Window();Assert(!ProcessScrollViewTouch(rect,view,ref scroll),"scroll leaked into another window");

   // Nested ScrollView priority & boundary handoff
   Verse.Find.WindowStack.currentlyDrawnWindow=w;uiScrollGestureActive=false;uiScrollWindow=null;uiScrollCandidateWindow=null;
   var outerRect=new UnityEngine.Rect(0,40,864,584);var outerView=new UnityEngine.Rect(0,0,844,1200);var outerScroll=new UnityEngine.Vector2(0,200);
   var innerRect=new UnityEngine.Rect(20,80,400,240);var innerView=new UnityEngine.Rect(0,0,380,600);var innerScroll=new UnityEngine.Vector2(0,100);
   startGui=new UnityEngine.Vector2(300,250);
   Contact(300,252); // below slop pass observes outer then inner
   Assert(!ProcessScrollViewTouch(outerRect,outerView,ref outerScroll),"outer claimed below slop");
   Assert(!ProcessScrollViewTouch(innerRect,innerView,ref innerScroll),"inner claimed below slop");
   Contact(300,235); // upward swipe (dragFromStart.y < -6 -> scroll down)
   Assert(!ProcessScrollViewTouch(outerRect,outerView,ref outerScroll)&&!uiScrollGestureActive,"outer stole gesture from scrollable inner");
   Assert(ProcessScrollViewTouch(innerRect,innerView,ref innerScroll)&&uiScrollGestureActive&&SameRect(uiScrollActiveRect,innerRect),"inner did not claim nested scroll");
   // When inner is already at maxY (bottom boundary), outer takes over immediately
   uiScrollGestureActive=false;uiScrollWindow=null;uiScrollCandidateWindow=null;innerScroll=new UnityEngine.Vector2(0,360); // maxY = 600-240 = 360
   startGui=new UnityEngine.Vector2(300,250);Contact(300,252);
   ProcessScrollViewTouch(outerRect,outerView,ref outerScroll);ProcessScrollViewTouch(innerRect,innerView,ref innerScroll);
   Contact(300,235);
   Assert(ProcessScrollViewTouch(outerRect,outerView,ref outerScroll)&&SameRect(uiScrollActiveRect,outerRect),"outer did not take over when inner had no headroom");

   // Slider jitter filter
   uiScrollGestureActive=false;uiScrollWindow=null;uiScrollCandidateWindow=w;
   startGui=new UnityEngine.Vector2(400,300);Contact(402,303);UnityEngine.Event.current.type=UnityEngine.EventType.MouseDrag;FilterHorizontalSliderDrag();Assert(UnityEngine.Event.current.type==UnityEngine.EventType.Used,"jitter reached slider");
   Contact(430,303);UnityEngine.Event.current.type=UnityEngine.EventType.MouseDrag;FilterHorizontalSliderDrag();Assert(UnityEngine.Event.current.type==UnityEngine.EventType.MouseDrag,"horizontal slider drag blocked");

   // ButtonInvisibleDraggable same-frame multi-pass dedup (Layout + MouseDown + MouseUp + Repaint)
   var targetParamName = typeof(Verse.Widgets).GetMethod("ButtonInvisibleDraggable").GetParameters()[0].Name;
   Assert(typeof(RimTouch.WidgetsButtonInvisibleDraggableTouchPatch).GetMethod("Prefix").GetParameters()[0].Name == targetParamName, "Prefix parameter name mismatch with ButtonInvisibleDraggable");
   Assert(typeof(RimTouch.WidgetsButtonInvisibleDraggableTouchPatch).GetMethod("Postfix").GetParameters()[0].Name == targetParamName, "Postfix parameter name mismatch with ButtonInvisibleDraggable");
   uiScrollGestureActive=false;
   var toggleRectA=new UnityEngine.Rect(20,100,240,24);
   var toggleRectB=new UnityEngine.Rect(20,130,240,24);
   bool stateA=false, stateB=false;
   UnityEngine.Time.frameCount=500;
   UnityEngine.Input.mouseDown=true; UnityEngine.Input.mouseUp=true; UnityEngine.Input.mouseHeld=false;
   UnityEngine.Event.current=new UnityEngine.Event{type=UnityEngine.EventType.Layout,mousePosition=new UnityEngine.Vector2(50,110)};
   foreach(var pass in new[]{UnityEngine.EventType.Layout,UnityEngine.EventType.MouseDown,UnityEngine.EventType.MouseUp,UnityEngine.EventType.Repaint}) {
    UnityEngine.Event.current.type=pass;
    Verse.Widgets.Toggle(toggleRectA, ref stateA);
   }
   Assert(stateA,"same-frame 4-pass tap bounced checkbox back to false");
   // Another control in the same frame is independent
   UnityEngine.Event.current.mousePosition=new UnityEngine.Vector2(50,140);
   Verse.Widgets.Toggle(toggleRectB, ref stateB);
   Assert(stateB,"distinct control in same frame was wrongly suppressed");
   // Next frame tap toggles back cleanly
   UnityEngine.Time.frameCount=501;
   UnityEngine.Event.current.mousePosition=new UnityEngine.Vector2(50,110);
   foreach(var pass in new[]{UnityEngine.EventType.Layout,UnityEngine.EventType.MouseDown,UnityEngine.EventType.MouseUp,UnityEngine.EventType.Repaint}) {
    UnityEngine.Event.current.type=pass;
    Verse.Widgets.Toggle(toggleRectA, ref stateA);
   }
   Assert(!stateA,"second tap in next frame failed to toggle off");
   // Paintable drag across two checkboxes across frames
   UnityEngine.Time.frameCount=510; Verse.Widgets.checkboxPainting=false;
   UnityEngine.Input.mouseDown=true; UnityEngine.Input.mouseUp=false; UnityEngine.Input.mouseHeld=true;
   UnityEngine.Input.mousePosition=new UnityEngine.Vector3(50,110,0);
   UnityEngine.Event.current.mousePosition=new UnityEngine.Vector2(50,110);
   Verse.Widgets.Toggle(toggleRectA, ref stateA, true);
   UnityEngine.Time.frameCount=511; UnityEngine.Input.mouseDown=false; UnityEngine.Input.mousePosition=new UnityEngine.Vector3(50,140,0);
   Verse.Widgets.Toggle(toggleRectA, ref stateA, true); // triggers Dragged -> checkboxPainting=true, stateA=true
   UnityEngine.Event.current.mousePosition=new UnityEngine.Vector2(50,140);
   stateB=false; Verse.Widgets.Toggle(toggleRectB, ref stateB, true);
   Assert(stateA && stateB,"paintable drag across checkboxes broken");
   UnityEngine.Input.mouseHeld=false; Verse.Widgets.checkboxPainting=false;

   // HUD hit-testing accuracy (1280x807 tablet UI)
   Verse.Find.UIRoot = new RimWorld.UIRoot_Play();
   Verse.Find.CurrentMap = new Verse.Map();
   Verse.Find.ColonistBar = new RimWorld.ColonistBar { occupiedRect = new UnityEngine.Rect(400, 10, 48, 48) };
   Verse.GizmoGridDrawer.HeightDrawnRecently = 85f;
   Verse.Find.WindowStack.windows.Clear();
   var inspectWin = new Verse.Window { windowRect = new UnityEngine.Rect(0, 606, 432, 165) };
   Verse.Find.WindowStack.windows.Add(inspectWin);
   // Bare map points that used to be falsely swallowed by 190/90/170/700x380 magic margins:
   Assert(!IsLikelyUiPosition(new UnityEngine.Vector2(200, 500)),"bare map above inspect pane falsely marked UI");
   Assert(!IsLikelyUiPosition(new UnityEngine.Vector2(600, 620)),"bare map above gizmos falsely marked UI");
   Assert(!IsLikelyUiPosition(new UnityEngine.Vector2(200, 40)),"bare map at top-left falsely marked UI");
   Assert(!IsLikelyUiPosition(new UnityEngine.Vector2(1200, 300)),"bare map at right edge with no alerts falsely marked UI");
   // Real UI regions still accurately detected:
   Assert(IsLikelyUiPosition(new UnityEngine.Vector2(200, 700)),"inspect window missed");
   Assert(IsLikelyUiPosition(new UnityEngine.Vector2(600, 790)),"bottom main buttons bar missed");
   Assert(IsLikelyUiPosition(new UnityEngine.Vector2(500, 720)),"active gizmo bar missed");
   Assert(IsLikelyUiPosition(new UnityEngine.Vector2(420, 30)),"colonist bar entry missed");
   // Two-finger window check only blocks on real windows, not HUD margins:
   Assert(IsBlockingWindowAt(new UnityEngine.Vector2(200, 700)),"blocking window missed");
   Assert(!IsBlockingWindowAt(new UnityEngine.Vector2(420, 30)) && !IsBlockingWindowAt(new UnityEngine.Vector2(600, 790)),"HUD blocked two-finger zoom");
  }
 }
}
'@
$vel = [IO.File]::ReadAllText((Join-Path $root 'Source/Input/VelocityHistory.cs'))
$scroll = [IO.File]::ReadAllText((Join-Path $root 'Source/TouchInputDriver.UiScroll.cs'))
$tap = [IO.File]::ReadAllText((Join-Path $root 'Source/TouchTapRepair.cs'))
$hit = [IO.File]::ReadAllText((Join-Path $root 'Source/UiHitTester.cs'))
$boot = [IO.File]::ReadAllText((Join-Path $root 'Source/RimTouchBootstrap.cs'))
$bFrom = $boot.IndexOf('    [HarmonyPatch(typeof(Widgets), "ButtonInvisibleDraggable"')
$bTo = $boot.IndexOf('    [HarmonyPatch(typeof(Widgets), "HorizontalSlider"', $bFrom)
$draggablePatch = 'namespace RimTouch {' + $boot.Substring($bFrom, $bTo - $bFrom) + '}'
$body = ($vel + "`n" + $scroll + "`n" + $tap + "`n" + $hit + "`n" + $draggablePatch) -replace '(?m)^using [^;]+;\r?\n',''
Add-Type -WarningAction SilentlyContinue -CompilerOptions '/nowarn:0169,0649,0414' -TypeDefinition ("using System; using System.Collections.Generic; using System.Reflection; using HarmonyLib; using RimWorld; using RimWorld.Planet; using UnityEngine; using Verse; using RimTouch.TouchPlatform;`n" + $stubs + $body)
[RimTouch.TouchInputDriver]::RunTests()
'PASS UI touch: scaled coordinates, scroll claim/rejection, nested ScrollView priority & boundary handoff, slider jitter, ButtonInvisibleDraggable 4-pass dedup & drag-painting, accurate HUD & two-finger window hit-testing'
