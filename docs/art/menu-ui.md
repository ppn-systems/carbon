# Account menu UI: research and design rules

This document covers the account menus of the desktop client (ADR-037): Start, Login, Register, Server and Character select.
It records what was learned from well-regarded games and UX references, and the rules the menus follow as a result.
Code lives in `desktop/Carbon.Desktop/Menu/` (widgets, form models) and `desktop/Carbon.Desktop/Scenes/` (screens).
Review the screens with the `menu_*` scenarios of `tools/screenshots/capture.sh`.

## 1. Research findings

### 1.1 Composition of title and menu screens

- **A living world behind the menu.**
  - Stardew Valley opens on a layered mountain scene that pans slowly up into the sky to reveal the title and menu
    ([PC Gamer](https://www.pcgamer.com/our-favorite-title-screens-and-main-menus-in-pc-gaming)). Its sky, clouds and
    mountains are separate layers ([Nexus mod](https://www.nexusmods.com/stardewvalley/mods/7416)).
  - Valheim's menu shows a live view of the world rather than a still image
    ([Steam discussion](https://steamcommunity.com/app/892970/discussions/0/3877093297620122222)).
  - Terraria lets the player drag the sun or moon on the title screen ([wiki](https://terraria.wiki.gg/wiki/Sun)).
- **Quiet backdrops, with an off switch.**
  - Stardew ships a "Menu Backgrounds" option (Standard, Graphical or None)
    ([wiki](https://stardewvalleywiki.com/Options)).
  - Terraria exposes a parallax-strength setting ([wiki](https://terraria.wiki.gg/wiki/Settings)).
  - Xbox Accessibility Guideline 117 asks for a way to pause or hide background animation behind menu text
    ([XAG 117](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/117)).
  - WCAG 2.3.3 names parallax as non-essential motion that users should be able to turn off
    ([W3C](https://w3.org/WAI/WCAG21/Understanding/animation-from-interactions)).
- **Hierarchy.**
  - Hollow Knight's menu API lays a screen out as title, then content, then control buttons
    ([HK modding API](https://hk-modding.github.io/api/articles/menu_api.html)).
  - Hades keeps menus readable and themed, "like an instrument panel"
    ([analysis](https://saschb2b.github.io/game-mechanics/games/hades/)).
  - The common pattern: a large logo in the top third, a compact menu stack below it, and one visually primary action.
- **Parallax technique.** The farthest layer moves slowest, and each nearer layer steps up by a whole-pixel increment
  ([Pixelblog 23](https://raymond-schlitter.squarespace.com/blog/2019/11/12/pixelblog-23-parallax-scrolling)).

### 1.2 Pixel UI, typography and contrast

- **Pixel art and pixel fonts scale by whole multiples only**, with nearest-neighbour filtering
  ([gamedev.net](https://gamedev.net/forums/topic/719580-ui-scaling-problem/5472784/)). The menus therefore draw the
  font at scale 1 and the title at scale 3.
- **PC text should be at least 18 px tall at 1080p** (Xbox Accessibility Guideline 101,
  [XAG 101](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/101)).
- **Contrast.**
  - Text needs 4.5:1, and large text 3:1 (WCAG 1.4.3,
    [W3C](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum)).
  - Component borders and state cues need 3:1 against their surroundings (WCAG 1.4.11,
    [W3C](https://w3.org/WAI/WCAG22/Understanding/non-text-contrast)).
  - Disabled controls are exempt from both.
- **An opaque plate behind text over a moving backdrop** keeps the text readable
  ([XAG 117](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/117)).

### 1.3 Buttons, focus and micro-animation

- **Focus indicator size.** WCAG 2.2 Focus Appearance (2.4.13, AAA) asks for an indicator at least as large as a 2 px
  perimeter, with 3:1 change against the unfocused state
  ([W3C](https://www.w3.org/WAI/WCAG22/Understanding/focus-appearance)).
- **Focus never hidden.** Focus Not Obscured (2.4.11, AA) requires that the focused control is never hidden
  ([W3C](https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/)).
- **Focus must be highly visible, ideally with several cues** such as outline plus fill change
  ([XAG 113](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/113)).
- **Navigation is logical, the same everywhere, and works across mouse, keyboard and gamepad**
  ([XAG 112](https://devdocs.xbox.com/gaming/accessibility/xbox-accessibility-guidelines/112)).
- **Animation timing.** Simple feedback takes about 100 ms, larger changes 200–300 ms, and anything above about
  500 ms feels sluggish ([NN/g](https://nngroup.com/articles/animation-duration)).

### 1.4 Forms

- **When to validate.**
  - Validate when the player leaves a field. Once an error shows, re-check it on every keystroke so it disappears the
    moment the input is fixed. Confirm correct input with a positive mark
    ([Baymard](https://baymard.com/blog/inline-form-validation)).
  - "Reward early, punish late": never flag an empty field before the player tries to send, and give live feedback on
    password creation ([Smashing Magazine](https://smashingmagazine.com/2022/09/inline-validation-web-forms-ux/)).
- **How errors read.** An error sits next to its field, says what is wrong and how to fix it, and keeps the player's
  input. A summary on send helps too ([GOV.UK](https://design-system.service.gov.uk/patterns/validation)).
- **Password rules.**
  - Length matters most. Use no composition rules, offer a show-password option, and allow paste
    ([NIST SP 800-63B-4 §3.1.1.2](https://pages.nist.gov/800-63-4/sp800-63b/authenticators)).
  - Use a generic login error, and add a strength meter on registration
    ([OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)).
- **Show password.** Hide the password by default and give it a toggle (NN/g via
  [CXL](https://cxl.com/blog/password-ux/)).
- **Caps Lock.** Warn about Caps Lock only while the password field has focus
  ([osu! issue](https://github.com/ppy/osu/issues/2234),
  [SitePoint](https://www.sitepoint.com/better-passwords-3-caps-lock-warnings/)).
- **Remember me.** It is opt-in and keeps a revocable server-issued token, never the password
  ([OWASP session cheat sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html),
  ADR-037).

### 1.5 Transitions

- **Durations.** Desktop transitions run 150–200 ms, and anything over 400 ms feels slow. Entering elements decelerate
  and exiting elements accelerate ([Material motion](https://m1.material.io/motion/duration-easing.html)).
- **Reduced motion.** Offer a way to turn motion off
  ([Game Accessibility Guidelines](http://gameaccessibilityguidelines.com/provide-an-option-to-turn-off-hide-background-movement)).

### 1.6 Character creator and server select

- **Character creator.**
  - Rotate buttons let the player see every side of the character, and the preview refreshes on every change.
  - A randomize button must always produce a complete, good-looking character
    ([creator docs](https://legendaryswordsman2.github.io/BlazerTech-Character-Management-System-Docs/docs/character-creator/character-creator-character-preview.html)).
- **Server select.**
  - Status is a plain word: Lost Ark uses Good, Busy, Full and Maintenance
    ([TechRadar](https://www.techradar.com/how-to/lost-ark-servers)).
  - Regions have friendly names ([Albion](https://mmos.com/news/albion-online-europe-server-f2p-launch)).
  - Latency shows as bars or Good/Fair/Poor, never as an address.

## 2. Design rules

The rules below are binding for the account menus. Tests named in brackets check them.

### 2.1 Visual language

1. **Palette.**
   - All menu colours come from `CarbonUiPalette` and `Menu/MenuPalette.cs`.
   - Text reaches 4.5:1 on the field, focused-field and panel fills, and the focus ring 3:1 on the panel
     [`MenuPalette_TextColoursMeetWcagAaOnTheFieldAndPanelFills`].
   - The placeholder grey was raised from about 4.1:1 to 4.8:1 on the focused field.
2. **Type scale: three steps.** The title is at scale 3 with an outline and a warm drop shadow; headings and body are at
   scale 1. Everything is integer scaled.
3. **Panel.**
   - One opaque nine-slice window per screen.
   - A centred heading over a hairline gold divider with a diamond ornament.
   - A status line at the bottom for messages that belong to no single field: the send summary and server replies.
4. **Buttons have six states** (`Menu/MenuButton.cs`): normal, hovered, pressed, focused, disabled and busy.
   - Hover eases in over 120 ms and press over 60 ms. A pressed button moves 1 px down, and its top bevel fades out.
   - Focus draws the shared focus ring and lights the button as if hovered (two cues).
   - Disabled and busy dim the fill and the label. Busy also shows an 8-dot spinner and ignores clicks.
   - [`MenuButtonUpdate_*`, `MenuButtonState_*`]
5. **Focus ring.**
   - A 2 px gold outline drawn 4 px outside the control, with a slow ±15 % alpha pulse.
   - Fields, buttons, checkboxes and the show-password eye all use the same ring (`MenuButton.DrawFocusRing`).
6. **Fields** (`Menu/MenuTextField.cs`).
   - The label sits above the field and turns warm while focused. An icon (user, lock, globe, hero) sits at the left.
   - The placeholder states the rule, for example "3-24 letters, digits or _".
   - The border shows the state: subtle at rest, lighter on hover, gold when focused, red when invalid. Valid input
     gets a green check inside the field.
   - One line under the field holds the error (with a "!" badge), the caps-lock warning, or a hint.
7. **Icons are vector only** (`Menu/MenuIcons.cs`, `GeometryBatch` primitives), drawn with no textures and no
   allocation.

### 2.2 Form behaviour

8. **Tab order follows the visual order and wraps.** Shift+Tab walks backwards.
   - Login: username → password → show password → remember me → Login.
   - Register: username → password → show passwords → confirm → Register.
   - [`LoginFormTab_*`, `RegisterFormTab_*`]
9. **Enter moves to the next empty field; once the form is filled in, Enter sends.** Space activates the focused
   checkbox, eye or button, and Esc goes back. [`LoginFormEnter_*`, `RegisterFormEnter_*`]
10. **Validation timing: reward early, punish late.**
    - A field is checked once the player leaves it, or on send.
    - An empty field is never flagged before a send.
    - A shown error re-checks on every keystroke.
    - The confirm field compares live: while its text is still the beginning of the password it shows nothing; a
      differing character shows "do not match" at once; a full match shows "Passwords match" with a check.
    - On a failed send, focus moves to the first field in error.
    - [`LoginFormErrors_*`, `RegisterForm*Error_*`, `RegisterFormConfirm_*`]
11. **Caps lock.** The window layer reports no lock state, so `CapsLockTracker` infers it from letter case and Shift,
    and the Caps Lock key flips a known state. The warning shows only on a focused password field.
    [`CapsLockTracker*`, `MenuTextFieldCapsWarning_*`]
12. **Show password.**
    - The eye toggles only what is drawn. The text stays in the field's buffer and is never logged, stored or put into
      a different string.
    - Signing in clears the field and hides it again.
    - On Register one toggle reveals both password fields.
    - [`MenuTextFieldToggleReveal_*`]
13. **Password strength meter** (Register only; `Menu/PasswordStrength.cs`).
    - Length sets the base level: 6–7 characters weak, 8–11 fair, 12–15 good, 16 or more strong.
    - Three or more kinds of character lift the level one step.
    - A single repeated character, a straight run, or the username inside the password counts as weak.
    - It shows as four segments plus a word. It is advice only: the server's rules (`AccountRules`) decide.
    - [`PasswordStrengthEvaluate_*`]
14. **Remember me** carries a note under its label: "Keeps a sign-in token here, never your password." The ADR-037
    guarantee is unchanged: `MenuFlow.RememberAsync` has no password parameter.

### 2.3 Backdrop, screens and motion

The rules here and in 2.4 apply as each screen is reworked.

15. **The menu backdrop is a slow, dimmed pan over a real generated world**, behind an opaque panel
    (`Menu/MenuBackdrop.cs`).
    - **Where it looks.** The view centres on the most scenic spot within 8 chunks of the spawn: the most distinct
      biomes, plus a bonus for 8–45 % water (`MenuBackdropSite`).
    - **How it moves.** The camera traces a closed ellipse once every 6 minutes, under 16 world px/s, so only a
      handful of chunks are ever needed (`MenuBackdropClock`).
    - **How it is dimmed.** A dusk-to-night tint cycles every 3 minutes, a smooth top and bottom gradient keeps the
      title readable, and fireflies drift over it.
    - **Where the world comes from.**
      - The world is generated once, on a worker thread, by a generator the session builds with the synced building
        catalog (`IGameConfigSource.TryCreateWorldGenerator`). The client has no catalog of its own.
      - Chunk meshes are baked one per frame, then the terrain fades in over 0.8 s.
      - Until the first config sync (a fresh launch, before signing in), and if generation fails, the backdrop is a
        vector night sky with two whole-pixel parallax ridges instead.
    - **Who owns it.** `MenuFlow` owns the backdrop, so it survives screen changes and never restarts its pan. Its
      meshes are freed on entering the world.
    - **Rules it keeps.**
      - It never blocks input.
      - It pauses while its scene is inactive (only the active menu scene advances it).
      - It allocates nothing per frame once warm.
      - `CARBON_MENU_MOTION=0` holds it still.
16. **Screen transitions keep the world and title still and bring only the panel in** (`Menu/MenuTransition.cs`).
    - Menu to menu: no fade to black. The new panel slides up 14 px while its contents rise out of the panel body,
      over 220 ms with a cubic ease-out.
    - Entering or leaving the world keeps the scene manager's 150 ms fade.
    - With `CARBON_MENU_MOTION=0` the panel appears at once.
    - Input is never blocked during a transition.
    - The title sits at the same place on every screen (`MenuScene.TitleTop`), so it never jumps.
    - A footer line names the keys for each screen.
    - On Start, Up/Down or Tab walk the buttons and then Quit, wrapping; Enter or Space activates the focused one.
17. **Server cards** (`Scenes/ServerSelectScene.cs`, `Menu/ServerStatus.cs`).
    - Each card shows the friendly name, a status word (Checking / Online / Can't reach this server right now), a
      connection word and four signal bars.
    - Quality thresholds: excellent < 60 ms, good < 120 ms, fair < 250 ms, otherwise weak.
    - A card never shows an address, a port or milliseconds.
    - Each listed server is checked once per visit with a plain TCP connect that is closed at once (`TcpServerProbe`,
      2 s timeout). It is never repeated on a timer.
    - The selected server shows a gold card with a check mark.
    - Up/Down or Tab walk the cards and then Back, wrapping; Enter picks.
18. **The character creator** (`Scenes/CharacterSelectScene.cs`).
    - **Turntable** (`Menu/HeroTurntable.cs`).
      - The turn buttons, Left/Right or Q/E turn the hero a quarter at a time. Four dots under the pedestal show the
        facing.
      - Left alone, the turntable turns a quarter every 2.5 s so every side is shown. A manual turn pauses that for
        6 s.
      - With motion off it only turns when asked.
    - **Layout.** The panel is 920 × 580.
      - Left column: the selector rows, then Randomize at the bottom.
      - Middle: the turntable.
      - Right column: the name, Create, and a slim lore strip with the fresh-start note.
    - **Selector rows** (`Menu/MenuPicker.cs`): race, body type, then the race's options from `races.json`
      (ADR-042), 36 px apart.
      - Each row reads `◀ LABEL: value  n/N ▶`; the arrows, or Left/Right on the focused row, step and wrap.
      - A row with a single choice is dimmed and does not cycle.
      - The mouse wheel over the option rows scrolls options that do not fit.
      - Tab walks the rows and then the name field.
      - Randomize picks every option, so the result is a complete look.
    - **Body type** (`Menu/MenuToggle.cs`): when the race has exactly two bodies, the body row is a two-state toggle,
      the chosen half lit gold. Otherwise it is a selector row.
    - **Hero name.**
      - The field mirrors the server's rule (`AccountRules.HeroNameError`, matching `AuthService.IsValidIdentifier`):
        2–20 ASCII letters, digits or `_`.
      - A refused key, such as a space or an accented letter, is explained under the field for 3 s.
      - A name the server will take gets a check and "The server will accept this name."
      - Create with an invalid name is held back with the rule, instead of a round trip to the server.
    - **Roster.** Cards light on hover. An empty slot shows a "+" and "Empty slot - new hero".

### 2.4 Localization and review

19. **Every visible string is a key in `[login]` / `[character_creator]`**, in both `en.ini` and `vi.ini`. The
    localization literal guard enforces this.
20. **Every screen is reviewed in en and vi, at 1280×720 and 1920×1080.**
    - The `menu_*` scenarios capture each screen.
    - `CARBON_UI_PREVIEW=form-feedback` captures the error, caps-lock and strength states.

## 3. Known gaps

- **Password length.** NIST SP 800-63B-4 asks for a 15-character minimum for single-factor passwords. The server
  still accepts 6 (`AccountRules.MinPassword`). Raising it changes the auth rules and needs its own ADR; the strength
  meter only nudges players toward longer passwords.
- **Gamepad.** Navigation uses keyboard focus today. Gamepad mapping onto the same focus order is future work.
