# Changelog

## 0.1.0-beta.1

Initial Windows beta:

- Six-part animated turtle with blinking, pointer reactions, sleep, and waking.
- Flipper-driven walking, climbing, upside-down top traversal, random falls, height-dependent landing dust, and click-to-help recovery.
- Windows voice typing with a linked message box and monitor-specific screenshot paste.
- Read-only Codex activity monitoring and optional ChatGPT Windows accessibility monitoring.
- Workstation typing while an assistant works and a completion dance.
- Grouped menu, saved preferences, restart, and a dedicated executable with a turtle taskbar icon.
- Retired downloaded STL and reference images removed from current assets and release packaging. Character materials come from the owner's Blender model; workstation planks and the fallback character use generated colors. Old Git history requires separate cleanup.
- Pre-upload malware/source review, exact linked-control focus checks, restricted CI credentials, and a bounded upload-secret check. See docs/SECURITY_REVIEW.md for validation limits.
- Imported the owner's separated Blender turtle and authored material colors. Added attachment pivots, closed the head attachment in the prepared copy, retained blinking, adapted ground contact, and included mesh/source assets in packages.

- Fix activity-monitor UI blocking, atomically replaced session tracking, retained climbing rotation after dragging, and startup error reporting when logging fails; add regression and CI checks.
- Preserve Blender export inputs, resolve active material colors, allow renamed materials, stage paired exports with rollback, and scan Python files for credential patterns.
