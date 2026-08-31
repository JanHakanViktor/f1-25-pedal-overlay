# Microsoft Store submission

The production application is the native C#/.NET 10 WPF build under `src/F1TelemetryOverlay.Wpf`. It is packaged as an x64 MSIX with the Windows SDK `makeappx.exe` tool. The package is unsigned locally; Microsoft signs the package during Store ingestion after certification.

The MSIX targets Windows 11, minimum build `10.0.22000.0`, because the native application uses the ordinary-client support target for .NET 10. The separate Inno Setup installer remains the easiest distribution path for direct GitHub downloads.

## Information needed from Partner Center

1. Create or sign in to a Windows developer account at [Partner Center](https://partner.microsoft.com/dashboard).
2. Create a new **MSIX or PWA app** and reserve the product name.
3. Under **Product management → Product identity**, copy these exact values:
   - Package/Identity/Name
   - Package/Identity/Publisher
   - Package/Properties/PublisherDisplayName
4. Choose a four-part numeric package version, for example `1.0.3.0`.
5. Build the package with those values. They are public package metadata, not passwords or API credentials. Never share Partner Center login details.

## Build the package

Set the exact identity values in the current PowerShell session. The values are case-sensitive and must match Partner Center exactly:

```powershell
$env:MSIX_IDENTITY = "value copied from Partner Center"
$env:MSIX_PUBLISHER = "value copied from Partner Center"
$env:MSIX_PUBLISHER_DISPLAY_NAME = "publisher display name"
$env:MSIX_DISPLAY_NAME = "reserved Store product name"
$env:MSIX_VERSION = "1.0.3.0"
powershell -ExecutionPolicy Bypass -File .\scripts\build-native-msix.ps1 -Mode Store
```

The unsigned package is written to `artifacts\msix\x64\F1-25-Telemetry-Overlay.msix`. Store mode fails when any identity value is missing and never creates a placeholder package by accident. For local testing only, use `-Mode Local -AllowPlaceholderIdentity`; do not upload that package to Partner Center.

The build uses the Windows SDK's x64 `makeappx.exe`, preferably version `10.0.19041.0`. The manifest is generated from `packaging\msix\AppxManifest.xml`, and the existing generated artwork under `assets\msix` is included in the package.

## Capability and certification explanation

The package declares `runFullTrust` because this is a classic desktop utility. It needs normal desktop functionality to create an always-on-top transparent window, provide a system-tray menu, register user-configurable global shortcuts, listen for user-enabled F1 telemetry over UDP and save settings locally. It does not request administrator elevation, install a service, modify protected system settings or collect user information.

Paste that explanation into the certification notes if Partner Center asks why the application requires full trust.

## Store listing checklist

- App description and feature list
- At least one clear screenshot of the overlay
- The custom app icon and Store artwork
- Category and age-rating questionnaire
- Support URL: the GitHub issue tracker
- Privacy-policy URL: publish [PRIVACY.md](../PRIVACY.md) from this repository
- Testing notes explaining how reviewers can enable the built-in demo signal with `Control+Shift+D`
- A statement that the application is an independent utility and is not affiliated with or endorsed by Electronic Arts Inc. or Formula One

The built-in demo signal is important because Store reviewers can verify the complete interface without owning or launching the game.
