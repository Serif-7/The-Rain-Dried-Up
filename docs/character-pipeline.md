# Character pipeline: from a `.glb` to a body Kenshi animates

The order of work that turns a character `.glb` (Meshy, Hi3D, Blender, a rip of your own model) into a body the game animates with its own
human animations. What each tool does: [retopology.md](retopology.md), [race-import.md](race-import.md).

Folders, as PowerShell variables (set them once; every command below uses them):

```powershell
$Repo   = "C:\Users\<you>\source\repos\The-Rain-Dried-Up"                # this repo
$Kenshi = "C:\Program Files (x86)\Steam\steamapps\common\Kenshi"
$Models = "C:\Users\<you>\Models"                                         # original high-poly .glb files, outside the repo
$Blender = "C:\Users\<you>\bpyenv\Scripts\python.exe"                     # Python with bpy (step 2)
```

**Work in `$Repo`:**

```powershell
cd $Repo
```

## 1. Inspect the model

```powershell
python tools\model-info.py "$Models\raiden_textured_rigged.glb"
```

It needs a skinned mesh with a rig. RaceImport reads 3ds Max Biped joints (`Bip001 Pelvis`, ...) or Mixamo joints (`mixamorig:Hips`, ...);
any other rig gets a joint map in step 2 ([race-import.md "What the model needs"](race-import.md#what-the-model-needs)).

## 2. Retopologize to the budget

Kenshi's `human_male.mesh` is 5,168 triangles (body only; heads and hair are separate meshes). Aim for about 7,000 for a whole character
with its own head, hair and gear, one material and one 2048² texture. A model already in budget with one material skips this step.

```powershell
& $Blender tools\Retopo\retopo.py -- --source "$Models\raiden_textured_rigged.glb" --out Models\raiden\raiden-retopo-rigged --bones Models\raiden\raiden-bones.json --hair Hair_1 --drop Hair --drop rai_decal --hair-shade 0.5
```

Options and what the script does: [retopology.md](retopology.md). Check the result before importing:
[retopology.md "Check it"](retopology.md#check-it).

## 3. Choose the import options

| The model | Option |
| --- | --- |
| A woman with a man and woman in the subrace | `female` (fits `female_skeleton`, female animations, bust bones) |
| A single-gender woman (forced onto the male rig) | `male bust=female` |
| Palms face forward or down in the rest pose (twisted hands in game) | `palms` |
| A stretched neck in the first import | `neck=0.7` (lower for shorter) |
| Meant to look bulkier than its clothes | a number from 0 to 1, e.g. `0.5` (slimming strength, default 1) |
| Should wear the human head, its hair and face sliders | `headless` |
| Armour or a mechanical body whose shape must stay (spikes, plates, open ribs) | `rigid` |

Without `rigid` the body is slimmed onto the human's skin so clothing covers it; that flattens armour plates and spikes.

## 4. Run the import

```powershell
dotnet run --project tools\RaceImport -c Release -- Models\raiden\raiden-retopo-rigged.glb $Kenshi TheRainDriedUp\items\races theraindriedup_raiden male rigid
```

- The stem (`theraindriedup_<character>`) names the files and must be unique across all mods: the game finds files by bare name.
- Writes `<stem>.mesh`, `<stem>_d.dds`, `<stem>_n.dds` and `<stem>_parts.png`. A bad model exits non-zero; the log line gives the vertex
  count, scale and bounds (20 units = 2 m).

## 5. Check the poses

Pose the body with clips and render it, without the game. The clips are KenshiRaces' animation library
(`KenshiRaces/items/animations/kenshiraces_anims_male.skeleton` in Meitou-Modding-Ecosystem/KenshiRaces):

```powershell
dotnet run --project tools\Poser -c Release -- TheRainDriedUp\items\races\theraindriedup_raiden.mesh "$Kenshi\data\character\meshes\male_skeleton\male_skeleton.skeleton" <anims.skeleton> kr_walking 0.3 walk.obj
```

Arguments: mesh, skeleton, clip file (`-` for the bind pose), clip name, time (0 to 1 of the clip), output OBJ (metres, Z up). Open the OBJ in
Blender with `<stem>_d.dds` as its texture. Check the idle, `kr_walking` and `kr_charged_slash` (an arm across the chest): no webs between arms
and hips, the chest keeps its shape, the hair follows the head.

## 6. Bodies

| Body | Source | Retopo | Import options |
| --- | --- | --- | --- |
| `theraindriedup_raiden` | `raiden_textured_rigged.glb` (65,585 triangles, 15 materials, own rig) | `Models/raiden/raiden-bones.json`, `--hair Hair_1 --drop Hair --drop rai_decal --hair-shade 0.5`: 6,997 triangles | `male rigid` |
| `theraindriedup_raidenfull` | the same | the same with `--body-tris 1000000 --hair-tris 6000 --atlas 4096` (no decimation; one material, solid hair): 52,265 triangles | `male rigid` |

## 7. Add it to the mod

1. Add a `ModCharacter` to `TheRainDriedUp/Characters.cs` with the next free ids (`<n>-TheRainDriedUp.mod`) and add it to `All`. The data
   phase (`CharacterRecords`) makes its RACE, a copy of the Greenlander with the body and no human head or hair, and its CHARACTER, a Drifter
   of that race with no kit.
2. Build and deploy (`dotnet run --project Play`, [play.md](play.md)): Play copies `items/` into the mod's folder.
3. In game: F10 **The Rain Dried Up: recruit window** opens a window listing every character; **Recruit** spawns one into the selected
   character's squad at the camera target ([README "Testing in game"](../README.md#testing-in-game)).
