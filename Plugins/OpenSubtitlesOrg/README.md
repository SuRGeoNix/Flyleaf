# FlyleafLib.Plugins.OpenSubtitlesOrg

Search for and download subtitles from [OpenSubtitles.org](https://www.opensubtitles.org/) in applications using [FlyleafLib](https://github.com/SuRGeoNix/Flyleaf).

The plugin integrates with Flyleaf's online subtitle search and download interfaces. It can search using media information, including file hashes and title/language matches, and download a selected subtitle for playback.

## Install

Add the NuGet package to a **.NET 10 Windows** Flyleaf application:

```powershell
dotnet add package FlyleafLib.Plugins.OpenSubtitlesOrg
```

The package declares a dependency on **FlyleafLib >= 3.11.17 and < 4.0.0**. Its MSBuild target automatically places the plugin DLL at the following path during **build and publish**:

```text
<App output>/Plugins/OpenSubtitlesOrg/FlyleafLib.Plugins.OpenSubtitlesOrg.dll
```

No manual DLL copying or compile-time plugin reference is required. The application must use Flyleaf's plugin-loading mechanism to load plugins from the `Plugins` directory.

## Source and development

See the [Flyleaf repository](https://github.com/SuRGeoNix/Flyleaf) and its `Plugins/README.md` for local builds, NuGet packaging, and publication instructions.

License: LGPL-3.0-or-later.
