[CmdletBinding()]
param(
  [string]$DotnetPath = "",
  [string]$InnoCompilerPath = "",
  [string]$Configuration = "Release",
  [string]$Version = "",
  [string]$PublishDirectory = "",
  [string]$OutputDirectory = "",
  [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "publish-native.ps1"
$installerScript = Join-Path $projectRoot "installer\F1TelemetryOverlay.iss"
$buildPropsPath = Join-Path $projectRoot "Directory.Build.props"
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) { $PublishDirectory = Join-Path $projectRoot "artifacts\publish\win-x64" }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot "artifacts\installer" }
if ([string]::IsNullOrWhiteSpace($Version)) {
  $Version = $env:F1_OVERLAY_VERSION
}
if ([string]::IsNullOrWhiteSpace($Version)) {
  if (-not (Test-Path -LiteralPath $buildPropsPath -PathType Leaf)) {
    throw "Directory.Build.props was not found at '$buildPropsPath'; it supplies the native application version."
  }
  try {
    [xml]$buildProps = Get-Content -LiteralPath $buildPropsPath -Raw
    $Version = [string]($buildProps.Project.PropertyGroup.NativeAppVersion | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -First 1)
  } catch {
    throw "Could not read NativeAppVersion from '$buildPropsPath'."
  }
  if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Directory.Build.props does not define a non-empty NativeAppVersion."
  }
}
if ($Version -notmatch "^\d+(\.\d+){1,3}$") { throw "Installer version '$Version' is not a valid dotted numeric version." }

if (-not $SkipPublish) {
  $publishArguments = @{ OutputDirectory = $PublishDirectory; Configuration = $Configuration }
  if ($DotnetPath) { $publishArguments.DotnetPath = $DotnetPath }
  & $publishScript @publishArguments
  if ($LASTEXITCODE -ne 0) { throw "The native publish step failed." }
}
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory "F1-25-Telemetry-Overlay.exe") -PathType Leaf)) {
  throw "The native publish directory does not contain F1-25-Telemetry-Overlay.exe: $PublishDirectory"
}
if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) { throw "Inno Setup script not found: $installerScript" }
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
  $InnoCompilerPath = if ($env:ISCC_PATH) { $env:ISCC_PATH } else { "" }
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
  $candidatePaths = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
  )
  $InnoCompilerPath = $candidatePaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or -not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
  throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 or pass -InnoCompilerPath / set ISCC_PATH.`nDownload: https://jrsoftware.org/isinfo.php"
}

[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$defines = @(
  "/DAppVersion=$Version",
  "/DPublishDir=$PublishDirectory",
  "/DOutputDir=$OutputDirectory",
  "/DProjectRoot=$projectRoot"
)
Write-Host "Building selectable per-user/all-users installer with $InnoCompilerPath"
& $InnoCompilerPath @defines $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

$installerPath = Join-Path $OutputDirectory "F1-25-Telemetry-Overlay-Setup.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
  throw "Inno Setup reported success but the installer was not found: $installerPath"
}
Write-Host ("Installer complete: {0} ({1:N1} MB)" -f $installerPath, ((Get-Item -LiteralPath $installerPath).Length / 1MB))
