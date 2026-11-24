param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [Parameter(Mandatory=$true)][string]$HarmonyDll,
    [string]$RimTouchDll = (Join-Path (Split-Path $PSScriptRoot -Parent) '.build/1.7/RimTouch.dll')
)
$ErrorActionPreference = 'Stop'

$game = (Resolve-Path -LiteralPath $GameDir).Path
$harmony = (Resolve-Path -LiteralPath $HarmonyDll).Path
$product = (Resolve-Path -LiteralPath $RimTouchDll).Path

if (-not ([System.Management.Automation.PSTypeName]'RimTouchAssemblyResolver').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;

public static class RimTouchAssemblyResolver
{
    private static string[] dirs;

    public static void Register(string[] searchDirs)
    {
        dirs = searchDirs;
        AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
    }

    public static void Unregister()
    {
        AppDomain.CurrentDomain.AssemblyResolve -= OnResolve;
    }

    private static Assembly OnResolve(object sender, ResolveEventArgs args)
    {
        string simpleName = new AssemblyName(args.Name).Name;
        if (simpleName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (dirs == null) return null;
        for (int i = 0; i < dirs.Length; i++)
        {
            string candidate = Path.Combine(dirs[i], simpleName + ".dll");
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }
        }
        return null;
    }
}
'@
}

$resolvedDirs = [string[]]@(
    $game,
    (Join-Path $game 'RimWorldWin64_Data/Managed'),
    (Split-Path -Parent $harmony)
) | Where-Object { Test-Path -LiteralPath $_ }

[RimTouchAssemblyResolver]::Register($resolvedDirs)
try {
    $hAsm = [System.Reflection.Assembly]::LoadFrom($harmony)
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $game 'Assembly-CSharp.dll'))
    $rtAsm = [System.Reflection.Assembly]::LoadFrom($product)

    $harmonyPatchAttrType = $hAsm.GetType('HarmonyLib.HarmonyPatch')
    $harmonyArgAttrType = $hAsm.GetType('HarmonyLib.HarmonyArgument')

    $patchTypes = @()
    foreach ($t in $rtAsm.GetTypes()) {
        if ($t.GetCustomAttributes($harmonyPatchAttrType, $false).Length -gt 0) {
            $patchTypes += $t
        }
    }
    if (-not $patchTypes.Count) {
        throw 'No [HarmonyPatch] classes found in RimTouch.dll.'
    }

    $failures = 0
    foreach ($pt in $patchTypes) {
        $infoField = $harmonyPatchAttrType.GetField('info')
        $declType = $null
        $methodName = $null
        $argTypes = $null
        foreach ($attr in $pt.GetCustomAttributes($harmonyPatchAttrType, $false)) {
            $info = $infoField.GetValue($attr)
            if ($info.declaringType) { $declType = $info.declaringType }
            if ($info.methodName) { $methodName = $info.methodName }
            if ($info.argumentTypes) { $argTypes = $info.argumentTypes }
        }

        $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
        if ($null -ne $argTypes) {
            $target = $declType.GetMethod($methodName, $flags, $null, $argTypes, $null)
        } else {
            $candidates = @($declType.GetMethods($flags) | Where-Object { $_.Name -eq $methodName })
            $target = if ($candidates.Count -eq 1) { $candidates[0] } else { $null }
        }
        if ($null -eq $target) {
            Write-Host "  [FAIL] $($pt.Name): target '$($declType.FullName).$methodName' missing or ambiguous"
            $failures++
            continue
        }

        $targetParams = $target.GetParameters()
        $targetParamSummary = ($targetParams | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
        $patchErrors = @()

        $prep = $pt.GetMethod('Prepare', [System.Reflection.BindingFlags]'Public,NonPublic,Static')
        if ($prep -and $prep.GetParameters().Count -eq 0) {
            if (-not [bool]$prep.Invoke($null, @())) {
                $patchErrors += 'Prepare() returned false against current RimWorld 1.7 runtime'
            }
        }

        foreach ($hmName in @('Prefix', 'Postfix', 'Transpiler', 'Finalizer')) {
            $hm = $pt.GetMethod($hmName, [System.Reflection.BindingFlags]'Public,NonPublic,Static')
            if (-not $hm) { continue }
            foreach ($p in $hm.GetParameters()) {
                $pn = $p.Name
                $ptParam = $p.ParameterType
                $elemType = if ($ptParam.IsByRef) { $ptParam.GetElementType() } else { $ptParam }

                if ($hmName -eq 'Transpiler') {
                    if ($pn -notin @('instructions', 'generator', 'original') -and $elemType.Name -notlike 'IEnumerable*') {
                        $patchErrors += "$hmName param '$pn' unexpected in Transpiler"
                    }
                    continue
                }

                $argAttr = $p.GetCustomAttributes($harmonyArgAttrType, $false)
                if ($argAttr.Length -gt 0) {
                    $idx = $harmonyArgAttrType.GetProperty('Index').GetValue($argAttr[0])
                    $origName = $harmonyArgAttrType.GetProperty('OriginalName').GetValue($argAttr[0])
                    if ($null -ne $origName -and $origName -ne '') {
                        if (-not ($targetParams | Where-Object { $_.Name -eq $origName })) {
                            $patchErrors += "$hmName [HarmonyArgument('$origName')] not found in ($targetParamSummary)"
                        }
                    } elseif ($idx -lt 0 -or $idx -ge $targetParams.Length) {
                        $patchErrors += "$hmName [HarmonyArgument($idx)] out of range for ($targetParamSummary)"
                    } else {
                        $tp = $targetParams[$idx]
                        $tpElem = if ($tp.ParameterType.IsByRef) { $tp.ParameterType.GetElementType() } else { $tp.ParameterType }
                        if (-not $elemType.IsAssignableFrom($tpElem)) {
                            $patchErrors += "$hmName [HarmonyArgument($idx)] type $($elemType.FullName) incompatible with $($tpElem.FullName)"
                        }
                    }
                    continue
                }

                if ($pn -eq '__instance') {
                    if ($target.IsStatic -or -not $elemType.IsAssignableFrom($target.DeclaringType)) {
                        $patchErrors += "$hmName invalid __instance binding for $($target.DeclaringType.FullName)"
                    }
                } elseif ($pn -eq '__result') {
                    if (-not $elemType.IsAssignableFrom($target.ReturnType)) {
                        $patchErrors += "$hmName __result type $($elemType.FullName) incompatible with $($target.ReturnType.FullName)"
                    }
                } elseif ($pn -in @('__originalMethod', '__runOriginal', '__args')) {
                    # Valid Harmony injection
                } elseif ($pn.StartsWith('__')) {
                    if ($pn -match '^__(\d+)$' -and [int]$Matches[1] -ge $targetParams.Length) {
                        $patchErrors += "$hmName $pn out of range"
                    }
                } else {
                    $matched = $targetParams | Where-Object { $_.Name -eq $pn }
                    if (-not $matched) {
                        $patchErrors += "$hmName parameter '$pn' NOT FOUND in ($targetParamSummary)"
                    } else {
                        $targetElem = if ($matched.ParameterType.IsByRef) { $matched.ParameterType.GetElementType() } else { $matched.ParameterType }
                        if (-not $elemType.IsAssignableFrom($targetElem)) {
                            $patchErrors += "$hmName parameter '$pn' type $($elemType.FullName) incompatible with $($targetElem.FullName)"
                        }
                    }
                }
            }
        }

        if ($patchErrors.Count -gt 0) {
            Write-Host "  [FAIL] $($pt.Name) -> $($declType.Name).$methodName($targetParamSummary)"
            foreach ($err in $patchErrors) { Write-Host "         ERROR: $err" }
            $failures++
        }
    }

    if ($failures -gt 0) {
        throw "Harmony patch audit failed with $failures broken patch class(es)."
    }
    "PASS Harmony patch audit: all $($patchTypes.Count) [HarmonyPatch] classes verified against RimWorld 1.7 assemblies"
} finally {
    [RimTouchAssemblyResolver]::Unregister()
}
