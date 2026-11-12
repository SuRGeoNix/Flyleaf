# FlyleafLib.Plugins.SubtitlesConverter

Detect and convert external subtitle text encodings to UTF-8 for playback with [FlyleafLib](https://github.com/SuRGeoNix/Flyleaf).

The plugin hooks into Flyleaf's external subtitle opening process, detects the subtitle's character encoding, and converts text to a UTF-8 subtitle file when needed. This helps with subtitles that are not already UTF-8 encoded.

## Install

Add the NuGet package to a **.NET 10 Windows** Flyleaf application:

```powershell
dotnet add package FlyleafLib.Plugins.SubtitlesConverter
```

The package declares a dependency on **FlyleafLib >= 3.11.17 and < 4.0.0**. Its MSBuild target automatically places the plugin DLL at the following path during **build and publish**:

```text
<App output>/Plugins/SubtitlesConverter/FlyleafLib.Plugins.SubtitlesConverter.dll
```

No manual DLL copying or compile-time plugin reference is required. The application must use Flyleaf's plugin-loading mechanism to load plugins from the `Plugins` directory.

## Source and development

See the [Flyleaf repository](https://github.com/SuRGeoNix/Flyleaf) and its `Plugins/README.md` for local builds, NuGet packaging, and publication instructions.

License: LGPL-3.0-or-later.
