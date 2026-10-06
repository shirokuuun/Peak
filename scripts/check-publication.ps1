param([string]$Configuration = (Join-Path $PSScriptRoot '../src/UsageNotch.App/publishing.json'))
$ErrorActionPreference = 'Stop'
$peakConfig = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
$peakFields = @('publisherName', 'supportEmail', 'websiteUrl', 'releasesUrl', 'policyVersion')
foreach ($peakProperty in $peakConfig.PSObject.Properties.Name) {
    if ($peakProperty -notin $peakFields) { throw "Unexpected publishing field: $peakProperty. Ship public contact details only." }
}
foreach ($peakField in $peakFields) {
    if ([string]::IsNullOrWhiteSpace($peakConfig.$peakField)) { throw "Complete $peakField before creating a public package." }
}
try { $peakEmail = [Net.Mail.MailAddress]::new($peakConfig.supportEmail) } catch { throw 'Configure a support email address.' }
if ($peakEmail.Address -ne $peakConfig.supportEmail -or $peakConfig.supportEmail -match '[\r\n]') { throw 'Use a plain support email address.' }
foreach ($peakField in @('websiteUrl', 'releasesUrl')) {
    $peakUri = $null
    if (-not [Uri]::TryCreate($peakConfig.$peakField, [UriKind]::Absolute, [ref]$peakUri) -or
        $peakUri.Scheme -ne 'https' -or $peakUri.UserInfo) { throw "Use a public HTTPS URL for $peakField." }
}
if ($peakConfig.publisherName -match '[\r\n]' -or $peakConfig.policyVersion -notmatch '^\d+\.\d+$') { throw 'Invalid publisher name or policy version.' }
foreach ($peakPolicy in @('Privacy.md', 'Terms.md', 'Third-party-notices.md')) {
    $peakText = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../src/UsageNotch.App/Policies/$peakPolicy") -Raw
    if ($peakText -notmatch ([regex]::Escape('Policy version ' + $peakConfig.policyVersion) + '\b') -or $peakText -match '(?i)polar|refund policy|upcoming freeware|draft terms') { throw "Policy $peakPolicy does not match the freeware release." }
}
Write-Output 'Public freeware publishing configuration and policy versions are complete.'
