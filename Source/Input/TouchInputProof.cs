// Bounded, explicitly opted-in private diagnostics. GPL-3.0-or-later.
using System;
using System.IO;
using System.Text.Json;
using System.Text;
using UnityEngine;
using Verse;

namespace RimTouch.TouchPlatform
{
    public static class TouchInputProof
    {
        private static readonly bool FileEnabled = Array.Exists(Environment.GetCommandLineArgs(), a => a == "-rimtouch-input-proof");
        private static readonly bool LaunchDebugLogEnabled = Array.Exists(Environment.GetCommandLineArgs(), a => a == "-rimtouch-debug-log");
        private static bool runtimeDebugLogEnabled;
        private static bool DebugLogEnabled { get { return LaunchDebugLogEnabled || runtimeDebugLogEnabled; } }
        private static bool Enabled { get { return FileEnabled || DebugLogEnabled; } }
        private static int records, lastFrame = -1000, lastCount = -1;
        private static string lastStatus;
        private static long playCalls, entryCalls, lastHeartbeat;
        private static float lastContactRecordTime;
        private static readonly StringBuilder CaptureText = new StringBuilder();
        public static string CopyText { get { return CaptureText.ToString(); } }
        public static string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
        public static bool IsRecording { get { return Enabled && records < 1024; } }
        public static bool RuntimeDebugLogEnabled { get { return runtimeDebugLogEnabled && IsRecording; } }

        public static void SetRuntimeDebugLog(bool enabled)
        {
            if (!enabled)
            {
                if (runtimeDebugLogEnabled)
                    RecordDiagnostic(new { kind = "captureStop", session = SessionId, utc = DateTime.UtcNow });
                runtimeDebugLogEnabled = false;
                return;
            }
            if (RuntimeDebugLogEnabled) return;
            records = 0;
            CaptureText.Clear();
            lastFrame = -1000;
            lastCount = -1;
            lastStatus = null;
            lastContactRecordTime = -1f;
            lastHeartbeat = 0;
            playCalls = entryCalls = 0;
            SessionId = Guid.NewGuid().ToString("N");
            runtimeDebugLogEnabled = true;
            RecordDiagnostic(new { kind = "captureStart", schema = 2, session = SessionId, utc = DateTime.UtcNow,
                assembly = typeof(TouchInputProof).Assembly.ManifestModule.ModuleVersionId.ToString(),
                screen = new[] { Screen.width, Screen.height },
                status = TouchSource.Status });
        }

        public static void RecordDiagnostic(object entry)
        {
            if (!IsRecording) return;
            try
            {
                Append(JsonSerializer.Serialize(entry));
            }
            catch (Exception ex)
            {
                StopDiagnostics(ex);
            }
        }

        public static void StopDiagnostics(Exception ex)
        {
            records = 1024;
            Log.Warning("[RimTouch] Optional input proof stopped: " + ex.GetType().Name);
        }
        public static void RecordTwist(float degrees)
        {
            if (!Enabled || records >= 1024) return;
            RecordDiagnostic(new { kind = "worldTwist", session = SessionId, utc = DateTime.UtcNow, frame = Time.frameCount, clockwiseDegrees = degrees });
        }
        public static void MarkUpdate(bool play)
        {
            if (!Enabled) return;
            if (play) playCalls++; else entryCalls++;
        }
        public static void OnGuiHeartbeat()
        {
            if (!Enabled || records >= 1024 || Environment.TickCount64 - lastHeartbeat < 2000) return;
            lastHeartbeat = Environment.TickCount64;
            RecordDiagnostic(new { kind = "heartbeat", session = SessionId, utc = DateTime.UtcNow, frame = Time.frameCount, pumpCalls = TouchSource.PumpCalls, playCalls, entryCalls, touchEnabled = RimTouchMod.Settings != null && RimTouchMod.Settings.enableTouchMode, status = TouchSource.Status });
        }
        public static void Record(ContactFrame frame, ContactTouch[] contacts, string status)
        {
            if (!Enabled || records >= 1024) return;
            bool changed = frame.Canceled || lastCount != contacts.Length || lastStatus != status;
            bool edge = changed;
            foreach (ContactTouch c in contacts)
            {
                if (c.phase != ContactPhase.Stationary) changed = true;
                if (c.phase == ContactPhase.Began || c.phase == ContactPhase.Ended) edge = true;
            }
            // Keep press/release/cancel edges; sample continuous motion so UI proof
            // does not exhaust its shared record budget during one long swipe.
            if (!edge && changed && Time.realtimeSinceStartup - lastContactRecordTime < 0.1f) return;
            if (!changed && (contacts.Length == 0 || Time.frameCount - lastFrame < 30)) return;
            var rows = new object[contacts.Length];
            for (int i = 0; i < contacts.Length; i++)
            {
                ContactTouch c = contacts[i];
                rows[i] = new { id = c.fingerId, phase = c.phase.ToString(), x = c.position.x, y = c.position.y, dx = c.deltaPosition.x, dy = c.deltaPosition.y };
            }
            RecordDiagnostic(new { kind = "contactFrame", session = SessionId, utc = DateTime.UtcNow, frame = Time.frameCount, status, width = Screen.width, height = Screen.height, canceled = frame.Canceled, reason = frame.CancelReason, contacts = rows });
            lastFrame = Time.frameCount; lastCount = contacts.Length; lastStatus = status;
            lastContactRecordTime = Time.realtimeSinceStartup;
        }
        private static void Append(string json)
        {
            try
            {
                if (DebugLogEnabled)
                {
                    Log.Message("[RimTouch.Input] " + json);
                }
                if (FileEnabled)
                {
                    File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "RimTouch-Input-Proof.jsonl"),
                        json + Environment.NewLine);
                }
                records++;
                // A bounded copy buffer lets players paste just JSON, without the
                // renderer's stack trace attached to each normal Log.Message.
                if (CaptureText.Length + json.Length + Environment.NewLine.Length <= 1000000) CaptureText.AppendLine(json);
                if (records == 1024) Log.Message("[RimTouch] Optional input proof reached its 1024-record limit; enable input diagnostics in RimTouch settings for another capture.");
            }
            catch (Exception ex)
            {
                // Diagnostic I/O must never disable input or flood a low-space disk.
                StopDiagnostics(ex);
            }
        }
    }
}
