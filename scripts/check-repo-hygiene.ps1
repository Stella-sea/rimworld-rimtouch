$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Fail([string]$Message) {
    throw "Repo hygiene check failed: $Message"
}

# 1. Forbidden tracked/unignored artifacts (proprietary binaries, build outputs, saves, player logs)
$tracked = @(& git -C $root ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { Fail 'git ls-files failed.' }

$forbiddenPatterns = @(
    '\.dll$',
    '\.pdb$',
    '\.exe$',
    '\.rws$',
    '\.zip$',
    '^\.build/',
    '^TestReports/',
    'RimTouch-Input-Proof.*\.jsonl$'
)
foreach ($rel in $tracked) {
    $norm = $rel.Replace('\', '/')
    foreach ($pat in $forbiddenPatterns) {
        if ($norm -match $pat) {
            Fail "Forbidden file tracked in git: $norm"
        }
    }
}

# 2. Conflict markers, accidental local machine paths, and trailing whitespace
$textCheckExts = @('.cs', '.ps1', '.sh', '.xml', '.yml', '.yaml', '.json', '.md')
$strictTrailingExts = @('.cs', '.ps1', '.xml', '.yml', '.yaml')
$localPathRegex = '([A-Za-z]:\\(Rimworld|Users\\)|/home/[^/\s]+/workspace/research)'

$candidates = @($tracked)
foreach ($rel in $candidates) {
    $norm = $rel.Replace('\', '/')
    $full = Join-Path $root $norm
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
    $ext = [IO.Path]::GetExtension($norm).ToLowerInvariant()
    if ($ext -notin $textCheckExts) { continue }

    $lines = [IO.File]::ReadAllLines($full)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        $lineNo = $i + 1
        if ($line -match '^(<{7}|>{7}) ') {
            Fail "Merge conflict marker at ${norm}:${lineNo}"
        }
        if ($norm -ne 'scripts/package-release.sh' -and $norm -ne 'scripts/check-repo-hygiene.ps1' -and $line -match $localPathRegex) {
            Fail "Machine-specific path leaked at ${norm}:${lineNo}"
        }
        if ($ext -in $strictTrailingExts -and $line -match '[ \t]+$') {
            Fail "Trailing whitespace at ${norm}:${lineNo}"
        }
    }
}

# 3. Build.json, PublishedFileId.txt, and About/About.xml validation
$buildSpec = Get-Content -LiteralPath (Join-Path $root 'Build.json') -Raw | ConvertFrom-Json
if ($buildSpec.assemblyName -ne 'RimTouch' -or $buildSpec.sourceDirectory -ne 'Source') {
    Fail 'Build.json assemblyName/sourceDirectory mismatch.'
}

$pubId = (Get-Content -LiteralPath (Join-Path $root 'About/PublishedFileId.txt') -Raw).Trim()
if ($pubId -ne '3827698857') {
    Fail "About/PublishedFileId.txt changed unexpectedly ($pubId)."
}

[xml]$aboutXml = Get-Content -LiteralPath (Join-Path $root 'About/About.xml') -Raw
$meta = $aboutXml.ModMetaData
if (-not $meta) { Fail 'About/About.xml missing <ModMetaData>.' }
if ($meta.packageId -ne 'cinder.rimtouch17.private') {
    Fail "About/About.xml packageId changed unexpectedly ($($meta.packageId))."
}
$versions = @($meta.supportedVersions.li)
if ($versions -notcontains '1.7') {
    Fail 'About/About.xml supportedVersions must include 1.7.'
}
$modVersion = "$($meta.modVersion)".Trim()
if ($modVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-test\.[0-9]+)?$') {
    Fail "About/About.xml <modVersion> '$modVersion' is not valid MAJOR.MINOR.PATCH[-test.N]."
}

# 4. Localization XML validity & English/ChineseSimplified key parity
$xmlFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'Languages') -Recurse -File -Filter '*.xml')
foreach ($xf in $xmlFiles) {
    try {
        [void]([xml](Get-Content -LiteralPath $xf.FullName -Raw))
    } catch {
        Fail "Invalid XML in $($xf.FullName): $($_.Exception.Message)"
    }
}

function Get-KeyedMap([string]$Path) {
    [xml]$doc = Get-Content -LiteralPath $Path -Raw
    $map = [ordered]@{}
    foreach ($node in $doc.LanguageData.ChildNodes) {
        if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        if ($map.Contains($node.Name)) {
            Fail "Duplicate translation key '$($node.Name)' in $Path"
        }
        $val = "$($node.InnerText)"
        if ([string]::IsNullOrWhiteSpace($val)) {
            Fail "Empty translation value for '$($node.Name)' in $Path"
        }
        $map[$node.Name] = $val
    }
    return $map
}

$enMap = Get-KeyedMap (Join-Path $root 'Languages/English/Keyed/RimTouch.xml')
$zhMap = Get-KeyedMap (Join-Path $root 'Languages/ChineseSimplified/Keyed/RimTouch.xml')

foreach ($k in $enMap.Keys) {
    if (-not $zhMap.Contains($k)) { Fail "Key '$k' missing in ChineseSimplified/Keyed/RimTouch.xml" }
}
foreach ($k in $zhMap.Keys) {
    if (-not $enMap.Contains($k)) { Fail "Key '$k' missing in English/Keyed/RimTouch.xml" }
}

"PASS repo hygiene: clean tracked tree, valid About.xml ($modVersion), and $($enMap.Count)/$($zhMap.Count) synced localization keys"
