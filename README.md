# Stonewards Auto Class Selection

**Automatically open Stonewards' native character-class selection screen when you join or host a multiplayer session.** No extra UI, hotkeys, or changes to the game lobby are needed.

## ✨ Features

- Opens the game's own character selection menu after the local player connects.
- Works when joining an existing lobby or hosting a new one (client-side behavior).
- Opens at most once per connection; does not reopen after the player closes it.
- Leaves the menu alone if Stonewards has already opened it.
- On/off toggle and a configurable opening delay via BepInEx config.

## 🎮 How to use

1. Install the mod in a BepInEx 5 profile.
2. Create a lobby or join another player's session.
3. Wait for your character to connect; the normal class-selection window should open automatically.

The mod never chooses a class for you. Other players do not need to install it.

## ⚙️ Configuration

BepInEx creates `BepInEx/config/com.thereisnouser.stonewards.autoclassselection.cfg`:

```ini
[General]
Enabled = true
OpenDelaySeconds = 1
```

The delay accepts values from `0` to `10` seconds. For slower computers, a slightly larger delay may help ensure the lobby screen is ready.

## 📦 Installation

### From source

Requirements: Stonewards (Unity Mono), BepInEx 5 / BepInExPack, a .NET SDK with `netstandard2.1` support, and PowerShell.

Create an **r2modman profile named `stonewards-auto-class-selection`**, install BepInExPack in it, close Stonewards, and run from the repository root:

```powershell
.\build-and-install.ps1 `
  -GameDir 'F:\Steam\steamapps\common\Stonewards' `
  -ProfileDir "$env:APPDATA\r2modmanPlus-local\Stonewards\profiles\stonewards-auto-class-selection"
```

Adjust `GameDir` if Stonewards is installed elsewhere. The script compiles against your *own installed game libraries* and installs the DLL into that profile's `BepInEx/plugins/StonewardsAutoClassSelection/` folder. Launch the same profile with **Start modded**.

## 🔧 Compatibility

- **Game:** Stonewards; implemented against the `CharacterSelectionScreen` API used by our existing Stonewards mods.
- **Mod loader:** BepInEx 5.
- **Multiplayer:** client-only UI; host and teammates do not need the mod.
- **Status:** prototype — compile and in-game behavior are **not verified** in a local Stonewards installation yet.

## ⚠️ Known limitations

- Class selection opens only after the local multiplayer player exists, not during the initial loading screen.
- Whether the native menu's `Show(bool)` / `BaseShow(bool)` implementation works when invoked just after joining must be verified on the actual installed game build. If that game API changes, the plugin logs a warning and leaves the UI untouched.
- This mod does not bypass in-game restrictions on class changes, and does not synchronize anything over the network.

## 🛠️ Implementation

The plugin observes Mirror client connection changes, waits for the local player and `CharacterSelectionScreen.Instance`, and calls the game's native menu method once. When a connection ends it resets the one-shot guard. No game assemblies or third-party binaries are bundled in the source.

## 🔗 Links

[GitHub profile](https://github.com/thereisnouser)

---

*Made by thereisnouser*
