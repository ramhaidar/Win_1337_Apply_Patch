# Release signing and antivirus false-positive review

**Current status:** the patcher binaries are unsigned. No signing enrollment, certificate acquisition, antivirus submission or issue comment has been made as part of this work. The manual tagged draft-release pipeline has passed local verification; hosted Actions execution, an official dispatch and a completed tag creation remain deferred. Assembly version is `2.4.0.0`.

Use the [release/rebuild instructions](../README.md#manual-tagged-draft-releases) to establish the exact source, build run and SHA-256 before distribution or vendor review. This utility intentionally rewrites executable files. Vendors may classify it as a patcher/HackTool even without malicious behavior; neither signing nor a low VirusTotal detection count proves safety.

## Authenticode options

### Open-source sponsorship: investigate eligibility first

[SignPath Foundation](https://signpath.org/) offers free signing for qualifying open-source projects. Its [published conditions](https://signpath.org/terms.html) require maintained, released, documented open-source software, verifiable builds, manual signing approval, contributor MFA, assigned reviewers/approvers and a published signing/privacy policy. Acceptance is discretionary, not an entitlement.

**This project must not assume eligibility.** The conditions include a “No hacking tools” clause covering features designed to circumvent security measures. DLL patching, ownership changes and PE certificate removal need candid disclosure; the project's fork relationship and upstream-signing rules also need review. Only the provider can decide whether this project qualifies. Do not rename or hide those functions to obtain approval. The terms page currently labels its code of conduct a draft, so recheck the current policy before applying.

If approved later, follow the provider's exact build-origin and manual-approval requirements. Do not publish “signing provided by SignPath” or name it as this project's publisher before approval and actual signature verification. A signing program must not be used to re-sign Microsoft runtime libraries or third-party target DLLs as if they were this project's own code.

### Alternatives and limits

Microsoft's [Windows code-signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) describe Azure Artifact Signing (formerly Trusted Signing) and CA-issued OV/EV certificates. These are paid, identity-validated options with eligibility and key-storage requirements, not promised free sponsorship. Check current geographic and legal-identity requirements rather than assuming a maintainer qualifies.

Self-signed certificates are for controlled testing or managed enterprise trust, not a public Windows trust solution. Do not ask users to install an arbitrary root certificate to run this tool. Microsoft Store MSIX signing requires a different distribution/certification path and is not implemented by this ZIP pipeline. Signing does not guarantee SmartScreen reputation or removal of AV detections; SmartScreen download reputation and Defender Antivirus verdicts are different mechanisms.

## Requirements for any future signing integration

This is a design checklist, **not a description of the current workflow**:

1. Approve a provider and stable publisher identity; protect signing access with MFA, restricted approvers and provider-managed secure key storage. Never place a PFX/private key or its password in the repository or build logs.
2. Sign only the project's own validated build outputs, using SHA-256 file digests and an approved RFC 3161 timestamp service with SHA-256 timestamp digests. Follow the provider's signing interface; no enrollment or signing command is supplied here for automatic execution.
3. Verify the signature, chain, expected publisher and timestamp before packaging. Preserve third-party runtime signatures. Signing modifies bytes, so package and calculate final SHA-256 **after** signing; record both unsigned-build and signed-release identities separately.
4. Promote exactly the verified signed artifacts, without a rebuild. Extend the current unsigned provenance/schema and fake-boundary tests through a separately approved change, rather than attaching signed replacements to existing hashes.
5. Document what is reproducible: deterministic unsigned build payloads versus final timestamped signatures. Do not promise byte-identical signed ZIPs across signing operations. Never patch the running patcher or treat its target-PE certificate removal as a release-signing step.

Read-only signature inspection on a trusted, extracted executable:

```powershell
$signature = Get-AuthenticodeSignature -LiteralPath '.\Win_1337_Patch.exe'
$signature | Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

The current executable is unsigned; `NotSigned` is expected, not a successful signature check. For a future signed release, require `Valid` and independently match the publisher/certificate details to the approved policy. If Windows SDK SignTool is available, `signtool verify /pa /all /v Win_1337_Patch.exe` provides an additional Authenticode-policy check. These commands do not execute the patcher. See [Get-AuthenticodeSignature](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/get-authenticodesignature) and [SignTool](https://learn.microsoft.com/en-us/dotnet/framework/tools/signtool-exe).

## Microsoft Defender: submit the exact detected release

Only a maintainer-authorized official release should be submitted. Do not submit local snapshot/checkpoint binaries as official releases, proprietary patched targets, private `.1337` files or secrets. Submission uploads disclose the sample to the vendor; review its current terms and obtain authorization first.

1. Identify the exact detected file from the official CI-built release. Verify its ZIP against `SHA256SUMS`, extract a disposable copy, and record the detected executable/DLL's own SHA-256 with `Get-FileHash -Algorithm SHA256`. ZIP and executable hashes are different; record both with the selected tag/source SHA, build-run URL, signature status, detection name and security-intelligence version.
2. Open the official [Microsoft Security Intelligence submission portal](https://www.microsoft.com/en-us/wdsi/filesubmission?persona=SoftwareDeveloper), sign in, select the **software developer** role, and identify an incorrect detection of your software. Portal wording can change; do not select an employee-only or incident-response route merely because it is visible.
3. Upload that exact detected release file and describe the legitimate behavior transparently: expected-byte validation, backups, in-place binary rewriting, explicit one-shot elevation, opt-in ownership fallback, optional RunOnce scheduling and PE certificate/checksum normalization. Link the repository, tagged source, release/run/provenance and disposable-file verification steps.
4. Record the submission ID and final determination privately where appropriate. Track it through the portal's submission history. If the determination is disputed, use the developer contact form provided with the submission results; do not repeatedly rebuild/resubmit to search for a less detectable variant.
5. Record the vendor's outcome against the **same file hash**, detection version and date. Do not label an unanswered case “cleared” or infer approval of future versions. Leave security protections enabled; blanket exclusions or “disable Defender” are not this project's false-positive remedy.

Microsoft's [submission guide](https://learn.microsoft.com/en-us/defender-xdr/submission-guide) describes the official portal, sign-in, tracking and dispute path; email is not an accepted sample-submission channel. Its [developer FAQ](https://learn.microsoft.com/en-us/defender-xdr/developer-faq) states there is no advance known-list/false-positive-prevention program for developers. Consistent trusted signing can help establish origin, but does not guarantee a verdict. A PUA/HackTool classification must be assessed under the vendor's criteria, not automatically called a proven false positive.

## Other antivirus vendors and VirusTotal

VirusTotal [aggregates vendor verdicts](https://docs.virustotal.com/docs/false-positive) and cannot correct a vendor's detection. Use its [false-positive contacts](https://virustotal.readme.io/docs/false-positive-contacts) to locate the detecting vendor's **official** submission/support channel, then confirm the link on that vendor's site. Follow that vendor's sample format and privacy requirements; a VirusTotal comment is not a vendor case.

Use the same official release/sample hashes, evidence and candid behavior description for each case. Record vendor, detection label/version/date, sample SHA-256, submission ID, status and final response. Do not publicly expose account details, private support correspondence or confidential files. Do not mutate the program to optimize detection counts: no obfuscation, packing, scanner detection, delays, environment probes or vendor-specific workarounds. Zero detections is not an acceptance criterion, and a signature is not a safety certification.

## Issue #901 cleanup tracking — draft, not posted

[nvidia-patch #901](https://github.com/keylase/nvidia-patch/issues/901) was observed open on 2026-10-01. Its report concerns the **v2.2** sample with SHA-256 `14e94f9a844c9a869bf7066021b59e7b24e9d610e8550a76b0f06dbf4b003f18`; the reported vendor count is historical, not a current measurement. Do not replace that sample's identity with a new release's hash.

Proposed maintainer comment (requires separate authorization to post):

> Keeping this issue open temporarily while tracking cleanup in https://github.com/ramhaidar/Win_1337_Apply_Patch rather than closing it as purely upstream. The current uncommitted changes make normal startup unelevated, keep ownership consent off by default, and separate privileged operations. Windows CI and a manual draft-release pipeline that builds from the default branch and creates its own `v`-prefixed version tag now exist locally, producing two x64 ZIPs with SHA-256 and provenance; local tests and two-root rebuild comparisons passed. A hosted run and an official tagged release have not yet been verified or published, and the binaries remain unsigned. Signing eligibility and exact-release vendor submissions are documented, not completed. This does not establish that every detection of the original v2.2 sample is incorrect or guarantee zero detections. Once an official release is available, we will link its source/run/hashes and record vendor case outcomes for those exact bytes.

Before posting, update the draft to the then-current committed/released state. Keep the issue open during tracking; record actual hosted build, release source/SHA-256, signing decision and vendor determinations as evidence becomes available. Do not close it solely because code changed or detection counts decreased.

Official sources above were checked on 2026-10-01; recheck program policies and portal requirements before any external action. No enrollment, submission, publication, version bump or issue-state change was performed for this guide.
