# Public freeware releases

The source repository stays private. The public `shirokuuun/Peak-releases` repository holds only distribution documents and release packages. The separate `shirokuuun/peak-website` repository deploys to https://peakforwindows.vercel.app.

Contact configuration: `src/UsageNotch.App/publishing.json`. Blank details are allowed in development without restricting monitoring, but block public packages. Policies version 1.1 are identical in app resources, package files and website content. Never ship credentials, observations, signing keys or debug artifacts.

## Build and package

Use .NET 10 SDK, Windows x64 and Inno Setup. Increment the csproj version for each later release; never overwrite a published version.

```powershell
./scripts/verify.ps1
./scripts/publish.ps1 -PublicRelease -OutputDirectory dist/Peak-1.0.1
./scripts/package-installer.ps1 -Published dist/Peak-1.0.1 -Unsigned
./scripts/package-release.ps1 -Published dist/Peak-1.0.1
```

The first freeware release is explicitly unsigned. The website and user guide explain Windows warnings. For later signed builds, omit `-Unsigned` and pass `-CertificateThumbprint` and optional `-SignTool`. The signing path verifies trusted signatures and timestamps.

The ignored `artifacts/releases` folder contains a versioned installer, complete portable ZIP, SHA256SUMS.txt and release.json. The manifest starts unavailable and unverified. Inspect the package files, runtime, bridges, dependency license texts and policies. Record source commit and tests in release notes. No paid activation service is included.

## Publish and activate downloads

Create a draft release in Peak-releases. Upload the complete packages, hashes, notes and metadata; compare upload digests, then publish. Independently verify anonymous downloads:

```powershell
./scripts/verify-public-downloads.ps1 -WebsiteRoot D:/Languages/codex/peak_website
```

This compares contact details and policy text, downloads both assets without credentials, verifies SHA-256 and writes the generated verified manifest into the website. Commit and push website main. Vercel uses `npm run build:public`, which rejects incomplete release metadata. Verify production buttons, contact, indexing and canonical URLs. No API token or payment flow exists in the website.

## Verification limits

Automated tests cover provider defaults, explicit demo selection, independent free access, safe contact links, privacy retention and safe restoration/disconnection. Legacy license.dat is ignored and preserved until explicit complete deletion. No merchant requests occur.

Package construction and development-PC startup do not establish clean-machine compatibility. Separately test fresh Windows 10/11 installs, updates, uninstall with retained/deleted preferences, custom provider paths, mixed-DPI monitors, sleep/lock and live quotas after owner sign-in and hook trust. Record these results accurately. The owner manages mailbox verification, two-step verification and the stated support retention practice.
