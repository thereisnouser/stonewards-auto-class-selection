# Changelog

## 0.1.4 — Experimental

- Identify scene type 1 as an **ongoing run**, not a pre-game hub; previous builds waited indefinitely for the hub there.
- Stop retrying when gameplay joins are intentionally blocked by default.
- Add opt-in `AllowSelectionDuringActiveRun=false` to test showing native class UI during an ongoing session. **Risk:** changing classes while playing can reset inventory or upgrades; the native menu may not exist in that scene.
- Include real numeric scene type and native-menu availability in diagnostics.

## 0.1.3 — Unreleased

- Fix missed auto-open after switching Steam lobbies without a repeated hub-loading callback.
- Remove the unnecessary local-player spawn gate: the game can open class selection before a Mirror local player is available.
- Keep the hub scene guard to avoid opening during an active run.
- Add concise diagnostic logs for a blocked or delayed native menu.
- Preserve the game's class preference and existing host/network behavior.

## 0.1.2 — Unreleased

- Trigger auto-opening after HubManager.OnLoadingScreenCompleted, not while the loading animation is still running.
- Call the game's own UnlockableMenu.Show(bool) method rather than a derived Show overload.
- Verify the menu really opened, retry safely up to three times, and log diagnostic markers.
- Keep automatic selection disabled during active runs, and preserve native saved class behavior.

## 0.1.1 — Unreleased

- Detect new Steam lobbies, even if Mirror connection identity does not change.
- Reopen the native class selection screen once for each lobby entry.
- Restrict automatic opening to the hub scene (not an active run).
- Document saved class preference retained by the game.

## 0.1.0 — Initial prototype

- Open the native class selection screen on multiplayer connection.
- Add BepInEx configuration and build-and-install PowerShell script.
