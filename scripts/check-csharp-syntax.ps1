$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# Ensure Microsoft.CodeAnalysis.CSharp is loaded (bundled with PowerShell 7 or .NET SDK Roslyn)
$caAsm = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.CodeAnalysis.CSharp' } | Select-Object -First 1
if (-not $caAsm) {
    $psCandidate = Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'
    if (Test-Path -LiteralPath $psCandidate) {
        [void][System.Reflection.Assembly]::LoadFrom((Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'))
        $caAsm = [System.Reflection.Assembly]::LoadFrom($psCandidate)
    } else {
        # Trigger Add-Type once so pwsh loads its internal Roslyn assemblies
        Add-Type -TypeDefinition 'public static class __RoslynWarmup {}'
        $caAsm = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.CodeAnalysis.CSharp' } | Select-Object -First 1
    }
}
if (-not $caAsm) {
    throw 'Microsoft.CodeAnalysis.CSharp could not be loaded for syntax verification.'
}

$syntaxTreeType = $caAsm.GetType('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree')
$parseOptionsType = $caAsm.GetType('Microsoft.CodeAnalysis.CSharp.CSharpParseOptions')
$langVersionType = $caAsm.GetType('Microsoft.CodeAnalysis.CSharp.LanguageVersion')
$latestLang = [System.Enum]::Parse($langVersionType, 'Latest')
$parseOptions = $parseOptionsType.GetMethod('get_Default').Invoke($null, @()).WithLanguageVersion($latestLang)
$parseMethod = $syntaxTreeType.GetMethod('ParseText', [Type[]]@([string], $parseOptionsType, [string], [System.Text.Encoding], [System.Threading.CancellationToken]))

$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'Source') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object FullName)
if (-not $sources.Count) {
    throw 'No C# source files found under Source/.'
}

$errors = @()
foreach ($file in $sources) {
    $rel = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
    $text = [IO.File]::ReadAllText($file.FullName)
    $tree = $parseMethod.Invoke($null, @($text, $parseOptions, $rel, [System.Text.Encoding]::UTF8, [System.Threading.CancellationToken]::None))
    foreach ($diag in $tree.GetDiagnostics()) {
        if ("$($diag.Severity)" -eq 'Error') {
            $errors += "$($diag.ToString())"
        }
    }
}

if ($errors.Count -gt 0) {
    foreach ($e in $errors) { Write-Error $e -ErrorAction Continue }
    throw "C# syntax verification failed with $($errors.Count) error(s)."
}

# Verify RimTouchBootstrap isolates Harmony patches per class rather than fragile PatchAll()
$bootstrap = [IO.File]::ReadAllText((Join-Path $root 'Source/RimTouchBootstrap.cs'))
if ($bootstrap -match '\bPatchAll\s*\(') {
    throw 'RimTouchBootstrap.cs must use isolated CreateClassProcessor(type).Patch() instead of PatchAll().'
}

"PASS C# syntax: $($sources.Count) Source/*.cs files parsed with 0 Roslyn syntax errors and isolated Harmony bootstrap"
