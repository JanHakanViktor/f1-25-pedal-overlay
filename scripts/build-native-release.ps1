[CmdletBinding()]
param(
  [string]$DotnetPath = "",
  [string]$InnoCompilerPath = "",
  [string]$MakeAppxPath = "",
  [string]$Version = "",
  [switch]$StoreMsix,
  [switch]$AllowPlaceholderIdentity
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot "artifacts\publish\win-x64"
$installerArguments = @{ PublishDirectory = $publishDirectory; SkipPublish = $true }
if ($DotnetPath) { $installerArguments.DotnetPath = $DotnetPath }
if ($InnoCompilerPath) { $installerArguments.InnoCompilerPath = $InnoCompilerPath }
if ($Version) { $installerArguments.Version = $Version }

& (Join-Path $PSScriptRoot "publish-native.ps1") -DotnetPath $DotnetPath -OutputDirectory $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "The native publish step failed." }
& (Join-Path $PSScriptRoot "build-native-installer.ps1") @installerArguments
if ($LASTEXITCODE -ne 0) { throw "The native installer step failed." }

$msixArguments = @{ PublishDirectory = $publishDirectory; SkipPublish = $true }
if ($MakeAppxPath) { $msixArguments.MakeAppxPath = $MakeAppxPath }
if ($StoreMsix) {
  $msixArguments.Mode = "Store"
} else {
  $msixArguments.Mode = "Local"
  if ($AllowPlaceholderIdentity) { $msixArguments.AllowPlaceholderIdentity = $true }
}
& (Join-Path $PSScriptRoot "build-native-msix.ps1") @msixArguments
if ($LASTEXITCODE -ne 0) { throw "The native MSIX step failed." }

Write-Host "Native release artifacts are ready under artifacts\installer and artifacts\msix\x64."
