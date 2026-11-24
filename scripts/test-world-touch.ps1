param(
    [switch]$VerifyRegression,
    [ValidateSet('free-anchor', 'mixed-axes', 'outside-tap')][string]$RegressionCase
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if ($VerifyRegression -and !$RegressionCase) {
    foreach ($caseName in @('free-anchor', 'mixed-axes', 'outside-tap')) {
        & pwsh -NoProfile -File $PSCommandPath -VerifyRegression -RegressionCase $caseName
        if ($LASTEXITCODE -ne 0) { throw "Negative control failed: $caseName" }
    }
    exit
}
if ($RegressionCase -and !$VerifyRegression) { throw 'RegressionCase requires VerifyRegression.' }

# Compile the real camera/inertia implementation and real gesture methods.
# Only Unity math, camera projection and game services are substituted.
function Read-Source([string]$relative) {
    [IO.File]::ReadAllText((Join-Path $root $relative)) -replace '(?m)^using [^;]+;\r?\n', ''
}
function Get-Method([string]$text, [string]$signature) {
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production method: $signature" }
    $open = $text.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0 -and $end -lt $text.Length) {
        if ($text[$end] -eq '{') { $depth++ }
        if ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced method: $signature" }
    $text.Substring($start, $end - $start)
}

$driver = Read-Source 'Source/TouchInputDriver.cs'
$ui = Read-Source 'Source/UiHitTester.cs'
$world = Read-Source 'Source/WorldMapTouchCamera.cs'
$constants = [regex]::Matches($driver, 'private const float \w+ = [^;]+;').Value -join "`n"
$fieldStart = $driver.IndexOf('private static TouchMode mode')
$fieldEnd = $driver.IndexOf('private static readonly Vector2[] MapTargetSearchOffsets')
if ($fieldStart -lt 0 -or $fieldEnd -le $fieldStart) { throw 'Driver field block not found.' }
$fields = $driver.Substring($fieldStart, $fieldEnd - $fieldStart)
$methods = @(
    (Get-Method $driver 'private static void UpdateOneFingerWorld('),
    (Get-Method $driver 'private static void UpdateTwoFinger('),
    (Get-Method $driver 'private static void CompleteActiveGesture('),
    (Get-Method $ui 'private static Vector2 TouchToGui('),
    (Get-Method $ui 'private static Vector3 GuiToScreenPoint(')
) -join "`n"

if ($VerifyRegression) {
    # Compilable mutations must be rejected by behavior, in separate processes.
    switch ($RegressionCase) {
        'free-anchor' {
            $correct = 'CommitWorldRotation(driver, sphereRotation * deltaRotation)'
            if (!$world.Contains($correct)) { throw 'Free-anchor mutation point not found.' }
            $world = $world.Replace($correct, 'CommitWorldRotation(driver, deltaRotation * sphereRotation)')
            $expectedFailure = 'free anchor screen error'
        }
        'mixed-axes' {
            $correct = 'Vector3.Cross(Vector3.up, forward)'
            if (!$world.Contains($correct)) { throw 'Axis mutation point not found.' }
            $world = $world.Replace($correct, 'Vector3.Cross(driver.sphereRotation * Vector3.up, forward)')
            $expectedFailure = 'screen drag reversed|not screen-relative|roll drift'
        }
        'outside-tap' {
            $correct = 'if (moveDistance > DragSlopPixels)'
            if (!$methods.Contains($correct)) { throw 'Outside-tap mutation point not found.' }
            $methods = $methods.Replace($correct, 'if (moveDistance > DragSlopPixels || !TryGetWorldSpherePointAtGui(startGui, out worldPanAnchorPoint))')
            $expectedFailure = 'outside tap did not clear selection'
        }
    }
}

$parts = @(
    [IO.File]::ReadAllText((Join-Path $root 'scripts/tests/WorldTouchHarness.cs')),
    (Read-Source 'Source/TouchMode.cs'),
    (Read-Source 'Source/Input/VelocityHistory.cs'),
    (Read-Source 'Source/Input/TwistGesture.cs'),
    $world,
    (Read-Source 'Source/TouchInertia.cs'),
    "namespace RimTouch { public static partial class TouchInputDriver { $constants`n$fields`n$methods } }"
)
Add-Type -TypeDefinition ($parts -join "`n")
try {
    [RimTouch.TouchInputDriver]::RunWorldTouchTests()
    if ($VerifyRegression) { throw 'Regression mutation unexpectedly passed.' }
} catch {
    if (!$VerifyRegression) { throw }
    if ($_.Exception.ToString() -notmatch $expectedFailure) { throw }
    "PASS negative control: $RegressionCase was rejected by its behavioral assertion"
}
