param(
    [string]$Compiler,
    [string]$Published = (Join-Path $PSScriptRoot '../dist/Peak'),
    [string]$SignTool,
    [ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [Uri]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$UnsignedTest,
    [switch]$Unsigned
)
$ErrorActionPreference = 'Stop'
$peakRoot = Split-Path -Parent $PSScriptRoot
$peakPublished = [IO.Path]::GetFullPath($Published, $peakRoot)
if (-not $UnsignedTest) {
    & (Join-Path $PSScriptRoot 'check-publication.ps1') -Configuration (Join-Path $peakPublished 'publishing.json')
    if (-not $CertificateThumbprint -and -not $Unsigned) { throw 'Supply a signing certificate or explicitly choose -Unsigned for the first freeware release.' }
}
$peakSigned = -not $UnsignedTest -and -not $Unsigned
$peakPublishing = Get-Content -LiteralPath (Join-Path $peakPublished 'publishing.json') -Raw | ConvertFrom-Json
if (-not $Compiler) {
    $peakCompilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($peakCompilerCommand) { $Compiler = $peakCompilerCommand.Source }
    else {
        foreach ($peakCandidate in @((Join-Path $peakRoot '.research/tools/Inno/ISCC.exe'), "${env:ProgramFiles}/Inno Setup 7/ISCC.exe", "${env:ProgramFiles(x86)}/Inno Setup 7/ISCC.exe", "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe", "$env:LOCALAPPDATA/Programs/Inno Setup 7/ISCC.exe", "$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe")) {
            if (Test-Path -LiteralPath $peakCandidate) { $Compiler = $peakCandidate; break }
        }
    }
}
if (-not $Compiler -or -not (Test-Path -LiteralPath $Compiler -PathType Leaf)) { throw 'Install the Inno Setup compiler, or pass its ISCC.exe path with -Compiler.' }
$peakRequired = @('Peak.exe', 'Peak.dll', 'coreclr.dll', 'PresentationFramework.dll', 'publishing.json', 'licenses/dependencies.txt', 'bridge/UsageNotch.ClaudeBridge.exe')
foreach ($peakFile in $peakRequired) {
    if (-not (Test-Path -LiteralPath (Join-Path $peakPublished $peakFile) -PathType Leaf)) { throw "Publish a self-contained release first: missing $peakFile" }
}
$peakAllItems = @(Get-Item -LiteralPath $peakPublished) + @(Get-ChildItem -LiteralPath $peakPublished -Recurse -Force)
if ($peakAllItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'The published files contain a link. Packaging stopped.' }
foreach ($peakFile in ($peakAllItems | Where-Object { -not $_.PSIsContainer })) {
    if ($peakFile.Name -in 'commercial.json', 'license.dat', 'settings.json', 'auth.json', 'setup.json', 'integrations.json' -or
        $peakFile.Extension -in '.pfx', '.key', '.pem', '.pdb', '.jsonl', '.log') { throw "Private, obsolete or debug file in published tree: $($peakFile.Name)" }
}
$peakVersion = (Get-Item -LiteralPath (Join-Path $peakPublished 'Peak.exe')).VersionInfo.ProductVersion.Split('+')[0]
if ($peakVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'A semantic release version is required.' }
$peakOutput = Join-Path $peakRoot 'artifacts/releases'
$peakSuffix = if ($UnsignedTest) { '-unsigned-test' } else { '' }
$peakInstaller = Join-Path $peakOutput "Peak-$peakVersion-Windows-x64-Setup$peakSuffix.exe"
if (Test-Path -LiteralPath $peakInstaller) { throw 'An installer with this version already exists. Preserve it and increment the version before rebuilding.' }
$peakCompilerArguments = @('/Qp', "/DAppVersion=$peakVersion", "/DPublishDir=$peakPublished", "/DOutputDir=$peakOutput", "/DPublisherName=$($peakPublishing.publisherName)")
if ($UnsignedTest) { $peakCompilerArguments += '/DUnsignedTest' }
if ($peakSigned) {
    $peakCompilerArguments += '/DSignRelease'
    if (-not $SignTool) {
        $peakToolCommand = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($peakToolCommand) { $SignTool = $peakToolCommand.Source }
        else { $SignTool = Get-ChildItem -Path "${env:ProgramFiles(x86)}/Windows Kits/10/bin/*/x64/signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName }
    }
    if (-not $SignTool -or -not (Test-Path -LiteralPath $SignTool)) { throw 'Windows SDK signtool.exe is required for a signed release.' }
    $peakCertificate = Get-Item -LiteralPath "Cert:/CurrentUser/My/$CertificateThumbprint"
    if (-not $peakCertificate.HasPrivateKey -or $peakCertificate.NotAfter -le (Get-Date) -or $peakCertificate.NotBefore -gt (Get-Date)) { throw 'The selected signing certificate is not usable.' }
    if ($TimestampUrl.Scheme -notin 'http', 'https') { throw 'Use an HTTP(S) timestamp service.' }
    $peakOwnedBinaries = @('Peak.exe', 'Peak.dll', 'UsageNotch.Core.dll', 'UsageNotch.Providers.dll', 'UsageNotch.Windows.dll',
        'bridge/UsageNotch.ClaudeBridge.exe', 'bridge/UsageNotch.ClaudeBridge.dll', 'bridge/UsageNotch.Core.dll')
    foreach ($peakBinary in $peakOwnedBinaries) {
        $peakPath = Join-Path $peakPublished $peakBinary
        & $SignTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl.AbsoluteUri /td SHA256 $peakPath
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $peakBinary" }
        & $SignTool verify /pa /all $peakPath
        if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $peakBinary" }
    }
    # Inno expands $q/$f. No certificate passwords or private credentials are passed here.
    $peakInnoSign = '$q' + $SignTool + '$q sign /sha1 ' + $CertificateThumbprint + ' /fd SHA256 /tr $q' + $TimestampUrl.AbsoluteUri + '$q /td SHA256 $f'
    $peakCompilerArguments += '/Speak=' + $peakInnoSign
}
$peakCompilerArguments += (Join-Path $peakRoot 'installer/Peak.iss')
& $Compiler @peakCompilerArguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $peakInstaller)) { throw 'Installer compilation failed.' }
if ($peakSigned) {
    & $SignTool verify /pa /all $peakInstaller
    if ($LASTEXITCODE -ne 0) { throw 'The installer signature could not be verified.' }
    $peakSignature = Get-AuthenticodeSignature -LiteralPath $peakInstaller
    if ($peakSignature.Status -ne 'Valid' -or -not $peakSignature.TimeStamperCertificate) { throw 'The installer must have a trusted signature and timestamp.' }
}
$peakHash = (Get-FileHash -LiteralPath $peakInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($peakInstaller + '.sha256', "$peakHash  $([IO.Path]::GetFileName($peakInstaller))`n", [Text.UTF8Encoding]::new($false))
Write-Output "Installer ready: $peakInstaller"
