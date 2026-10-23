# Build CoffeeStrap

The shipped target is Windows x64. Install the .NET 10 SDK, Rust with the Windows MSVC target, Visual Studio C++ build tools and PowerShell running on .NET 10 (7.6 or newer). The dependency-linking check requires that PowerShell runtime. Clone recursively so the upstream UI libraries are available:

```powershell
git clone --recurse-submodules https://github.com/EDest100/CoffeeStrap.git
cd CoffeeStrap
./tools/Build.ps1 -Publish
```

`Release/CoffeeStrap.exe` is a framework-dependent single-file application. Recipients need the .NET 10 x64 runtime. The build runs native loading, CLI, single-file manifest and signed-config verification checks before refreshing Release. It does not install CoffeeStrap, start Roblox or modify protocol associations. A running Release executable is left alone. Use `-NoRestore` after dependencies have been restored.

Use `-Publish -StageOnly` to build and verify in the fixed `.build/publish` staging directory while your current Release launcher stays open. Close CoffeeStrap before the normal publish command replaces the usual EXE.

For isolated development checks and renders:

```powershell
$env:PATH = "$env:USERPROFILE/.cargo/bin;$env:PATH"
$env:RUSTFLAGS = '-C debuginfo=0'
dotnet build tools/Preview/CoffeeStrap.Preview.csproj -c Release -p:UsedAvaloniaProducts= -p:AppVersion=0.10.0
dotnet tools/Preview/bin/Release/net10.0/CoffeeStrap.Preview.dll .build/theme-verification --coffee-theme-only .build/theme-package/CoffeeStrap-Espresso.zip
```

The preview harness isolates settings and player fixtures, disables account access and blocks network requests. Its renders and file-application checks do not establish in-game appearance or FPS gains. See [ESPRESSO-THEME.md](ESPRESSO-THEME.md) for theme regeneration.
