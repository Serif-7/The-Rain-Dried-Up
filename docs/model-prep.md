# Preparing a model

How to get a generated `.glb` (Meshy, Hi3D, Blender) to a polycount, texture size and real-world size the game handles, before it goes into
`<Mod>/Structures/` (buildings) or `<Mod>/ItemModels/` (items). The import itself: [structures.md](structures.md).

Commands run from the repo root in PowerShell. Needs Python 3; `prepare-model.py` also needs `pip install pillow numpy`.

## 1. Inspect

```powershell
python tools\model-info.py <Mod>\Structures\my-hut.glb
```

It prints the size in metres (X, Y up, Z), the longest side, triangles, materials, and each texture's pixel size and MB. A note about node
scale or rotation means `prepare-model.py` can't rescale it: export it again from Blender with transforms applied (Object > Apply > All
Transforms).

## 2. Cut the polycount

| Kind | Triangles | Textures |
| --- | --- | --- |
| Building or decoration | 5,000 to 20,000 | 2048 |
| Item (held, dropped, in a shop) | under 5,000 | 1024 |

Generators often give 100,000 or more; the import warns above 150,000. To cut it:

- **Meshy:** Remesh the model with a target polycount in the budget above (quad or triangle topology both work), keeping the textures, and
  download it as GLB again.
- **Blender:** File > Import > glTF, select the mesh, add a Decimate modifier (Collapse) and lower Ratio until the face count in the status
  bar is in budget, apply it, then File > Export > glTF Binary (`.glb`) with Apply Modifiers on.

Collision is simplified separately (`collisionTriangles`, default 2000), so cutting the visible mesh is about frame rate and file size.

## 3. Choose the size

The import treats the model as metres; a generator's size is arbitrary. Pick the real size and set it in the model's settings file
([structures.md "Settings"](structures.md#settings)), not in the model:

- Building: `"height"` in metres in `<model>.json`. Kenshi's people are about 1.8 m tall and read small beside real-world sizes, so take the
  size you'd describe and add 20% (a 2.2 m post becomes `2.64`).
- Item: `"size"`, its longest side in metres, in `<model>.json` beside it in `ItemModels/`.

## 4. Shrink the textures

```powershell
python tools\prepare-model.py <Mod>\Structures\my-hut.glb 2048
```

This rewrites the file in place with every texture at most that many pixels a side (2048 for buildings, 1024 for items), usually cutting it
from tens of MB to a few. An optional third argument also rescales the model to that longest side in metres, for when a settings file isn't
wanted. The import halves bigger textures too (`textureSize`), but committing the shrunk file keeps the repo small.

Run `model-info.py` again to check the result.

## 5. Import and check

Build and deploy with Play (or run the import alone, [structures.md "Quick start"](structures.md#quick-start)). The log has one line per model:

```
Structure my-hut.glb -> "My Hut": 2 part(s), 12480 triangles, 3.1 m tall, collision mesh, unlocked by start
Item beast-capsule.glb -> mesh mymod_beast_capsule.mesh, material 950000-MyMod.mod, 4082 triangles, 0.11 m long
```

Check the height or length and the triangle count, then look at it in the game: place the building, or drop and pick up the item.

## 6. Commit

Commit the `.glb`, its `.json` and the folder's `ids.json` together. `ids.json` keeps each model's record ids fixed, and saves name placed
buildings by those ids.
