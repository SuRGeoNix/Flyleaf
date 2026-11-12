# Flyleaf Plugins

Official plugins for [FlyleafLib](https://github.com/SuRGeoNix/Flyleaf). Each plugin is built and distributed as an independent NuGet package. A consuming Flyleaf application gets each plugin in its own `Plugins/<PluginName>/` folder, rather than a plugin DLL in the application root.

| Project | NuGet package | Purpose |
| --- | --- | --- |
| `OpenSubtitlesOrg` | `FlyleafLib.Plugins.OpenSubtitlesOrg` | Find and download subtitles from OpenSubtitles.org |
| `SubtitlesConverter` | `FlyleafLib.Plugins.SubtitlesConverter` | Detect subtitle encodings and convert external subtitles to UTF-8 |
| `BitSwarm` | `FlyleafLib.Plugins.BitSwarm` | Stream media directly from torrents using BitSwarm |

The projects target **.NET 10 on Windows** (`net10.0-windows`, represented in the NuGet packages as `net10.0-windows7.0`). The current plugin packages depend on FlyleafLib **3.11.17 or later in the 3.x line** (`[3.11.17,4.0.0)`).

## Developing and building

Run commands below from the **repository root** (the parent of `Plugins/`). The shared `Plugins/Directory.Build.props` controls assembly/package names, framework, output locations, and the Flyleaf reference mode.

`UseLocalFlyleaf` defaults to `true`:

- `true`: compile against the neighboring `FlyleafLib/FlyleafLib.csproj`. Useful for developing Flyleaf and plugins together; **no NuGet release of FlyleafLib is needed** to test local core changes.
- `false`: compile against the published `FlyleafLib` NuGet package. Recommended before releasing a plugin so the build uses publicly available Flyleaf APIs.

The default is unchanged; there is nothing to configure for normal local development.

```powershell
# Build one plugin against the local FlyleafLib project (the default)
dotnet build Plugins/BitSwarm/BitSwarm.csproj -c Debug

# Or build against the published FlyleafLib NuGet package
dotnet build Plugins/BitSwarm/BitSwarm.csproj -c Release -p:UseLocalFlyleaf=false
```

Project build outputs go to `Plugins/bin/<Project>/<Configuration>/net10.0-windows/` (for example, `Plugins/bin/BitSwarm/Release/net10.0-windows/`). These are **build artifacts**, not the output layout used by a NuGet consumer.

## Packing NuGet packages

Before packing, update `<Version>` in the relevant plugin `.csproj` file, and make sure the code builds against the published FlyleafLib package. Then pack in Release mode:

```powershell
dotnet pack Plugins/OpenSubtitlesOrg/OpenSubtitlesOrg.csproj -c Release -p:UseLocalFlyleaf=false
dotnet pack Plugins/SubtitlesConverter/SubtitlesConverter.csproj -c Release -p:UseLocalFlyleaf=false
dotnet pack Plugins/BitSwarm/BitSwarm.csproj -c Release -p:UseLocalFlyleaf=false
```

All `.nupkg` and `.snupkg` archives go to `Plugins/bin/`, as set by `PackageOutputPath` (unless `-o` overrides it). Each `.nupkg` contains:

```text
README.md                                        # That plugin's own README
Flyleaf.png                                     # NuGet package icon
tools/net10.0-windows7.0/FlyleafLib.Plugins.<Name>.dll
buildTransitive/FlyleafLib.Plugins.<Name>.targets
lib/net10.0-windows7.0/_._
```

The BitSwarm package additionally includes `buildTransitive/_flyleaf/FlyleafLib.Plugins.BitSwarm.dependencies.targets`, which handles the placement of its runtime dependencies.

The empty `_._` file is a **NuGet framework compatibility marker**, not a DLL. Keeping it under `lib/` declares the supported target framework without introducing a normal compile-time reference to the plugin. The plugin assembly is instead packaged under `tools/` and staged by the build target.

`IncludeSymbols=true` and `SymbolPackageFormat=snupkg` generate separate symbol packages containing matching `.pdb` files. The standard plugin `.nupkg` does not deploy PDBs into the consumer output.

## Publishing

After checking the contents of the packages and testing them in a clean consuming application, publish the `.nupkg` files to NuGet.org (example for BitSwarm):

```powershell
dotnet nuget push Plugins/bin/FlyleafLib.Plugins.BitSwarm.1.3.0.nupkg `
  --api-key $env:NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json
```

Replace `1.3.0` with the version you are publishing. Keep the corresponding `.snupkg` alongside the `.nupkg` so the NuGet tooling can upload matching symbols. Do not reuse an already-published package version; NuGet.org versions are immutable.

## Using the NuGet packages in an application

Add the desired packages to a **.NET 10 Windows Flyleaf application** (example: from the directory containing the application's `.csproj`):

```powershell
dotnet add package FlyleafLib.Plugins.OpenSubtitlesOrg
dotnet add package FlyleafLib.Plugins.SubtitlesConverter
dotnet add package FlyleafLib.Plugins.BitSwarm
```

No manual post-build copy is needed. Each package imports its own `buildTransitive` MSBuild target and places its plugin DLL in the corresponding folder on **build and publish**. With all three packages installed, the intended output is:

```text
<App output>/
└── Plugins/
    ├── OpenSubtitlesOrg/
    │   └── FlyleafLib.Plugins.OpenSubtitlesOrg.dll
    ├── SubtitlesConverter/
    │   └── FlyleafLib.Plugins.SubtitlesConverter.dll
    └── BitSwarm/
        ├── FlyleafLib.Plugins.BitSwarm.dll
        ├── BitSwarmLib.dll
        ├── Partfiles.dll
        └── BencodeNET.dll
```

The three extra BitSwarm files are runtime assemblies from the `BitSwarm`, `APF`, and `BencodeNET` NuGet packages. `Packaging/BitSwarm.Dependencies.targets` redirects their SDK copy destinations to `Plugins/BitSwarm/`, avoiding duplicate copies in the application root. This assumes these dependencies are **not also required by the host or another plugin**. The Flyleaf plugin loader must be able to resolve them from that subfolder.

NuGet installs the files; it does not itself activate plugins. The host still needs to use Flyleaf's plugin-loading mechanism.

## Packaging files and troubleshooting

- `Directory.Build.props`: shared framework, package metadata, paths, and `UseLocalFlyleaf` default.
- `Directory.Build.targets`: includes each project's `README.md`, the icon, shared deployment target, and (for BitSwarm) the dependency target in its NuGet package.
- `Packaging/Plugin.targets`: copies each plugin's own DLL to its own `Plugins/<PluginName>/` folder. It derives the plugin identity from its imported target filename, avoiding MSBuild global-property collisions when multiple plugins are installed.
- `Packaging/BitSwarm.Dependencies.targets`: redirects BitSwarm-related dependency runtime assets during build and publish.
- `Packaging/_._`: placeholder packed under `lib/net10.0-windows7.0/`.

When diagnosing installation issues, **clean the consuming application's `bin/` and `obj/`** before rebuilding/publishing: old DLLs in the output root can remain even after the targets are fixed. When testing changes to a NuGet target, increment the package version or clear that package's cached version before restore. Verify the clean output layout **and actual plugin loading**, particularly for BitSwarm's transitive dependencies.

Each project also has its own `README.md` describing its purpose, installation, and expected output. That per-project README is the one included in its NuGet package; **this root README is for developers of the plugins**.
