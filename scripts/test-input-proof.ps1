param([string]$Case)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Case) {
    $cases = @(
        @{Name='disabled'; Flags=@()},
        @{Name='runtime'; Flags=@()},
        @{Name='debug'; Flags=@('-rimtouch-debug-log')},
        @{Name='file'; Flags=@('-rimtouch-input-proof')},
        @{Name='both'; Flags=@('-rimtouch-debug-log','-rimtouch-input-proof')}
    )
    foreach ($test in $cases) {
        & pwsh -NoProfile -File $PSCommandPath -Case $test.Name @($test.Flags)
        if ($LASTEXITCODE -ne 0) { throw "Logger test failed: $($test.Name)" }
    }
    exit
}

# All output stays in ignored repository build space; no game files are touched.
$dir = Join-Path $root ('.build/proof-tests/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($dir)
$stubs = @'
namespace UnityEngine {
 public static class Time { public static int frameCount; public static float realtimeSinceStartup; }
 public static class Screen { public static int width=1280,height=720; }
 public struct Vector2 { public float x,y; }
}
namespace Verse {
 public static class GenFilePaths { public static string SaveDataFolderPath; }
 public static class Log {
  public static System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
  public static int Warnings;
  public static void Message(string text) { Messages.Add(text); }
  public static void Warning(string text) { Warnings++; }
 }
}
namespace RimTouch {
 public class TestSettings { public bool enableTouchMode=true; }
 public static class RimTouchMod { public static TestSettings Settings=new TestSettings(); }
}
namespace RimTouch.TouchPlatform {
 public enum ContactPhase { Began, Moved, Stationary, Ended }
 public struct ContactTouch { public long fingerId; public ContactPhase phase; public UnityEngine.Vector2 position,deltaPosition; }
 public struct ContactFrame { public bool Canceled; public string CancelReason; }
 public static class TouchSource { public static long PumpCalls; public static string Status="test"; }
}
'@
$proof = [IO.File]::ReadAllText((Join-Path $root 'Source/Input/TouchInputProof.cs'))
$body = $proof -replace '(?m)^using [^;]+;\r?\n',''
Add-Type -TypeDefinition ("using System; using System.IO; using System.Text; using System.Text.Json; using UnityEngine; using Verse;`n" + $stubs + $body)
[Verse.GenFilePaths]::SaveDataFolderPath = $dir
$file = Join-Path $dir 'RimTouch-Input-Proof.jsonl'
function Assert($condition, $message) { if (!$condition) { throw "$Case : $message" } }
function ProofMessages { @([Verse.Log]::Messages | Where-Object { $_.StartsWith('[RimTouch.Input] ') }) }

if ($Case -eq 'runtime') {
    Assert (![RimTouch.TouchPlatform.TouchInputProof]::IsRecording) 'Runtime capture started automatically'
    [RimTouch.TouchPlatform.TouchInputProof]::SetRuntimeDebugLog($true)
    Assert ([RimTouch.TouchPlatform.TouchInputProof]::RuntimeDebugLogEnabled) 'Runtime toggle failed'
    $session=[RimTouch.TouchPlatform.TouchInputProof]::SessionId
    [RimTouch.TouchPlatform.TouchInputProof]::SetRuntimeDebugLog($true)
    Assert ([RimTouch.TouchPlatform.TouchInputProof]::SessionId -eq $session) 'Repeated enable reset active capture'
    [RimTouch.TouchPlatform.TouchInputProof]::RecordDiagnostic([int[]]@(1))
    [RimTouch.TouchPlatform.TouchInputProof]::SetRuntimeDebugLog($false)
    Assert (![RimTouch.TouchPlatform.TouchInputProof]::IsRecording) 'Uncheck did not disable capture'
    $count=[Verse.Log]::Messages.Count
    [RimTouch.TouchPlatform.TouchInputProof]::RecordDiagnostic([int[]]@(2))
    Assert ([Verse.Log]::Messages.Count -eq $count) 'Disabled runtime capture still logged'
    $entries=@(ProofMessages | ForEach-Object { $_.Substring(17) | ConvertFrom-Json })
    Assert ($entries[0].kind -eq 'captureStart' -and $entries[-1].kind -eq 'captureStop') 'Capture boundaries missing'
    Assert ($entries[0].assembly.Length -eq 36) 'Build identity missing'
    $copy=@([RimTouch.TouchPlatform.TouchInputProof]::CopyText -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
    Assert ($copy.Count -eq $entries.Count) 'Compact copy lost records'
    Assert ($copy[0].schema -eq 2) 'Compact schema version missing'
    [RimTouch.TouchPlatform.TouchInputProof]::SetRuntimeDebugLog($true)
    Assert ([RimTouch.TouchPlatform.TouchInputProof]::SessionId -ne $session) 'New capture reused session ID'
    for ($i=0; $i -lt 1030; $i++) { [RimTouch.TouchPlatform.TouchInputProof]::RecordDiagnostic([int[]]@($i)) }
    Assert (![RimTouch.TouchPlatform.TouchInputProof]::RuntimeDebugLogEnabled) 'Budget did not turn checkbox state off'
    Assert (![IO.File]::Exists($file)) 'Runtime capture created a separate file'
    $session=[RimTouch.TouchPlatform.TouchInputProof]::SessionId
    [RimTouch.TouchPlatform.TouchInputProof]::SetRuntimeDebugLog($true)
    Assert ([RimTouch.TouchPlatform.TouchInputProof]::RuntimeDebugLogEnabled) 'Could not restart after budget exhaustion'
    Assert ([RimTouch.TouchPlatform.TouchInputProof]::SessionId -ne $session) 'Restart did not create new session'
    [RimTouch.TouchPlatform.TouchInputProof]::RecordDiagnostic([float[]]@([float]::NaN))
    Assert (![RimTouch.TouchPlatform.TouchInputProof]::RuntimeDebugLogEnabled) 'Failure did not stop runtime capture'
    [RimTouch.TouchPlatform.TouchInputProof]::SetRuntimeDebugLog($true)
    Assert ([RimTouch.TouchPlatform.TouchInputProof]::RuntimeDebugLogEnabled) 'Could not restart after failure'
    'PASS runtime: no flags, start/stop, build identity, fresh session, budget/failure stop and restart without reboot'
    exit
}

if ($Case -eq 'disabled') {
    Assert (![RimTouch.TouchPlatform.TouchInputProof]::IsRecording) 'Capture enabled without flag'
    [RimTouch.TouchPlatform.TouchInputProof]::RecordDiagnostic([int[]]@(1))
    Assert (![IO.File]::Exists($file)) 'Disabled capture wrote a file'
    Assert ([Verse.Log]::Messages.Count -eq 0) 'Disabled capture wrote to Debug log'
    'PASS disabled: no output'
    exit
}
Assert ([RimTouch.TouchPlatform.TouchInputProof]::IsRecording) 'Flag did not enable capture'
$frame = [RimTouch.TouchPlatform.ContactFrame]::new()
$contact = [RimTouch.TouchPlatform.ContactTouch]::new()
$contact.fingerId=1
$contact.phase=[RimTouch.TouchPlatform.ContactPhase]::Began
[RimTouch.TouchPlatform.TouchInputProof]::Record($frame,@($contact),'test')
$contact.phase=[RimTouch.TouchPlatform.ContactPhase]::Moved
for ($i=1; $i -le 9; $i++) {
    [UnityEngine.Time]::frameCount=$i
    [UnityEngine.Time]::realtimeSinceStartup=$i*0.01
    [RimTouch.TouchPlatform.TouchInputProof]::Record($frame,@($contact),'test')
}
$usesFile = $Case -eq 'file' -or $Case -eq 'both'
$usesDebug = $Case -eq 'debug' -or $Case -eq 'both'
if ($usesFile) { Assert (([IO.File]::ReadAllLines($file)).Length -eq 1) 'Movement not sampled in file' }
if ($usesDebug) { Assert (@(ProofMessages).Count -eq 1) 'Movement not sampled in Debug log' }
$contact.phase=[RimTouch.TouchPlatform.ContactPhase]::Ended
[RimTouch.TouchPlatform.TouchInputProof]::Record($frame,@($contact),'test')
if ($usesFile) { Assert (([IO.File]::ReadAllLines($file)).Length -eq 2) 'Release edge missing in file' }
if ($usesDebug) { Assert (@(ProofMessages).Count -eq 2) 'Release edge missing in Debug log' }
for ($i=0; $i -lt 1030; $i++) { [RimTouch.TouchPlatform.TouchInputProof]::RecordDiagnostic([int[]]@($i)) }
Assert (![RimTouch.TouchPlatform.TouchInputProof]::IsRecording) 'Budget did not stop capture'
$debug = @(ProofMessages)
if ($usesDebug) { Assert ($debug.Count -eq 1024) 'Incorrect Debug log record budget' }
else { Assert ($debug.Count -eq 0) 'File-only capture leaked to Debug log' }
if ($usesFile) {
    $lines=[IO.File]::ReadAllLines($file)
    Assert ($lines.Length -eq 1024) 'Incorrect file record budget'
    if ($usesDebug) {
        for ($i=0; $i -lt $lines.Length; $i++) {
            Assert ($lines[$i] -ceq $debug[$i].Substring(17)) 'Mirrored records differ'
        }
    }
} else {
    Assert (![IO.File]::Exists($file)) 'Debug-only capture created a separate file'
    $lines=@($debug | ForEach-Object { $_.Substring(17) })
}
foreach ($line in $lines) { $doc=[Text.Json.JsonDocument]::Parse($line); $doc.Dispose() }
$first=$lines[0] | ConvertFrom-Json
Assert ($first.kind -eq 'contactFrame' -and $first.session.Length -eq 32) 'Missing capture metadata'
Assert (@([Verse.Log]::Messages | Where-Object { $_ -like '*1024-record limit*' }).Count -eq 1) 'Budget notice missing/repeated'

# Mutate counters only in this isolated child to exercise serialization and I/O failure.
$flags=[Reflection.BindingFlags]'NonPublic,Static'
[RimTouch.TouchPlatform.TouchInputProof].GetField('records',$flags).SetValue($null,0)
[RimTouch.TouchPlatform.TouchInputProof]::RecordDiagnostic([float[]]@([float]::NaN))
Assert (![RimTouch.TouchPlatform.TouchInputProof]::IsRecording) 'Serialization failure escaped or did not stop capture'
Assert ([Verse.Log]::Warnings -eq 1) 'Serialization failure warning missing'
if ($usesFile) {
    [RimTouch.TouchPlatform.TouchInputProof].GetField('records',$flags).SetValue($null,0)
    [Verse.GenFilePaths]::SaveDataFolderPath = Join-Path $dir 'missing/nested'
    [RimTouch.TouchPlatform.TouchInputProof]::RecordDiagnostic([int[]]@(1))
    Assert (![RimTouch.TouchPlatform.TouchInputProof]::IsRecording) 'I/O failure escaped or did not stop capture'
    Assert ([Verse.Log]::Warnings -eq 2) 'I/O failure warning missing'
}
"PASS $Case : routing, sampling, release edge, 1024-record budget, valid JSON, failure isolation"
