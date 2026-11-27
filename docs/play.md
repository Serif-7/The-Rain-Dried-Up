# Build and run locally

Building `Play` builds the engine and every mod in this repo, copies the mods and their bodies into the game's mods folder, and then
starts the game. Use whichever tool you like: Visual Studio, VS Code or a terminal all run the same build.

All three need the .NET 11 SDK and a clone with its submodules (`git clone --recurse-submodules <repo URL>`, or
`git submodule update --init` in an existing clone). Without them the build stops with `MEITOU001`, naming that command.

## Visual Studio

1. Open the repo's `.slnx`.
2. Set `Play` as the startup project if Visual Studio didn't pick it (right-click > Set as Startup Project).
3. Pick a profile in the drop-down next to the Start button, then press F5 (debug) or Ctrl+F5:

| Profile | Runs | Use it for |
| --- | --- | --- |
| Play (engine + mods) | `MeitouClient.exe` built from this repo's `engine` submodule | testing and debugging your mod; breakpoints in mod code work |
| Meitou launcher (installed) | the launcher installed from the Meitou releases, `%LOCALAPPDATA%\MeitouLauncher\current\meitou-launcher.exe` | changing the mod list or load order and trusting a new mod's code |

## VS Code

1. Install the C# Dev Kit extension (VS Code offers it when you open the folder; it's in `.vscode/extensions.json`).
2. Open the repo's root folder (File > Open Folder).
3. Run and Debug (Ctrl+Shift+D), pick **Play (engine + mods)**, press F5. It builds and deploys first, then starts the engine under the
   debugger; breakpoints in mod code work.
4. For the installed launcher: Terminal > Run Task > **Meitou launcher (installed)**. It builds and deploys, then opens the launcher.

Terminal > Run Build Task (Ctrl+Shift+B) only builds and deploys.

## Terminal

In the repo's root folder:

```powershell
dotnet build
dotnet test
dotnet run --project Play
```

- `dotnet build` and `dotnet test` build and test every project.
- `dotnet run --project Play` builds, deploys and starts the engine (the "Play (engine + mods)" profile).
- To use the installed launcher instead, deploy without starting the engine, then open the launcher:

```powershell
dotnet build Play
& "$env:LOCALAPPDATA\MeitouLauncher\current\meitou-launcher.exe"
```

## What a play build does

- Finds the mods: every top-level folder holding `meitou.json` and `<Folder>/<Folder>.csproj`. A new mod needs no change to `Play`.
- Builds the engine and each mod in **Release**, whatever the configuration you picked: Debug engine builds throw in `TaskStart.CheckStarts`
  once a mod starts a character's task. Breakpoints in mod code still work (Release symbols are deployed).
- Copies each mod's `<Mod>.dll`, `<Mod>.pdb` and `meitou.json` to `<Kenshi>\mods\<Mod>\`. Close the game first: a running game locks the dll.
- Copies each mod's `items` folder (the imported bodies in `items/races`) to `<Kenshi>\mods\<Mod>\items\`.
- A mod with a data generator (`<Mod>.Data/<Mod>.Data.csproj`, run as `<generator> <Kenshi folder> <output .mod>`) gets its `<Mod>.mod`
  generated from the installed game; a failure is a build warning and the mod is deployed without it. Every other mod gets an empty
  `<Mod>.mod` when it has none; an existing one is never replaced.
- A mod with a `Structures` folder gets its `.glb` models imported into its deployed folder, records merged into its `.mod`
  ([structures.md](structures.md)).
- When the mods folder doesn't exist, nothing is copied and the build says so.
- The launcher's Play button starts the release it installed, not this repo's engine. Close it and run "Play (engine + mods)" to test your
  build.

## Settings

Defaults: game `C:\Program Files (x86)\Steam\steamapps\common\Kenshi`, mods folder `<game>\mods`. Change them with the `KENSHI_PATH`
environment variable (the engine reads it too, so both agree), or per machine in a git-ignored `Meitou.user.props` in the repo's root folder:

```xml
<Project>
  <PropertyGroup>
    <KenshiDir>D:\Games\Kenshi</KenshiDir>           <!-- game folder; also sets the mods folder -->
    <KenshiModsDir>D:\Games\Kenshi\mods</KenshiModsDir> <!-- mods folder only -->
    <PlayConfiguration>Debug</PlayConfiguration>      <!-- engine and mods configuration (default Release) -->
    <PlayDeploy>false</PlayDeploy>                    <!-- build and run without copying -->
    <PlayArguments>--continue</PlayArguments>         <!-- engine command line (Visual Studio and dotnet run) -->
  </PropertyGroup>
</Project>
```

The engine finds the game on its own (Steam); `KenshiDir` set only in `Meitou.user.props` isn't passed to it. VS Code's launch configuration
runs the Release engine; edit `.vscode/launch.json` locally if you change `PlayConfiguration`.

## First run

- Run the installed launcher once (any of the ways above): enable the mod and trust its code.
- If the copy fails with "access denied", give your user Modify rights on `Kenshi\mods` (Properties > Security) rather than running your
  editor as administrator.
