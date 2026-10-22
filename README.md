# Stonewards Auto Class Selection

**Automatically open Stonewards' native character-class selection screen when you join or host a multiplayer session.** No extra UI, hotkeys, or changes to the game lobby are needed.

## ✨ Features

- Opens the game's own character selection menu after the local player connects.
- Works when joining an existing lobby or hosting a new one (client-side behavior), detecting Steam lobby ID changes.
- Opens once per new connection or Steam lobby ID in the hub, including lobby switches without a second hub loading event.
- Leaves the menu alone if Stonewards has already opened it.
- On/off toggle and a configurable opening delay via BepInEx config.

## 🎮 How to use

1. Install the mod in a BepInEx 5 profile.
2. Create a lobby or join another player's session.
3. Wait for your character to connect in the hub; the normal class-selection window should open automatically.

The mod never chooses a class for you. Other players do not need to install it. **Stonewards still remembers your previous class as a preferred/assigned class until you select another one; this mod does not reset it.**

## ⚙️ Configuration

BepInEx creates `BepInEx/config/com.thereisnouser.stonewards.autoclassselection.cfg`:

```ini
[General]
Enabled = true
OpenDelaySeconds = 1
AllowSelectionDuringActiveRun = false
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

- Class selection opens in the hub. An ongoing run has GameManager.SceneType=1, and Stonewards normally does not open character selection there. Set AllowSelectionDuringActiveRun=true only if you deliberately want to test showing its menu on joining gameplay; class changes may reset inventory or upgrades, and the game's class selection UI may not exist in that scene.
- Whether the native menu's `Show(bool)` / `BaseShow(bool)` implementation works when invoked just after joining must be verified on the actual installed game build. If that game API changes, the plugin logs a warning and leaves the UI untouched.
- This mod does not bypass in-game restrictions on class changes, and does not synchronize anything over the network.

## 🛠️ Implementation

The plugin detects a new Mirror connection or Steam lobby ID and invokes the game's native UnlockableMenu.Show(bool) when the hub scene and CharacterSelectionScreen are ready. Harmony observes the hub-loading callback but does not require it, because Stonewards can switch lobbies without firing it. The plugin does not require NetworkClient.localPlayer to be spawned before showing the menu. The game itself retains its saved class preference; this plugin does not alter networking or account preferences. No game assemblies or third-party binaries are bundled in the source.

## 🔗 Links

[GitHub profile](https://github.com/thereisnouser)

---

*Made by thereisnouser*

## Troubleshooting v0.1.4

After rebuilding, check the **same r2modman profile** you launched: BepInEx/LogOutput.log. Search for `Auto Class Selection v0.1.4 loaded`, `New lobby detected`, `joined an active run`, `CharacterSelectionScreen.Instance is null`, and `Native class selection opened`. If the UI remains unchanged, share these lines so we can identify whether the plugin loaded, the hub callback ran, or the native Show method failed. Make sure the game was fully closed before replacing the DLL.
