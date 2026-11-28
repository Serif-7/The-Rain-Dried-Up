# Retopology: a high-poly character to a Kenshi-style budget

`tools/Retopo/retopo.py` runs in Blender's Python (`pip install bpy` into a venv, Python 3.13) and turns a rigged, multi-material character
`.glb` into one RaceImport reads: few triangles, one material, one baked atlas, Biped joint names. No Blender window, no GPU.

## Run it

```powershell
& $Blender tools\Retopo\retopo.py -- --source <in.glb> --out <out stem> [--bones <map.json>] [--body-tris 6200] [--hair-tris 800] [--hair <material>]... [--drop <material>]... [--hair-shade 1.0] [--atlas 2048]
```

- `--bones`: a JSON object from this rig's joint names to Biped names without `Bip001 ` (`"pelvis": "Pelvis"`, `"arm left elbow":
  "L Forearm"`). Required unless the rig already has `Bip001` or `mixamorig:` joints. The joints RaceImport needs:
  [race-import.md "What the model needs"](race-import.md#what-the-model-needs). Example: `Models/raiden/raiden-bones.json`.
- `--body-tris`: the body's triangles after decimation, default 6,200.
- `--hair`: a material of alpha hair cards, rebuilt as a solid shell of `--hair-tris` triangles (default 800). Repeat for more.
- `--drop`: a material to leave out: decals and duplicate hair layers that need alpha. Repeat for more.
- `--hair-shade`: multiplies the hair colour; strand cards look darker than their texels, so 0.5 matches dark hair.
- `--atlas`: the colour and normal size, default 2048 (roughness half that).
- Writes `<out>.glb` (rigged, one material) and the baked `<out>_d.png`, `<out>_n.png`, `<out>_mr.png`. Model-info lists the materials' names.

## What it does

1. Renames the joints by `--bones`, and adds `mixamorig:HeadTop_End` at the top of the skull (the highest vertex carried mostly by
   `Bip001 Head`, hair left out): RaceImport scales by the head joint and points the head at that joint.
2. Body: every material but the hair, vertices welded within 0.5 mm, then Decimate (Collapse, symmetric in X) to `--body-tris`. Decimation
   keeps the vertex groups, so the weights stay.
3. Hair: the game's character textures carry gloss in alpha, not transparency, so cards would show as flat planes. The cards are voxel
   remeshed (1.2 cm) into one closed shell, decimated, and take the cards' weights (Data Transfer, nearest face interpolated).
4. Weights limited to 4 per vertex and normalized (the game's limit).
5. New UVs (Smart UV Project, 60°), packed into one atlas.
6. Baked from the original (selected to active, Cycles, cage extrusion 3 cm): base colour as emission (a diffuse bake turns metallic parts
   black), the tangent-space normal map (with the original's own normal maps), and roughness into the green of a metallic-roughness map.
   Transparent hair texels are first filled with the opaque ones' mean colour.

## Check it

- The log's `low poly:` line: triangles near the target, one material.
- Render it front, side and back beside the original, and as a wireframe; pose an arm and a leg with its own rig to see the weights follow.
- Things decimation loses: thin straps and cables (raise `--body-tris`), small emblems (they survive in the baked colour), hair strands (a
  shell keeps the silhouette only).
- Then import it ([character-pipeline.md](character-pipeline.md#4-run-the-import)) and check the poses there.
