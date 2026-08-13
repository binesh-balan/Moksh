<#
.SYNOPSIS
    Authenticode-sign MOKSH build output.

.DESCRIPTION
    Signs the executables and MOKSH-owned assemblies in a publish directory, then
    verifies every signature before returning.

    Third-party binaries that already carry a valid signature are skipped, not
    re-signed - notably es.exe, which is signed by voidtools. Overwriting a
    publisher's signature with our own would destroy the stronger guarantee.

.PARAMETER Path
    Directory containing the build output to sign.

.PARAMETER Thumbprint
    Certificate thumbprint from Cert:\CurrentUser\My or Cert:\LocalMachine\My.
    Defaults to the MOKSH code-signing certificate.

.PARAMETER TimestampUrl
    RFC-3161 timestamp authority. Timestamping is what keeps a signature valid
    after the certificate expires; without it every signed binary "expires" too.
    Signing continues with a warning if the TSA is unreachable.

.EXAMPLE
    .\sign.ps1 -Path bin\release-stage

.NOTES
    A self-signed certificate does NOT clear SmartScreen or the "Unknown
    Publisher" UAC prompt for the public - only a certificate chaining to a
    trusted public CA does that. Self-signed is still useful for tamper-evidence
    and for enterprise deployment, where the certificate is pushed to Trusted
    Publishers via Intune or GPO. To switch to a real CA certificate, pass its
    thumbprint; nothing else here changes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [string] $Thumbprint = 'ED6A1D7398ABE80493FF1A3848EA2C1B217B233C',
    [string] $TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    $root = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    $tool = Get-ChildItem $root -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName | Select-Object -Last 1
    if (-not $tool) { throw "signtool.exe not found. Install the Windows SDK." }
    return $tool.FullName
}

$signtool = Find-SignTool
$cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1
if (-not $cert) { throw "Certificate $Thumbprint not found in CurrentUser\My or LocalMachine\My." }

$selfSigned = $cert.Subject -eq $cert.Issuer

Write-Host "signtool    : $signtool"
Write-Host "certificate : $($cert.Subject)"
Write-Host "expires     : $($cert.NotAfter.ToString('yyyy-MM-dd'))"
if ($selfSigned) {
    Write-Warning "This certificate is SELF-SIGNED. Signed binaries will still trigger SmartScreen and show as an unknown publisher unless the certificate is installed in Trusted Publishers on the target machine."
}
Write-Host ''

# Candidates: our own build output only.
$candidates = Get-ChildItem $Path -Recurse -Include *.exe, *.dll |
              Where-Object { $_.Name -notmatch '^(es)\.exe$' }

$toSign = @()
$skipped = @()
foreach ($f in $candidates) {
    $sig = Get-AuthenticodeSignature $f.FullName
    # Already validly signed by someone else - leave it alone.
    if ($sig.Status -eq 'Valid' -and $sig.SignerCertificate.Thumbprint -ne $Thumbprint) {
        $skipped += [pscustomobject]@{ File = $f.Name; Reason = "already signed by $($sig.SignerCertificate.Subject -replace '^CN=([^,]+).*','$1')" }
        continue
    }
    $toSign += $f
}

foreach ($s in $skipped) { Write-Host ("  skip  {0,-34} {1}" -f $s.File, $s.Reason) -ForegroundColor DarkGray }
Write-Host ''
Write-Host "signing $($toSign.Count) file(s)..."

$files = $toSign | ForEach-Object { $_.FullName }
$args = @('sign', '/sha1', $Thumbprint, '/fd', 'SHA256', '/td', 'SHA256', '/tr', $TimestampUrl, '/q')
& $signtool @args @files 2>&1 | Out-String -Stream | Where-Object { $_ -match '\S' } | ForEach-Object { Write-Host "  $_" }

if ($LASTEXITCODE -ne 0) {
    Write-Warning "Timestamped signing failed (exit $LASTEXITCODE). Retrying WITHOUT a timestamp - signatures will stop verifying when the certificate expires on $($cert.NotAfter.ToString('yyyy-MM-dd'))."
    $args = @('sign', '/sha1', $Thumbprint, '/fd', 'SHA256', '/q')
    & $signtool @args @files 2>&1 | Out-String -Stream | Where-Object { $_ -match '\S' } | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
}

# Verify - never report success without checking.
Write-Host ''
Write-Host 'verifying...'
$bad = @()
foreach ($f in $toSign) {
    $sig = Get-AuthenticodeSignature $f.FullName
    $timestamped = $null -ne $sig.TimeStamperCertificate
    # UnknownError is the expected status for a self-signed chain: the signature
    # itself is intact, the chain just isn't trusted by this machine.
    $ok = $sig.Status -eq 'Valid' -or ($selfSigned -and $sig.SignatureType -eq 'Authenticode')
    if (-not $ok) { $bad += "$($f.Name): $($sig.Status) $($sig.StatusMessage)" }
    elseif ($f.Extension -eq '.exe') {
        Write-Host ("  {0,-34} {1}  timestamped={2}" -f $f.Name, $sig.Status, $timestamped)
    }
}

Write-Host ''
if ($bad) {
    $bad | ForEach-Object { Write-Host "  FAILED $_" -ForegroundColor Red }
    throw "$($bad.Count) file(s) failed verification."
}
Write-Host "All $($toSign.Count) file(s) signed and verified." -ForegroundColor Green
