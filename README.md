# The Rain Dried Up

A Kenshi mod for [MeitouClient](https://github.com/VlcekM/MeitouClient). GPL-3.0-or-later ([LICENSE](LICENSE)).

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
- Kenshi installed: RaceImport and Poser read the game's skeletons and human body.

## Credits

The import tools and their docs come from the [Meitou-Modding-Ecosystem](https://github.com/Meitou-Modding-Ecosystem) mods
(MeitouModTemplate, KenshiRaces), GPL-3.0.
