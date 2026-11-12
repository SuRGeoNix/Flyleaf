# FlyleafLib.Plugins.BitSwarm

Play media directly from torrents with [FlyleafLib](https://github.com/SuRGeoNix/Flyleaf) using [BitSwarm](https://www.nuget.org/packages/BitSwarm), without waiting for the entire download to complete.

The plugin integrates BitSwarm with Flyleaf to open torrents and stream playable media files as their data becomes available. It also supports downloading the next item.

## Install

Add the NuGet package to a **.NET 10 Windows** Flyleaf application:

```powershell
dotnet add package FlyleafLib.Plugins.BitSwarm
```

The package declares dependencies on **FlyleafLib >= 3.11.17 and < 4.0.0** and **BitSwarm 2.4.12** (with its transitive dependencies). During **build and publish**, the package's MSBuild targets place the plugin and its BitSwarm runtime assemblies under:

```text
<App output>/
└── Plugins/
    └── BitSwarm/
        ├── FlyleafLib.Plugins.BitSwarm.dll
        ├── BitSwarmLib.dll
        ├── Partfiles.dll
        └── BencodeNET.dll
```

`BitSwarmLib.dll`, `Partfiles.dll`, and `BencodeNET.dll` come from the `BitSwarm`, `APF`, and `BencodeNET` NuGet packages. The package-specific MSBuild target redirects their copy destinations so they are not also copied to the application root.

No manual DLL copying or compile-time plugin reference is required. The application must use Flyleaf's plugin-loading mechanism, including dependency resolution from the `Plugins/BitSwarm/` directory. **Note:** The dependency redirection assumes the host application and other plugins do not independently require those same assemblies.

## Source and development

See the [Flyleaf repository](https://github.com/SuRGeoNix/Flyleaf) and its `Plugins/README.md` for local builds, NuGet packaging, and publication instructions.

License: LGPL-3.0-or-later (the separate BitSwarm dependency has its own license).
