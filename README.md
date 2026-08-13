# Simple Missile & Bomb Tracker + Tac Map Trails

A simple, client-side Nuclear Option mod that tracks missiles and bombs fired by your aircraft. It draws their flight trails on the tactical map and cockpit radar, then marks where they hit, miss, or get intercepted.

## What it does

- Shows missile and bomb flight trails on the tactical map and cockpit radar.
- Tracks submunitions from your cluster bombs and compatible modded dispensers.
- Uses up to 256 smoothed positions to follow the actual flight path.
- Once a smoothed point is drawn, later movement does not move it.
- On unusually long paths, the mod may remove a redundant point to stay within the 256-point limit.
- Keeps the launch and final impact position exact.
- Marks confirmed hits, intercepted shots, and failed shots with different colors.
- Keeps trails visible for 35 seconds after impact or interception.
- Keeps impact and intercepted markers visible for 135 seconds.
- Can show up to 15 recent flight paths in the 3D world.
- Keeps 3D world paths disabled by default.
- Tracks only weapons whose ownership leads back to your aircraft.

## Installation

1. Install BepInEx 5 for Nuclear Option.
2. Remove any older `ImpactMarkers` DLL. Do not run both versions together.
3. Download the latest ZIP from the GitHub Releases page.
4. Extract the ZIP directly into your Nuclear Option game folder. The ZIP already contains the correct `BepInEx/plugins` folders.
5. Check that the mod DLL is located here:

   `Nuclear Option/BepInEx/plugins/NuclearOption-Simple-Missile-Bomb-Tracker-and-Tac-Map-Trails/NuclearOption-Simple-Missile-Bomb-Tracker-and-Tac-Map-Trails.dll`

6. Start or restart the game.

## Default appearance

- Tac-map and radar trails: enabled
- Tac-map and radar trail opacity: 42.5%
- Trail time after impact or interception: 35 seconds
- Hit, intercepted, and failed markers: 135 seconds
- Hit, intercepted, and failed marker opacity: 70%
- 3D world paths: disabled
- Maximum 3D world paths when enabled: 15
- Maximum recorded points per trail: 256
- Path smoothing window: 1 second

## Marker colors

- Blue: confirmed hit
- Purple: intercepted
- Gray: missed or failed

## Configuration

Start the game once with the mod installed, then edit:

`Nuclear Option/BepInEx/config/nuclearoption.simplemissilebombtrackerandtacmaptrails.cfg`

You can change trail and marker times, path smoothing, colors, sizes, line thickness, 3D path settings, or turn the mod off.

## Notes

- This is a visual client-side mod. It does not change weapon damage, guidance, or multiplayer game state.
- Submunitions are included only when their parent weapon belongs to your aircraft. Other players' and AI weapons are ignored.
- Only detected results are shown. An intercepted result is based on the game reporting outside damage to the weapon before it fails.
- Trails and markers are cleared when the mission resets or the tactical map is replaced.
