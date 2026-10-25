# Source

[简体中文](README.md)

With PowerShell 7, run `./build.ps1 -BepInExDir 'your-profile/BepInEx'` here. The build entry point is `MastabaInspect.slnx`; it uses the profile's .NET 6, BepInEx and game interop assemblies, then runs the knife and firearm motion checks.

Provide the game and BepInEx assemblies locally. Output goes to `bin/`. From the repository root, run `./tools/package.ps1 -Rebuilt` to package the newly compiled DLL.
