---
name: model-import
description: Use when importing a .glb into The Rain Dried Up as a building, an item or a character body, or when a model is too high poly for Kenshi.
---

# Model import

## Decide the kind

| Model | Path |
| --- | --- |
| Building, wall, furniture | `TheRainDriedUp/Structures/<name>.glb` → docs/structures.md |
| Hand-held item, weapon, worn unarmed weapon | `TheRainDriedUp/ItemModels/<name>.glb` → docs/structures.md "Item models" |
| Rigged character | docs/character-pipeline.md |
| Unrigged character | Rig it first (Meshy auto-rig, 3ds Max Biped template); this repo has no rigging tool |

## Steps

1. `python tools/model-info.py <model.glb>`: size, triangles, materials, textures.
2. Over budget (CLAUDE.md "Importing models"):
   - building or item: docs/model-prep.md (`tools/prepare-model.py` for textures and scale; decimate in Blender for triangles);
   - character: `tools/Retopo/retopo.py` (docs/retopology.md). Write a joint map for a rig without `Bip001` or `mixamorig:` joints; name
     alpha hair materials with `--hair`, alpha decals and duplicate hair layers with `--drop`.
3. Render before and after (front, side, back, a wireframe) and show them to the user with the triangle counts before going on.
4. Import:
   - building or item: `dotnet run --project tools/StructureImport -c Release -- TheRainDriedUp/Structures <Kenshi>/mods/TheRainDriedUp`;
   - character: `dotnet run --project tools/RaceImport -c Release -- <glb> <Kenshi> TheRainDriedUp/items/races theraindriedup_<name> male|female [options]`
     (options: docs/character-pipeline.md step 3; `rigid` for armour that must keep its shape).
5. Character: pose it with `tools/Poser` (idle, `kr_walking`, `kr_charged_slash`) and render; no webs, chest keeps its shape, hair follows the
   head. Add the body to docs/character-pipeline.md "Bodies".
6. Commit the inputs and outputs listed in CLAUDE.md; never Kenshi's own files.

## Without the game on this machine

RaceImport and Poser need only `male_skeleton.skeleton` (or `female_skeleton`), `human_male.mesh` (`human_female.mesh`) and
`MaleMask_Default.png` (`FemaleMask_Default.png`): copy those into a folder as `<folder>/data/...` and pass `<folder>` as the Kenshi folder.
They find the files anywhere under `data/`.
