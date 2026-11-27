# ADR-042: Data-Driven Races, Body Types and Character Looks

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amends**: [ADR-014](ADR-014-server-authoritative-config.md) (new `races.json` document),
  [ADR-030](ADR-030-classless-empty-handed-start.md) (`CharacterCreate` carries a structured look),
  [ADR-005](ADR-005-data-driven-sprite-animation-and-combat-vfx-pipeline.md) (character rigs are picked per body type, not per preset)
- **Scope**: `contracts/Carbon.Contracts.Identity`, `contracts/Carbon.Contracts.Entities`, `engine/Carbon.Items/Races`,
  `server/`, `desktop/`, `resources/config`, `resources/art`, `resources/localization`, `tests/`

## Context

The owner asked for a female character and said more races may follow, so it has to be built properly.

What master (`12a3897`) does:

- `CharacterAppearance.Pack` packs four 2-bit traits (hair style, hair colour, face style, skin tone) into one byte.
  That byte is `CharacterCreate.AppearancePreset`, `CharacterSummary.AppearancePreset`,
  `CharacterEnterWorld.AppearancePreset` and the `characters.appearance_preset` column. For remote players it rides in
  `EntitySpawn.ArchetypeId`, a field that means "monster or NPC archetype" for every other kind.
- The palettes (four skin tones, four hair colours, four iris colours) and the hair styles are hard-coded in
  `ModularCharacterOverlays`. The creator has four hard-coded trait rows. `catalog.cvg.json` lists the same
  `base_hero.cvg.json` four times, once per preset.
- The server stores whatever byte the client sends. Any byte is "valid" because every byte unpacks to four traits.
- There is no notion of race or body type. A second body would need a fifth bit, a new byte, a migration and a client
  change; a new race would need all of that again.

## Decision

1. **D1 — `races.json` defines races, bodies and appearance options.** A new server-authoritative config document
   (`resources/config/races.json`, synced to clients per ADR-014, not server-only). Each race has:
   - `id` (`1..65535`, the stored and wire value), `code`, `nameKey`;
   - `bodies[]`: `id` (`0..255`, unique in the race), `code`, `nameKey`, `rig` (an art-relative `.cvg.json` path);
   - `options[]` (at most 16): `code`, `nameKey`, `role`, and `choices[]` (1..256). A choice is either a palette
     (`colors`: 1..4 `#RRGGBB`, base first) that recolours the rig shapes tagged with the option's role, or a rig
     part (`part`) of that role's group. All choices of one option are the same kind;
   - `statModifiers`: flat changes to the `progression.json` derived stats (may be negative, finite, within ±10000).

   Human (race 1) ships with the male body (body 0), its four options in the order the creator already showed them
   (hair style, hair colour, eye colour, skin tone), today's exact palettes, and no stat change. `RaceCatalog`
   (`engine/Carbon.Items/Races`) loads and validates it and fails fast on anything malformed.
2. **D2 — A look is ids and indices.** `CharacterLook` (`contracts/Carbon.Contracts.Identity`) is an unmanaged value:
   race id, body type id and one choice index per option of the race, in the race's order (at most 16). It carries
   no prose and no colours. `RaceCatalog.Check` says whether a look names only things the catalog defines.
3. **D3 — The server never trusts a look.** `AuthService.CreateCharacterAsync` rejects (`invalid_look`, replied as
   `OpStatus.Invalid`) a look whose race, body, option count or any choice index is not in the `races.json` the world
   runs; nothing is stored. A wire look with more than 16 options is refused before that.
4. **D4 — Storage.** `characters.appearance` holds `CharacterLookBinaryCodec` v1:
   `[version=1][raceId u16 LE][bodyType u8][count u8][option u8 × count]`. A look is chosen once, at creation:
   `SaveBatchAsync` never writes it.
5. **D5 — Migration `0022_character_look`** (SQLite and Postgres) adds `appearance`, fills it for every existing row
   from `appearance_preset` through `LegacyAppearanceMigration.ToLook`, and drops `appearance_preset`. Every legacy
   character becomes human (1), male body (0), options `[hairStyle, hairColor, faceStyle, skinTone]` — exactly the
   traits the old `Unpack` returned. The mapping is frozen code: it describes data written before protocol 8.
6. **D6 — Wire, protocol 8** (ADR-038 D5). The packed byte leaves `CharacterCreate` (order 1),
   `CharacterSummary` (8) and `CharacterEnterWorld` (10); their gaps stay. Each gains `RaceId` (`ushort`),
   `BodyType` (`byte`) and `AppearanceOptions` (`byte[]`): `CharacterCreate` 3–5, `CharacterSummary` 11–13,
   `CharacterEnterWorld` 26–28. `EntitySpawn` gains the same three at 22–24 for players; `ArchetypeId` is 0 for
   players and keeps meaning "archetype" for every other kind. No opcode is added. The outbound sink reuses the
   pooled packet's options array when its length matches, so spawns do not allocate in steady state.
7. **D7 — Races are append-only.** A hot reload of `races.json` is rejected (`RaceCatalog.RequireCompatibleWith`)
   when it removes a race or a body type, removes or reorders an option, or removes choices from one, because stored
   looks may use them. Adding races, bodies, trailing options and trailing choices is allowed. A reload re-reads every
   connected player's race stat changes and recomputes their stats.
8. **D8 — Race stats.** `CharacterStatCalculator.Compute` and `ProgressionConfig.ComputeStats` take the race's
   `statModifiers` and add them once, after base and per-level growth and before gear. The server passes them on
   create, on world entry and on every recompute (`PlayerState.RaceModifiers`).
9. **D9 — Client rendering reads the catalog, never a race.** `CharacterLookResolver` turns a look into the body's
   rig (`IVectorArtRepository.TryGetCharacterRig(body.rig)`) and a `CharacterPalette` built by role: `skin` and
   `hair` palettes recolour the tagged shapes, `eye` sets the iris, and the `hair` part option picks the hair part.
   Unknown races fall back to the catalog's first character rig and the default palette. `catalog.cvg.json` lists each
   character rig once, keyed by file.
10. **D10 — A new race is config plus art.** Add the race to `races.json`, its rigs under `resources/art/characters/`
    with entries in `catalog.cvg.json`, and its label keys to `en.ini` / `vi.ini` (`LocalizationTests` derives the
    required keys from `races.json`). No code, wire or schema change. A test race that exists only in a test config
    is created, stored, entered and replicated end to end (`CharacterLookTests`), and the creator offers it
    (`CharacterLookResolverTests`).
11. **D11 — Bodies share one skeleton.** Every body rig uses the same `VectorBoneId` bones and names, so every clip
    (walk, attacks, tool use, swim, dive, downed) and every equipment and held-item attachment works on every body
    without per-clip copies. A body rig differs only in its shapes: it borrows its race's first body's clips with
    `"animationsFrom"`, and its `torsoWidthScale` fits the modular chest armor and cape to its torso. Pauldrons
    follow the arm pivots, held items the rig's `weaponGrip`. `BodyRigParityTests` checks bone names and identical
    poses for every clip. The female human rig (`characters/human_female.cvg.json`, body 1) is config plus art on top
    of this ADR; see [docs/art/character-bodies.md](../art/character-bodies.md).
12. **D12 — Creator.** The creator lists whatever `races.json` holds: race, then the race's body types, then its
    options, with labels from the config's `nameKey`s (`appearance.row = {label}: {value}`). No literal names.

## Alternatives Considered

- **Widen the packed byte to a `ushort` or `ulong`.** Rejected. Every new trait or race would still need code that
  knows the bit layout, and a race with different options cannot share one layout.
- **Option values keyed by code (`{"skin_tone":"umber"}`) on the wire.** Rejected. It sends prose, costs bytes per
  spawn, and still needs validation. Indices into the race's own lists are as stable once options are append-only.
- **A JSON look column.** Rejected for a 5–21 byte versioned blob: the look is never queried by field, and the
  binary form is the same shape as the wire.
- **One rig with body-type blend shapes.** Rejected for now. Separate rig files on one shared skeleton keep the art
  pipeline unchanged (ADR-005) and give the artist full control of each silhouette.

## Consequences

- Protocol 8: older clients are refused at login with `ProtocolMismatch`, as for any bump.
- Existing characters look exactly as before: the palettes moved to `races.json` unchanged, and migration 0022 maps
  each packed trait to the same choice.
- `races.json` is part of the contract with stored data: removing anything from it needs a data migration and a new
  ADR, the same as removing a wire field.
- The hair and eye shapes are still drawn by `ModularCharacterOverlays` from the part and eye indices. Moving them
  into the rigs as layered parts is art work on the same model, with no wire or storage change.
