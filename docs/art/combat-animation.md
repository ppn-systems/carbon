# Combat Animation & Impact Feel

How the hero's attacks (bare hands, held weapons and tools) are animated, timed against the server, and given impact
feedback ("juice"). The art stays vector-only (`.cvg.json` rigs, ADR-005). The server stays authoritative: nothing here
delays a tick, an input or a hit.

Contents:
1. [Research](#1-research): what good attack animation does, with sources.
2. [Current state](#2-current-state-audit): what the hero attacks looked like before this work, and why they looked wrong.
3. [Rules](#3-rules): what Carbon's attacks must do.
4. [Tooling](#4-tooling): filmstrips and frame stats.

## 1. Research

### 1.1 Anticipation, active, recovery

Fighting games split every attack into **startup** (anticipation), **active** frames (hitbox live) and **recovery**.

Smash Ultimate, Mario, 60 fps [5]:

| Attack | Startup | Active frames | Total |
| :--- | ---: | :---: | ---: |
| Jab 1 | 2 f | f2–3 | 19 f |
| Forward tilt | 5 f | f5–7 | 25 f |
| Forward smash | 15 f | f15–17 | 47 f |

- A heavy attack has about 7× the startup and 2.5× the total length of a light one.
- The active window is always short: 2–3 frames.
- Dark Souls 3 enemy hitboxes are live for 2–4 frames. The end pose ("return") is usually the longest phase, at 170–240 ms or more. Responses under about 100 ms feel responsive [6].
- Jonathan Cooper (*Game Anim*) [7]:
  - Designers want the shortest anticipation and animators want more.
  - Bigger attacks earn longer anticipation (risk against reward).
  - **Enemy telegraphs must be longer than player wind-ups.**
  - Mark the frame where the player regains control, so follow-through never reads as lag.

### 1.2 Arcs, smears, follow-through, overlap

- In a punch, the head and torso lead and the arm drags, then snaps forward just before impact. Hair and cloth catch up afterwards. Holding a strong pose in follow-through helps readability [7].
- Dead Cells [8]:
  - Attacks are pose-to-pose keyframes.
  - In-betweens sit next to a key, not halfway between keys. The motion lives in the snap.
  - VFX carry the sense of speed and impact.
  - Use the fewest frames that still read.
- A smear or trail must follow the actual motion path, and appear only during the fast (active) part of the swing [12, unverified].

### 1.3 Squash and stretch

- Celeste (`Player.cs`) [9]:
  - A jump sets the sprite scale to (0.6, 1.4) and a bounce to (0.5, 1.5) or (1.5, 0.5).
  - Scale then eases back to 1 at 1.75/s.
- Preserve volume: stretch on one axis means squash on the other [7].
- Bones that cannot squash fake it through poses [7]. Carbon's rig has per-bone `scaleX`/`scaleY`, so it can do both.

### 1.4 Hit-stop (hitlag)

- Smash freezes **both** attacker and victim on hit.
  - Ultimate: `floor((damage*0.65 + 6) * multipliers)` frames, capped at 30.
  - Melee caps it at 20 [4].
- Celeste freezes for 50 ms on a dash start and 100 ms on an explosion launch [10].
- Rule of thumb [11, unverified]:
  - Light hits stop for 2–3 frames (at 60 fps), medium for 4–6 and heavy for 6–10.
  - Freeze both sides: freezing only the target looks like a hiccup.
- In Smash the freeze touches the simulation (the victim can still steer it). **Carbon's does not.** It is a presentation offset on the client, because the server owns the simulation.

### 1.5 Screen shake

- Squirrel Eiserloh, GDC 2016 [1]:
  - Drive shake from a **trauma** value in [0, 1] that decays linearly.
  - Offset = trauma² (or trauma³) × max offset, sampled from smooth noise, not white noise.
  - Small hits barely move the camera; stacked hits build up.
- Jan Willem Nijman, "The art of screenshake" [2]: tiny shakes on every impact, a kick opposite the direction of fire, and a short sleep (hit pause).
- Xbox Accessibility Guideline 117 [13]: "avoid the use of camera shake … or provide an option to turn it off".

### 1.6 Hit flash, particles, photosensitivity

- A target flash should be short and local to the target.
- XAG 118 [14] and the Game Accessibility Guidelines [15]:
  - No more than 3 flashes per second over a large share of the screen (20–25 %).
  - Saturated red has a lower threshold.
  - Do not label a setting "epilepsy safe".
- Per-material impact particles (flesh, wood, stone) are general practice ("Juice it or lose it" [3]). No source gives numbers for them.

### 1.7 Per-weapon motion and combos

There is no citable per-weapon keyframe spec. The choices below are design decisions grounded in 1.1–1.3:

| Weapon | Motion |
| :--- | :--- |
| Jab | Short and straight. |
| Hook | Wide horizontal arc with a torso twist. |
| One-handed slash | Diagonal arc with a wrist snap. |
| Thrust | Linear lunge. |
| Axe, pickaxe | Overhead with a held apex, then a heavy drop. |
| Bow | Draw, short hold, sharp release with recoil. |
| Staff | Raise, then push forward. |

**Combo variation** (a forehand, then a backhand, then a finisher) is common practice, but no source was verified for it. Cutting the previous swing's end pose short inside a combo is documented [6, 7].

### Sources

[1] S. Eiserloh, *Math for Game Programmers: Juicing Your Cameras With Math*, GDC 2016 — https://gdcvault.com/play/1023146/Math-for-Game-Programmers-Juicing ;
implementation reference: https://bevy.org/examples/camera/2d-screen-shake/ <br>
[2] J. W. Nijman, *The art of screenshake*, INDIGO Classes 2013 — https://www.youtube.com/watch?v=AJdEqssNZ-U ;
write-up: https://gamedeveloper.com/design/vlambeer-co-founder-shares-advice-on-building-better-action-games <br>
[3] M. Jonasson, P. Purho, *Juice It or Lose It*, GDC Europe 2012 — https://gdcvault.com/play/1016487/Juice-It-or-Lose <br>
[4] *Hitlag*, SmashWiki — https://ssbwiki.com/Hitlag <br>
[5] *Ultimate Frame Data: Mario* — https://ultimateframedata.com/mario <br>
[6] *Anatomy of an Enemy Attack in Dark Souls 3*, Game Developer 2022 — https://www.gamedeveloper.com/game-platforms/anatomy-of-an-enemy-attack-in-dark-souls-3 <br>
[7] J. Cooper, *The 12 principles of animation in video games*, Game Developer 2019 — https://www.gamedeveloper.com/production/the-12-principles-of-animation-in-video-games <br>
[8] T. Vasseur, *Art Design Deep Dive: Using a 3D pipeline for 2D animation in Dead Cells*, Game Developer 2018 — https://www.gamedeveloper.com/production/art-design-deep-dive-using-a-3d-pipeline-for-2d-animation-in-i-dead-cells-i- <br>
[9] Celeste source, `Player.cs` — https://github.com/NoelFB/Celeste/blob/master/Source/Player/Player.cs <br>
[10] M. Thorson, *Celeste & Forgiveness* — https://maddymakesgames.com/articles/celeste_and_forgiveness/ <br>
[11] *Game feel: 2D combat juice tricks* (unverified blog) — https://freegamesprites.com/es/news/game-feel-2d-combat-juice-tricks <br>
[12] *The art of smear frames*, Bloop Animation (unverified) — https://www.bloopanimation.com/the-art-of-smear-frames/ <br>
[13] Xbox Accessibility Guideline 117 — https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/117 <br>
[14] Xbox Accessibility Guideline 118 — https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/118 <br>
[15] Game Accessibility Guidelines, *Avoid flickering images and repetitive patterns* — https://gameaccessibilityguidelines.com/avoid-flickering-images-and-repetitive-patterns/

Hyper Light Drifter, Hades, Children of Morta and Stardew Valley were studied from footage only. No developer-confirmed
numbers were found for them, so none are quoted.

## 2. Current state (audit)

The audit was taken at `12a3897`. `tools/screenshots/filmstrip.sh` renders the filmstrip below, and §4 describes it.

### 2.1 How a hero attack plays today

- **Server timing.** A player swing has no phase timeline: `EntityAttackPhase` is for monsters only (ADR-013).
  - `PlayerActionService.Attack` (`server/Carbon.Application/World/Actions/PlayerActionService.cs`) resolves the hit **in the tick that processes the input**.
  - The cooldown is then `max(10, tickRate / 2)` ticks: 15 ticks = 0.5 s at the default 30 Hz. Attack speed shortens it to no less than 5 ticks.
  - The client therefore sees `CombatHit` about one round trip plus at most one tick after it starts the swing.
- **Stamina (ADR-033).** A melee, tool or bare-hand swing pays stamina only when it connects. A whiff is free but still starts the cooldown. A shot always pays.
- **Client.** `MultiplayerSession.TriggerPrimaryAction` starts a local `ActionState`:
  - The kind comes from the held item: bare hands and swords both give `SwordSlash`.
  - Durations are 0.24 s for a sword and 0.30 s for an axe or pickaxe.
  - `ComboStep` toggles 0/1.
- **Clips.** `WorldSceneRenderer.ResolveCharacterClip` maps the kind to a `base_hero.cvg.json` clip: `slash`, `chop`, `mine` or `cast`.
  - The clip is sampled at `ActionState.Progress`.
  - Keyframes are always smoothstep-eased (`CompiledVectorRig.EvaluatePose`).
- **Held item.** The item is drawn at the front-arm hand (`VectorRigRenderer.DrawEquippedItemOnRig`). Its grip comes from `weaponGrip`.
  - At rest the item follows the arm bone.
  - During an attack it is rotated by its own `EvaluateSwingArc` curve (windup ends at 0.24, strike at 0.56). That curve is independent of the clip's keys.
- **Trail.** `ActionVfxRenderer.DrawActionTrail` draws a fixed ribbon around the body, sized per kind, from progress 0 to 1.
- **Hit.** `CombatHit` sets a 0.16 s white tint on the target and spawns a damage number.
  - There is no hit-stop and no camera shake.
  - The only impact particles are on props (`CombatFeedbackTracker`).
- **Remote players.** They animate only when one of their hits lands (`OnCombatHit` hard-codes `SwordSlash`, 0.30 s) or when a cast is broadcast. `ComboStep` is always 0 for them.
- **Monster telegraphs** (ADR-013) are separate:
  - They play server phases (`MonsterAttackAnimator`, `AttackTelegraphRenderer`) and must stay readable.
  - Per 1.1, they are longer than any player wind-up.

### 2.2 What looks bad, and why

Read these against the before filmstrip: `hero_attacks.png` from `tools/screenshots/filmstrip.sh` at `12a3897`, world scale ×2, facing east, progress left to right. Each PR links its filmstrips.

1. **The arm swings backwards.**
   - In the rig, a negative `rotDeg` swings a limb forward or up, and a positive one swings it back. Hanging is 0 and straight forward is −90.
   - `attack`, `slash`, `chop` and `mine` all go from about −70° (forward) to about +50° (behind the hip).
   - So the "strike" key throws the fist or blade **behind** the hero, and the follow-through returns it forward. Every attack reads in reverse.
2. **Bare hands play the sword clip with a sword trail.** A punch shows a blue blade arc but has no fist motion.
3. **Combo alternation is half-done.** `ComboStep` mirrors the item arc and the trail, but the arm and body play the same keys. Swing 2 therefore shows the wrist flipping against the arm.
4. **The item and the arm disagree.** The item curve and the clip keys use different timings and arcs, so the blade drifts in the hand. The axe points backwards at the strike.
5. **The trail is detached and mistimed.**
   - It is drawn on a fixed radius, not along the blade tip's path.
   - It is visible from progress 0, during anticipation, and lingers through recovery.
   - It is not tinted by tier or enhancement.
6. **There is no snap.** Every segment is smoothstep (ease-in-out), including the strike, so the fastest part of the swing starts slowly. Dead Cells puts the motion in the snap (1.2).
7. **Bow and staff share `cast`** (both arms raised over the head). The bow draw is only a string offset; no arm pulls it.
8. **Nothing confirms a hit.** There is no hit-stop, shake or material particles. The only response is a 0.16 s tint.
9. **The hit can appear before the blow.** On LAN, `CombatHit` arrives within about one tick, while the swing is still winding up, so the target flashes before the blade arrives.
10. **The body does not commit.** There is no step, lean or squash toward the target, and in north/south views the clip cannot show a forward lunge at all.
11. **A held button outruns the server.** Holding the mouse repeats the swing when `ActionState` ends (0.24 s), but the server cooldown is 0.5 s. About every other held swing is refused by the server yet still animated.

Fixing (11) properly needs either the server's swing acknowledgement or the derived attack speed on the client. It is
tracked as a follow-up: the rules below make the clip length close to the cooldown, which halves the mismatch.

## 3. Rules

### 3.1 Timing: clips follow the server

Every hero attack has a timing record in `HeroAttackTiming`: **anticipation**, **active** and **recovery**, in seconds.
The clip duration is their sum. The clip's keys sit on those boundaries.

- **The active window overlaps the server's damage tick.** The server damages in the tick that processes the input: zero to one tick after the input arrives, plus the one-way latency.
  - Every class's active window must overlap `[1 tick, 1 tick + 150 ms]` at 20 Hz and at 30 Hz. That interval runs from the earliest a LAN hit can land to a typical round trip.
  - A test enforces this.
- **Anticipation is at most 120 ms.** This is the longest the client may hold back a confirmed hit's feedback (3.4). It keeps the response under the responsiveness threshold in 1.1.
- **The impact presents at contact.** A confirmed hit's feedback (flash, hit-stop, shake, particles) plays at the later of its arrival and the start of the active window. The target never reacts before the blow lands.
- **Player wind-ups stay shorter than monster telegraphs.** The shortest monster windup is several ticks plus travel (`docs/monsters.md`).

| Attack | Anticipation | Active | Recovery | Total |
| :--- | ---: | ---: | ---: | ---: |
| Jab (bare hands, combo 1) | 50 ms | 80 ms | 170 ms | 300 ms |
| Hook (bare hands, combo 2) | 70 ms | 90 ms | 180 ms | 340 ms |
| Slash, forehand (sword, combo 1) | 70 ms | 100 ms | 190 ms | 360 ms |
| Slash, backhand (sword, combo 2) | 70 ms | 100 ms | 190 ms | 360 ms |
| Thrust (sword, combo 3) | 60 ms | 80 ms | 220 ms | 360 ms |
| Overhead (axe, pickaxe, building) | 110 ms | 90 ms | 220 ms | 420 ms |
| Bow: draw, hold, release | 100 ms | 60 ms | 200 ms | 360 ms |
| Staff cast | 90 ms | 80 ms | 210 ms | 380 ms |

Heavier swings get longer anticipation and recovery, but within the 120 ms cap. Totals stay under the 0.5 s base cooldown.

### 3.2 Poses

Clips are pose-to-pose. The rig has one bone per arm (no elbow). In side view, a negative `rotDeg` swings an arm or leg
forward or up. A positive `rotDeg` leans the body forward.

| Phase | Pose | Easing out of the key |
| :--- | :--- | :--- |
| Start | Near the idle pose, so the swing starts without a pop. | In-out |
| Anticipation | Cocked: weight back, striking arm drawn back or up, cape trailing. | **Out**: the strike leaves the key at full speed |
| Contact | Mid-active: arm extended toward the target, body leaning in, front leg stepping. Squash on the body (`scaleY` about 0.94, `scaleX` about 1.05). | Out |
| Follow-through | End of active: overshoot past contact. The head and cape lag one key behind (overlapping action). | In-out |
| Recovery | Settles back to neutral. | In-out |

Keyframes gain an optional `ease` (`linear`, `in`, `out`, `inOut`; default `inOut`, the old smoothstep). It applies to the segment that leaves the key.

### 3.3 Per-weapon motion and combos

| Held | Combo | Motion |
| :--- | :--- | :--- |
| Bare hands | 1 jab, 2 hook | Jab: straight extension. Hook: wide arc from behind with a torso twist. |
| One-handed weapon | 1 forehand, 2 backhand, 3 thrust | Forehand: a high-to-low diagonal. Backhand: low-to-high. Thrust: a lunge with the blade level. |
| Axe, pickaxe, building | always overhead | Apex held above the head, a heavy drop, then squash. |
| Bow | — | The front arm holds the bow at the target. The back arm draws, holds, releases, and the bow recoils. |
| Staff | — | Raise, then push the staff head toward the target. A flash at release. |

- **Combo counter.** It advances when the next swing of the same family (fists or a one-handed weapon) starts within 0.9 s of the previous one. Otherwise it restarts at step 1.
- **The held item follows the hand.** During an attack the item's angle is the arm's angle plus a wrist curve (cocked, then snapped through at contact), so the blade never drifts against the arm.
- **The body commits.** On top of the clip, the whole rig shifts up to 2.5 units toward the aim during the active window. This works in all eight facings, including north and south, where a side lunge cannot read.

### 3.4 Impact

| Effect | Rule |
| :--- | :--- |
| Hit-stop | 50–90 ms, scaled by damage relative to the target's max health. A crit adds 20 ms. Only the attacker's pose and the target's reaction freeze. Client-side only: the session clock, input and the simulation keep running. After the freeze, the attack catches up within its recovery, so the clip still ends on time. |
| Screen shake | Trauma model. A hit adds trauma scaled by damage (a crit adds more). Offset = trauma² × 3 px with smooth noise, and trauma decays at 2.5/s. The local player getting hit also shakes. **Settings toggle; off means no camera offset at all.** |
| Hit flash | White on the target for 80–120 ms (a 0.16 s cap today), held through hit-stop. It covers the target only, never the screen. "Reduce flashes" scales it to a soft tint at about 35 % strength. |
| Particles | Per material: **flesh** (monsters: red-pink droplets and a puff), **wood** (trees, wooden structures: brown splinters), **stone** (rocks, ores: grey chips and sparks). Spawned at contact, along the swing direction, from a fixed-size pool. |
| Readability | Player juice never covers a monster telegraph. Trails and particles draw above the ground and are short-lived. Hit-stop never pauses a monster's server-driven telegraph timeline. |

### 3.5 Trail

- The trail is a ribbon along the **weapon tip's path**.
  - It is sampled analytically from the same pose function the renderer uses, at fixed steps across the active window up to the current time.
  - It needs no per-frame history, so it is exact at any frame rate and allocates nothing.
- It appears only in the active window, plus a 60 ms fade.
- Colour comes from the weapon tier (wood, stone, copper, iron, crystal, demon). An enhancement level of +4 or more adds a brighter edge, and +7 or more a cyan glow edge, matching the forge sheen.
- Fists draw a short streak from the knuckles.

### 3.6 Performance

- No per-frame allocation in steady state: poses and trail samples use `stackalloc`, and particles and shake are fixed-size structs.
- The trail and particles go through the existing `GeometryBatch`, so they add no draw calls.
- Measured before and after with `CARBON_FRAME_STATS` on the `combat_fists` and `combat_sword` scenarios (§4).

## 4. Tooling

- **`tools/screenshots/filmstrip.sh [out_dir]`** renders the offline art showcase under xvfb with no server.
  - `hero_attacks.png` has one row per attack (two bare-hand swings, a sword combo, axe, pickaxe, bow, staff), sampled through the swing at world scale ×2.
  - It goes through the same clip mapping, rig renderer and VFX as the world.
  - Run it on two commits to compare.
- **`combat_fists`, `combat_sword` scenarios** (`tools/screenshots/scenarios.txt`) use `CARBON_AUTO_ATTACK=0.55`, which swings due east every 0.55 s in place.
  - Add `CARBON_FRAME_STATS=1200` to the client env to log frame time, bytes per frame and draw calls with attacks on screen.

Baseline at `12a3897`: Mesa llvmpipe, 1280×720, steady-state 120-frame windows after the first.

| Scenario | CPU p50 | Bytes/frame | Draws/frame |
| :--- | ---: | ---: | ---: |
| `combat_fists` | 3.2–4.2 ms | 160–320 | 12 |
| `combat_sword` | 3.3–4.9 ms | 160–330 | 12 |

The swing path already allocated before this work. It is attributed and fixed where the swing path changes (§3.6).
