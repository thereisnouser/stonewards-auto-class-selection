# Changelog

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
