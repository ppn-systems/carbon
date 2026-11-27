# Character Bodies: Proportions, Silhouettes and Shared Rigs

How Carbon's playable bodies are drawn, so that a new body or a new race is art plus config and nothing else. The
data model is [ADR-042](../adr/ADR-042-data-driven-races-body-types-and-character-looks.md); the art stays vector-only
(`.cvg.json` rigs, [ADR-005](../adr/ADR-005-data-driven-sprite-animation-and-combat-vfx-pipeline.md)).

Contents:
1. [Research](#1-research): proportions and silhouette readability, with sources.
2. [Rules](#2-rules): what every body rig must do.
3. [The human bodies](#3-the-human-bodies): what the male and female rigs actually differ in.
4. [Adding a body or a race](#4-adding-a-body-or-a-race).
5. [Review](#5-review): the body sheet and the in-game scenarios.

## 1. Research

### 1.1 Proportions

Figure-drawing canons measure the body in head lengths. They agree on the pattern, not on exact numbers:

- Academic canons draw about 8 heads for men and 7.5 for women [1].
- Male figures are widest at the shoulders, a trapezoid; female figures are widest at the hips, an hourglass [2][3].
- Shoulder width: about 2 heads for men and 1.5–1.75 for women [4]. Older drafting manuals give up to 3 heads for male
  shoulders over 2 for the hips, and roughly equal shoulders and hips for women [5].
- For stylized characters, the advice is to exaggerate the contrast rather than copy the canon: broad shoulders read as
  strong, narrow shoulders as light or young [6].

### 1.2 Small sprites and silhouettes

- **Silhouette test**: fill the character in solid black; if it is not identifiable, the design needs work. In play,
  with the camera moving and effects on screen, a sprite reads by its silhouette alone [7][8].
- Make proportions distinct and keep large and small details apart [7]. Differences of a pixel or two in limb width do
  not survive at play scale; differences in the outline do.
- Chibi proportions do not follow the adult canon. One survey of chibi overworld sprites measured the head at about
  60–67 % of the sprite height [9]. Carbon's hero is in that family: a 35-unit figure whose head ellipse spans about
  13 units, the top of a 34-unit design.

### 1.3 What that means for Carbon

At 35 world units the head-count canons cannot apply (the head is roughly a third of the figure), so the bodies must
differ by **outline**, not by limb length:

- the widest point of the torso (shoulders versus hem),
- the waist,
- one or two small, high-contrast face cues that survive downscaling.

## 2. Rules

1. **One skeleton.** Every body rig declares the same `bonePivots` names (`root`, `cape`, `legLeft`, `legRight`,
   `body`, `armBack`, `armFront`, `head`, `headgear`, `aura`). Clips, gear overlays and held items attach to bones, so
   the same names mean they work everywhere. `BodyRigParityTests` fails otherwise.
2. **Shared clips, never copies.** A body rig other than a race's first sets `"animationsFrom": "<first body>.cvg.json"`
   and declares no `animations`. Clip work on the reference rig (walk, attacks, tool use, swim, dive, downed) reaches
   every body the same day. The loader rejects a rig that does both, and a chain.
3. **Pivots may move, clips may not change.** A body can move a pivot (the female shoulders sit 0.9 units further in),
   because clips are relative rotations and offsets per bone. `BodyRigParityTests` checks that every clip produces
   the same bone poses on both bodies.
4. **Gear fits by bone, not by body.** Held items attach at `weaponGrip` (one per facing, placed at the hand). The
   modular chest armor and cape scale by the rig's `torsoWidthScale`. Pauldrons follow the arm pivots. Leg armor and
   boots follow the leg bones.
5. **Colours come from `races.json`.** Shapes that take the player's colours carry `"role": "skin"`, `"skinShadow"`
   or `"hair"`. A body never hardcodes a skin or hair colour.
6. **Same head.** The head, headgear pivot and face overlay positions stay the same across a race's bodies, so hair
   styles, helmets and hats fit both.
7. **Pass the silhouette test** at world scale ×1 against the other bodies of the race.

## 3. The human bodies

Both are `designHeight` 34 and `worldHeight` 35, with the same head and the same legs.

| | Male (`characters/base_hero.cvg.json`) | Female (`characters/human_female.cvg.json`) |
| :--- | :--- | :--- |
| Torso outline | Trapezoid, widest at the shoulders (±6.5) narrowing to ±5.4 | Bodice ±5.4 at the shoulders, in to a ±3.9 waist, out to ±5.4 at the hips |
| Hem | ±5.6 to ±7.0, ends at y −7.2 | A-line, ±5.2 to ±8.2, ends at y −5.0: the widest point is the hem |
| Belt | At the hips | Sash at the waist |
| Arms | Pivots ±6.2 | Pivots ±5.3, 12 % slimmer, smaller hands |
| Legs | 3.8 wide | 3.4 wide |
| Face | — | Lash flicks at the outer eye corners (south and side) |
| Gear | `torsoWidthScale` 1 | `torsoWidthScale` 0.85; grips at the narrower hands |
| Clips | Own | `animationsFrom: base_hero.cvg.json` |

In solid black the male reads as a block that narrows downward, and the female as a narrow waist over a flared hem.
That is the trapezoid-versus-hourglass contrast of §1.1, put where a 35-unit sprite can show it.

Hair styles and colours, eye colours and skin tones are options the player picks for either body (`races.json`). No
body has a hair style of its own.

## 4. Adding a body or a race

1. Draw the rig under `resources/art/characters/` (copy the race's first body and change shapes only). Set
   `animationsFrom`, `torsoWidthScale` and the `weaponGrip` hands. Keep the bone names.
2. Add it to `catalog.cvg.json` under `characters` (one entry per file).
3. Add the body (or the race, with its bodies and options) to `resources/config/races.json`. Ids and options are
   append-only.
4. Add the label keys to `resources/localization/en.ini` and `vi.ini`; `LocalizationTests` lists any that are missing.
5. Run `tools/screenshots/filmstrip.sh` and review `hero_bodies.png`.

## 5. Review

- `tools/screenshots/filmstrip.sh` → `hero_bodies.png`: every body of every race in `races.json`, bare and geared,
  through idle, walk, north, sword, axe, swim, dive and downed (`HeroBodySheet`).
- `tools/screenshots/capture.sh <out> '^body_'` (and `--lang vi`): the same scenes in game on each body.
  `CARBON_CHARACTER_BODY=<code>` picks the auto-login character's body.

## Sources

1. Skyrye Design, "Male vs Female Body Proportions Explained Simply". https://skyryedesign.com/?p=112583
2. *Drawing People with* …, "Adult Body Proportions" (O'Reilly). https://www.oreilly.com/library/view/drawing-people-with/9781560101567/xhtml/ch15.html
3. Envato Tuts+, "Human Anatomy Fundamentals: Advanced Body Proportions". https://design.tutsplus.com/articles/human-anatomy-fundamentals-advanced-body-proportions--vector-19869
4. Fiveable, "Proportions of the human body". https://fiveable.me/drawing-foundations/unit-10/proportions-human-body/study-guide/bVYcw96jRRpCUZJk
5. Drafting manual, "Measuring Adult Figure Width With the Head". https://draftingmanuals.tpub.com/14262/css/Measuring-Adult-Figure-Width-With-The-Head-147.htm
6. Fiveable, "Essential human figure proportions". https://fiveable.me/lists/essential-human-figure-proportions
7. RocketBrush, "Shape language in game character design". https://rocketbrush.com/blog/shape-language-in-game-character-design-how-to-make-characters-readable-and-consistent
8. Free Game Sprites, "Sprite silhouette test: character readability". https://freegamesprites.com/es/news/sprite-silhouette-test-character-readability
9. Blake Crosley, "Pixel-Art People: Characters and a Creator on iPhone". https://blakecrosley.com/blog/pixel-art-characters-on-iphone
