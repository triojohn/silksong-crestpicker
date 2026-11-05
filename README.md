# CrestPicker

BepInEx 5 plugin for Hollow Knight: Silksong. **Early skeleton — no gameplay logic yet.**

Planned: the first snail shaman of the run offers a choice of crest instead of
handing out one crest, so a new save can start on the crest the player prefers.

GUID: `io.github.triojohn.crestpicker`. Version: `0.1.0`.

Current state: the plugin loads, creates a Harmony instance and calls `PatchAll`
with no patches registered. Behaviour is identical to vanilla.

## Build

Requires the game installed locally and the .NET SDK.

1. Copy `SilksongPath.props.example` to `SilksongPath.props` and set `SilksongFolder`
   to your game directory (the file is gitignored).

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

## Install

1. Install BepInEx 5.4.23.x (BepInExPack_Silksong 1.0.3) into the game folder.
2. Copy `CrestPicker.dll` into `BepInEx\plugins\`.
3. Start the game. `BepInEx\LogOutput.log` should contain `CrestPicker 0.1.0 loaded.`

## Status

Stage 0 (decompile, API map) is done; the work plan is in `PLAN.md`, the technical
facts and the open risk are in `REQUIREMENTS.md`.
