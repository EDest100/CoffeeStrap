# Build CoffeeStrap

The shipped target is Windows x64. Install the .NET 10 SDK, Rust with the Windows MSVC target, Visual Studio C++ build tools and PowerShell running on .NET 10 (7.6 or newer). The dependency-linking check requires that PowerShell runtime. Clone recursively so the upstream UI libraries are available:

```powershell
git clone --recurse-submodules https://github.com/EDest100/CoffeeStrap.git
cd CoffeeStrap
./tools/Build.ps1 -Publish
```

`Release/CoffeeStrap.exe` is a framework-dependent application packaged in one compressed native EXE. Recipients need the .NET 10 x64 runtime. The build first verifies the normal SDK bundle, then losslessly repackages its exact application files with the Windows Compression API and the static `nethost` library from the installed .NET host pack. It verifies the final host's CLI, extracted hashes, native imports/library loading and actual shipped Ed25519 implementation before refreshing Release. It does not install CoffeeStrap, start Roblox or modify protocol associations. A running Release executable is left alone. Use `-NoRestore` after dependencies have been restored. The [packaging guide](PORTABLE-PACKAGING.md) covers cache behavior and the format.

Use `-Publish -StageOnly` to build and verify in the fixed `.build/publish` staging directory while your current Release launcher stays open. Close CoffeeStrap before the normal publish command replaces the usual EXE.

For isolated development checks and renders:

```powershell
$env:PATH = "$env:USERPROFILE/.cargo/bin;$env:PATH"
$env:RUSTFLAGS = '-C debuginfo=0'
dotnet build tools/Preview/CoffeeStrap.Preview.csproj -c Release -p:UsedAvaloniaProducts= -p:AppVersion=0.10.1
dotnet tools/Preview/bin/Release/net10.0/CoffeeStrap.Preview.dll .build/theme-verification --coffee-theme-only .build/theme-package/CoffeeStrap-Espresso.zip
```

The preview harness isolates settings and player fixtures, disables account access and blocks network requests. Its renders and file-application checks do not establish in-game appearance or FPS gains. See [ESPRESSO-THEME.md](ESPRESSO-THEME.md) for theme regeneration.

To exercise the same native host around the isolated preview harness:

```powershell
./tools/Build-Portable.ps1 -PayloadDirectory tools/Preview/bin/Release/net10.0 -EntryAssembly CoffeeStrap.Preview.dll -OutputExecutable .build/compact-host/test-host/CoffeeStrap.Preview.exe -WorkDirectory .build/compact-host/preview-build
./tools/Test-PortableUI.ps1
./tools/Test-Portable.ps1 -Executable Release/CoffeeStrap.exe
```

The test host includes additional test dependencies and is larger than the production launcher. Do not distribute it. All package staging, cache fixtures and reports reuse `.build/compact-host`; only `Release` is the delivery folder. `Test-Portable.ps1` compiles and exercises the actual native parser and checks real extraction, corruption recovery, sharing failures, concurrent launches and Unicode paths. UI checks also require the generated Espresso test ZIP from the theme guide.
