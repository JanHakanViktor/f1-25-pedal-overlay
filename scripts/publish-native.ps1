[CmdletBinding()]
param(
  [string]$DotnetPath = "",
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$OutputDirectory = "",
  [string]$NuGetSource = "https://api.nuget.org/v3/index.json",
  [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$projectPath = Join-Path $projectRoot "src\F1TelemetryOverlay.Wpf\F1TelemetryOverlay.Wpf.csproj"
if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
  $DotnetPath = if ($env:F1_DOTNET_PATH) { $env:F1_DOTNET_PATH } else { "C:\Users\unthz\AppData\Local\Programs\dotnet\dotnet.exe" }
}
if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
  $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
  if ($dotnetCommand) { $DotnetPath = $dotnetCommand.Source }
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
  $OutputDirectory = Join-Path $projectRoot "artifacts\publish\$Runtime"
}

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
    throw "$Label '$candidate' must be a descendant of '$artifactsRoot'. External or workspace-root output paths are refused."
  }

  # Do not recursively remove through a junction/symlink below the trusted root.
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

$OutputDirectory = Get-SafeArtifactChildPath -CandidatePath $OutputDirectory -ArtifactsRootPath $artifactsRoot -Label "Publish output directory"

if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
  throw "The .NET SDK executable was not found at '$DotnetPath'. Install .NET 10 SDK or pass -DotnetPath.`nSee https://dotnet.microsoft.com/download/dotnet/10.0"
}
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
  throw "The native WPF project was not found at '$projectPath'."
}
if ($Runtime -ne "win-x64") {
  throw "The production overlay currently supports only the x64 Windows runtime (win-x64)."
}

$outputParent = Split-Path -Parent $OutputDirectory
[System.IO.Directory]::CreateDirectory($outputParent) | Out-Null
if (Test-Path -LiteralPath $OutputDirectory) {
  Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

$publishArguments = @(
  "publish",
  $projectPath,
  "--configuration", $Configuration,
  "--framework", "net10.0-windows",
  "--runtime", $Runtime,
  "--self-contained", "true",
  "--output", $OutputDirectory,
  "-p:Platform=x64",
  "-p:PlatformTarget=x64",
  "-p:PublishSingleFile=false",
  "-p:PublishTrimmed=false",
  "-p:DebugSymbols=false",
  "-p:DebugType=None"
)
if ($NoRestore) { $publishArguments += "--no-restore" }
if (-not [string]::IsNullOrWhiteSpace($NuGetSource) -and -not $NoRestore) {
  $publishArguments += @("--source", $NuGetSource)
}

Write-Host "Publishing native WPF overlay to $OutputDirectory"
& $DotnetPath @publishArguments
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE. Resolve the build errors before packaging."
}

$executable = Join-Path $OutputDirectory "F1-25-Telemetry-Overlay.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
  throw "The publish completed but the expected executable was not produced: $executable"
}

$fileCount = @(Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse).Count
$sizeBytes = (Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse | Measure-Object -Property Length -Sum).Sum
Write-Host ("Native publish complete: {0} files, {1:N1} MB" -f $fileCount, ($sizeBytes / 1MB))
