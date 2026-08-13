# Simple Missile & Bomb Tracker + Tac Map Trails

A BepInEx mod for Nuclear Option that shows where your missiles and bombs travel. It draws smooth flight trails on the tactical map and cockpit radar, then marks where each weapon hit, missed, or was intercepted.

## Features

- Tracks missiles and bombs fired by your aircraft.
- Shows their full flight paths on the tactical map and cockpit radar.
- Tracks submunitions from cluster bombs and compatible modded dispensers.
- Keeps completed trails visible for 35 seconds.
- Keeps hit, intercepted, and failed markers visible for 135 seconds.
- Uses blue markers for hits, purple for interceptions, and gray for misses or failed shots.
- Keeps older parts of a trail in place as the weapon continues to fly.
- Includes optional 3D flight paths. They are disabled by default and can show up to 15 paths when enabled.

## Installation

1. Install BepInEx 5 for Nuclear Option.
2. Download the latest release ZIP.
3. Extract the ZIP directly into your Nuclear Option game folder.
4. Check that the mod DLL is located here:

   `Nuclear Option/BepInEx/plugins/NuclearOption-Simple-Missile-Bomb-Tracker-and-Tac-Map-Trails/NuclearOption-Simple-Missile-Bomb-Tracker-and-Tac-Map-Trails.dll`

5. Start or restart the game.

## Configuration

Start the game once with the mod installed, then edit:

`Nuclear Option/BepInEx/config/nuclearoption.simplemissilebombtrackerandtacmaptrails.cfg`

You can change trail and marker duration, color, transparency, size, line thickness, path smoothing, 3D path settings, or turn the mod off.

## Notes

- The mod only tracks weapons fired by your aircraft.
- This is a visual mod. It does not change weapon damage, guidance, or multiplayer gameplay.
- Trails and markers are cleared when a new mission starts.
