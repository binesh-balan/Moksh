<#
.SYNOPSIS
    Publish, sign and package MOKSH as a per-machine x64 MSI.

.DESCRIPTION
    Runs the whole chain in the order that matters:

        publish -> trim -> SIGN -> package -> sign the MSI

    Signing happens before packaging so the MSI can never ship unsigned payload
    binaries. The MSI itself is signed afterwards; an unsigned installer wrapping
    signed files is the weak link, since the installer is what the user runs.

.PARAMETER SkipSigning
    Build without signing. Useful on a machine without the certificate.

.PARAMETER Thumbprint
    Signing certificate. Defaults to the MOKSH code-signing certificate.

.EXAMPLE
    .\build-msi.ps1

.NOTES
    Requires the .NET Framework MSBuild from Visual Studio - `dotnet build` cannot
    build this solution, because ResolveComReference is unsupported on the .NET Core
    MSBuild (MSB4803).

    Deploy with:  msiexec /i Moksh_6.2_x64.msi /qn
    Uninstall:    msiexec /x Moksh_6.2_x64.msi /qn

    For Intune, declare the .NET 8 Desktop Runtime as an app dependency. The MSI's
    own runtime check only detects "no desktop runtime at all" - see Moksh.wxs.
#>
[CmdletBinding()]
param(
    [switch] $SkipSigning,
    [string] $Thumbprint = 'ED6A1D7398ABE80493FF1A3848EA2C1B217B233C',
    [string] $Version = '6.2'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Find-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $p = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\amd64\MSBuild.exe" |
             Select-Object -First 1
        if ($p) { return $p }
    }
    throw "MSBuild not found. Install Visual Studio with the MSBuild component."
}

$msbuild = Find-MSBuild
$stage   = Join-Path $root 'bin\release-stage'
$msi     = Join-Path $root "bin\Moksh_${Version}_x64.msi"

Write-Host "==> Publishing" -ForegroundColor Cyan
$projects = Get-ChildItem (Join-Path $root 'source') -Filter *.csproj -Recurse |
            Where-Object { (Get-Content $_.FullName -Raw) -match 'exe</OutputType>' -and $_.Name -notmatch 'Tests' }

foreach ($p in $projects) {
    $errs = & $msbuild $p.FullName /restore /m /t:Publish `
        /p:Configuration=Release /p:Platform="Any CPU" /p:PublishDir="$stage" `
        /p:SelfContained=False /p:PublishSingleFile=False /p:PublishReadyToRun=false `
        /p:PublishTrimmed=False /p:PublishProtocol=FileSystem /verbosity:quiet 2>&1 |
        Select-String ': error'
    if ($errs) { throw "Publish failed for $($p.Name): $($errs[0])" }
}

# Dev-only artifacts that must not ship.
foreach ($f in @('SimpleTreeMapTestApp.exe', 'SimpleTreeMapTestApp.dll',
                 'SimpleTreeMapTestApp.deps.json', 'SimpleTreeMapTestApp.runtimeconfig.json')) {
    $t = Join-Path $stage $f
    if (Test-Path $t) { Remove-Item $t -Force }
}
Get-ChildItem $stage -Filter *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

$count = (Get-ChildItem $stage -Recurse -File | Measure-Object).Count
Write-Host "    $count files staged"

if (-not $SkipSigning) {
    Write-Host "==> Signing payload" -ForegroundColor Cyan
    & (Join-Path $root 'sign.ps1') -Path $stage -Thumbprint $Thumbprint | Out-Null
    Write-Host "    payload signed"

    # Ship the public certificate so the signature can be verified and the cert
    # deployed to Trusted Publishers.
    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1
    if ($cert) {
        Export-Certificate -Cert $cert -FilePath (Join-Path $stage 'MOKSH-CodeSigning.cer') -Type CERT | Out-Null
    }
}

Write-Host "==> Building MSI" -ForegroundColor Cyan
$icon = Join-Path $root 'installer\assets\logo.ico'
& wix build (Join-Path $root 'installer\wix\Moksh.wxs') `
    -arch x64 -d "PublishDir=$stage" -d "IconFile=$icon" -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed with exit code $LASTEXITCODE" }

if (-not $SkipSigning) {
    Write-Host "==> Signing MSI" -ForegroundColor Cyan
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName | Select-Object -Last 1
    if (-not $signtool) { throw "signtool.exe not found." }
    & $signtool.FullName sign /sha1 $Thumbprint /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /q $msi
    if ($LASTEXITCODE -ne 0) { throw "MSI signing failed with exit code $LASTEXITCODE" }
}

$item = Get-Item $msi
Write-Host ''
Write-Host "MSI    : $($item.FullName)" -ForegroundColor Green
Write-Host "size   : $([math]::Round($item.Length/1MB,2)) MB"
Write-Host "sha256 : $((Get-FileHash $msi -Algorithm SHA256).Hash)"
if (-not $SkipSigning) {
    $sig = Get-AuthenticodeSignature $msi
    Write-Host "signer : $($sig.SignerCertificate.Subject)"
}
