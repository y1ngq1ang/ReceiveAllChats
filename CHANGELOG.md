# Changelog

All notable changes to ReceiveAllChats are documented here.

## [3.0.1] - 2026-10-18

### Fixed

- Receive All could open the transfer window without some of the attachments
  when they belonged to a dialogue that had not been opened in the UI yet. The
  aggregated server response is deserialized on its own, without the client's
  message bookkeeping, so messages from unopened dialogues could go missing
  when the window was built. Every dialogue that still reports pending
  attachments is now also fetched through the game's own
  `SocialNetwork.AllAttachmentsFromDialog` and merged with the aggregate by
  message id, with the client-fetched message winning.

## [3.0.0] - 2026-10-17

Port to SPT 5.0. The 3.x line requires SPT 5.0; 2.0.1 remains the final release
for SPT 4.1. Collecting behavior is unchanged.

### Changed

- Client plugin rebuilt for the SPT 5.0 IL2CPP runtime: it is now a BepInEx 6
  `BasePlugin` for `net10.0` compiled against the interop assemblies in
  `BepInEx\interop`, and patching uses `SPTushonka.Reflection.Patching`.
- Instance fields are read as properties (`__instance._receiveAllButton` instead
  of Harmony `___field` injection), as IL2CPP lowerings expose fields that way.
- The 5.0 game code moved several targets, so the patches were re-anchored:
  - `ChatScreen.TransferAll` now takes a single dialogue; the receive-all flow
    replaces it and hands the aggregated messages to `ChatScreen.ProcessRewards`
    (which replaced `CG_TransferAll`).
  - `SocialNetwork.UpdateDialogueList` returns a `Task` instead of taking a
    callback, so the updating flag is cleared once that task completes.
  - The messenger no longer exposes a social-network field on
    `ChatMessageSendBlock`, so the chat screen's instance is reached through the
    `CommonUI` singleton.
  - Server aggregation requests go through the session's `Send<T>` pipeline on
    `EftClientBackendSession` instead of the obfuscated request method, and the
    session is retrieved through the item UI context.
- Server mod compiles against the SPT 5.0 assemblies shipped with the game
  (`SPT_Runtime`) instead of the 4.1.0 NuGet packages; both projects resolve the
  install root from the `SptRoot` MSBuild property (default `C:\SPT-5.0-BE`).
- Install layout follows SPT 5.0: the client goes to `BepInEx\plugins\` and the
  server mod extracts to `SPT_Runtime\user\mods\ReceiveAllChats\`, which now
  exists directly in the game folder.

## [2.0.1] - 2026-08-21

### Fixed

- After a collect that found rewards in exactly one dialogue, that dialogue's
  attachment badge (and the lit "Receive All" button) stayed on until the
  dialogue was selected or the button pressed again when UI Fixes was not
  installed. The mod now recognises its own transfers and finalises the
  counters of every collected dialogue itself, including the single-dialogue
  case; transfers started elsewhere (e.g. the per-message "receive" button)
  are left to vanilla/UI Fixes as before.

### Changed

- Counter updates after the transfer window are now written strictly after
  UI Fixes' own update (extra yield after the operation-queue flush), so the
  two mods can no longer race on the same dialogue counters.
- Declares a soft dependency on UI Fixes (`com.tyfon.uifixes`).

## [2.0.0] - 2026-08-09

Port to SPT 4.1. The 2.x line requires SPT 4.1; 1.1.0 remains the final
release for SPT 4.0.

### Changed

- Client plugin and server mod rebuilt against SPT 4.1 (updated game
  assemblies and SPT server packages). Collecting behavior is unchanged.
- Install layout follows SPT 4.1: the server mod extracts to
  `SPT_Runtime/user/mods/ReceiveAllChats/` (SPT 4.1 renamed the server
  folder from `SPT` to `SPT_Runtime`).

## [1.1.0] - 2026-07-30

First release with an optional server component. Installing the client alone
keeps everything working; adding the server mod makes collecting instant.

### Added

- **Optional server mod** (`SPT/user/mods/ReceiveAllChats/maschine-ReceiveAllChats.Server.dll`):
  serves a single aggregated route that returns the reward mail of *all*
  dialogues in one response. With it installed, "Receive All" costs one
  request regardless of how many dialogues exist — instead of paging the
  dialogue list and querying every dialogue individually (which took roughly
  1.7 s at 43 dialogues, growing with every additional dialogue).
- Advanced config entry `UseServerAggregationRequest` (default on) to disable
  the aggregated request if ever needed.

### Fixed

- **Messenger selection loop:** with 30 or more dialogues, opening the
  messenger caused the dialogue list to be re-requested continuously; the
  selection kept jumping to the first dialogue of every response (with a
  click sound) and other dialogues were barely clickable, while the server
  log was spammed with `/client/mail/dialog/list`. Root cause was the mod
  clearing an internal "list updating" flag unconditionally; it now only
  does so while its own collection is running. All 1.0.x versions are
  affected — updating the client alone fixes it, no server mod required.
- Stale attachment counters (e.g. from expired mail) are now also cleared
  when the aggregated fast path is used.

### Changed

- Without the server mod, the client now refreshes the dialogue list with a
  single request instead of a multi-page loading loop (SPT returns the full
  list in one response), making the fallback path faster as well.
- Config entry `DialogueListIdleGraceMilliseconds` was removed; leftover
  entries in existing configs are ignored.
- Internal restructuring: client/server/shared project layout with a common
  mod GUID (`com.maschine.ReceiveAllChats`) for both components.

### Compatibility

- Verified against Fika 2.3.4 (client, server, headless) and UIFixes at
  source level; no conflicts. On Fika setups the server mod belongs on the
  shared SPT server, and if client mod checking is enabled, add
  `com.maschine.ReceiveAllChats` to the allowed client mods.

## [1.0.4]

- Last client-only release: reliability fixes for collecting across many
  dialogues (dialogue-list paging tuning, request timeouts, stale-counter
  cleanup) and quieter default logging (debug logging off by default,
  tuning entries marked as advanced).

## [1.0.3] and earlier

- Initial client-only releases: "Receive All" collects attachments from all
  loaded chats instead of only the selected one.
