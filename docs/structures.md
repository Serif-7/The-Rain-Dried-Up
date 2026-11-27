# Structures from 3D models

Put a `.glb` model (Meshy, Hi3D, a Blender export) in your mod's `Structures` folder and `tools/StructureImport` turns it into a building you
can place from the build menu: Ogre mesh, compressed textures, collision and the records in `<Mod>.mod`. No FCS, no Blender, no game install
needed for the import itself.

## Quick start

`$Kenshi` is the game folder, e.g. `$Kenshi = "C:\Program Files (x86)\Steam\steamapps\common\Kenshi"`.

1. Put the model in `TheRainDriedUp/Structures/`, e.g. `TheRainDriedUp/Structures/watch-tower.glb`.
2. Optional: beside it, `watch-tower.json` with any [settings](#settings), e.g.

   ```json
   { "name": "Watch Tower", "height": 6, "materials": 20, "description": "A lookout of scrap and rope." }
   ```

3. Build and deploy ([play.md](play.md)): Visual Studio F5, VS Code F5, or `dotnet run --project Play`. The build log shows one
   `Structure ...` line per model. Without Play:

   ```powershell
   dotnet run --project tools/StructureImport -c Release -- TheRainDriedUp/Structures "$Kenshi\mods\TheRainDriedUp"
   ```
4. In the game: build menu, category `EXTERIOR` (or the one you set). With the default `"research": "start"` it is known from the start of a
   game, and in existing saves too (Meitou works out unlocks from the finished research, which always includes `_Default Start`).
5. Commit the model, its `.json` and `Structures/ids.json` (written on the first import). The import's output goes to the game's mod folder and is not
   committed.

The arguments: the models folder (`ItemModels` beside it is read too), then the mod's folder in Kenshi's `mods` folder, whose name is the mod's.

## Settings

All optional (`<model>.json`, comments allowed; an unknown name is an error so typos don't go unnoticed).

| Setting | Default | Meaning |
| --- | --- | --- |
| `name` | file name, `-`/`_` as spaces | name in the build menu |
| `description` | `""` | build menu text |
| `height` | model's own size | finished height in metres; the model is scaled evenly to it. AI models come at arbitrary sizes, so set this |
| `sink` | `0` | metres sunk into the ground (keeps it from floating on slopes) |
| `yaw` | `0` | degrees turned about the vertical axis |
| `category` | `EXTERIOR` | build menu category (`building category`): the game's (`BUILDINGS`, `CAMPING`, `FARMING`, ...) or a new name |
| `group` | `""` | `building group`: buildings sharing one are variants of one entry |
| `materials` | `5` | Building Materials it costs |
| `buildSpeed` | `2` | `build speed mult` (the game's tents 5, houses 1 to 2) |
| `research` | `start` | `start`: known from the start; `none`: nothing unlocks it (your own records do); or a RESEARCH string id, e.g. `96564-rebirth.mod` |
| `placement` | `outdoor` | `outdoor`, `indoor` or `both` |
| `createsTown` | `true` | placing it outside a town starts an outpost |
| `collision` | `mesh` | `mesh` (a simplified copy of the model), `box` (its bounding box) or `none` (walk through it) |
| `collisionTriangles` | `2000` | most triangles the `mesh` collision keeps (12 to 50,000) |
| `textureSize` | `2048` | largest texture side in pixels (a power of two, at most 8192) |

## Item models

Put an item's model in `<Mod>/ItemModels/` instead (`Structures` would make it a building). The import writes its mesh and textures beside the
structures' and one MATERIAL_SPECS_CLOTHING record; your mod's ITEM record then points at them. The build log shows one `Item ...` line per model
with the names to use.

1. `MyMod/ItemModels/lantern.glb`, optionally `lantern.json`: `{ "size": 0.3 }`.
2. In your item record (data generator or data phase), set:

   | Field | Value |
   | --- | --- |
   | `mesh` and `ground mesh` (filenames) | `<mod>_<model>.mesh`, e.g. `mymod_lantern.mesh` |
   | `material` (reference) | `<950000 + 100 × block>-<Mod>.mod`, block from `ItemModels/ids.json`, e.g. `950000-MyMod.mod` |

   Look the material up first (`GameDatabase.Find`) and keep the stand-in's look when it is missing: a released zip, and tests that read the game
   without the deployed mod, don't have it.
3. Commit the model, its `.json` and `ItemModels/ids.json`.

| Setting | Default | Meaning |
| --- | --- | --- |
| `name` | file name, `-`/`_` as spaces | the material record's name |
| `size` | model's own size | longest side in metres; the model is scaled evenly to it |
| `yaw` | `0` | degrees turned about the vertical axis |
| `textureSize` | `1024` | largest texture side in pixels (a power of two, at most 8192) |

- One material per item model (the ITEM names one `material`); the model stands on its lowest point, centred, as the game's item meshes do.
- The game draws an item on the ground, in a hand and in an auto icon with its `mesh` (`ground mesh` first on the ground) and the
  MATERIAL_SPECS_CLOTHING its `material` names (engine `docs/formats/runtime-materials.md` "Callers", `WorldObjectLayout.ItemMesh`).
- The inventory icon is drawn from the model unless the record sets `auto icon image` false and its own `icon`.

### Worn on the hands

An item model with `worn` set also becomes a mesh skinned to the body's hand and forearm bones: knuckles, claws or gauntlets that a WEAPON
of `skill category` 5 (Unarmed) shows on the arms whether or not the character fights (engine `docs/characters.md` "Worn unarmed weapons").

| Setting | Default | Meaning |
| --- | --- | --- |
| `worn` | none | `hands` (one copy on each), `right hand` or `left hand` |
| `grip` | `0.5` | where the palm sits along the model's length, 0 at its fist end and 1 at its arm opening |
| `gripHeight` | `0.5` | the height on the model the hand bone runs through, 0 at its base and 1 at its top. The hand lies below the bone: its back about 0.2 units above it, its palm 0.7 below (**Observed**, `human_male.mesh`) |
| `follows` | `hand` | `hand`: the part past the wrist bends with it (knuckles, claws). `forearm`: the whole model stays on the arm's line when the wrist bends (a blade along the arm, as in a palm strike) |
| `tilt` | `0` | degrees the model is turned about the palm's centre, its fist end towards the back of the hand (negative: the palm), to lay the sleeve along the forearm |

- Model it lying along X: what sticks out past the knuckles towards -X, the arm opening towards +X, the back of the hand up (+Y). `yaw`
  turns it first.
- The import writes `<mod>_<model>_worn.mesh` (the man's skeleton) and `<mod>_<model>_worn_female.mesh` (the woman's) beside the plain mesh.
  Set the WEAPON's `mesh` and `mesh female` to them and keep `ground mesh` the plain one.
- It reads the game's `male_skeleton.skeleton` and `female_skeleton.skeleton`, from `KENSHI_PATH` or the folder above the mods folder. Without
  them (CI's stand-in mods folder) it skips the worn meshes and says so in the log.
- Each copy sits in its hand bone's frame, so the model's top stays on the back of both hands (the left one is the right turned about the arm,
  not mirrored). The palm's centre is 0.7 units past the wrist (**Observed**: `human_male.mesh`'s hand reaches 1.8 units); vertices pass from
  the forearm's weight to the hand's within 0.3 units of the wrist (`follows: forearm`: all on the forearm).
- A model that encloses the fist can hide the wearer's hands: set the WEAPON's bool `hide hands` (engine `docs/characters.md` "Worn
  unarmed weapons").

## Getting a good model

- Ask Meshy or Hi3D for a PBR-textured model and download it as GLB. A few tens of thousands of triangles is plenty; the log flags a model
  over 150,000. Cutting the polycount, choosing the size and shrinking textures: [model-prep.md](model-prep.md).
- Front of the model faces +Z in the file (glTF's convention); use `yaw` to turn it.
- Each material becomes its own part. Generators usually give one; at most 49.
- The model stands on its lowest point, centred.

## What it writes

Into the deployed mod folder (`<Kenshi>/mods/<Mod>/`):

- `structures/<mod>_<model>.mesh` (one per material, `_1`, `_2`, ... when several): Ogre `[MeshSerializer_v1.8]`, position, normal, UV and
  tangent with handedness in w, the layout of the game's own building meshes.
- `structures/<mod>_<model>_d.dds`, `_n.dds`, `_m.dds`: diffuse (BC3), normal (BC1, BC3 when cut out), metalness (BC1), with mips.
- `structures/<mod>_<model>_col.xml`: the collision.
- For an item model: `structures/<mod>_<model>.mesh` and its textures, no collision.
- Records merged into `<Mod>.mod` beside anything else in it (a data generator's records are kept; an earlier import's are replaced): a
  BUILDING, one BUILDING_PART and MATERIAL_SPEC per material, and the unlock (the research's `enable buildings` gains the building); one
  MATERIAL_SPECS_CLOTHING per item model.

File names start with the mod's name because the game looks resources up by bare file name across every mod.

Record ids are `<n>-<Mod>.mod` with n from 900000 up, a block of 100 per model (building +0, parts +1, materials +50); item models count from
950000 (material +0) in `ItemModels/ids.json`. `Structures/ids.json` keeps each model's block so the ids never change: a save names a placed building by its record's id. Never edit or remove a block; removing a
model removes its building from games that placed it.

## Facts the import relies on

Checked against the game's files (Kenshi 1.0.68 data) and with the engine's readers:

- **Units**: the world unit is 0.1 m, Y up; glTF is metres, Y up, so the scale is 10 (the game's collision files use gravity 98.1).
- **Materials**: a building part's look is its MATERIAL_SPEC (`texture map`, `normal map`, `metalness map`, `material type`), not the mesh's
  material name (engine `docs/formats/runtime-materials.md`). The game's building shader (`data/materials/deferred/objects.hlsl`) reads
  diffuse RGB as colour and A as gloss (times `specular mult`), normal RGB as the tangent-space normal and A as the cut-out mask
  (`material type` 1, `alpha threshold`), metalness from R. The import writes gloss = 1 - glTF roughness and metalness from glTF's blue
  channel; glTF MASK and BLEND become a cut-out.
- **Tangents**: the shader's binormal is cross(tangent, normal) × w, and on the game's meshes it points along increasing V (**Verified**:
  `prisoner_pole.mesh` 86 of 86 triangles, `Camp_tent1.mesh` 1858 of 1865). glTF normal maps point green up the image (decreasing V), so the
  import turns the green channel round and sets w the game's way.
- **Collision**: a part's `xml collision` is a PhysX NxuStream2 file; a hand-written minimal file loads (the game's `Camp_tent1_COL.xml` is
  one box). PhysX is Z up and the game maps (x, y, z) to (x, z, -y), so the import writes the part's points as (x, -z, y). The triangle mesh
  goes in as plain points and triangles; the game cooks it on load, Meitou reads it as is (engine `docs/formats/collision.md`).
- **Fields**: BUILDING and BUILDING_PART carry the full field set of the game's Camping Tent 1 (`18026-small_changes_otto.mod`), so nothing
  falls to a default; no `functionality`, so it is a plain building.
- **Unlocking**: a building is in the build menu once a finished research lists it in `enable buildings`; every game starts with
  `_Default Start` (`5359-gamedata.base`) finished. **Verified** with the engine's `GameDatabase`, `ResearchGate` and `BuildingBuildData` over
  the base game plus an imported mod: the structures are unlocked and cost Building Materials as set.

## Not covered yet

Interiors, doors, functions (storage, beds, crafting) and turning parts.
