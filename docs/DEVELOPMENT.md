# Development

Copy `Local.props.example` to `Local.props` and configure the game Managed directory, publicized game assembly, BepInEx core directory, and installed mod dependencies.

Build and package:

```powershell
dotnet build -c Release
./package.ps1
```

Run the isolated in-game checks:

```powershell
dotnet build tests/RuntimeChecks.csproj -c Release
./tests/run.ps1
```

The test launcher reads the game and BepInEx paths from `Local.props` and uses a separate profile under `obj/runtime-checks`. Results and rendered previews are written to `obj/runtime-checks/profile/BepInEx/plugins`.

The release archive contains the plugin, package metadata, player documentation, and screenshots. Game assemblies, dependencies, test plugins, and local configuration are excluded.

The packaged README uses the screenshot's public GitHub URL; the repository README uses its relative path.
