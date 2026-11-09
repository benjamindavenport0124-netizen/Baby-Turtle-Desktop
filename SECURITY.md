# Security

Baby Turtle is an unsigned, experimental Windows desktop application. The current beta is the supported development version. Keep Windows and its bundled .NET Framework and PowerShell components updated.

## Reporting a problem

Do not publish credentials, private chat content, screenshots, or a working exploit against someone else's machine in a public issue. If the repository owner has enabled GitHub private vulnerability reporting, use the repository's Security tab to report privately. Otherwise, open a minimal issue asking the maintainer for a private reporting channel without including sensitive details.

Include the app version, Windows version, affected feature, expected behavior, and a minimal reproduction using dummy data. State whether your report requires replacing local app files or already running code as the same Windows user.

## Trust boundaries

- The executable runs the C# and PowerShell files beside it with the current user's permissions. Install the extracted folder somewhere you control and do not run a modified copy you do not trust. There is no elevation or automatic updater.
- Message-box and ChatGPT links are explicit, temporary selections. Review dictated text and screenshot attachments before sending. The turtle never sends a chat message for you.
- The clipboard is shared with other desktop applications. Screenshots placed there are not protected storage.
- The project has no bundled package-manager dependencies or network listener. Windows handles online voice recognition.

See [the scoped review](docs/SECURITY_REVIEW.md) for the latest checks and limitations. No scan is a guarantee that the app is free of vulnerabilities.
