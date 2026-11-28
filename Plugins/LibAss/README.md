# FlyleafLib.Plugins.LibAss

Optional Windows x64 subtitle rendering for SubRip (`.srt`) and ASS/SSA, including embedded tracks. Flyleaf demuxes and decodes the selected track, and libass renders it into the Direct3D subtitle overlay. SubRip uses the player's `Subtitles.RenderStyle` defaults and preserves the inline formatting translated by FFmpeg. ASS/SSA uses script styles and available fonts.

For SubRip, **Keep inside video** selects the visible video area. Turning it off uses the full player surface. **Bottom offset** moves bottom-aligned cues within the selected area. Cues with explicit coordinates place themselves in that area.

The plugin follows the existing `Plugins/LibAss` install layout. Set its `Enabled` plugin option to `False` before opening a subtitle track to use basic text. Flyleaf also uses basic text when the plugin or its native runtime is unavailable.

## Local build

From the repository root, download the pinned native build and build the solution or plugin project.

```powershell
./Plugins/LibAss/Download-LibAss.ps1
dotnet build Plugins/LibAss/LibAss.csproj -c Release
```

For manual testing, copy `FlyleafLib.Plugins.LibAss.dll` and the `native` directory from the plugin build output into the application's `Plugins/LibAss` directory. Packing the plugin includes `ass.dll` and its third-party notices. To supply a compatible x64 build, point `LibAssNativeDirectory` at a directory containing `ass.dll` and the corresponding notices. The plugin targets .NET 10 like the other solution plugins.
