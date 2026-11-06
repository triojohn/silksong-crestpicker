# CrestPicker

BepInEx 5 plugin for Hollow Knight: Silksong. When the chapel maid in
`Bonetown` finishes her dialogue, a list of crests opens and the player picks
which crest to use.

GUID: `io.github.triojohn.crestpicker`. Version: `1.0.0`.

## What it does

The vanilla chapel maid dialogue hands out a crest and ends. CrestPicker appends
its own action to the head of the dialogue FSM's `End` state, so the offer shows
up exactly once the conversation is over. The offer is shown once per save slot;
cancelling it does not consume the flag, so the maid offers again on the next
talk.

## How it works

- The Harmony postfix on `PlayMakerFSM.Start` looks for
  `Bonetown` / `Churchkeeper` / FSM `Conversation` and prefixes its `End` state
  with a custom `OfferCrestAction`. Every dialogue branch of that FSM funnels
  into `End`, so one injection covers all of them.
- The crest list is `ToolItemManager.GetAllCrests()` filtered to
  `IsBaseVersion && !IsHidden` — the seven normal crests.
- By default the list is further filtered by `ToolCrest.IsUnlocked`, so only
  crests the save already has appear. Set `GrantAllCrests = true` in the config
  to offer all seven regardless of save state.
- Applying a crest calls `ToolItemManager.SetEquippedCrest(id)` followed by
  `SendEquippedChangedEvent(force: true)`; without the second call the hero keeps
  the previous moveset.
- The once-per-slot flag is stored in `BepInEx/config/CrestPicker/state.json`,
  keyed by `profileID` (the game's save slot index).
- `F9` opens the picker anywhere in game (test hotkey, configurable).

## Config

`BepInEx/config/io.github.triojohn.crestpicker.cfg`

| Section | Key | Default | Meaning |
|---|---|---|---|
| General | `GrantAllCrests` | `false` | Offer all seven crests instead of the unlocked ones |
| General | `EnableSnailChoice` | `true` | Show the offer after the chapel maid dialogue |
| General | `TestHotkey` | `F9` | Hotkey that opens the picker for testing |
| Probe | `Enabled` | `false` | Log dialogue FSMs and item/tool name lists (diagnostics) |

## Build

Requires the game installed locally and the .NET SDK.

1. Copy `SilksongPath.props.example` to `SilksongPath.props` and set
   `SilksongFolder` to your game directory (the file is gitignored).

   ```xml
   <Project>
     <PropertyGroup>
       <SilksongFolder>C:/Program Files (x86)/Steam/steamapps/common/Hollow Knight Silksong</SilksongFolder>
       <SilksongPluginsFolder>$(SilksongFolder)/BepInEx/plugins</SilksongPluginsFolder>
     </PropertyGroup>
   </Project>
   ```

2. Build:

   ```
   dotnet build -c Release
   ```

The `CopyToPlugins` target copies `CrestPicker.dll` into `BepInEx\plugins\`.
The version comes from `Directory.Build.props`.

## Install

1. Install BepInEx 5.4.23.x (BepInExPack_Silksong 1.0.3) into the game folder.
2. Copy `CrestPicker.dll` into `BepInEx\plugins\`.
3. Start the game. `BepInEx\LogOutput.log` should contain `CrestPicker 1.0.0 loaded.`

## Known limitations

- The menu is built from scratch (uGUI panel, rows from the game's TMP font and
  menu sounds). It is not the vanilla crest or shop menu, so it is styled to
  match rather than being identical.
- Crests are granted with the vanilla `ToolCrest.Unlock()`, which means the
  `FIRST_CREST` / `ALL_CRESTS` achievements may fire early. Avoiding them would
  require writing `PlayerData.ToolEquips` by hand instead.
- The vanilla crest handout is not suppressed yet. A crest already unlocked stays
  unlocked, so the repetition is harmless, but the placeholders at the seven
  vanilla handout points are still missing (stage 5 in `PLAN.md`).
- With `GrantAllCrests = false` on a fresh save only `Hunter` is unlocked, so the
  list has a single entry. That is the point of the filter, not a bug.

## Docs

`PLAN.md` — work plan and stage status. `REQUIREMENTS.md` — technical facts from
the decompiled game and the remaining decisions.
`docs/crest-shop-centering.md` — why shop row labels are centred the way they are,
what anchor failed and what replaced it.
