# Pre-upload security review â€” 2026-11-08

Later asset cleanup removed the downloaded STL and texture-reference images from the working tree and package allowlist. The active character is now the owner's material-colored Blender model; the wood pattern is generated in code. Those later functional changes are separate from the original scoped security pass below. Git history was not rewritten.

## Scope and result

Scoped local source review and upload inspection of the Windows application, launch/build/package scripts, CI workflow, and files eligible for Git upload. This is not a completed Strix penetration test or a comprehensive independent audit. The project currently has uncommitted source and no reviewed commit hash.

### Checks completed

- Microsoft Defender custom folder scan, with remediation disabled, returned exit code 0 and reported no threats. The scan covered the project folder, including its assets and local build output. This is malware scanning, not source-level vulnerability validation.
- A parent-run credential-pattern check inspected the upload-eligible text files for private-key headers, GitHub tokens, AWS access-key IDs, and OpenAI-style keys. No matching credentials were found. This is bounded pattern matching, not Gitleaks or an exhaustive secret scan.
- Git ignore checks confirmed exclusion of local settings, logs, generated EXE/ZIP output, archives, environment files, and common private-key container files.
- PowerShell files passed trusted parser-only syntax checks; the reviewed scripts were not executed as part of this security pass.
- Reviewed the linked-input lifecycle, password-field rejection, process/window identity checks, screenshot clipboard/paste path, lifecycle JSON parsing, image loading, app host, executable build, release file allowlist, and GitHub workflow.

No remote-code execution, outbound credential transmission, network listener, automatic chat submission, or bundled third-party package-manager dependency was identified in the reviewed source. This describes the paths inspected; it does not prove absence of every vulnerability or cover vulnerabilities in Windows components.

## Source changes made

### Exact input focus is rechecked before paste

Previously `UpdateScreenshot` checked only the foreground window while waiting for modifier keys and after writing the clipboard. Focus could move to another control in the same window after the initial focus task completed. The reviewed path therefore did not enforce the intended linked-control check at the final input boundary.

`DictationTarget.IsFocusedTarget` now checks the link generation, selected window, process identity/start time, editability/password status, and keyboard focus. It is used when consuming focus results and immediately before clipboard/paste actions. Relinking or forgetting invalidates old focus results. Failure cancels automatic paste; if the clipboard has already been written, the user can paste manually.

This is a source-grounded privacy hardening change, not an exploit-validated severity finding. Actual two-control UI regression validation remains pending. OS-level focus can still change between a check and synthetic key delivery; Windows input injection is not atomic with UI Automation checks. Provider calls during the final check may also affect responsiveness and need a live compatibility check.

### CI and upload hardening

- Workflow token permissions are explicitly restricted to `contents: read`.
- Checkout no longer persists repository credentials for subsequent build steps.
- Added `Verify-UploadSafety.ps1` to inspect the upload set, withholding matched secret values from errors.
- Ignored common environment/key-container files and Strix output directories.

These are preventive controls, not claims that an attacker had already exploited the workflow. GitHub actions remain referenced by version tags rather than immutable commit hashes.

## Tools unavailable and validation gaps

- Strix CLI, Docker, Gitleaks, and Semgrep were not available on PATH. No Strix sandbox or cloud scan ran, no source was uploaded to a scanning service, and no paid scan was started.
- This environment did not establish the full isolated execution controls required by the security-audit skill. Target-controlled builds, app execution, and regression tests were not run during this pass. Earlier functional tests predate the final focus hardening change and do not validate that change.
- The new focus guard should be tested in a disposable Windows desktop with dummy controls: link field A, request a screenshot while holding a modifier, focus field B in the same window, release the modifier, and verify that B receives no paste. Also test forgetting/relinking while a focus request is pending, normal paste, and normal dictation.
- Before publishing, run the functional checks in an appropriate isolated environment, including `Verify-Codex.ps1`, `Verify-Menu.ps1`, and `Verify-Restart.ps1`, and rebuild any release ZIP so it includes these changes. A second-computer compatibility test and GitHub-hosted workflow run remain pending.
- Defender's result is time/signature dependent. No standalone review of every image decoder or .NET/Windows binary was performed. Third-party artwork redistribution terms are a separate issue documented in CREDITS.md.

## References

- [Microsoft Defender command-line scan options](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus)
- [GitHub workflow permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions)
- [GitHub Actions security guidance](https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions)


### 8 November findings follow-up

Resolved R1–R4 from the separate repo-review/FINDINGS-DETAIL.md source review:

- R1: parser/file work retains a worker lock; UI reads use an independently published snapshot. Disposal cancels polling and does not wait on a busy worker. A deterministic blocked-worker regression verifies Read/Dispose finish within one second; this models blocked worker I/O rather than injecting a slow filesystem.
- R2: beginning a drag clears all climbing/hanging/fall orientation offsets. Ceiling and descent regressions verify upright carried, drop, and idle poses.
- R3: compare Windows volume/file-index identities for the open handle and current pathname. Replacement reopens and resets the decoder/lifecycle state without replaying completion dances. Equal-size and larger atomic replacements, subsequent appends, and in-place truncation pass.
- R4: startup logging is best effort, including normal output logging. A directory occupying the log filename verifies errors still reach the reporting callback. The rebuilt host includes the fix.

Removed unused ReverseQuads and the screenshot wrapper used only by tests; tests now call the actual display-selection action. Retired STL/reference images and their loaders were already removed in the asset migration. The old report's instruction to keep image loaders describes the previous renderer; the current renderer uses authored Blender materials and procedural planks. Git history remains unchanged.

Added non-relaunch/headless menu and restart checks, plus startup diagnostics, to Windows CI. Local verification passed Turtle, Codex, Adventure, Assets, BlenderModel, Menu (-Headless), Restart (-Headless), Startup, and UploadSafety. A fresh staged release built and compiled successfully. These were ordinary local Windows runs, not an isolated disposable security sandbox; hosted CI execution is not yet verified. Synthetic screenshots and unlinked-target guards are covered, but live dummy-control focus-race scenarios remain a manual validation gap. No new exploit/security-clearance claim is made.

Executable signing and immutable Action SHA pinning remain optional release hardening, not confirmed defects. Signing requires a signing identity; no certificate or account changes were made.
