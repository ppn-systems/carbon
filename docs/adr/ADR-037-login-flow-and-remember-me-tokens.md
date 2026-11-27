# ADR-037: Login Flow and Remember-Me Tokens

- **Date**: 2026-11-27
- **Status**: Accepted
- **Scope**: `contracts/Carbon.Contracts.Items`, `server/` (identity service, handlers, persistence), `desktop/` (menu scenes, session), `resources/client/servers.ini`, `resources/localization/`, `tests/`

## Context

The owner's feedback, translated: "Fix the login properly: first the username field, then the password, a 'remember password'
checkbox, then Login. Before that there is a screen: if already logged in, show the account name with Continue, then
Log out, then Server. If not logged in, show Login and Register. Keep them separate."

How sign-in worked before this ADR:

- **One screen did everything.** `LoginScene` had Sign-In / Create-Account tabs on one panel, a show/hide password
  toggle and a raw `host:port` gateway editor. It had no start screen and no way to stay signed in.
- **Credentials.** `DesktopOptions` reads `CARBON_USERNAME` / `CARBON_PASSWORD` (defaults `adventurer` / `carbon123`)
  from the environment. `CARBON_AUTO_LOGIN=1` makes `MultiplayerSession.InitializeAsync` log in or register with
  them and enter the world. Nothing was written to disk.
- **Wire.** `AuthLogin` (`0x0102`) and `AuthRegister` (`0x0101`) carry the username and the plaintext password over the
  X25519-encrypted session. The server answers `AuthResult` (`0x0110`). `AuthService` verifies a PBKDF2 hash, throttles
  failures per account and per address, and has no notion of a session or refresh token.
- **Reconnect.** `MultiplayerSession` keeps the last username and password in memory to sign in again after a drop.

"Remember password" cannot mean storing the password: a stolen file would give away the account, and a password often
opens other services too.

## Decision

1. **D1 — Server-issued remember-me tokens.** A token is 32 random bytes (`RandomNumberGenerator`). The server stores
   only `SHA-256(token)` in a new `remember_tokens` table (migration `0020_remember_tokens`, SQLite and PostgreSQL:
   `token_hash` primary key, `account_id` referencing `accounts` with cascade delete, `created_at_utc`,
   `expires_at_utc`). A token signs in for 30 days (`AuthService.RememberTokenLifetime`). One account keeps at most 10
   tokens, one per remembered device; the oldest is dropped (`MaxRememberTokensPerAccount`). A plain hash is enough,
   with no salt or slow KDF, because the token is uniformly random, unlike a password.
2. **D2 — Four opcodes in `0x40A0–0x40A3`** (allocated to this work; `0x40A4–0x40A7` stay free). They live in
   `Carbon.Contracts.Items`, because the opcode-band guard ties each band to its assembly:
   - `RememberTokenRequest` `0x40A0` C2S, permission `USER`: issue a token for the signed-in account.
   - `RememberTokenResult` `0x40A1` S2C: `Status` (`OpStatus`), `Token` (`byte[]`), `ExpiresAtUnixSeconds`. The token is
     bytes, not text, so it adds no string field to an S2C packet (ADR-023 D4 guard).
   - `TokenSignInRequest` `0x40A2` C2S, permission `NONE`: `Token`, `ClientProtocolVersion`. The answer is the existing
     `AuthResult`. An unknown, expired or revoked token gets `InvalidCredentials`, a banned account `Locked`, an old
     client `ProtocolMismatch`. On `Ok` the handler elevates the connection and sends the config manifest exactly like
     a password login.
   - `RememberTokenRevoke` `0x40A3` C2S, permission `NONE`: `Token`. It deletes the row and always answers `Ok`, so the
     reply does not say which tokens exist. Holding the token is the authority.
   Existing packets are unchanged, so `CarbonProtocol.Version` stays the same.
3. **D3 — The client stores the token, never the password.** `FileRememberedAccountStore` writes
   `%LOCALAPPDATA%/Carbon/account.ini` (or `~/.local/share/Carbon/account.ini`; override `CARBON_ACCOUNT_FILE`) with
   the username, the server host and port, the token (Base64Url) and its expiry. On Linux and macOS the file is created
   with mode `0600` through a temp file and a rename, so it is never readable by others. On Windows it inherits the
   per-user ACL of `%LOCALAPPDATA%`. The token is not additionally wrapped with DPAPI or libsecret: it is revocable,
   expires, works only against this game's server, and the server holds only its hash. `MenuFlow.RememberAsync` takes
   no password parameter, so the password cannot reach the store.
4. **D4 — Log out deletes and revokes.** The start screen's Log out, the character screen's LOGOUT and the in-game
   LOGOUT all delete the local file first, then revoke the token on the server (best effort; a failed revoke leaves a
   token that expires by itself). Logging in again without "Remember me" also forgets an earlier remembered account.
5. **D5 — Separate screens.** All text comes from the `[login]` locale section, which is rewritten for the new screens.
   The old single-panel login is gone. All screens use the existing nine-slice skin through a shared `MenuScene` base.
   - **Start** (`StartScene`, the first scene). With a remembered account it shows "Signed in as {name}" and three
     separate buttons: Continue (token sign-in, then character select), Log out and Server. Without one it shows Login,
     Register and Server. Both states show the chosen server's name. If Continue gets `InvalidCredentials`, the
     remembered account is forgotten and the player is asked to log in again.
   - **Login** (`LoginScene`). Username, then masked password, the "Remember me" checkbox, Login and Back. Tab moves
     username → password → checkbox → Login (`LoginForm.TabOrder`), Space toggles the checkbox and Enter submits. Errors
     come from `AuthStatus`: wrong password, banned, version out of date, server unreachable.
   - **Register** (`RegisterScene`). Username, password, confirm password, Register and Back. `RegisterForm.Validate`
     mirrors `AuthService` (3–24 of `[A-Za-z0-9_]`, password 6–128) and blocks a confirmation mismatch. The server
     stays authoritative.
   - **Server** (`ServerSelectScene`). The servers of `resources/client/servers.ini`, shown by friendly name. A start
     endpoint that is not listed (`CARBON_SERVER_HOST` / `CARBON_TCP_PORT`) appears as "Custom server". A typed address
     field exists only with `CARBON_DEV_SERVERS=1`.
6. **D6 — Reconnect with the token.** A session signed in by token keeps the token (not a password) in memory and
   reconnects with `TokenSignInRequest`.
7. **D7 — Developer and screenshot paths keep working.** `CARBON_AUTO_LOGIN=1` and `CARBON_CAPTURE_SCENE=characters` run
   from the start screen. `CARBON_CAPTURE_SCENE=start|login|register|server` captures each screen, and
   `CARBON_UI_PREVIEW=signed-in` shows the signed-in start screen with a sample account without touching disk.

## Alternatives Considered

- **Store the password encrypted with the OS key (DPAPI, libsecret).** Rejected because a token is cheaper here and
  safer: it can be revoked one device at a time, it expires, and a leak never exposes the password. It also avoids a
  new package (`System.Security.Cryptography.ProtectedData`) and a libsecret native dependency.
- **Append a "remember" flag to `AuthLogin`.** Rejected because changing a packet layout needs a protocol version bump
  for every client. Separate opcodes are additive.
- **Send the token as a string.** Rejected because S2C packets must not gain text fields (ADR-023 D4).
- **Rotate the token on every use.** Deferred: one token per device with a 30-day expiry and revoke on log out covers
  the request. Rotation would need a second reply on each sign-in.

## Consequences

- Players can stay signed in on a device without the password ever touching disk. Log out on any screen forgets them.
- A stolen `account.ini` signs in as that player until its expiry or until they log out on that device. That is the
  accepted risk of "remember me", and the file is owner-only.
- A ban takes effect on remembered devices at their next sign-in (`Locked`).
- `IAccountStore` gains `FindByIdAsync`, because token sign-in re-checks the account by id.
- Servers created before this ADR get `remember_tokens` on their next start (migration `0020`).
