# Linux

Source 2 Viewer runs natively on Linux through an Avalonia shell and OpenGL. It is
distributed as a self-contained `linux-x64` tarball, so the .NET runtime does not need to
be installed separately.

## Installing

Download `source2viewer-linux-x64-<version>.tar.gz` from the
[releases page](https://github.com/MonsterDomosed/ValveResourceFormat-Linux/releases), then:

```bash
tar -xzf source2viewer-linux-x64-*.tar.gz
./Source2Viewer/Source2Viewer
```

The archive contains the `Source2Viewer` binary, a `source2viewer.desktop` entry, the
application icon, and the README and LICENSE.

To integrate it with your desktop, copy the binary onto your `PATH`, install the desktop
entry and the icon, then refresh the database:

```bash
mkdir -p ~/.local/bin ~/.local/share/applications ~/.local/share/icons/hicolor/256x256/apps
install -Dm755 Source2Viewer/Source2Viewer ~/.local/bin/Source2Viewer
install -Dm644 Source2Viewer/source2viewer.desktop ~/.local/share/applications/source2viewer.desktop
install -Dm644 Source2Viewer/source2viewer.png ~/.local/share/icons/hicolor/256x256/apps/source2viewer.png
update-desktop-database ~/.local/share/applications
```

## System requirements

| Requirement | Notes |
| ----------- | ----- |
| 64-bit Linux (`linux-x64`) | `linux-arm64` is not built yet. |
| OpenGL 4.6 | AMD (Mesa `radeonsi`), Intel, and NVIDIA driver stacks work. The bundled shaders and viewports require a 4.6 core profile. |
| Software rendering | `llvmpipe` works for lightweight views and is enough for the smoke test, but world and particle rendering are slow. |
| Wayland or X11 | The shell prefers Wayland and falls back to X11/XWayland automatically. |
| PulseAudio or PipeWire-Pulse | Required for audio playback and scene sound events. Without a running sound server the app still starts, silently. |
| fontconfig, ICU | Provided by essentially every desktop distribution; the app uses them through Skia and Avalonia. |

Skia, HarfBuzz, and the other rendering natives are bundled in the tarball.

### Display backends

The shell selects a backend at startup:

- `--wayland` forces the Wayland backend.
- `--x11` forces X11 (useful under XWayland or a plain X session).

Without either flag it uses Wayland when a compositor is available and otherwise X11.

## Configuration

Settings, recent files, and bookmarks are stored in
`$XDG_DATA_HOME/Source2Viewer/settings.vdf`, which defaults to
`~/.local/share/Source2Viewer/settings.vdf`. The **View → Settings** tab edits the active
game, game search paths, rendering options, theme, and audio volume.

The selected game (or configured search paths) supplies the content root used to open
resources and render worlds. Deadlock is preferred by default when it is installed.

## Building from source

With the .NET SDK pinned in `global.json`:

```bash
dotnet build GUI.Linux/GUI.Linux.csproj -c Release
dotnet GUI.Linux/bin/Release/Source2Viewer.dll
```

To produce the release tarball:

```bash
./Misc/Linux/package-linux.sh
# writes artifacts/source2viewer-linux-x64-<version>.tar.gz
```

### Self-checks

The shell has a non-interactive startup check:

```bash
# Starts the shell, renders one lightweight GL frame and exits non-zero on failure.
dotnet GUI.Linux/bin/Release/Source2Viewer.dll --self-check=smoke --x11

# Full content, viewer and rendering validation against an installed game.
dotnet GUI.Linux/bin/Release/Source2Viewer.dll --self-check --wayland
```

The full check needs a real game installation and takes several minutes; the smoke check is
what CI runs under Xvfb and `llvmpipe`.

## Supported resources

The application ships portable viewers for the common resource types. Resource types
with a native OpenGL preview include textures, models, meshes, materials, skyboxes,
particles and particle snapshots, worlds, maps, world nodes, navmeshes, physics collision
meshes, voxel visibility, animation clips, skeletons, smart props, panorama vector
graphics, color correction LUTs, animation graphs (AG1 and AG2), pulse graphs, entity I/O
graphs, and sound. The VPK browser, console, and portable data viewers are also available.

The model viewer includes an interactive inspection camera (left-drag orbit, right-drag pan,
wheel zoom, reset view) and a native animation sidebar (animation selection, play/pause,
timeline scrubbing, playback speed, looping and restart), with models that have animations
autoplaying their first sequence.

Known limitations:

- Advanced per-viewer sidebar controls (render modes, wireframe, debug toggles, texture
  mip/channel/cube controls, world layer and entity controls, graph search and filters) are
  not ported yet.
- Some views are not implemented: the interactive compiled-shader tree, the panorama image
  grid, and the choreography viewer.
- AAC and WAV ADPCM audio cannot be decoded, because no managed decoder is available.
  MP3 and uncompressed WAV PCM play normally.
- World sound events play through the portable mixer; the audio configuration UI is not exposed.
