# Kitsune7Den release build helper
#
# Builds a single-file, self-contained Release exe, code-signs it with Azure
# Artifact Signing, and writes a SHA-256 checksum of the SIGNED exe. Run from
# the repo root:
#
#   .\scripts\publish-release.ps1             # build + sign (fails if it can't sign)
#   .\scripts\publish-release.ps1 -SkipSign   # build an unsigned exe on purpose
#
# Outputs:
#   publish\Kitsune7Den.exe
#   publish\Kitsune7Den.exe.sha256
#
# Signing
# -------
# Uses Microsoft's Artifact Signing (formerly Trusted Signing) through signtool and
# Microsoft's signing plug-in (the Microsoft.ArtifactSigning.Client NuGet package).
# Credentials, in order:
#   * A service principal from AZURE_CLIENT_ID / AZURE_CLIENT_SECRET / AZURE_TENANT_ID
#     (for CI), or
#   * Your Azure CLI login (`az login`), which needs the "Artifact Signing Certificate
#     Profile Signer" role on the signing account.
# Account and profile default to the PackRelay signing account and can be overridden
# with the same variables packrelay-launcher uses: AZURE_ENDPOINT,
# AZURE_CODE_SIGNING_ACCOUNT_NAME, AZURE_CERT_PROFILE_NAME.
#
# The plug-in is downloaded once (pinned version, checked against nuget.org's
# published SHA-512 and Microsoft's Authenticode signature) and cached under
# %LOCALAPPDATA%\Kitsune7Den-build.

[CmdletBinding()]
param(
    [string]$OutputDir = "publish",
    [switch]$SkipSign
)

$ErrorActionPreference = "Stop"

# ── Signing configuration ──
$SigningEndpoint = if ($env:AZURE_ENDPOINT) { $env:AZURE_ENDPOINT } else { "https://eus.codesigning.azure.net" }
$SigningAccount  = if ($env:AZURE_CODE_SIGNING_ACCOUNT_NAME) { $env:AZURE_CODE_SIGNING_ACCOUNT_NAME } else { "packrelay" }
$SigningProfile  = if ($env:AZURE_CERT_PROFILE_NAME) { $env:AZURE_CERT_PROFILE_NAME } else { "packrelay-prod" }
$TimestampUrl    = "http://timestamp.acs.microsoft.com"

# Pinned signing plug-in. To move to a newer version, update both values from
# https://api.nuget.org/v3/registration5-gz-semver2/microsoft.artifactsigning.client/index.json
# (catalog entry -> packageHash).
$ClientVersion     = "1.0.128"
$ClientPackageHash = "mPBqaR9Pwvoi8Z3PhVZzPphgf775GjEsRTubB5jMkIja4KyzbjibVSoRtNIyAyR4W4VBwrUQkackwFvF31y/lQ=="

function Find-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $found = Get-ChildItem $kits -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq "x64" } |
        Sort-Object {
            # Newest SDK first; older SDKs keep it at bin\x64 with no version folder
            $v = $null
            if ([version]::TryParse($_.Directory.Parent.Name, [ref]$v)) { $v } else { [version]"0.0" }
        } -Descending |
        Select-Object -First 1
    if (-not $found) {
        throw "signtool.exe not found. Install the Windows SDK (Signing Tools for Desktop Apps), or run with -SkipSign."
    }
    return $found.FullName
}

function Get-SigningPlugin {
    $root = Join-Path $env:LOCALAPPDATA "Kitsune7Den-build\artifact-signing-client\$ClientVersion"
    $dlib = Join-Path $root "bin\x64\Azure.CodeSigning.Dlib.dll"

    if (-not (Test-Path $dlib)) {
        Write-Host "Downloading Microsoft.ArtifactSigning.Client $ClientVersion..." -ForegroundColor Cyan
        New-Item -ItemType Directory -Force $root | Out-Null
        $nupkg = Join-Path $root "client.nupkg"
        $url = "https://api.nuget.org/v3-flatcontainer/microsoft.artifactsigning.client/$ClientVersion/microsoft.artifactsigning.client.$ClientVersion.nupkg"
        Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing

        $sha = [System.Security.Cryptography.SHA512]::Create()
        $stream = [System.IO.File]::OpenRead($nupkg)
        try { $actual = [Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $stream.Dispose() }
        if ($actual -ne $ClientPackageHash) {
            Remove-Item -Recurse -Force $root
            throw "Signing plug-in download doesn't match nuget.org's published hash. Not using it."
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $root)
        Remove-Item $nupkg
    }

    # Checked on every run, not just after download, in case the cache was tampered with
    $sig = Get-AuthenticodeSignature $dlib
    if ($sig.Status -ne "Valid" -or $sig.SignerCertificate.Subject -notmatch "O=Microsoft Corporation") {
        throw "Signing plug-in at $dlib isn't validly signed by Microsoft (status: $($sig.Status)). Delete $root and try again."
    }
    return $dlib
}

function New-SigningMetadata {
    # Only the credential we mean to use, so the plug-in doesn't wander through
    # Visual Studio / browser logins looking for one.
    $exclude = @(
        "WorkloadIdentityCredential", "ManagedIdentityCredential", "SharedTokenCacheCredential",
        "VisualStudioCredential", "VisualStudioCodeCredential", "AzurePowerShellCredential",
        "AzureDeveloperCliCredential", "InteractiveBrowserCredential"
    )
    if ($env:AZURE_CLIENT_SECRET) {
        $exclude += "AzureCliCredential"     # service principal from the environment
    } else {
        $exclude += "EnvironmentCredential"  # your az login
        if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
            throw "Azure CLI (az) not found and no AZURE_CLIENT_SECRET set. Install the Azure CLI and run 'az login', or run with -SkipSign."
        }
        # Native command: don't let its stderr trip $ErrorActionPreference = Stop
        $previous = $ErrorActionPreference; $ErrorActionPreference = "Continue"
        az account show --output none 2>$null
        $loggedIn = $LASTEXITCODE -eq 0
        $ErrorActionPreference = $previous
        if (-not $loggedIn) {
            throw "Not logged in to Azure. Run 'az login' first, or run with -SkipSign."
        }
    }

    $path = Join-Path ([System.IO.Path]::GetTempPath()) "kitsune7den-signing-$([guid]::NewGuid().ToString('N')).json"
    $json = @{
        Endpoint               = $SigningEndpoint
        CodeSigningAccountName = $SigningAccount
        CertificateProfileName = $SigningProfile
        ExcludeCredentials     = $exclude
    } | ConvertTo-Json
    # No byte-order mark: Windows PowerShell's "-Encoding UTF8" adds one, and the
    # signing plug-in fails with "An unexpected internal error" on it
    [System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding $false))
    return $path
}

function Invoke-Sign([string]$file) {
    $signtool = Find-SignTool
    $dlib = Get-SigningPlugin
    $metadata = New-SigningMetadata
    try {
        Write-Host "Signing with Artifact Signing ($SigningAccount / $SigningProfile)..." -ForegroundColor Cyan
        $previous = $ErrorActionPreference; $ErrorActionPreference = "Continue"
        & $signtool sign /fd SHA256 /tr $TimestampUrl /td SHA256 /dlib $dlib /dmdf $metadata $file
        $code = $LASTEXITCODE
        $ErrorActionPreference = $previous
        if ($code -ne 0) { throw "signtool sign failed (exit $code)." }
    }
    finally {
        Remove-Item $metadata -ErrorAction SilentlyContinue
    }

    # Trust the result only after an independent check
    $previous = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    & $signtool verify /pa /tw $file | Out-Null
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous
    $sig = Get-AuthenticodeSignature $file
    if ($code -ne 0 -or $sig.Status -ne "Valid" -or -not $sig.TimeStamperCertificate) {
        throw "Signature check failed after signing (signtool verify exit $code, status $($sig.Status))."
    }
    return $sig
}

# ── Build ──

# Kill any running instance so the build can overwrite the exe
Get-Process Kitsune7Den -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Clean previous build output, but keep anything else in publish\ (e.g. publish\nexus upload files)
if (Test-Path $OutputDir) {
    Get-ChildItem $OutputDir -File | Remove-Item -Force
}

Write-Host "Building Release exe..." -ForegroundColor Cyan
dotnet publish src/Kitsune7Den `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $OutputDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed (exit $LASTEXITCODE)"
    exit 1
}

$exePath = Join-Path $OutputDir "Kitsune7Den.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Build did not produce $exePath"
    exit 1
}

# ── Sign (before hashing: the checksum must cover the signed file) ──
$signer = $null
if ($SkipSign) {
    Write-Warning "Skipping code signing (-SkipSign). This exe is NOT signed; don't publish it as a release."
} else {
    try {
        $signer = (Invoke-Sign $exePath).SignerCertificate.Subject
    } catch {
        Write-Error "Code signing failed: $($_.Exception.Message)`nThe exe was built but is unsigned. Fix the above, or run with -SkipSign to build unsigned on purpose."
        exit 1
    }
}

# ── Checksum ──
Write-Host "Generating SHA-256..." -ForegroundColor Cyan
$hash = (Get-FileHash $exePath -Algorithm SHA256).Hash.ToLower()
$shaFile = "$exePath.sha256"
"$hash  Kitsune7Den.exe`n" | Out-File $shaFile -Encoding ASCII -NoNewline

# ── Report ──
$size = (Get-Item $exePath).Length
$sizeMB = [math]::Round($size / 1MB, 1)
$version = (Get-Item $exePath).VersionInfo.ProductVersion
Write-Host ""
Write-Host "Build complete" -ForegroundColor Green
Write-Host "  $exePath ($sizeMB MB, $version)"
Write-Host "  $shaFile"
Write-Host "  SHA256: $hash"
if ($signer) {
    Write-Host "  Signed: $signer" -ForegroundColor Green
} else {
    Write-Host "  Signed: NO" -ForegroundColor Yellow
}
