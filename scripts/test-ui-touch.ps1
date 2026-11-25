param(
    [switch]$VerifyRegression,
    [ValidateSet('inspect-label', 'tap-dispatch', 'ui-claim', 'control-claim')][string]$RegressionCase
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($VerifyRegression -and !$RegressionCase) {
    foreach ($caseName in @('inspect-label', 'tap-dispatch', 'ui-claim', 'control-claim')) {
        & pwsh -NoProfile -File $PSCommandPath -VerifyRegression -RegressionCase $caseName
        if ($LASTEXITCODE -ne 0) { throw "Negative control failed: $caseName" }
    }
    exit
}
if ($RegressionCase -and !$VerifyRegression) { throw 'RegressionCase requires VerifyRegression.' }
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
  public static float Round(float a)=>(float)System.Math.Round(a);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
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
 public static class GUI {
  public static bool enabled=true;
  private static int _guiSliderDragId;
  public static int DebugSliderDragId { get => _guiSliderDragId; set => _guiSliderDragId = value; }
  public static float HorizontalSlider(Rect position, float value, float leftValue, float rightValue) {
   int id = ((int)position.x << 16) ^ (int)position.y ^ 0x101;
   var e = Event.current;
   if (e != null && e.type == EventType.MouseDown && e.button == 0 && position.Contains(e.mousePosition)) { _guiSliderDragId = id; e.Use(); }
   if (_guiSliderDragId == id) {
    if (e != null && Input.GetMouseButton(0) && e.type != EventType.MouseUp) {
     float t = position.width > 0 ? Mathf.Clamp01((e.mousePosition.x - position.x) / position.width) : 0f;
     value = Mathf.Lerp(Mathf.Min(leftValue, rightValue), Mathf.Max(leftValue, rightValue), t);
     if (e.type == EventType.MouseDrag) e.Use();
    } else _guiSliderDragId = 0;
   }
   return value;
  }
  public static float VerticalSlider(Rect position, float value, float topValue, float bottomValue) {
   int id = ((int)position.x << 16) ^ (int)position.y ^ 0x202;
   var e = Event.current;
   if (e != null && e.type == EventType.MouseDown && e.button == 0 && position.Contains(e.mousePosition)) { _guiSliderDragId = id; e.Use(); }
   if (_guiSliderDragId == id) {
    if (e != null && Input.GetMouseButton(0) && e.type != EventType.MouseUp) {
     float t = position.height > 0 ? Mathf.Clamp01((e.mousePosition.y - position.y) / position.height) : 0f;
     value = Mathf.Lerp(topValue, bottomValue, t);
     if (e.type == EventType.MouseDrag) e.Use();
    } else _guiSliderDragId = 0;
   }
   return value;
  }
 }
 public static class GUIUtility {
  public static float scale=1.5f; public static Vector2 offset, groupOffset;
  public static int keyboardControl;
  public static Vector2 ScreenToGUIPoint(Vector2 p)=>(p-offset)/scale;
  public static Vector2 GUIToScreenPoint(Vector2 p)=>offset + groupOffset + p*scale;
  public static int GetControlID(FocusType f, Rect r)=>((int)(r.x*10+groupOffset.x*10)<<16)^((int)(r.y*10+groupOffset.y*10));
 }
  public enum EventType {MouseDown,MouseUp,MouseDrag,Layout,Repaint,Used,KeyDown}
 public class Event {public static Event current;public EventType type;public int button,clickCount;public Vector2 mousePosition;public bool isMouse=>type==EventType.MouseDown||type==EventType.MouseUp||type==EventType.MouseDrag;public void Use(){type=EventType.Used;}}
}
namespace Verse {
 public enum ProgramState { Entry, MapInitializing, Playing }
 public static class Current { public static ProgramState ProgramState = ProgramState.Playing; }
 public class UIRoot {}
 public class UIRoot_Entry : UIRoot {}
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
  public class InspectTabBase {public bool IsVisible=true, Hidden;}
  public class Window {public UnityEngine.Rect windowRect;public bool absorbInputAroundWindow;}
 public class WindowStack {
  public Window currentlyDrawnWindow;
   public readonly System.Collections.Generic.List<Window> windows = new System.Collections.Generic.List<Window>();
   public System.Collections.Generic.IList<Window> Windows=>windows;
   public bool GetsInput(Window window) {
    for(int i=windows.Count-1;i>=0;i--) { if(windows[i]==window)return true;if(windows[i].absorbInputAroundWindow)return false; }
    return true;
   }
  public Window GetWindowAt(UnityEngine.Vector2 p) {
   for(int i=windows.Count-1;i>=0;i--) if(windows[i].windowRect.Contains(p)) return windows[i];
   return null;
  }
 }
 public static class Mouse { public static bool IsOver(UnityEngine.Rect r)=>UnityEngine.Event.current!=null&&r.Contains(UnityEngine.Event.current.mousePosition); }
 public struct FloatRange { public float min, max; public FloatRange(float a, float b) { min = a; max = b; } }
 public struct IntRange { public int min, max; public IntRange(int a, int b) { min = a; max = b; } }
 public static class Widgets {
  public enum DraggableResult { Idle, Pressed, Dragged, DraggedThenPressed }
  private enum RangeEnd : byte { None, Min, Max }
  static int activeControl; static UnityEngine.Vector3 mouseStart; static bool dragged;
  private static int sliderDraggingID;
  private static int draggingId;
  private static RangeEnd curDragEnd;
  public static int DebugSliderDraggingID { get => sliderDraggingID; set => sliderDraggingID = value; }
  public static int DebugRangeDraggingId { get => draggingId; set => draggingId = value; }
  public static bool checkboxPainting, checkboxPaintingState;
  public static void ResetSliderDraggingIDs() { sliderDraggingID = 0; draggingId = 0; curDragEnd = RangeEnd.None; }
  public static float HorizontalSlider(UnityEngine.Rect rect, float value, float min, float max, bool middleAlignment = false, string label = null, string leftAlignedLabel = null, string rightAlignedLabel = null, float roundTo = -1f) {
   int id = ((int)rect.x << 16) ^ (int)rect.y ^ 0x303;
   var e = UnityEngine.Event.current;
   if (e.type == UnityEngine.EventType.MouseDown && Mouse.IsOver(rect) && sliderDraggingID != id) { sliderDraggingID = id; e.Use(); }
   if (sliderDraggingID == id && e.type == UnityEngine.EventType.MouseDrag && e.button == 0) {
    float t = UnityEngine.Mathf.Clamp01((e.mousePosition.x - rect.x) / rect.width);
    value = UnityEngine.Mathf.Clamp(min + t * (max - min), min, max);
    e.Use();
   }
   return value;
  }
  public static void FloatRange(UnityEngine.Rect rect, int id, ref FloatRange range, float min, float max) {
   var e = UnityEngine.Event.current;
   bool started = false;
   if (Mouse.IsOver(rect) || draggingId == id) {
    if (e.type == UnityEngine.EventType.MouseDown && e.button == 0 && draggingId != id) { draggingId = id; curDragEnd = RangeEnd.Max; started = true; e.Use(); }
    if (started || (curDragEnd != RangeEnd.None && e.type == UnityEngine.EventType.MouseDrag && e.button == 0)) {
     float v = UnityEngine.Mathf.Clamp(min + ((e.mousePosition.x - rect.x) / rect.width) * (max - min), min, max);
     range.max = v;
     if (e.type == UnityEngine.EventType.MouseDrag) e.Use();
    }
   }
  }
  public static void IntRange(UnityEngine.Rect rect, int id, ref IntRange range, int min, int max) {
   var e = UnityEngine.Event.current;
   bool started = false;
   if (Mouse.IsOver(rect) || draggingId == id) {
    if (e.type == UnityEngine.EventType.MouseDown && e.button == 0 && draggingId != id) { draggingId = id; curDragEnd = RangeEnd.Max; started = true; e.Use(); }
    if (started || (curDragEnd != RangeEnd.None && e.type == UnityEngine.EventType.MouseDrag && e.button == 0)) {
     int v = (int)UnityEngine.Mathf.Round(UnityEngine.Mathf.Clamp(min + ((e.mousePosition.x - rect.x) / rect.width) * (max - min), min, max));
     range.max = v;
     if (e.type == UnityEngine.EventType.MouseDrag) e.Use();
    }
   }
  }
  public static float PatchedHorizontalSlider(UnityEngine.Rect rect, float value, float min, float max) {
   RimTouch.SliderTouchState st;
   RimTouch.WidgetsHorizontalSliderTouchPatch.Prefix(rect, false, null, out st);
   value = HorizontalSlider(rect, value, min, max);
   RimTouch.WidgetsHorizontalSliderTouchPatch.Postfix(st);
   return value;
  }
  public static void PatchedFloatRange(UnityEngine.Rect rect, int id, ref FloatRange range, float min, float max) {
   RimTouch.FloatRangeTouchState st;
   RimTouch.WidgetsFloatRangeTouchPatch.Prefix(rect, id, ref range, out st);
   FloatRange(rect, id, ref range, min, max);
   RimTouch.WidgetsFloatRangeTouchPatch.Postfix(ref range, st);
  }
  public static void PatchedIntRange(UnityEngine.Rect rect, int id, ref IntRange range, int min, int max) {
   RimTouch.IntRangeTouchState st;
   RimTouch.WidgetsIntRangeTouchPatch.Prefix(rect, id, ref range, out st);
   IntRange(rect, id, ref range, min, max);
   RimTouch.WidgetsIntRangeTouchPatch.Postfix(ref range, st);
  }
  public static float PatchedGuiHorizontalSlider(UnityEngine.Rect rect, float value, float left, float right) {
   RimTouch.GuiSliderTouchState st;
   RimTouch.GuiHorizontalSliderTouchPatch.Prefix(rect, value, out st);
   float res = UnityEngine.GUI.HorizontalSlider(rect, value, left, right);
   RimTouch.GuiHorizontalSliderTouchPatch.Postfix(ref res, st);
   return res;
  }
  public static float PatchedGuiVerticalSlider(UnityEngine.Rect rect, float value, float top, float bottom) {
   RimTouch.GuiSliderTouchState st;
   RimTouch.GuiVerticalSliderTouchPatch.Prefix(rect, value, out st);
   float res = UnityEngine.GUI.VerticalSlider(rect, value, top, bottom);
   RimTouch.GuiVerticalSliderTouchPatch.Postfix(ref res, st);
   return res;
  }
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
  public static object World=new object();
  public static RimWorld.ColonistBar ColonistBar;
  public static LetterStack LetterStack=new LetterStack();
  public static DesignatorManager DesignatorManager=new DesignatorManager();
   public static Targeter Targeter=new Targeter();
   public static RimWorld.WorldTargeter WorldTargeter=new RimWorld.WorldTargeter();
 }
}
namespace RimWorld {
  public interface IInspectPane {
   bool AnythingSelected {get;} float PaneTopY {get;}
   System.Collections.Generic.IEnumerable<Verse.InspectTabBase> CurTabs {get;}
  }
  public class InspectPane : Verse.Window, IInspectPane {
   public bool AnythingSelected {get;set;}=true;public float PaneTopY=>windowRect.y;
   public System.Collections.Generic.List<Verse.InspectTabBase> Tabs=new System.Collections.Generic.List<Verse.InspectTabBase>();
   public System.Collections.Generic.IEnumerable<Verse.InspectTabBase> CurTabs=>Tabs;
  }
  public static class InspectPaneUtility {
   public static float PaneWidthFor(IInspectPane pane) {
    int count=0;foreach(var tab in pane.CurTabs)if(tab.IsVisible&&!tab.Hidden)count++;
    return 72*System.Math.Max(6,count);
   }
  }
  public class WorldTargeter {public bool IsTargeting;}
 public class AlertsReadout { public float AlertsHeight; }
 public class UIRoot_Play : Verse.UIRoot { public AlertsReadout alerts = new AlertsReadout(); }
 public class ColonistBar {
  public struct Entry {}
  public UnityEngine.Rect occupiedRect;
  public bool TryGetEntryAt(UnityEngine.Vector2 pos, out Entry entry) { entry = default(Entry); return occupiedRect.width > 0 && occupiedRect.Contains(pos); }
 }
}
namespace RimWorld.Planet {}
namespace HarmonyLib {
 public class HarmonyPatch : System.Attribute { public HarmonyPatch(System.Type t, string m, System.Type[] p=null){} }
 public static class AccessTools {
  public static System.Reflection.FieldInfo Field(System.Type t, string n) => t.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
  public static System.Reflection.MethodInfo Method(System.Type t, string n, System.Type[] p = null) => p == null ? t.GetMethod(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance) : t.GetMethod(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance, null, p, null);
 }
}
namespace RimTouch.TouchPlatform {
 public enum ContactPhase {Began,Moved,Stationary,Ended}
 public struct ContactTouch {public long fingerId;public ContactPhase phase;public UnityEngine.Vector2 position,deltaPosition;}
 public static class TouchSource {public static bool Canceled;public static int Count;public static ContactTouch touch;public static ContactTouch GetTouch(int i)=>touch;}
 public static class TouchInputProof {public static bool IsRecording=>false;}
}
namespace RimTouch {
 internal static class ReflectionGuard {
  public static System.Reflection.FieldInfo Field(System.Type t,string n,bool l)=>HarmonyLib.AccessTools.Field(t,n);
  public static System.Reflection.MethodInfo Method(System.Type t,string n,bool l)=>HarmonyLib.AccessTools.Method(t,n);
  public static bool TryGetField<T>(System.Reflection.FieldInfo f,object o,out T v){v=default(T);if(f==null)return false;object r=f.GetValue(o);if(r is T){v=(T)r;return true;}return false;}
  public static bool TrySetField(System.Reflection.FieldInfo f,object o,object v){if(f==null)return false;f.SetValue(o,v);return true;}
  public static bool TryInvoke(System.Reflection.MethodInfo m,object o,object[] a){if(m==null)return false;m.Invoke(o,a);return true;}
 }
  public static partial class TouchInputDriver {
   internal static bool TouchModeEnabled=true;static bool ignoreTouchesUntilAllReleased,uiScrollGestureActive,uiScrollInertiaActive,suppressReleaseTap;
   static TouchOwner gestureOwner;static bool startedOverUi=>gestureOwner==TouchOwner.Ui;
   static bool startedOverBlockingUi;
   static bool twoFingerStartedWithVanillaMapTool, worldActive;
   static int lastTouchFrame=-1000,lastMultiTouchFrame=-1000;
   static bool suppressVanillaMapUntilMouseMoves;
  static int suppressUiClickUntilFrame=-1000,suppressWindowCloseUntilFrame=-1000,suppressVanillaMapInputUntilFrame=-1000;
  static long primaryFingerId=1;static UnityEngine.Vector2 startGui,uiScrollLastGui,uiScrollVelocity;
  static UnityEngine.Rect uiScrollActiveRect,uiScrollInertiaRect;
  static float uiScrollInertiaStartTime,lastUiScrollInertiaTime,lastUiScrollSampleTime;
  static int lastUiScrollApplyFrame=-1000,lastUiScrollInertiaApplyFrame=-1000;
  const float MovementDetectionSlopPixels=6,DragSlopPixels=18,MinUiScrollInertiaSpeed=5,MinUiScrollInertiaStartSpeed=50,MaxUiScrollInertiaSpeed=2000,PanInertiaDamping=5;
   static TouchMode mode;
   static UnityEngine.Vector2 lastGui,lastWorldTapGui;
   static UnityEngine.Vector3 startMap,panAnchorMap;
   static Verse.Map gestureMap;
   static bool gestureIsWorld,rightClickDone,selectPrimed,worldDoubleTapPrimed;
   static bool hadTouchLastFrame;
   static float startTime,lastWorldPanSampleTime,lastWorldTapTime,lastSignificantMovementElapsed,panGestureDistance,lastPanSampleTime;
   const float SelectHoldSeconds=.32f,RightClickHoldSeconds=.5f,RightClickHoldSlopPixels=22;
   static int localClicks,worldClicks,rightClicks,uiTaps,worldUpdates,panUpdates;
   static bool WorldPointDragEnabled()=>false;
   static bool IsEnded(TouchPlatform.ContactTouch t)=>t.phase==TouchPlatform.ContactPhase.Ended;
   static void RecordTouchLifecycle(string action){}
   static void ClearInertiaSamples(){}
   static void UpdateOneFingerWorld(TouchPlatform.ContactTouch t,UnityEngine.Vector2 p) { worldUpdates++;if(IsEnded(t))CompleteActiveGesture(p);else lastGui=p; }
   static void DoMapRightClick(UnityEngine.Vector2 p){rightClicks++;}
   static UnityEngine.Vector3 KeepMapPointUnderGui(UnityEngine.Vector3 p,UnityEngine.Vector2 g){panUpdates++;return new UnityEngine.Vector3();}
   static void SamplePanVelocityFromMapDelta(UnityEngine.Vector3 d){}
   static void BeginRimWorldDragBox(){}
   static void FinishRimWorldDragBox(){}
   static void FinishWorldDragBox(){}
   static void FinalizeInertiaFromGesture(){}
   static bool TryDoDraftedPawnTapCommand(UnityEngine.Vector2 p)=>false;
   static bool TrySelectFromTwoFingerDraftedTap()=>false;
   static void StopInertia(){}
   static void DoMapLeftClick(UnityEngine.Vector2 p){localClicks++;}
   static void DoWorldLeftClick(UnityEngine.Vector2 p){worldClicks++;}
   static void QueueUiTap(UnityEngine.Vector2 p){uiTaps++;}
   static void ResetState(){mode=TouchMode.None;uiScrollGestureActive=false;}
   static void RecordMapTapDecision(string action){}
  static bool IsWorldMapActive()=>worldActive;
  static bool IsTwoFingerTapGesture()=>false;
  static void CancelRimWorldDragBox(){}static void CancelWorldDragBox(){}
  public static bool ShouldSuppressUiClickInRect(UnityEngine.Rect r)=>uiScrollGestureActive;
  static bool IsFinite(UnityEngine.Vector2 p)=>true;
  struct ScrollProofEvent {}static ScrollProofEvent CaptureScrollProofEvent()=>new ScrollProofEvent();
  static void RecordScrollDecision(UnityEngine.Rect a,UnityEngine.Rect b,UnityEngine.Vector2 c,UnityEngine.Vector2 d,bool e,bool f,UnityEngine.Vector2 g,ScrollProofEvent i,bool j,ScrollDecision k){}
  static void RecordSliderArbitration(string a,UiSliderKind b,int c,UiControlDragAxis d,UnityEngine.Vector2 e,ScrollProofEvent f){}
  static void Assert(bool p,string m){if(!p)throw new System.Exception(m);}
   static void Contact(float x,float y) {TouchPlatform.TouchSource.Count=1;TouchPlatform.TouchSource.touch=new TouchPlatform.ContactTouch {fingerId=1,phase=TouchPlatform.ContactPhase.Moved,position=new UnityEngine.Vector2(x*1.5f,1210-y*1210/807)};UnityEngine.Time.frameCount++;}
   static void Finger(float x,float y,TouchPlatform.ContactPhase phase) {
    Contact(x,y);var touch=TouchPlatform.TouchSource.touch;touch.phase=phase;TouchPlatform.TouchSource.touch=touch;
    lastTouchFrame=UnityEngine.Time.frameCount;UpdateOneFinger(touch);
   }
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

   // Slider-scroll arbitration, locking, diagonal resolution, and full slider coverage
   Action<float,float> SyncMouseGui = (gx, gy) => {
    UnityEngine.Input.mousePosition = new UnityEngine.Vector3(gx * 1.5f, 1210f - gy * 1210f / 807f, 0f);
    UnityEngine.Event.current.mousePosition = new UnityEngine.Vector2(gx - 208f, gy - 71.33333f);
   };
   Action ResetGesture = () => {
    uiScrollGestureActive = false; uiScrollWindow = null; uiScrollCandidateWindow = null;
    ResetUiSliderArbitration();
    Verse.Widgets.ResetSliderDraggingIDs();
    UnityEngine.GUI.DebugSliderDragId = 0;
   };

   // 1) Legacy FilterHorizontalSliderDrag compatibility + commit locking
   ResetGesture();
   uiScrollCandidateWindow = w; uiTouchStartWindow = w; Verse.Find.WindowStack.currentlyDrawnWindow = w;
   startGui = new UnityEngine.Vector2(400, 300);
   Contact(402, 303); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag;
   FilterHorizontalSliderDrag(); Assert(UnityEngine.Event.current.type == UnityEngine.EventType.Used, "jitter reached slider");
   Contact(430, 303); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag;
   FilterHorizontalSliderDrag(); Assert(UnityEngine.Event.current.type == UnityEngine.EventType.MouseDrag, "horizontal slider drag blocked");
   Assert(uiSliderState == UiSliderArbitrationState.Committed, "slider did not commit after horizontal drag");

   // 2) Exact reproduction of real device log (F2996-F3554): once committed, vertical drift (25..77px)
   //    and crossing back over startGui never drop MouseDrag or let scroll steal the gesture.
   ResetGesture();
   var sliderA = new UnityEngine.Rect(20, 200, 800, 22);
   var sliderB = new UnityEngine.Rect(20, 256, 800, 22);
   scroll = new UnityEngine.Vector2(0, 120); // non-zero scroll so upward & downward headroom both exist
   float valA = 0.5f, valB = 0.5f;
   startGui = new UnityEngine.Vector2(865.3f, 338.1f);
   Contact(865.3f, 338.1f); SyncMouseGui(865.3f, 338.1f);
   UnityEngine.Event.current.type = UnityEngine.EventType.MouseDown;
   Assert(!ProcessScrollViewTouch(rect, view, ref scroll), "MouseDown claimed scroll");
   valA = Verse.Widgets.PatchedHorizontalSlider(sliderA, valA, 0f, 1f);
   valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
   Assert(uiSliderState == UiSliderArbitrationState.Pending && Verse.Widgets.DebugSliderDraggingID != 0, "HorizontalSlider did not enter Pending on MouseDown");
   // Sub-slop jitter freezes slider B
   Contact(868.3f, 340.1f); SyncMouseGui(868.3f, 340.1f); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag;
   float beforeValB = valB;
   valA = Verse.Widgets.PatchedHorizontalSlider(sliderA, valA, 0f, 1f);
   valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
   Assert(System.Math.Abs(valB - beforeValB) < 0.0001f, "sub-slop jitter mutated HorizontalSlider");
   // Horizontal drag commits slider B (even though slider A draws earlier)
   Contact(808f, 338.1f); SyncMouseGui(808f, 338.1f); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag;
   Assert(!ProcessScrollViewTouch(rect, view, ref scroll), "horizontal drag stolen by scroll");
   valA = Verse.Widgets.PatchedHorizontalSlider(sliderA, valA, 0f, 1f);
   valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
   Assert(uiSliderState == UiSliderArbitrationState.Committed && valB != beforeValB, "HorizontalSlider failed to commit");
   // Replay drifted / crossing-origin offsets from real device log: (12, 67.4), (48.7, 76.7), (2.7, 70.0), (4.7, 9.3)
   foreach (var off in new[] {
    new UnityEngine.Vector2(12f, 67.4f),
    new UnityEngine.Vector2(48.7f, 76.7f),
    new UnityEngine.Vector2(2.7f, 70f),
    new UnityEngine.Vector2(4.7f, 9.3f)
   }) {
    float tx = startGui.x + off.x, ty = startGui.y + off.y;
    Contact(tx, ty); SyncMouseGui(tx, ty);
    UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag;
    float prevScrollY = scroll.y;
    ScrollDecision dec;
    bool scHandled = ProcessScrollViewTouchCore(rect, view, ref scroll, out dec);
    Assert(!scHandled && dec == ScrollDecision.ControlLocked && System.Math.Abs(scroll.y - prevScrollY) < 0.001f,
     "committed slider was stolen by scroll at offset " + off.x + "," + off.y);
    Assert(!ShouldAllowWindowDrag(), "committed slider allowed window drag");
    float expected = UnityEngine.Mathf.Clamp01(((tx - 208f) - sliderB.x) / sliderB.width);
    valA = Verse.Widgets.PatchedHorizontalSlider(sliderA, valA, 0f, 1f);
    valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
    Assert(System.Math.Abs(valB - expected) < 0.002f, "committed HorizontalSlider dropped drag at offset " + off.x + "," + off.y);
    // Negative control: the old stateless formula WOULD have rejected every one of these points
    bool oldWouldBlock = System.Math.Abs(off.x) <= MovementDetectionSlopPixels || System.Math.Abs(off.x) <= System.Math.Abs(off.y) * 1.5f;
    Assert(oldWouldBlock, "test point did not exercise legacy dropout zone");
   }

   // 3) Diagonal > 12px tie-break: |dx| >= |dy| -> slider commits; |dy| > |dx| with headroom -> scroll claims & clears slider
   ResetGesture();
   scroll = new UnityEngine.Vector2(0, 120); valB = 0.5f;
   startGui = new UnityEngine.Vector2(500, 338);
   Contact(500, 338); SyncMouseGui(500, 338); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDown;
   ProcessScrollViewTouch(rect, view, ref scroll);
   valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
   Contact(511, 348); SyncMouseGui(511, 348); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag; // dx=11, dy=10, dist=14.87 > 12
   Assert(!ProcessScrollViewTouch(rect, view, ref scroll), "diagonal dx>=dy stolen by scroll");
   valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
   Assert(uiSliderState == UiSliderArbitrationState.Committed, "diagonal dx>=dy did not commit slider");

   ResetGesture();
   scroll = new UnityEngine.Vector2(0, 120); valB = 0.5f;
   startGui = new UnityEngine.Vector2(500, 338);
   Contact(500, 338); SyncMouseGui(500, 338); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDown;
   ProcessScrollViewTouch(rect, view, ref scroll);
   valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
   Contact(510, 349); SyncMouseGui(510, 349); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag; // dx=10, dy=11, dist=14.87 > 12
   Assert(ProcessScrollViewTouch(rect, view, ref scroll) && uiScrollGestureActive, "diagonal dy>dx did not claim scroll");
   Assert(Verse.Widgets.DebugSliderDraggingID == 0 && uiSliderState == UiSliderArbitrationState.None, "scroll claim did not clear slider drag state");
   valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
   Assert(System.Math.Abs(valB - 0.5f) < 0.0001f, "slider value changed after diagonal scroll claim");

   // Diagonal dy>dx with NO vertical headroom (top of page pulling down) must neither scroll nor falsely lock slider
   ResetGesture();
   scroll = new UnityEngine.Vector2(0, 0); valB = 0.5f;
   startGui = new UnityEngine.Vector2(500, 338);
   Contact(500, 338); SyncMouseGui(500, 338); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDown;
   ProcessScrollViewTouch(rect, view, ref scroll);
   valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
   Contact(510, 349); SyncMouseGui(510, 349); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag;
   Assert(!ProcessScrollViewTouch(rect, view, ref scroll) && !uiScrollGestureActive, "no-headroom scroll claimed");
   valB = Verse.Widgets.PatchedHorizontalSlider(sliderB, valB, 0f, 1f);
   Assert(uiSliderState == UiSliderArbitrationState.Pending && System.Math.Abs(valB - 0.5f) < 0.0001f, "vertical-dominant drag with no headroom falsely committed slider");

   // 4) Multi-FloatRange / IntRange: earlier unrelated range never eats MouseDrag; MouseDown value is guarded until commit
   ResetGesture();
   var rangeRect1 = new UnityEngine.Rect(20, 180, 400, 28);
   var rangeRect2 = new UnityEngine.Rect(20, 260, 400, 28);
   var fr1 = new Verse.FloatRange(0.2f, 0.5f);
   var fr2 = new Verse.FloatRange(0.2f, 0.5f);
   scroll = new UnityEngine.Vector2(0, 120);
   startGui = new UnityEngine.Vector2(500, 345); // local (292, 273.7) inside rangeRect2
   Contact(500, 345); SyncMouseGui(500, 345); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDown;
   ProcessScrollViewTouch(rect, view, ref scroll);
   Verse.Widgets.PatchedFloatRange(rangeRect1, 101, ref fr1, 0f, 1f);
   Verse.Widgets.PatchedFloatRange(rangeRect2, 202, ref fr2, 0f, 1f);
   Assert(uiSliderState == UiSliderArbitrationState.Pending && System.Math.Abs(fr2.max - 0.5f) < 0.0001f, "FloatRange mutated on undecided MouseDown inside ScrollView");
   // Horizontal drag commits range 2 even though range 1 draws first!
   Contact(530, 346); SyncMouseGui(530, 346); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag;
   Assert(!ProcessScrollViewTouch(rect, view, ref scroll), "FloatRange drag stolen by scroll");
   Verse.Widgets.PatchedFloatRange(rangeRect1, 101, ref fr1, 0f, 1f);
   Verse.Widgets.PatchedFloatRange(rangeRect2, 202, ref fr2, 0f, 1f);
   Assert(uiSliderState == UiSliderArbitrationState.Committed && fr2.max > 0.7f && System.Math.Abs(fr1.max - 0.5f) < 0.0001f, "earlier FloatRange blocked second FloatRange commit");
   // Vertical swipe on IntRange never mutates value and clears draggingId
   ResetGesture();
   var ir = new Verse.IntRange(2, 5);
   scroll = new UnityEngine.Vector2(0, 120);
   startGui = new UnityEngine.Vector2(500, 345);
   Contact(500, 345); SyncMouseGui(500, 345); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDown;
   ProcessScrollViewTouch(rect, view, ref scroll);
   Verse.Widgets.PatchedIntRange(rangeRect2, 303, ref ir, 0, 10);
   Assert(ir.max == 5, "IntRange mutated on undecided MouseDown");
   Contact(501, 325); SyncMouseGui(501, 325); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag;
   Assert(ProcessScrollViewTouch(rect, view, ref scroll) && uiScrollGestureActive && Verse.Widgets.DebugRangeDraggingId == 0, "vertical scroll on IntRange failed or leaked draggingId");
   Verse.Widgets.PatchedIntRange(rangeRect2, 303, ref ir, 0, 10);
   Assert(ir.max == 5, "IntRange mutated during vertical scroll");

   // 5) Waylith GUI.HorizontalSlider: guards Layout/Repaint immediate writes while Pending, commits smoothly, clears on scroll
   ResetGesture();
   UnityEngine.Input.mouseHeld = true;
   var guiSliderRect = new UnityEngine.Rect(20, 260, 400, 20);
   float guiVal = 0.25f;
   scroll = new UnityEngine.Vector2(0, 120);
   startGui = new UnityEngine.Vector2(500, 340); // local (292, 268.7)
   Contact(500, 340); SyncMouseGui(500, 340); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDown;
   ProcessScrollViewTouch(rect, view, ref scroll);
   guiVal = Verse.Widgets.PatchedGuiHorizontalSlider(guiSliderRect, guiVal, 0f, 1f);
   Assert(uiSliderState == UiSliderArbitrationState.Pending && System.Math.Abs(guiVal - 0.25f) < 0.0001f && UnityEngine.GUI.DebugSliderDragId != 0, "GUI.HorizontalSlider mutated on undecided MouseDown");
   // Repaint pass while still below slop must NOT mutate guiVal
   Contact(503, 341); SyncMouseGui(503, 341); UnityEngine.Event.current.type = UnityEngine.EventType.Repaint;
   guiVal = Verse.Widgets.PatchedGuiHorizontalSlider(guiSliderRect, guiVal, 0f, 1f);
   Assert(System.Math.Abs(guiVal - 0.25f) < 0.0001f, "GUI.HorizontalSlider mutated on Pending Repaint pass");
   // Horizontal drag commits and updates value even with large vertical drift
   Contact(540, 341); SyncMouseGui(540, 341); UnityEngine.Event.current.type = UnityEngine.EventType.Repaint;
   guiVal = Verse.Widgets.PatchedGuiHorizontalSlider(guiSliderRect, guiVal, 0f, 1f);
   Assert(uiSliderState == UiSliderArbitrationState.Committed && guiVal > 0.7f, "GUI.HorizontalSlider failed to commit");
   Contact(502, 410); SyncMouseGui(502, 410); UnityEngine.Event.current.type = UnityEngine.EventType.Repaint;
   Assert(!ProcessScrollViewTouch(rect, view, ref scroll), "scroll stole committed GUI.HorizontalSlider");
   guiVal = Verse.Widgets.PatchedGuiHorizontalSlider(guiSliderRect, guiVal, 0f, 1f);
   Assert(System.Math.Abs(guiVal - ((502f - 208f - 20f) / 400f)) < 0.01f, "committed GUI.HorizontalSlider dropped update on vertical drift");
   UnityEngine.Input.mouseHeld = false;

   // 6) Bidirectional ScrollView (maxX > 1 && maxY > 1): Pending horizontal slider masks horizontal scroll
   ResetGesture();
   var biView = new UnityEngine.Rect(0, 0, 1400, 1200);
   var biScroll = new UnityEngine.Vector2(100, 120);
   float biVal = 0.5f;
   startGui = new UnityEngine.Vector2(500, 338);
   Contact(500, 338); SyncMouseGui(500, 338); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDown;
   ProcessScrollViewTouch(rect, biView, ref biScroll);
   biVal = Verse.Widgets.PatchedHorizontalSlider(sliderB, biVal, 0f, 1f);
   Contact(530, 338); SyncMouseGui(530, 338); UnityEngine.Event.current.type = UnityEngine.EventType.MouseDrag;
   Assert(!ProcessScrollViewTouch(rect, biView, ref biScroll) && !uiScrollGestureActive, "bidirectional ScrollView stole horizontal slider drag");
   biVal = Verse.Widgets.PatchedHorizontalSlider(sliderB, biVal, 0f, 1f);
   Assert(uiSliderState == UiSliderArbitrationState.Committed && biVal != 0.5f, "slider in bidirectional ScrollView did not commit");
   ResetGesture();

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

    // Shared Inspect labels live above their owner's windowRect, not inside it.
    Verse.Find.WindowStack.windows.Clear();
    Verse.GizmoGridDrawer.HeightDrawnRecently=0;
    var pane=new RimWorld.InspectPane { windowRect=new UnityEngine.Rect(0,607,432,165) };
    pane.Tabs.Add(new Verse.InspectTabBase { IsVisible=false });
    pane.Tabs.Add(new Verse.InspectTabBase { Hidden=true });
    pane.Tabs.Add(new Verse.InspectTabBase());
    Verse.Find.WindowStack.windows.Add(pane);
    foreach(bool world in new[]{false,true}) {
     worldActive=world;
     Assert(IsLikelyUiPosition(new UnityEngine.Vector2(396,592)) && IsBlockingWindowAt(new UnityEngine.Vector2(396,592)),"Inspect label above window missed");
     Assert(!IsLikelyUiPosition(new UnityEngine.Vector2(200,592)),"blank Inspect label row blocked map");
     Assert(!IsBlockingWindowAt(new UnityEngine.Vector2(396,576)),"blank map above label blocked");
    }
    pane.Tabs.Add(new Verse.InspectTabBase());
    Assert(IsBlockingWindowAt(new UnityEngine.Vector2(324,592)),"second visible label missed");
    pane.AnythingSelected=false;
    Assert(!IsBlockingWindowAt(new UnityEngine.Vector2(396,592)),"deselected Inspect pane left ghost labels");
    pane.AnythingSelected=true;
    while(pane.Tabs.Count<9)pane.Tabs.Add(new Verse.InspectTabBase());
    Assert(IsBlockingWindowAt(new UnityEngine.Vector2(468,592)),"wide Inspect label row missed");
    foreach(int height in new[]{600,807,1000}) {
     Verse.UI.screenHeight=height;pane.windowRect=new UnityEngine.Rect(0,height-200,504,165);
     Assert(IsLikelyUiPosition(new UnityEngine.Vector2(468,height-215)),"resized Inspect label missed");
    }
    Verse.UI.screenHeight=807;pane.windowRect=new UnityEngine.Rect(0,607,432,165);
    pane.Tabs.RemoveRange(3,pane.Tabs.Count-3);
    Verse.Find.WindowStack.windows.Clear();
    Assert(!IsBlockingWindowAt(new UnityEngine.Vector2(396,592)),"closed pane left ghost labels");

    // Modal absorption applies outside windowRect, without inventing HUD margins.
    var modal=new Verse.Window { windowRect=new UnityEngine.Rect(0,0,200,200),absorbInputAroundWindow=true };
    Verse.Find.WindowStack.windows.Add(modal);
    Assert(IsLikelyUiPosition(new UnityEngine.Vector2(800,400)) && IsBlockingWindowAt(new UnityEngine.Vector2(800,400)),"modal exterior did not block map");
    mode=TouchMode.None;Finger(800,400,TouchPlatform.ContactPhase.Began);
    Assert(startedOverUi,"modal exterior acquired map ownership");
    Finger(800,400,TouchPlatform.ContactPhase.Ended);
    Verse.Find.WindowStack.windows.Clear();
    Assert(!IsBlockingWindowAt(new UnityEngine.Vector2(800,400)),"closed modal left a ghost blocker");
    Verse.Find.WindowStack.windows.Add(pane);

    // Drive real begin/update/release and real suppression getters.
    UnityEngine.Event.current.type=UnityEngine.EventType.MouseDown;
    foreach(bool world in new[]{false,true}) {
     worldActive=world;mode=TouchMode.None;uiScrollGestureActive=false;
     localClicks=worldClicks=rightClicks=uiTaps=worldUpdates=panUpdates=0;
     Finger(396,592,TouchPlatform.ContactPhase.Began);
     Assert(startedOverUi && startedOverBlockingUi,"label did not acquire UI ownership");
     if(world) {
      Assert(!WorldTargeterProcessInputEventsPatch.Prefix(),"UI-owned mouse event reached world targeter");
      UnityEngine.Event.current.type=UnityEngine.EventType.KeyDown;
      Assert(WorldTargeterProcessInputEventsPatch.Prefix(),"touch ownership blocked world targeting keyboard cancel");
      UnityEngine.Event.current.type=UnityEngine.EventType.MouseDown;
     }
     Finger(396,592,TouchPlatform.ContactPhase.Ended);
     Assert(uiTaps==1 && localClicks==0 && worldClicks==0,"label tap changed map selection");
     TouchPlatform.TouchSource.Count=0;
     Assert(!ShouldSuppressVanillaWorldInput,"UI release acquired world-map suppression tail");
     Finger(396,592,TouchPlatform.ContactPhase.Began);
     UnityEngine.Time.realtimeSinceStartup+=.6f;
     Finger(396,592,TouchPlatform.ContactPhase.Stationary);
     Finger(600,400,TouchPlatform.ContactPhase.Moved);
     Assert(startedOverUi && worldUpdates==0 && panUpdates==0 && rightClicks==0,"label hold/drag operated map");
     Finger(600,400,TouchPlatform.ContactPhase.Ended);
     Assert(localClicks==0 && worldClicks==0,"label drag release selected map");

     Finger(800,400,TouchPlatform.ContactPhase.Began);
     Finger(396,592,TouchPlatform.ContactPhase.Moved);
     Assert(!startedOverUi && (world?ShouldSuppressVanillaWorldInput:ShouldSuppressVanillaMapInput),"map touch stopped suppressing native selection over UI");
     Assert(!world || ShouldSuppressVanillaWorldToolInput,"ordinary map touch reached world targeting");
     Finger(396,592,TouchPlatform.ContactPhase.Ended);
    }
    worldActive=false;Verse.Find.DesignatorManager.SelectedDesignator=new Verse.Designator();
    Finger(800,400,TouchPlatform.ContactPhase.Began);
    Assert(gestureOwner==TouchOwner.LocalMapTool && mode==TouchMode.VanillaMapTool && !ShouldSuppressVanillaMapToolInput,"local tool lost input ownership");
    int clicksBefore=localClicks+worldClicks;
    Finger(800,400,TouchPlatform.ContactPhase.Ended);
    Assert(localClicks+worldClicks==clicksBefore,"local tool tap also selected map");
    Verse.Find.DesignatorManager.SelectedDesignator=null;
    worldActive=true;Verse.Current.ProgramState=Verse.ProgramState.Entry;Verse.Find.UIRoot=new Verse.UIRoot();
    Verse.Find.WorldTargeter.IsTargeting=true;
    Finger(800,400,TouchPlatform.ContactPhase.Began);
    Assert(gestureOwner==TouchOwner.WorldMapTool && !ShouldSuppressVanillaWorldToolInput && ShouldSuppressVanillaWorldInput,"entry-screen world tool ownership failed");
    Finger(800,400,TouchPlatform.ContactPhase.Ended);
    Assert(localClicks+worldClicks==clicksBefore,"world tool tap also selected map");
    Verse.Find.WorldTargeter.IsTargeting=false;
    TouchModeEnabled=false;
    Assert(!ShouldSuppressVanillaMapInput && !ShouldSuppressVanillaWorldInput && !ShouldSuppressVanillaMapToolInput && !ShouldSuppressVanillaWorldToolInput,"disabled touch blocked native input");
    TouchModeEnabled=true;TouchPlatform.TouchSource.Count=0;gestureOwner=TouchOwner.None;
    lastTouchFrame=lastMultiTouchFrame=suppressVanillaMapInputUntilFrame=-1000;suppressVanillaMapUntilMouseMoves=false;
    Assert(!ShouldSuppressVanillaMapInput && !ShouldSuppressVanillaWorldInput && !ShouldSuppressVanillaMapToolInput && !ShouldSuppressVanillaWorldToolInput,"idle mouse input blocked");
  }
 }
}
'@
$vel = [IO.File]::ReadAllText((Join-Path $root 'Source/Input/VelocityHistory.cs'))
$scroll = [IO.File]::ReadAllText((Join-Path $root 'Source/TouchInputDriver.UiScroll.cs'))
$tap = [IO.File]::ReadAllText((Join-Path $root 'Source/TouchTapRepair.cs'))
$hit = [IO.File]::ReadAllText((Join-Path $root 'Source/UiHitTester.cs'))
if ($RegressionCase -eq 'inspect-label') {
    $correct = '|| IsInspectTabAt(stack, gui)'
    if (!$hit.Contains($correct)) { throw 'Inspect label mutation point not found.' }
    $hit = $hit.Replace($correct, '')
    $expectedFailure = 'Inspect label above window missed'
}
$boot = [IO.File]::ReadAllText((Join-Path $root 'Source/RimTouchBootstrap.cs'))
$driver = [IO.File]::ReadAllText((Join-Path $root 'Source/TouchInputDriver.cs'))
function Get-DriverMember([string]$signature) {
    $start = $driver.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $open = $driver.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0 -and $end -lt $driver.Length) {
        if ($driver[$end] -eq '{') { $depth++ }
        if ($driver[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced member: $signature" }
    $driver.Substring($start, $end - $start)
}
$ownership = @(
    (Get-DriverMember 'public static bool ShouldSuppressVanillaMapInput'),
    (Get-DriverMember 'public static bool ShouldSuppressVanillaWorldInput'),
    (Get-DriverMember 'public static bool ShouldSuppressVanillaMapToolInput'),
    (Get-DriverMember 'public static bool ShouldSuppressVanillaWorldToolInput'),
    (Get-DriverMember 'public static bool ShouldSuppressAfterMultiTouch'),
    (Get-DriverMember 'private static void UpdateOneFinger('),
    (Get-DriverMember 'private static void CompleteActiveGesture('),
    (Get-DriverMember 'private static void FinishTouch(')
) -join "`n"
$uiTap = [IO.File]::ReadAllText((Join-Path $root 'Source/TouchInputDriver.UiTap.cs'))
if ($RegressionCase -eq 'tap-dispatch') {
    $ownership = $ownership.Replace('ReleaseMapTap(releaseGui);', 'if (IsWorldMapActive()) DoWorldLeftClick(releaseGui); else DoMapLeftClick(releaseGui);')
    $expectedFailure = 'tap committed before UI'
}
if ($RegressionCase -eq 'ui-claim') {
    $uiTap = $uiTap.Replace('if (state.Input.type == EventType.Used)', 'if (state.Input.type == EventType.MouseDrag)')
    $expectedFailure = 'consumed UI press leaked to map'
}
if ($RegressionCase -eq 'control-claim') {
    $uiTap = $uiTap.Replace('if (rect.Contains(GuiToLocalGuiPoint(gui)))', 'if (rect.width < 0 && rect.Contains(GuiToLocalGuiPoint(gui)))')
    $expectedFailure = 'non-consuming control leaked to map'
}
$ownership = "namespace RimTouch { public static partial class TouchInputDriver { $ownership } }"
$modes = [IO.File]::ReadAllText((Join-Path $root 'Source/TouchMode.cs'))
$bFrom = $boot.IndexOf('    [HarmonyPatch(typeof(Widgets), "ButtonInvisibleDraggable"')
$bTo = $boot.IndexOf('    [HarmonyPatch(typeof(Widgets), "BeginScrollView"', $bFrom)
$draggablePatch = 'namespace RimTouch {' + $boot.Substring($bFrom, $bTo - $bFrom) + '}'
$wFrom = $boot.IndexOf('    [HarmonyPatch(typeof(WorldTargeter)')
$wTo = $boot.IndexOf('    [HarmonyPatch(', $wFrom + 5)
$worldToolPatch = 'namespace RimTouch {' + $boot.Substring($wFrom, $wTo - $wFrom) + '}'
$dispatchFrom = $boot.IndexOf('    [HarmonyPatch(typeof(UIRoot_Entry)')
$dispatchTo = $boot.IndexOf('    [HarmonyPatch(typeof(Selector)', $dispatchFrom)
$dispatchPatches = 'namespace RimTouch {' + $boot.Substring($dispatchFrom, $dispatchTo - $dispatchFrom) + '}'
$tapTests = [IO.File]::ReadAllText((Join-Path $root 'scripts/tests/UiTapDispatchTests.cs'))
$body = ($modes + "`n" + $vel + "`n" + $scroll + "`n" + $tap + "`n" + $hit + "`n" + $ownership + "`n" + $uiTap + "`n" + $tapTests + "`n" + $dispatchPatches + "`n" + $draggablePatch + "`n" + $worldToolPatch) -replace '(?m)^using [^;]+;\r?\n',''
Add-Type -WarningAction SilentlyContinue -CompilerOptions '/nowarn:0169,0649,0414' -TypeDefinition ("using System; using System.Collections.Generic; using System.Reflection; using HarmonyLib; using RimWorld; using RimWorld.Planet; using UnityEngine; using Verse; using RimTouch.TouchPlatform;`n" + $stubs + $body)
try {
    [RimTouch.TouchInputDriver]::RunTests()
    [RimTouch.TouchInputDriver]::RunTapDispatchTests()
    if ($VerifyRegression) { throw 'Regression mutation unexpectedly passed.' }
} catch {
    if (!$VerifyRegression -or $_.Exception.ToString() -notmatch $expectedFailure) { throw }
    "PASS negative control: $RegressionCase was rejected by its behavioral assertion"
    return
}
'PASS UI touch: coordinates, scroll/slider arbitration, draggable dedup, Inspect labels/modal blocking, real gesture ownership and map/tool input'
