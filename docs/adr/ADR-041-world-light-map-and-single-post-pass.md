# ADR-041: World Light Map and a Single Post Pass

- **Date**: 2026-11-27
- **Status**: Proposed
- **Amends**: [ADR-005](ADR-005-data-driven-sprite-animation-and-combat-vfx-pipeline.md) (how the vector world is lit
  and composited); [ADR-023](ADR-023-client-localization-and-vietnamese-font.md) (new `[display] graphics_quality`
  setting)
- **Scope**: `graphics/Carbon.Graphics.OpenGL`, `graphics/Carbon.Graphics.Batching`, `desktop/Carbon.Desktop.Rendering`,
  `desktop/Carbon.Desktop`, `resources/localization`, `tests/desktop`
- **Research**: [docs/art/graphics-quality.md](../art/graphics-quality.md)

## Context

The owner asked for a better-looking world, night above all, without losing performance on integrated GPUs.

What master (`12a3897`) does:

- The world draws straight into the back buffer (`desktop/Carbon.Desktop.Rendering/World/WorldSceneRenderer.cs`,
  `Render`). Day and night come from one full-screen quad with a multiply blend (`Atmosphere.DrawAmbient`), then
  lights are *added* on top (`Atmosphere.DrawLights`).
- Adding light on top of a darkened scene washes lit areas toward the light's colour instead of revealing the art's
  own colours, so night reads as "the same picture, tinted dark".
- Each light is 10 stacked 28-segment circles: 290 vertices per light.
- `graphics/` has no framebuffer abstraction. `GlStateCache.BindFramebuffer` exists, but nothing renders offscreen.
- Villages are built from real structures (ADR-025, ADR-027). Their houses are `BuildingGrid` rooms drawn by
  `RoofRenderer`, not landmark pieces, so a village emits no light at night.

## Decision

1. **D1 — Render targets.** `graphics/Carbon.Graphics.OpenGL/Textures/RenderTarget2D.cs`: a framebuffer object with
   one linear-filtered RGBA8 colour texture, no depth. It is created once per size and recreated only when the
   viewport changes.
2. **D2 — Light map.**
   - On Medium and High, the world renders into a full-resolution scene target.
   - Lights render additively into a **half-resolution** light map cleared to the ambient colour of the hour.
   - The light map stores **half** the light, and the composite doubles it, so a torch can light the scene past 1×
     even though RGBA8 clamps at 1.
   - A light is two radial-gradient fans (`GeometryBatch.FillRadialGradient`), 42 vertices in place of 290.
3. **D3 — One composite pass.**
   - A single full-screen triangle (`WorldPostPipeline`, an empty VAO and `gl_VertexID`) writes the result to the back
     buffer:
     - scene × light map;
     - High only: light bloom from 12 bilinear taps of the light map's overbright part;
     - a hue-preserving luminance shoulder;
     - the time-of-day grade (lift, gain, contrast, saturation);
     - a vignette;
     - a half-LSB dither.
   - Saturation stays near full where a point light (not the ambient) lights the pixel, so torchlit pools keep their
     colour in a desaturated night.
   - Any later full-screen effect (fog, a LUT) joins this shader. It does not add a pass.
4. **D4 — Bloom from the light map.** In a 2D world nearly everything that glows is a light. So bloom is gathered
   from the light map inside the composite, with no bright-pass, downsample chain or extra draw call. A dual-filter
   chain stays available if scene-wide bloom is wanted later.
5. **D5 — Presets.** `GraphicsQuality` Low / Medium / High maps to a `PostPassPlan`. The renderer reads only the plan,
   so a lower preset cannot run a higher preset's pass:
   - **Low:** no offscreen targets. The old ambient multiply quad and additive glows remain, drawn with the cheaper
     gradient fans.
   - **Medium:** light map, grading and vignette.
   - **High:** Medium plus light bloom.
6. **D6 — Default by GPU.**
   - `GraphicsQualityPolicy.DefaultFor` reads `GL_RENDERER`: software rasterizers (llvmpipe, SwiftShader…) get Low,
     integrated GPUs (Intel, Radeon Graphics APUs, mobile) get Medium, and everything else gets High.
   - The Settings window (Display tab) saves the player's choice as `[display] graphics_quality = auto|low|medium|high`.
   - `CARBON_GRAPHICS_QUALITY` overrides both, for captures and perf runs.
7. **D7 — Lights collected.**
   - Village rooms (`RoofRenderer.CollectLights`) add a soft window glow, light spilling from the door, and a hearth or
     forge fire at the stove.
   - The well has a lantern.
   - On Medium and High, night fireflies are added as small lights, so they light the ground around them.
8. **D8 — Cosmetic only.** No gameplay value reads a visual setting, and nothing is sent to the server. `graphics/`
   stays client-only.

## Alternatives Considered

- **Keep additive lights on a multiplied scene, but use a `DstColor, One` blend.** This needs no offscreen target. It
  gives `scene × ambient × (1 + light)`, so a torch in a dark night can never reach the art's real colour. Rejected.
- **Normal-mapped sprites (Eastward, Graveyard Keeper).** Carbon's art is flat vector shapes with no normals. Painting
  or deriving normals for every `.cvg.json` is an art-pipeline change far larger than the gain. Rejected for now. A
  per-vertex lit-edge tint is the cheap alternative (research doc, section 1).
- **A 3D LUT for grading.** One lookup, artist-friendly. But it needs a 3D texture asset per hour band, and 8-bit LUTs
  band. Analytic curves are tested on the CPU and cost nothing to ship. A LUT can replace them later inside the same
  pass.
- **A dual-filter or 13-tap bloom chain.** 6–8 extra passes and draw calls, against the draw-call budget. The light-map
  bloom covers what glows in this game.
- **Light map at full resolution.** Lights are smooth gradients, so half resolution is invisible after bilinear
  sampling. Unity's 2D renderer defaults to 0.5× for the same reason.

## Consequences

- **Positive:**
  - Night shows the art's colours inside warm light pools, under a cool, desaturated, vignetted ambient.
  - Villages glow from doors, windows, forges and the well, and fireflies light the grass.
  - The per-light vertex cost drops about 7×.
  - The draw count holds on Medium and High: the light map and the composite replace the ambient and light batches,
    and the weather overlay (clouds, rain, fireflies, interior dimming) joins the scene batch, so it is lit and graded
    too.
- **Cost:**
  - Two offscreen targets: 1280×720 plus 640×360, RGBA8, about 4.6 MB.
  - One full-screen composite with 2 texture reads (14 on High).
  - Under a software rasterizer, each target switch flushes the pipeline, which roughly doubles CPU frame time there.
    That is why such GPUs default to Low.
- **Debt:**
  - On Low the weather overlay still draws after the lighting, as before.
  - Pass 6 nameplates and the HUD are deliberately left ungraded.
