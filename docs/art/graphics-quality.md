# Graphics quality: research and rules

How Carbon's world gets its look, and what each effect costs. This file records the research (with sources) and the
rules every visual change follows. The pipeline decision itself is [ADR-041](../adr/ADR-041-world-light-map-and-single-post-pass.md).

Carbon draws the world as flat vector art: CPU-tessellated vertex-colour triangles (ADR-005), cached terrain chunk
meshes, Y-sorted props and canopies. There are no sprite textures and no normal maps. The client targets integrated
GPUs and must stay usable on Mesa llvmpipe (CI and the screenshot harness).

## What the reference games do

Few studios have published how their rendering works. The most useful primary write-up is Graveyard Keeper's. It is
not on the original list, but it is top-down, Y-sorted and has a day/night cycle, like Carbon. Claims taken only from
a search-result summary are marked *(snippet)*.

| Game | What is known | Source |
| :--- | :--- | :--- |
| Sea of Stars | Dynamic lighting drove the whole asset pipeline; one technical artist worked on it full time from 2018. The sun and moon change the time of day live. No technique details published. | [MobileSyrup interview](https://mobilesyrup.com/2023/09/11/sea-of-stars-level-design-sabotage-studio-thierry-boulanger-interview/) |
| Eastward | "A 3D game with a 2D perspective": layered assets rebuilt in 3D with hand-painted bump maps; atmosphere, weather and time of day through shaders. A LUT and a CRT filter. | [Game Developer](https://www.gamedeveloper.com/art/eastward-s-creators-share-insights-on-making-pixel-art-adventures), [80.lv](https://80.lv/articles/eastward-charming-chinese-pixel-art-adventure) |
| Octopath Traveler (HD-2D) | Sprites in 3D sets; a point light gives sprites shadows; bloom, volumetric fog, tilt-shift and depth of field on top. | [Wikipedia: HD-2D](https://en.wikipedia.org/wiki/HD-2D) |
| Graveyard Keeper | Ambient follows a time-of-day gradient that turns blue at night; torches and ovens are point lights; ~10 artist LUTs swapped over the day; shadows are rotated sprites (sun + 3 nearest lights); wind shifts x by a function of y with a per-position phase; stacked fog sprites cover object bottoms more than tops. | [Game Developer](https://www.gamedeveloper.com/programming/graveyard-keeper-how-the-graphics-effects-are-made) |
| Don't Starve | Light at a point is the ambient magnitude plus every light whose radius reaches it *(snippet)*. | [Klei forums](https://forums.kleientertainment.com/forums/topic/138533-solved-how-do-light-intensityradiusfalloff-work/) |
| Stardew Valley | No official source. The common community recipe: a dark-blue layer plus white radial-gradient lights, multiplied over the scene. | [Solar2D forum](https://forums.solar2d.com/t/lighting-effects-using-filters-or-composites/352370) |
| RimWorld (Realistic Lighting and Shadows mod) | Sun and moon positions from planetary tilt; shadows point away from the sun with tunable length *(snippet)*. | [mod page](https://catalogue.smods.ru/?p=386478) |
| Hyper Light Drifter, Children of Morta | Only colour and silhouette direction is documented, not the lighting method. | [Game Developer](https://gamedeveloper.com/business/the-ultra-modern-stylings-of-hyper-light-drifter) |

## Techniques, chosen approach and cost

Costs are for integrated GPUs (shared memory, so bandwidth and fill rate bind first) and for llvmpipe (every
full-screen pass is CPU work).

### 1. Lighting

- **Technique (chosen):** a screen-space light map. Clear an offscreen buffer to the ambient colour of the hour, add
  lights into it, multiply it over the scene in one pass. Slembcke calls it "extremely fast"; its one limit is that
  it is colour × light, with no normals ([Slembcke](https://www.slembcke.net/blog/2DLightingTechniques/)). Godot's
  CanvasModulate plus additive lights is the same model ([Godot docs](https://docs.godotengine.org/en/stable/tutorials/2d/2d_lights_and_shadows.html)).
- **Why it beats the old pass:** the old path darkened the world with a multiply quad, then *added* light on top,
  which washed lit areas out to orange. A multiply light map reveals the art's own colours inside a torch's pool, so
  night reads as lit, not tinted.
- **Overbright:** the light map stores half the light (RGBA8 clamps at 1), and the composite doubles it, so a torch
  can push the scene up to 2×.
- **Light shape:** one triangle fan per light with a radial colour gradient (centre to transparent rim), two fans
  per light for a peaked falloff. This replaces 10 stacked 28-segment circles (290 vertices) with 50.
- **Rim light for vector art:** no normals exist, so the cheap fake is per-vertex (tint the lit-side edge at
  tessellation). Not adopted yet.
- **Cost:** the light map is **half resolution**. Unity's 2D renderer defaults to 0.5× for "good performance with
  almost no noticeable artifact" ([Unity docs](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@12.1/manual/Lights-2D-intro.html)).
  Light radius is fill, so radii stay capped.

### 2. Colour grading, bloom, vignette

- **Grading:** Graveyard Keeper and Eastward use LUTs. A single 3D LUT lookup replaces any grading chain, but 8-bit
  LUTs band and need sample-centre care ([GPU Gems 2 ch. 24](https://developer.nvidia.com/gpugems/GPUGems2/gpugems2_chapter24.html)).
  Carbon starts with analytic lift/gain/contrast/saturation keyframed by hour (`TimeOfDayGrading`): no asset, testable
  on the CPU, and a LUT can replace it later without changing the pass.
- **Night:** cool lift, desaturated (the eye loses colour first in the dark), stronger vignette. Torchlight is then
  the only warm, saturated colour on screen.
- **Bloom:** the reference chains are the dual filter (5-tap down, 8-tap up, ~7% of the bandwidth of a linear blur on
  Mali; [ARM SIGGRAPH 2015](https://community.arm.com/cfs-file/__key/communityserver-blogs-components-weblogfiles/00-00-00-20-66/siggraph2015_2D00_mmg_2D00_marius_2D00_notes.pdf))
  and the 13-tap / tent chain with a mix of 0.03–0.15 ([LearnOpenGL](https://learnopengl.com/Guest-Articles/2022/Phys.-Based-Bloom)).
  Each costs 6–8 extra passes. In a 2D world nearly everything that should glow is a light, so Carbon gathers bloom
  from the **half-res light map's overbright part** with 12 bilinear taps inside the composite: no extra pass, no extra
  draw call.
- **Vignette:** a `smoothstep` on radial distance in the same pass.
- **Chromatic aberration:** two extra texture reads per pixel for little gain in a top-down game. Not adopted.
- **Dither:** a half-LSB noise in the composite stops dark night gradients banding in 8-bit.

### 3. Shadows

- Graveyard Keeper: rotated shadow sprites from the sun and the 3 nearest lights, ~10 shared shapes, rotated in the
  vertex shader. HD-2D: real point-light shadows.
- **Chosen:** a contact shadow (flattened ellipse, radial alpha gradient) under every actor, tree and building, plus a
  sun shadow skewed by the hour (long at dawn and dusk, short at noon, gone at night).
- **Cost:** a few triangles in the existing batch. Draw shadows before Y-sorted props so they never darken what stands
  on them.

### 4. Foliage sway and grass

- Graveyard Keeper: x shifted by a function of y (tips move, roots stay), phase by position, and the player pushing
  through grass. prime31: move only the top vertices with a spring plus a sine wind ([prime31](https://prime31.github.io/grass2d)).
- **Chosen:** canopy sway keyed to a shared wind field (gusts, stronger in rain), with deterministic per-position phase,
  so it is a pure function of (time, position, weather) and testable.
- **Cost:** a few ALU ops per vertex on the CPU; nothing uploaded beyond the dynamic batch already in use.

### 5. Water

- No developer source exists for Sea of Stars' or Eastward's water. References: a shoreline band with foam from
  thresholded noise ([2D water tutorial](https://gamedevserj.github.io/posts/2d-water-shader-tutorial)), two-phase flow
  blending ([Catlikecoding](https://catlikecoding.com/unity/tutorials/flow/texture-distortion/)).
- **Chosen:** depth tint baked per vertex (ADR-032 tiers), animated foam along shore edges and caustic glints that fade
  with depth; keep the underwater filter (#149) as the only full-screen water effect.
- **Cost:** water is large, so no per-pixel noise on Low.

### 6. Weather and ambient life

- Overdraw, not count, is the limit; overdraw consistently above 2.0 starts to hurt
  ([Imagination](https://blog.imaginationtech.com/improving-particle-systems-on-powervr/)). Off-screen low-res particles
  cut particle pixels from 46.9 M to 3.5 M in GPU Gems 3 ([ch. 23](https://developer.nvidia.com/gpugems/gpugems3/part-iv-image-effects/chapter-23-high-speed-screen-particles)).
- **Chosen:** rain as thin two-triangle streaks (near-zero fill); fireflies and other glowing life write into the
  light map, so they light the ground around them; birds and dust motes as a handful of CPU agents near the camera.

### 7. Fog

- Graveyard Keeper covers object bottoms more than tops; Unity fades layers toward a horizon colour
  ([Unity](https://unity.com/how-to/use-2d-lights-unity-set-mood)).
- **Chosen later:** one or two large, low-alpha fog polygons in misty biomes. Each full-screen alpha layer is pure
  overdraw, so at most two.

## Rules

1. **Cosmetic only.** No gameplay value reads a visual setting; the server never sees one.
2. **One composite pass.** Grading, vignette and bloom live in the single composite shader. A new full-screen effect
   joins that shader; it does not add a pass.
3. **Lights go in the light map.** Anything that glows (torches, forges, fireflies, glowing items, projectiles) is a
   `PointLight`, never an additive sprite on top of the lit scene.
4. **Draw calls hold.** A change may not raise the per-frame draw count of a scene in the perf table; move geometry
   into an existing batch instead.
5. **Zero steady-state allocation.** Targets are created once per viewport size; per-frame code uses spans, fixed
   arrays and cached uniform locations.
6. **Presets switch passes off.** Low runs no offscreen targets; Medium adds the light map, grading and vignette; High
   adds light bloom. The renderer reads only `PostPassPlan`, so a lower preset cannot run a higher preset's pass.
7. **Default by GPU.** Software rasterizers get Low, integrated GPUs Medium, everything else High
   (`GraphicsQualityPolicy.DefaultFor`); the player's choice in Settings wins, and `CARBON_GRAPHICS_QUALITY` overrides
   both for captures.
8. **Measure.** Every visual PR carries before/after screenshots at 1280×720 for day, dusk, night, rain, water,
   village and abyss, and a before/after `CARBON_FRAME_STATS` table.

## Baseline (master `12a3897`)

Measured with the scenarios in `tools/screenshots/graphics_perf.txt` (`CARBON_FRAME_STATS=360`) through
`tools/screenshots/capture.sh` (real client and local server, xvfb with
Mesa llvmpipe, 1280×720). The ranges cover the two steady-state 120-frame windows after the loading window.

- **CPU ms** is update + render on the loop thread.
- **Frame ms** is the wall-clock interval between frames. It includes the buffer swap, so it also counts llvmpipe's
  rasterization, the "GPU" cost of fill.
- **Alloc** is managed bytes per frame on the loop thread (HUD strings and packets; not zero on master).

| Scenario | What it shows |
| :--- | :--- |
| `day` | Lush plains, 11:00, clear |
| `dusk` | Spawn, 19:12 |
| `night` | Spawn, 23:00 |
| `rain` | Spawn, 11:00, rain |
| `water` | Seed 42 coast, swimming |
| `village` | River village, 11:00 |
| `village_night` | Snow village, 22:00 |
| `abyss` | Seed 42 abyss, diving |

| Scene | Draws/frame | CPU ms p50 | Frame ms p50 | Alloc B/frame |
| :--- | ---: | ---: | ---: | ---: |
| `day` | 11 | 3.4–3.5 | 11.6–11.7 | 242–910 |
| `dusk` | 12 | 4.6–4.8 | 12.7–13.9 | 167–314 |
| `night` | 12 | 4.2–5.1 | 12.9–15.3 | 200–584 |
| `rain` | 12 | 4.1–5.0 | 11.9–13.4 | 161–212 |
| `water` | 10 | 2.8–3.0 | 9.7–10.5 | 78–217 |
| `village` | 11 | 4.2–4.4 | 15.6–17.6 | 1–25 |
| `village_night` | 11 | 3.6–4.1 | 12.6–13.6 | 7–293 |
| `abyss` | 9 | 2.7–3.2 | 20.2–23.4 | 7–13 |

Every visual PR adds its before/after rows to its description. llvmpipe is a CPU rasterizer, so a full-screen pass
costs milliseconds there, while on an integrated GPU it costs a fraction of one. That is why software rasterizers
default to Low.
