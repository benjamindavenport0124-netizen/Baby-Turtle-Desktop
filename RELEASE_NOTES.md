# Baby Turtle 0.1.0-beta.1

A Windows desktop turtle with voice typing, monitor screenshots, assistant activity reactions, and animated screen adventures.

Extract the entire ZIP and open **Baby Turtle.exe**. Keep all files together. Link a chat input with **Ctrl+Alt+L**; optionally link ChatGPT Windows activity with **Ctrl+Alt+G**. Right-click for settings, help, and restart.

## Beta limits

- Windows PowerShell 5.1 and .NET Framework 4.x are required; no macOS/Linux support.
- Unsigned executable; source and build script are included.
- Input and ChatGPT activity links must be renewed after restart.
- Codex follows the most recently recorded local chat, not necessarily the visible tab.
- ChatGPT activity detection depends on English accessibility controls exposed by the installed version.
- The active character uses the owner's Blender model and material colors. Retired downloaded geometry and texture-reference images are no longer bundled. Earlier Git history and remotely uploaded assets require separate cleanup; see CREDITS.md.

## Validation status

- Local geometry, stroke timing, animation, screen-route, fall/recovery, screenshot payload, assistant-fixture, menu, and restart-cleanup checks passed.
- Original texture fallback works without the supplied images or STL.
- The staged release compiles from its bundled files; the package includes a SHA-256 checksum.
- Testing installation, microphone input, attachment delivery, taskbar identity, and restart on a second Windows computer is pending.
- The included GitHub Actions workflow has not yet been run on GitHub.

This file is ready to use as the starting description for a prerelease. Packaging does not upload or publish it.
