# Installing Kitsune7Den

## Download

Grab the latest `Kitsune7Den.exe` from [Releases](https://github.com/Kitsune-Den/Kitsune7Den/releases/latest).

Requires **Windows 10 or 11 x64**. No installer, no dependencies, no runtime needed — it's a single self-contained exe.

## Verify the download (optional but recommended)

Every release ships with a `Kitsune7Den.exe.sha256` file containing the SHA-256 hash of the exe. You can verify you got the exact file we published:

```powershell
# In the folder where you downloaded both files:
Get-FileHash .\Kitsune7Den.exe -Algorithm SHA256
Get-Content .\Kitsune7Den.exe.sha256
```

The two hashes should match. If they don't, don't run it — download again or report it.

## Check the signature

From v1.1.0 on, `Kitsune7Den.exe` is code-signed through Microsoft's Artifact Signing service. To see it, right-click the exe → **Properties** → **Digital Signatures**, or in PowerShell:

```powershell
Get-AuthenticodeSignature .\Kitsune7Den.exe | Format-List Status, SignerCertificate
```

`Status` should be `Valid`, and the certificate's issuer should be a **Microsoft ID Verified CS** certificate authority. If the signature is missing or invalid, don't run it.

## First run: the SmartScreen warning

Even signed, a new release can still get a blue SmartScreen dialog the first time you run it, while Microsoft's reputation system gets to know the signature:

> **Windows protected your PC**
>
> Microsoft Defender SmartScreen prevented an unrecognized app from starting. Running this app might put your PC at risk.

**This doesn't mean the app is malicious.** It means the signature hasn't built up enough download history yet. The warning goes away as more people run signed releases. Builds older than v1.1.0 aren't signed at all, so they always show it.

**To run it anyway:**

1. Click **More info** (the small link under the message)
2. Click **Run anyway**

You only have to do this once per downloaded version. After that Windows remembers your decision.

If you want to be extra careful before clicking "Run anyway":

- **Check the signature** is valid (see above)
- **Verify the SHA-256** matches the value in the release (see above)
- **Read the source** — everything Kitsune7Den does is in [this repo](https://github.com/Kitsune-Den/Kitsune7Den)
- **Build it yourself** — see [Building from Source](README.md#building-from-source) in the README

## After the first launch

Kitsune7Den stores its config in `%LocalAppData%\Kitsune7Den\settings.json`. Backups go under your server directory in a `Kitsune7Den-Backups` subfolder.

From v1.0.2 onwards the app can update itself — go to **Settings → Check for Updates** and it'll pull the latest release straight from GitHub. No need to re-download from the web once you're in.

## Uninstalling

Delete `Kitsune7Den.exe` and (optionally) the config folder at `%LocalAppData%\Kitsune7Den`. That's it. No registry keys, no leftover services.
