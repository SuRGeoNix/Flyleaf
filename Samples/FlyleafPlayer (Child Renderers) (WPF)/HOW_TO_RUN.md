# How to Build and Run Without Visual Studio

## Prerequisites

1. **Windows 10/11** (required - this uses Windows-specific APIs)
2. **.NET 8 SDK or later** 
   Download from: https://dotnet.microsoft.com/download

## Quick Start

### 1. Install .NET SDK (if not installed)
```powershell
# Download and install from:
https://dotnet.microsoft.com/download/dotnet/8.0

# Verify installation
dotnet --version
```

### 2. Clone/Copy the Repository to Windows
Transfer this entire `/workspaces/Flyleaf` folder to your Windows machine.

### 3. Build the Project
Open PowerShell or Command Prompt in the Flyleaf directory:

```powershell
# Navigate to the solution directory
cd path\to\Flyleaf

# Restore NuGet packages
dotnet restore FlyleafLib.sln

# Build the solution
dotnet build FlyleafLib.sln --configuration Release
```

### 4. Run the Child Renderers Sample
```powershell
# Navigate to the sample directory
cd "Samples\FlyleafPlayer (Child Renderers) (WPF)"

# Run the application
dotnet run
```

That's it! The application should launch.

## Alternative: Build Only the Sample

If the full solution has issues, build just the sample:

```powershell
cd "Samples\FlyleafPlayer (Child Renderers) (WPF)"
dotnet build
dotnet run
```

## Alternative: Using Visual Studio Code

If you have VS Code on Windows:

1. Install VS Code: https://code.visualstudio.com/
2. Install C# extension: https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp
3. Open the Flyleaf folder in VS Code
4. Press `F5` to run

## Alternative: Using Rider

JetBrains Rider is another option:
1. Install Rider: https://www.jetbrains.com/rider/
2. Open `FlyleafLib.sln`
3. Click Run

## Troubleshooting

### "SDK not found"
Install .NET SDK from: https://dotnet.microsoft.com/download

### "Project targets .NET 10"
The sample I created uses .NET 8. If you see errors, check the .csproj file has:
```xml
<TargetFramework>net8.0-windows</TargetFramework>
```

### "Cannot find FFmpeg"
The application will try to load FFmpeg binaries. Make sure they exist in the `FFmpeg` folder.

### "Build errors"
Try:
```powershell
dotnet clean
dotnet restore
dotnet build
```

## What You'll See

When running successfully:
- Main window with video player
- Two thumbnail views on the right
- Zoom, rotate, flip controls
- Status bar at bottom

Open a video file (MP4, MKV, etc.) using the "Open Video" button to see all three views synchronized!

## Running from Executable

After building, you can run the .exe directly:
```
Samples\FlyleafPlayer (Child Renderers) (WPF)\bin\Release\net8.0-windows\FlyleafPlayer.ChildRenderers.exe
```

## Still Having Issues?

Check:
1. You're on Windows (Linux/Mac won't work)
2. .NET 8 SDK is installed
3. You have a display (not running headless)
4. The project built successfully (check for error messages)
