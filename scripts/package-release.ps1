param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$OutputDir = (Join-Path (Split-Path $PSScriptRoot -Parent) '.build/releases')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-test\.([0-9]+))?$') {
    throw "Version '$Version' must match MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-test.N (no leading 'v')."
}
$isPrerelease = $Version -like '*-test.*'
$testNumber = if ($isPrerelease) { $Matches[2] } else { $null }

# 1. Validate About/About.xml matches requested version and mod identity
[xml]$aboutXml = Get-Content -LiteralPath (Join-Path $root 'About/About.xml') -Raw
$meta = $aboutXml.ModMetaData
if ("$($meta.modVersion)".Trim() -ne $Version) {
    throw "About/About.xml <modVersion> is '$($meta.modVersion)', expected '$Version'."
}
if ($meta.packageId -ne 'cinder.rimtouch17.private') {
    throw "About/About.xml <packageId> mismatch: '$($meta.packageId)'."
}
if (@($meta.supportedVersions.li) -notcontains '1.7') {
    throw 'About/About.xml <supportedVersions> is missing 1.7.'
}

# Stable releases must not retain test-only Chinese About marker
$zhAboutPath = Join-Path $root 'Languages/ChineseSimplified/About/About.xml'
if (-not $isPrerelease -and (Test-Path -LiteralPath $zhAboutPath)) {
    $zhAboutText = Get-Content -LiteralPath $zhAboutPath -Raw
    if ($zhAboutText -match '测试版') {
        throw 'Stable release blocked: Languages/ChineseSimplified/About/About.xml still contains test-only marker "测试版".'
    }
}

# 2. Verify fresh .build/1.7/RimTouch.dll and Build-Receipt.json match current Source/ tree
$dllPath = Join-Path $root '.build/1.7/RimTouch.dll'
$receiptPath = Join-Path $root '.build/1.7/Build-Receipt.json'
if (-not (Test-Path -LiteralPath $dllPath) -or -not (Test-Path -LiteralPath $receiptPath)) {
    throw 'Missing .build/1.7/RimTouch.dll or Build-Receipt.json. Run Build17.ps1 first.'
}

$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.status -ne 'COMPILED' -or $receipt.exitCode -ne 0) {
    throw "Build-Receipt.json status is '$($receipt.status)' (exitCode=$($receipt.exitCode)). Rebuild cleanly first."
}
if (@($receipt.diagnostics).Count -gt 0) {
    throw "Build-Receipt.json reports $(@($receipt.diagnostics).Count) compiler diagnostic(s). Expected 0."
}

$dllHashUpper = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($dllHashUpper -ne "$($receipt.productSHA256)".ToUpperInvariant()) {
    throw "DLL hash ($dllHashUpper) does not match Build-Receipt.json ($($receipt.productSHA256)). Rebuild first."
}

$currentSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'Source') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object FullName)
$receiptMap = @{}
foreach ($s in $receipt.sources) {
    $receiptMap["$($s.path)"] = "$($s.sha256)".ToUpperInvariant()
}
if ($currentSources.Count -ne $receiptMap.Count) {
    throw "Source file count ($($currentSources.Count)) differs from Build-Receipt.json ($($receiptMap.Count)). Run Build17.ps1 again."
}
foreach ($cs in $currentSources) {
    $rel = [IO.Path]::GetRelativePath($root, $cs.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $cs.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    if (-not $receiptMap.ContainsKey($rel) -or $receiptMap[$rel] -ne $hash) {
        throw "Source '$rel' changed after .build/1.7/RimTouch.dll was built. Run Build17.ps1 again."
    }
}

# 3. Create clean release zip with forward-slash RimTouch/ root entries
[void][IO.Directory]::CreateDirectory($OutputDir)
$zipName = if ($isPrerelease) { "RimTouch-fix-test$testNumber.zip" } else { "RimTouch-v$Version.zip" }
$zipPath = Join-Path (Resolve-Path -LiteralPath $OutputDir).Path $zipName
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$entriesAdded = 0
$fs = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
    $zip = [IO.Compression.ZipArchive]::new($fs, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($folder in @('About', 'Languages')) {
            $folderRoot = Join-Path $root $folder
            $files = @(Get-ChildItem -LiteralPath $folderRoot -Recurse -File | Sort-Object FullName)
            foreach ($f in $files) {
                $rel = [IO.Path]::GetRelativePath($root, $f.FullName).Replace('\', '/')
                $entryName = "RimTouch/$rel"
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
                $entriesAdded++
            }
        }
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $dllPath, 'RimTouch/Assemblies/RimTouch.dll', [IO.Compression.CompressionLevel]::Optimal)
        $entriesAdded++
    } finally {
        $zip.Dispose()
    }
} finally {
    $fs.Dispose()
}

$zipHashLower = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$dllHashLower = $dllHashUpper.ToLowerInvariant()

Write-Output "Asset: $zipPath"
Write-Output "Entries: $entriesAdded (root folder: RimTouch/)"
Write-Output "DLL SHA256: $dllHashLower"
Write-Output "ZIP SHA256: $zipHashLower"
