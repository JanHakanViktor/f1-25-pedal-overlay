[CmdletBinding()]
param(
  [ValidateSet("Store", "Local")]
  [string]$Mode = "Store",
  [string]$MakeAppxPath = "",
  [string]$PublishDirectory = "",
  [string]$OutputDirectory = "",
  [string]$IdentityName = "",
  [string]$Publisher = "",
  [string]$PublisherDisplayName = "",
  [string]$DisplayName = "",
  [string]$Version = "",
  [switch]$AllowPlaceholderIdentity,
  [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$buildPropsPath = Join-Path $projectRoot "Directory.Build.props"
$publishScript = Join-Path $PSScriptRoot "publish-native.ps1"
$manifestTemplate = Join-Path $projectRoot "packaging\msix\AppxManifest.xml"
$assetSource = Join-Path $projectRoot "assets\msix"
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) { $PublishDirectory = Join-Path $projectRoot "artifacts\publish\win-x64" }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot "artifacts\msix\x64" }
$stagingDirectory = Join-Path $projectRoot "artifacts\msix\staging\x64"

function Get-SafeArtifactChildPath {
  param(
    [Parameter(Mandatory = $true)][string]$CandidatePath,
    [Parameter(Mandatory = $true)][string]$ArtifactsRootPath,
    [Parameter(Mandatory = $true)][string]$Label
  )

  if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
    throw "$Label cannot be empty."
  }
  try {
    $candidateInput = if ([System.IO.Path]::IsPathRooted($CandidatePath)) {
      $CandidatePath
    } else {
      Join-Path (Get-Location).Path $CandidatePath
    }
    $candidate = [System.IO.Path]::GetFullPath($candidateInput).TrimEnd('\', '/')
    $artifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsRootPath).TrimEnd('\', '/')
  } catch {
    throw "$Label is not a valid path: '$CandidatePath'."
  }

  if ([string]::IsNullOrWhiteSpace($candidate) -or
      [System.IO.Path]::GetPathRoot($candidate).TrimEnd('\', '/').Equals($candidate, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label must not be a drive or filesystem root: '$candidate'."
  }
  if ($candidate.Equals($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label must be a child directory below '$artifactsRoot', not the artifacts root itself."
  }
  $requiredPrefix = "$artifactsRoot\"
  if (-not $candidate.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label '$candidate' must be a descendant of '$artifactsRoot'. External or workspace-root paths are refused."
  }

  $cursor = $candidate
  while ($true) {
    if (Test-Path -LiteralPath $cursor -PathType Any) {
      $item = Get-Item -LiteralPath $cursor -Force
      if ($cursor.Equals($candidate, [System.StringComparison]::OrdinalIgnoreCase) -and -not $item.PSIsContainer) {
        throw "$Label exists as a file, not a directory: '$candidate'."
      }
      if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label '$candidate' traverses a reparse point at '$cursor', so cleanup was refused."
      }
    }
    if ($cursor.Equals($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) { break }
    $parent = Split-Path -Parent $cursor
    if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($cursor, [System.StringComparison]::OrdinalIgnoreCase)) {
      throw "Could not prove that $Label '$candidate' is safely below '$artifactsRoot'."
    }
    $cursor = $parent.TrimEnd('\', '/')
  }
  return $candidate
}

$stagingDirectory = Get-SafeArtifactChildPath -CandidatePath $stagingDirectory -ArtifactsRootPath $artifactsRoot -Label "MSIX staging directory"

function Get-NativeBuildProperty {
  param(
    [Parameter(Mandatory = $true)][string]$PropertyName
  )

  if (-not (Test-Path -LiteralPath $buildPropsPath -PathType Leaf)) {
    throw "Directory.Build.props was not found at '$buildPropsPath'; it supplies native package defaults."
  }
  try {
    [xml]$buildProps = Get-Content -LiteralPath $buildPropsPath -Raw
    $value = [string]($buildProps.Project.PropertyGroup |
      ForEach-Object { $_.$PropertyName } |
      Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
      Select-Object -First 1)
  } catch {
    throw "Could not read $PropertyName from '$buildPropsPath'."
  }
  if ([string]::IsNullOrWhiteSpace($value)) {
    throw "Directory.Build.props does not define a non-empty $PropertyName."
  }
  return $value
}

if (-not $SkipPublish) {
  & $publishScript -OutputDirectory $PublishDirectory
  if ($LASTEXITCODE -ne 0) { throw "The native publish step failed." }
}
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory "F1-25-Telemetry-Overlay.exe") -PathType Leaf)) {
  throw "The native publish directory does not contain F1-25-Telemetry-Overlay.exe: $PublishDirectory"
}
if (-not (Test-Path -LiteralPath $manifestTemplate -PathType Leaf)) { throw "MSIX manifest template not found: $manifestTemplate" }
if (-not (Test-Path -LiteralPath $assetSource -PathType Container)) { throw "MSIX image assets not found: $assetSource" }

if ([string]::IsNullOrWhiteSpace($MakeAppxPath)) { $MakeAppxPath = $env:MAKEAPPX_PATH }
if ([string]::IsNullOrWhiteSpace($MakeAppxPath)) {
  $preferred = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\makeappx.exe"
  if (Test-Path -LiteralPath $preferred -PathType Leaf) {
    $MakeAppxPath = $preferred
  } else {
    $kitRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    $MakeAppxPath = Get-ChildItem -LiteralPath $kitRoot -Filter makeappx.exe -File -Recurse -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match "\\x64\\makeappx\.exe$" } |
      Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName -First 1
  }
}
if ([string]::IsNullOrWhiteSpace($MakeAppxPath) -or -not (Test-Path -LiteralPath $MakeAppxPath -PathType Leaf)) {
  throw "Windows SDK makeappx.exe was not found. Install the Windows 10 SDK or pass -MakeAppxPath / set MAKEAPPX_PATH. The preferred SDK is 10.0.19041.0 x64."
}

if ([string]::IsNullOrWhiteSpace($IdentityName)) { $IdentityName = $env:MSIX_IDENTITY }
if ([string]::IsNullOrWhiteSpace($Publisher)) { $Publisher = $env:MSIX_PUBLISHER }
if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) { $PublisherDisplayName = $env:MSIX_PUBLISHER_DISPLAY_NAME }
if ([string]::IsNullOrWhiteSpace($DisplayName)) { $DisplayName = $env:MSIX_DISPLAY_NAME }
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $env:MSIX_VERSION }

$hasMissingIdentity = [string]::IsNullOrWhiteSpace($IdentityName) -or
  [string]::IsNullOrWhiteSpace($Publisher) -or
  [string]::IsNullOrWhiteSpace($PublisherDisplayName) -or
  [string]::IsNullOrWhiteSpace($DisplayName) -or
  [string]::IsNullOrWhiteSpace($Version)
if ($hasMissingIdentity) {
  if ($Mode -eq "Store") {
    throw "Store MSIX builds require exact Partner Center values: MSIX_IDENTITY, MSIX_PUBLISHER, MSIX_PUBLISHER_DISPLAY_NAME, MSIX_DISPLAY_NAME and MSIX_VERSION. No placeholder identity is allowed in Store mode."
  }
  if (-not $AllowPlaceholderIdentity) {
    throw "Local MSIX builds have no identity values. Re-run with -AllowPlaceholderIdentity to create a clearly local-only package, or provide the MSIX_* values."
  }
  if ([string]::IsNullOrWhiteSpace($IdentityName)) { $IdentityName = "F1TelemetryOverlay.Dev" }
  if ([string]::IsNullOrWhiteSpace($Publisher)) { $Publisher = "CN=F1TelemetryOverlayDev" }
  if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) { $PublisherDisplayName = "F1 Telemetry Overlay (Development)" }
  if ([string]::IsNullOrWhiteSpace($DisplayName)) { $DisplayName = "F1 25 Telemetry Overlay (Development)" }
  if ([string]::IsNullOrWhiteSpace($Version)) { $Version = Get-NativeBuildProperty -PropertyName "NativeMsixVersion" }
}

if ($IdentityName -notmatch "^[A-Za-z0-9.\-]{3,50}$") { throw "MSIX identity '$IdentityName' is invalid. Use the exact Partner Center identity name (letters, digits, dots and hyphens)." }
if ($Publisher -notmatch "^CN=[^,]+$") { throw "MSIX publisher '$Publisher' is invalid. Use the exact Partner Center publisher value, for example CN=... ." }
if ($Version -notmatch "^\d+\.\d+\.\d+\.\d+$") { throw "MSIX version '$Version' must contain four numeric components, for example 1.0.3.0." }
if ($Version -notmatch "^([1-9]\d*)\.\d+\.\d+\.0$") { throw "MSIX version '$Version' must start at 1 or greater and end in .0 for Store submission." }

if (Test-Path -LiteralPath $stagingDirectory) { Remove-Item -LiteralPath $stagingDirectory -Recurse -Force }
[System.IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
Copy-Item -Path (Join-Path $PublishDirectory "*") -Destination $stagingDirectory -Recurse -Force
$stagingAssets = Join-Path $stagingDirectory "assets"
[System.IO.Directory]::CreateDirectory($stagingAssets) | Out-Null
Copy-Item -Path (Join-Path $assetSource "*") -Destination $stagingAssets -Recurse -Force

$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$replacements = @{
  "__IDENTITY_NAME__" = [System.Security.SecurityElement]::Escape($IdentityName)
  "__PUBLISHER__" = [System.Security.SecurityElement]::Escape($Publisher)
  "__VERSION__" = [System.Security.SecurityElement]::Escape($Version)
  "__DISPLAY_NAME__" = [System.Security.SecurityElement]::Escape($DisplayName)
  "__PUBLISHER_DISPLAY_NAME__" = [System.Security.SecurityElement]::Escape($PublisherDisplayName)
}
foreach ($placeholder in $replacements.Keys) {
  $manifest = $manifest.Replace($placeholder, $replacements[$placeholder])
}
$manifestPath = Join-Path $stagingDirectory "AppxManifest.xml"
Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8

[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$packagePath = Join-Path $OutputDirectory "F1-25-Telemetry-Overlay.msix"
if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
Write-Host ("Packing unsigned {0} MSIX with {1}" -f $Mode.ToLowerInvariant(), $MakeAppxPath)
& $MakeAppxPath pack /d $stagingDirectory /p $packagePath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "makeappx reported success but the MSIX was not found: $packagePath" }
Write-Host ("MSIX complete: {0} ({1:N1} MB)" -f $packagePath, ((Get-Item -LiteralPath $packagePath).Length / 1MB))
if ($Mode -eq "Local") { Write-Warning "This package uses a development or caller-supplied identity and is not a Store upload until Partner Center identity values are used." }
