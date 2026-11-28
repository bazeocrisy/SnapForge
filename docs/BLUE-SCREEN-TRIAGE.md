# "Blue screen" while testing SnapForge: triage (DEF-RC3-07, open)

**Status:** open. The owner reports a blue screen during RC3 testing, but what triggers it is not confirmed yet. "Blue screen" can mean three very different things, and each needs a different fix. This issue stays open until the symptom is identified and the outcome is verified.

## Which one is it?

| | A. SmartScreen when starting Setup | B. Blue or blank picture in SnapForge or in a recording | C. Windows stop error (the PC restarts) |
|---|---|---|---|
| When it appears | Right after double-clicking `SnapForge-…-setup.exe` | While using SnapForge, or when playing a saved recording | At any time; the PC then restarts by itself |
| What it looks like | A blue window titled **"Windows protected your PC"** that says Microsoft Defender SmartScreen prevented an unrecognized app from starting. The rest of the desktop is still there. | A SnapForge window, or the video picture, is solid blue, black or empty. Windows keeps running. | The whole screen turns blue with a sad face and **"Your device ran into a problem and needs to restart"**, plus a **stop code** such as `VIDEO_TDR_FAILURE`. |
| Cause | The test installer is **not code-signed**. SmartScreen does this for every unsigned program it has not seen often. It is not an error in SnapForge. | A SnapForge or graphics-driver issue in a specific workflow | A driver or hardware fault in Windows. An ordinary app like SnapForge cannot cause one on its own, but it can trigger a faulty graphics, camera or audio driver. |
| What we need | Which file was started, and whether it was the CI package with the published SHA-256 | Which window or recording, what you did just before, and a photo or screenshot | The **stop code** and the time it happened |

**The one detail still missing:** which of A, B or C you saw. A phone photo of the screen is the quickest way to tell. For C, the stop code is printed at the bottom of the blue screen.

## Collect diagnostics (all three cases, read-only)

Run this in PowerShell as yourself (no administrator rights needed):

```
powershell -ExecutionPolicy Bypass -File tools\diagnostics\collect-snapforge-diagnostics.ps1 -Setup "<path to the SnapForge setup .exe you started>"
```

It writes one text file to your Desktop and uploads nothing. It contains:

* Windows version and build;
* the installed SnapForge programs, their versions and signing status;
* the Setup file's SHA-256, signing status and "downloaded from the internet" mark;
* **stop errors and unexpected restarts from the last 30 days, with stop codes** (System log events 1001/BugCheck, 41 and 6008);
* the names and sizes of crash dumps (the dumps are not copied);
* SnapForge and .NET application errors;
* SnapForge's own error logs and recording log;
* graphics adapters and driver versions.

Read the file before sharing it: it contains your PC name, user name and folder paths.

## Current evidence per case

**A. SmartScreen.** All CI test builds are **unsigned** (`Get-AuthenticodeSignature` reports `NotSigned`), and the website and PR say so. SmartScreen's blue window on an unsigned installer is expected Windows behaviour. It only goes away once the installer is signed with a trusted certificate and the signature builds reputation.

What signing still needs is in [SIGNING.md](SIGNING.md). In short:

* **Signing route (owner decision):** choose SignPath Foundation (free for open source, needs an application), Microsoft Artifact Signing (paid, needs identity verification) or an OV certificate (paid).
* **Accounts and signing:** the owner sets up the account and identity, then signs both programs and the installer in CI with timestamping.
* **Website:** record the certificate subject and thumbprint in the site's release config.

Nothing is self-signed. This project does not give instructions for bypassing SmartScreen.

**B. App or recording.** CI checks for this on every build:

* the recording self-tests decode real recordings and check their colours;
* the camera overlay and preview pixels are checked;
* the "not recorded" checks confirm that the Record panel, preview and controls never appear in the video.

All of these passed in RC3 CI run 159. Known causes of a blank or black picture on real PCs that CI cannot reproduce:

* protected content (some video players and DRM pages are captured as black by Windows itself);
* a graphics-driver reset during recording;
* the hardware video encoder of some graphics drivers.

The diagnostics file shows the driver, and the recording log shows which encoder was used and whether it fell back to the software encoder.

**C. Stop error.** Not reproducible in CI: CI runs in virtual machines without a real camera, microphone or graphics hardware. The stop code and the BugCheck event (which names the faulting module) are needed before any cause can be named.

## Next steps

1. Owner: report A, B or C, ideally with a photo, and run the diagnostics script.
2. **A:** confirm the file's SHA-256 matches the PR or website. The fix is code signing, which is an owner decision (SIGNING.md).
3. **B:** reproduce with the steps given, fix it, and verify the fix in the packaged Windows build (CI self-test plus owner re-test).
4. **C:** analyse the stop code and faulting module from the BugCheck event. Usually the fix is a driver update; check whether a SnapForge feature (camera, hardware encoder, screen capture) triggers it, and offer a safe fallback if so.
