# World screenshot review

`capture.sh` renders a fixed list of world scenarios (every biome, day/night, rain, macro view, wading,
monsters, the character panel) with the real online client against a throwaway local server, and lays the results out on a
labelled contact sheet. Use it to review terrain, props, lighting and actor art before and after a change.

```bash
tools/screenshots/capture.sh [--lang <code>] [out_dir] [name_regex]

tools/screenshots/capture.sh                                  # all scenarios -> artifacts/screenshots/
tools/screenshots/capture.sh --lang vi /tmp/shots-vi          # same scenarios with the Vietnamese UI
tools/screenshots/capture.sh /tmp/shots '^(dune_desert|red_mesa)$'
SKIP_BUILD=1 tools/screenshots/capture.sh /tmp/shots night    # reuse the last build
```

Output: one `<scenario>.png` per scenario (1280x720) and `contact_sheet.png`. The client log lines tagged
`[Focus]`, `[Biome]` and `[Terrain]` (chunk bake time and vertex count) are echoed per scenario.

## How it works

For every scenario the script:

1. starts `Carbon.Host` (Debug build) on SQLite in a temporary directory, seeded with `CARBON_SEED`
   (default `1337`), on free-ish local ports;
2. runs `Carbon.Desktop` (Release build) under `xvfb-run` with Mesa software GL, auto-login (a fresh account
   and character per scenario), `CARBON_AUTO_EXIT=1` and `CARBON_SCREENSHOT_PATH`;
3. stops the server, then converts the BMP captures to PNG and builds the contact sheet
   (`contact_sheet.py`, `python3 -I` with Pillow).

Requirements: the .NET SDK (`$HOME/.dotnet` is added to `PATH`), `xvfb-run`, GLFW and Mesa (`libglfw3`,
`libgl1-mesa-dri`), and `python3` with Pillow.

## Scenarios

`scenarios.txt` holds one scenario per line: `name | server env | client env`. Keep a name to 18 characters or fewer: the client logs in as `shots_<name>`, usernames are at most 24 characters, and a longer name stalls before login with no capture.

| Server variable (Debug host only) | Meaning |
| :--- | :--- |
| `CARBON_DEBUG_SPAWN_BIOME=<BiomeType>` | Every character enters at the nearest representative tile of the biome (`BiomeLocator`: ±10-tile interior; `ShallowWater` is framed wading by a shore). |
| `CARBON_DEBUG_SPAWN_TILE=x,y` | Every character enters at this world tile (wins over the biome). |
| `CARBON_DEBUG_SPAWN_POI=<PointOfInterestType>` | Every character enters just south of the well of the nearest point of interest of that type (e.g. `DesertVillage`; searched 12 sites out). A village type may name a building after a colon (`RiverVillage:Smithy`, `:Store`, `:Stall`, `:House`) to stand in front of it, and add `:inside` to stand one step through its front door (`RiverVillage:House:inside`). Villages default to the market stall. Combine with `CARBON_WORLD_SEED` in the server env (and `CARBON_SEED` in the client env) for a seed that has one. |
| `CARBON_DEBUG_STATUS_EFFECTS=key:ms:stacks;...` | Every character enters the world with these status effects (ADR-029), e.g. `poisoned:110000:2;slowed:90000:1`. Prefix `v2;` to give each entry a level (ADR-036): `v2;poisoned:110000:2:3`. Durations are not clamped, so short effects last long enough to capture. |
| `CARBON_DEBUG_SPAWN_OFFSET=dx,dy` | Offset in tiles added to the resolved spawn (e.g. far from the starter area). |
| `CARBON_DEBUG_START_HOUR=h` | The world clock starts at hour `h` (0..24); clients follow the server clock. |
| `CARBON_DEBUG_START_LEVEL=n` | Characters enter the world at level `n` or higher (shots of mid-level UI such as the character panel). |
| `CARBON_DEBUG_FORGE_KIT=1` | New characters start with a +8 iron sword, enhancement materials, protection scrolls and 50,000 gold (forge review). |
| `CARBON_DEBUG_STARTER_ITEMS=id[xQty][+Level],...` | New characters also get these items (e.g. `3+10,4x40`: a +10 iron sword and 40 logs), to review enhanced gear. |
| `CARBON_DEBUG_TREE_STAGES=1` | The trees nearest the spawn (out to 14 tiles) start at stump, sapling, young and mature in turn and do not grow on (ADR-040 stage art review). |
| `CARBON_DEBUG_WORN_GEAR=1` or `=p1,p2,...` | Gear from the other Debug kits starts worn to 40 % durability (blacksmith repair review; new characters otherwise start with nothing, ADR-030). A list of percents wears the durable items in slot order to those shares of their maximum, the last one repeating (durability warning review, ADR-035). |

These overrides are compiled into Debug builds of the host only (`HostRuntimeOptions.ReadDebugOverrides`);
Release builds ignore the variables, and nothing is ever taken from a client. The host logs a warning when
they are active.

| Client variable | Meaning |
| :--- | :--- |
| `CARBON_FOCUS_BIOME=<BiomeType>` | Start the camera on the biome's representative tile (pair it with the server spawn override, since the camera follows the player). |
| `CARBON_FOCUS_TILE=x,y` | Start the camera on a tile. |
| `CARBON_WEATHER=rain\|clear` | Force the weather. |
| `CARBON_MACRO_VIEW=1` | Zoomed-out overview. |
| `CARBON_AUTO_WALK=x,y` | Walk in a constant direction (actors in motion, wakes). |
| `CARBON_CAPTURE_FRAMES=n` | Also capture `n` consecutive frames after the main capture. |
| `CARBON_OPEN_PANEL=character` | Open the Character tab of the player panel on entry. |
| `CARBON_CAPTURE_MOUSE=x,y` | Pin the cursor at window pixel `x,y` so hover states (e.g. the stat preview) render. |
| `CARBON_CAPTURE_MODAL=settings\|controls\|inventory\|character\|skills\|crafting` | Open the Settings window (Display or Controls tab) or a player-window tab before the capture, to review localized UI. |
| `CARBON_CAPTURE_SCENE=start\|login\|register\|server\|characters` | Capture a menu screen instead of the world (ADR-037). Every value needs `CARBON_AUTO_LOGIN=0` in the scenario. `start` is the first screen, `login`, `register` and `server` open those screens from it, and `characters` registers the scenario's fresh account and captures the character creator. The script gives each scenario its own `CARBON_ACCOUNT_FILE`, so captures never read or write your remembered account. Run with `BASE_PORT=57207` to see the shipped server name instead of "Custom server". |
| `CARBON_FORGE_PREVIEW=<kind>[:s]` | Open the forge window and play one enhancement effect (`charge`, `success`, `nochange`, `downgrade`, `destroyed`), frozen at `s` seconds when given. Visual only: nothing is sent, the server alone rolls outcomes. The `forge_*` scenarios use it. |
| `CARBON_UI_PREVIEW=<state>` | Open a UI state for review: `signed-in` (the start screen with a sample remembered account, nothing written to disk; ADR-037), `inventory`, or `inventory-tooltip:N`, `inventory-split:N`, `inventory-destroy:N` for slot `N`, `status-hover:N` / `status-details:N` for the N-th status effect icon (ADR-029), `death` (the ADR-031 death screen for a sample slime kill, with the player drawn downed), or `item-break:N` (the ADR-035 break toast for the item in slot `N`, with its shatter shards replayed every 0.4 s). Visual only; nothing is sent to the server. |
| `CARBON_OPEN_TRADER=1` | Open the nearest trader's window once its stock arrives. The `trade_*` scenarios use it. |
| `CARBON_TRADE_PREVIEW=basket` | With `CARBON_OPEN_TRADER=1`: stage every sellable bag stack in the "To sell" basket and two stock rows in the cart (ADR-025 D18). Visual only: nothing is sent until Sell or Buy is pressed. The `trade_basket` scenario uses it. |
| `CARBON_OPEN_REPAIR=blacksmith\|station` | Open the repair window (ADR-028) at the blacksmith or at a player station. The `repair_*` scenarios use it. |
| `CARBON_REPAIR_PREVIEW=work\|success[:s]` | Play the repair animation in that window (`work`: hammering, `success`: the bar filled and flashing), frozen at `s` seconds when given. Visual only: nothing is sent. |

`--lang <code>` (or `CARBON_LANG`) sets the client language for every scenario; the code must have a file in
`resources/localization/` (`<code>.ini`, ADR-023). Capture `en` and `vi` into separate output directories to compare them.

Script knobs: `SKIP_BUILD=1`, `CARBON_SEED`, `BASE_PORT` (TCP; UDP is `BASE_PORT+1`), `WIDTH`/`HEIGHT`,
`CLIENT_TIMEOUT`, `SERVER_BOOT_TIMEOUT`, `SCENARIOS` (alternative scenario file).

## Smoke test

`tools/screenshots/smoke.sh` captures a single scenario and checks that the PNG exists and is not a flat
image. It needs the same headless stack and takes well under a minute after the build, so it can run in CI.
