<div align="center">
  <h1>OpenSteamTool</h1>

  <p>
    <strong>Open-source Steam unlock tool that keeps up with Steam updates on its own</strong>
  </p>

  <p>
    <img src="https://img.shields.io/badge/C%2B%2B-20%2B-2ea44f?logo=cplusplus&logoColor=white" alt="C++ 20+">
    <img src="https://img.shields.io/badge/CMake-3.20%2B-2ea44f?logo=cmake&logoColor=white" alt="CMake 3.20+">
    <img src="https://img.shields.io/badge/Windows-only-d73a49?logo=windows&logoColor=white" alt="Windows only">
    <img src="https://img.shields.io/badge/license-GPL--3.0-blue" alt="GPL-3.0">
  </p>
</div>

OpenSteamTool is a fork of [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool). It adds:

- **Self-maintained signatures.** [Smokestack](https://github.com/SvnFrs/smokestack) watches Valve's client releases and ports OpenSteamTool's hook signatures to each new build within hours, using [`tools/sigport`](tools/sigport).
- **Graceful degradation.** When Steam is newer than the published signatures, OpenSteamTool keeps working on the last build's signatures instead of disabling hooks (see [Steam version compatibility](#steam-version-compatibility)).

## Features

### Core unlocks
- Unlock an unlimited number of unowned games.
- Unlock all DLCs for unowned games.
- Load depot decryption keys from Lua config automatically.
- Download manifests automatically via the `opensteamtool` / `steamrun` / `wudrm` upstream APIs (default is `opensteamtool`), or a custom Lua endpoint (see [Manifest via Lua](#manifest-via-lua)).
- Download protected games or DLCs that require an access token.
- Bind a manifest to keep specific games from being updated.

### Hot reload
- Adding, modifying, deleting, or overwriting `.lua` files in any watched directory triggers a reload. No restart and no offline/online toggle needed.

### Injection
- Optional game-process library injection through `[inject]` in `OpenSteamTool.toml`.
- Configure `enabled`, `library_x64`, and `library_x86`; the injected library must match the target process architecture. Paths may be absolute or relative to the Steam root directory.

### Family Sharing and Remote Play
- Bypass Steam Family Sharing restrictions for games added to the library with `addappid` in Lua. Every account in the Steam Family that shares must run OpenSteamTool (or OpenSteamTool).

### Games protected by Denuvo and SteamStub
- SteamStub-only games need no `AppTicket`. OpenSteamTool reuses Steam's local ConfigStore ticket and forges the requested AppId through a SteamDRMP off-by-four ticket parsing vulnerability, without injecting into the game process.
- Denuvo-protected games still need explicit ticket data. OpenSteamTool stores `AppTicket` and `ETicket` through the platform credential store.
- Use `setAppTicket(appid, "hex")` and `setETicket(appid, "hex")` in Lua config to write these values to the credential store.
- Denuvo verification has a 30-minute validity window. After it expires, authorization may fail with Denuvo error code `88500005`; refresh the ticket data before retrying.
- AppTicket priority: explicit tickets come first (from `setAppTicket` or an existing cached `AppTicket`). Without one, OpenSteamTool falls back to the forged local ConfigStore ticket.
- SteamID priority: the cached `SteamID` first; if missing, parsed from the explicit `AppTicket`. On Windows the credential store is `HKCU\Software\Valve\Steam\Apps\<AppId>`.

#### Extracting tickets with `extract_tickets`

`extract_tickets` dumps the `AppTicket` and `ETicket` hex strings needed for `setAppTicket` / `setETicket`. Run it on a machine where Steam is running and logged into an account that **owns** the game.

1. Build the tools (see [Build](#build)); the binary lands in `build/tools/Release/extract_tickets.exe`.
2. Run it with the target AppId (or with no argument, then type the AppId):
   ```powershell
   extract_tickets.exe 1361510
   ```
3. It reads the Steam install path from the registry, loads `steamclient64.dll`, and writes an `<appid>/` folder next to the executable:
   - `appticket.bin`: raw app ownership ticket
   - `eticket.bin`: raw encrypted app ticket
   - `tickets.txt`: the hex strings
     ```
     appid:1361510
     appticket(184 bytes):14000000...
     eticket(143 bytes):...
     ```
   A ticket that could not be obtained is reported as `appticket:null` / `eticket:null`.
4. Paste the hex strings into your Lua config:
   ```lua
   setAppTicket(1361510, "14000000...")
   setETicket(1361510, "...")
   ```

> **Note:** Tickets are only valid when extracted from an account that **genuinely owns** the game.

### Stats and achievements
- Stats and achievements for unowned games.
- `setStat(appid, "steamid")` chooses which SteamID's achievement data to pull.
- Without `setStat`, OpenSteamTool queries `https://stats.opensteamtool.com/{appid}` when `[stats] enable_api = true` (default).
- Priority: `setStat` > stats API (when enabled and valid) > preset SteamID `76561198028121353`.

### Cloud saves
- Optional Steam Cloud save redirection for unlocked games through [CloudRedirect](https://github.com/Selectively11/CloudRedirect); enable it under `[cloud]` in `OpenSteamTool.toml`.

### Online fix
- Add `-onlinefix` to Steam's launch parameters to enable 480-based online play in games that use lobby matchmaking. Only one such game can run at a time. Remove the parameter to go back to normal on the next launch.

## Usage

1. Download a release (or [build](#build) it) and copy `dwmapi.dll`, `xinput1_4.dll` and `OpenSteamTool.dll` to the Steam root directory.
2. Create a Lua directory (for example `C:\steam\config\lua`) and put Lua scripts there. OpenSteamTool loads and runs them automatically.
3. Lua example:
```lua
addappid(1361510) -- unlock game with appid 1361510

addappid(1361511, 0,"5954562e7f5260400040a818bc29b60b335bb690066ff767e20d145a3b6b4af0") -- unlock appid 1361511 with depot key "5954...af0"

addtoken(1361510,"2764735786934684318") -- add access token for appid 1361510

setManifestid(1361511,"5656605350306673283") -- pin depot 1361511 to manifest gid 5656605350306673283, size defaults to 0
setManifestid(1361511,"5656605350306673283", 12345678) -- same with explicit size

setAppTicket(1361510,"0100000000000000...") -- write AppTicket to HKCU\Software\Valve\Steam\Apps\1361510\AppTicket

setETicket(1361510,"0100000000000000...") -- write ETicket to HKCU\Software\Valve\Steam\Apps\1361510\ETicket

setStat(1361510, "76561197960287930") -- use this SteamID's achievement data for appid 1361510
```

All function names are **case-insensitive**: `setAppTicket`, `setappticket` and `SETAPPTICKET` are equivalent, and the same goes for every registered function.

### Upgrading from OpenSteamTool

Replace `dwmapi.dll` and `xinput1_4.dll` with OpenSteamTool's, add `OpenSteamTool.dll`, and delete `OpenSteamTool.dll`. Nothing else is needed:

- An existing `<Steam>/opensteamtool.toml` is still read while there is no `OpenSteamTool.toml`.
- The `<Steam>/opensteamtool/` folder (logs, signature cache) is moved to `<Steam>/OpenSteamTool/` on first start.

### Configuration (optional)

Copy `OpenSteamTool.example.toml` to `OpenSteamTool.toml` in the Steam root directory (next to `steam.exe`).
Without a config file, built-in defaults are used. The file is watched while Steam runs; valid changes are hot-reloaded.

```toml
[log]
# Debug build only.  Level: trace, debug, info, warn, error
level = "info"

[manifest]
# Upstream API for depot manifest request codes.  Options: "opensteamtool", "steamrun", "wudrm"
url = "opensteamtool"

# HTTP timeouts for manifest requests (milliseconds)
timeout_resolve_ms = 5000
timeout_connect_ms = 5000
timeout_send_ms    = 10000
timeout_recv_ms    = 10000

[stats]
# Query https://stats.opensteamtool.com/{appid} when no Lua setStat override exists.
enable_api = true

# Additional Lua config directories (optional), loaded before <Steam>/config/lua
# so files in the default folder take priority.
[lua]
paths = []

[inject]
enabled = false
# library_x64 = "GameHook.x64.dll"
# library_x86 = "GameHook.x86.dll"

[cloud]
enabled = false

# Optional signature mirror. See "Steam version compatibility" below.
[remote]
# url_template = "https://your.server/{channel}/{component}/{sha256}.toml"
```

The `opensteamtool` manifest provider and the stats API are services run by the OpenSteamTool project. OpenSteamTool uses them as-is.

### Manifest via Lua

Two manifest code functions are supported:

- `fetch_manifest_code(gid)`: receives only the manifest GID.
- `fetch_manifest_code_ex(app_id, depot_id, gid)` *(recommended)*: also receives the app and depot IDs, for endpoints that need them.

The runtime provides two Lua helpers:

| Function | Signature | Returns |
|----------|-----------|---------|
| `http_get`  | `http_get(url [, headers])`       | `body, status_code` |
| `http_post` | `http_post(url, body [, headers])` | `body, status_code` |

`headers` is an optional table: `{["Key"]="Value", ...}`.

### Steam version compatibility

OpenSteamTool doesn't compile hook locations into the DLL. On each launch it hashes `steamclient64.dll` and `steamui.dll` (SHA-256) and loads the matching files from [Smokestack](https://github.com/SvnFrs/smokestack):

1. **GitHub raw**: `https://raw.githubusercontent.com/SvnFrs/smokestack/{pattern|ipc}/...`
2. **jsDelivr**: automatic fallback when GitHub raw is unreachable (useful where `raw.githubusercontent.com` is blocked).
3. **Local cache**: `<Steam>\OpenSteamTool\{pattern|ipc}\<component>\<sha256>.toml`, used when both are unreachable. It's rewritten after every successful fetch.
4. **Last known build**: if no file exists for this build yet (Steam updated in the last few hours), OpenSteamTool uses the newest cached file from an earlier build. Hooks resolve by signature, and a signature is trusted only if it matches exactly once in the new binary. Functions that can't be found that way are listed in a popup; everything else keeps working.

Smokestack's job checks Valve's stable and beta client manifests every three hours. When a build appears, it ports every function, grades how confident each match is, and opens an issue if any function needs a human look. See [`tools/sigport`](tools/sigport) for how matching works.

You can also drop a TOML into the cache directory by hand; the file name must be `<sha256>.toml`.

#### Using a different mirror

To use a private mirror, set a URL template containing `{channel}`, `{component}` and `{sha256}`. It replaces the built-in sources; the local cache still applies.

```toml
[remote]
url_template = "https://your.server/{channel}/{component}/{sha256}.toml"
# url_template = "https://fast.jsdelivr.net/gh/SvnFrs/smokestack@{channel}/{component}/{sha256}.toml"
```

### Debug logging

Debug builds write per-module logs under `<Steam>/OpenSteamTool/`:

| File | Source | Content |
|------|--------|---------|
| `main.log`          | General | Init, config loading, Lua parsing, signature loading |
| `ipc.log`           | `LOG_IPC_*` | IPC commands, InterfaceCall dispatch, spoofing |
| `netpacket.log`     | `LOG_NETPACKET_*` | Network packet send/recv, eMsg dispatch |
| `manifest.log`      | `LOG_MANIFEST_*` | Manifest download, `fetch_manifest_code`, manifest binding |
| `decryptionkey.log` | `LOG_DECRYPTIONKEY_*` | Depot decryption key injection |
| `keyvalue.log`      | `LOG_KEYVALUE_*` | KeyValues patching (manifest binding) |
| `misc.log`          | `LOG_MISC_*` | Engine pointer capture, AppId hints |
| `achievement.log`   | `LOG_ACHIEVEMENT_*` | UserStats requests/responses, SteamID spoofing |
| `pics.log`          | `LOG_PICS_*` | PICS access token injection |
| `package.log`       | `LOG_PACKAGE_*` | Package injection, FileWatcher events |
| `onlinefix.log`     | `LOG_ONLINEFIX_*` | Online fix (480 AppId spoofing) |
| `richpresence.log`  | `LOG_RICHPRESENCE_*` | Rich Presence packet construction and injection |
| `steamui.log`       | `LOG_STEAMUI_*` | SteamUI hook diagnostics |
| `pipe.log`          | `LOG_PIPE_*` | Pipe handshakes, process inspection, Denuvo authorization, library injection |
| `platform.log`      | `LOG_PLATFORM_*` | Platform helper diagnostics, including remote-process operations |

The level is set by `[log] level` in `OpenSteamTool.toml`.

## Build

### Requirements
- Windows 10/11
- CMake 3.20+ (Ninja recommended)
- Visual Studio 2022 or Build Tools 2022 with the C++ workload (x64)
- [Bun](https://bun.sh) 1.3+, only for working on `tools/sigport`

### Quick build
```powershell
build.bat
```

### Output
- Debug: `build/Debug/OpenSteamTool.dll`, `build/Debug/dwmapi.dll`, `build/Debug/xinput1_4.dll`
- Release: `build/Release/OpenSteamTool.dll`, `build/Release/dwmapi.dll`, `build/Release/xinput1_4.dll`

## Credits and license

OpenSteamTool is a fork of [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) by OpenSteam001 and contributors, and like it is licensed under the [GPL-3.0](LICENSE).

## Disclaimer
This project is provided for research and educational purposes only. You are responsible for complying with local laws, platform terms of service, and software licenses.
