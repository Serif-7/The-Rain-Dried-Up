# The Rain Dried Up

A Kenshi mod for [MeitouClient](https://github.com/VlcekM/MeitouClient). GPL-3.0-or-later ([LICENSE](LICENSE)).

The code mod is `TheRainDriedUp/` (id `the.rain.dried.up`): it adds its characters' races and characters in the data phase (the shipped
`.mod` is empty) and an F10 recruit window. Its imported bodies are in `TheRainDriedUp/items/races/`.

## Testing in game

1. Build and deploy: `dotnet run --project Play`, or F5 in Visual Studio or VS Code ([docs/play.md](docs/play.md)).
2. The first time: in the Meitou launcher, enable **TheRainDriedUp** and trust its code.
3. Load a game, point the camera at open ground near your squad, select one of your characters, press F10 and run **The Rain Dried Up:
   recruit window**.
4. Press **Recruit** beside a character to add it to that squad there. Run the F10 row again (or press Close) to close the window.

Every character is a subrace of the **Raiden** race:

| Character | Body |
| --- | --- |
| Raiden (retopo) | the original model retopologized: 6,971 triangles |
| Raiden (full poly) | the original model, every triangle kept: 52,265 triangles, for comparison |
| Raiden 1 to 5 | five Meshy models with the 3ds Max Biped rig, imported as they are: 4,989 to 14,962 triangles |

How each body was made, and how to remake it: [docs/character-pipeline.md "Bodies"](docs/character-pipeline.md#6-bodies).

## Importing models

| You have | You get | Doc |
| --- | --- | --- |
| A building `.glb` | A structure in the build menu (mesh, textures, collision, records) | [docs/structures.md](docs/structures.md) |
| An item `.glb` | An item's look (mesh, material record) | [docs/structures.md](docs/structures.md#item-models) |
| Any model over budget or with huge textures | A smaller model | [docs/model-prep.md](docs/model-prep.md) |
| A high-poly rigged character | A Kenshi-style low poly with one atlas | [docs/retopology.md](docs/retopology.md) |
| A rigged character `.glb` | A body on the game's skeleton, animated by the game | [docs/character-pipeline.md](docs/character-pipeline.md) |

Tools: `tools/StructureImport` and `tools/RaceImport` (C#, .NET 11), `tools/Poser` (pose previews), `tools/Retopo/retopo.py` (Blender as
Python), `tools/model-info.py` and `tools/prepare-model.py`. The C# tools read the game's formats through the `engine` submodule
(MeitouClient's `Meitou.Data`).

## Setup

**In the folder that will hold the clone:**

```powershell
git clone --recurse-submodules https://github.com/Serif-7/The-Rain-Dried-Up.git
cd The-Rain-Dried-Up
```

- .NET 11 SDK for the C# tools.
- Python 3.13 with `pip install bpy numpy` (a venv) for `retopo.py`; `pip install pillow numpy` for `prepare-model.py`.
- Kenshi installed: RaceImport and Poser read the game's skeletons and human body; Play deploys to its `mods` folder.

`dotnet build` builds everything, `dotnet test` runs the mod's tests.

## Credits

The import tools and their docs come from the [Meitou-Modding-Ecosystem](https://github.com/Meitou-Modding-Ecosystem) mods
(MeitouModTemplate, KenshiRaces), GPL-3.0.
