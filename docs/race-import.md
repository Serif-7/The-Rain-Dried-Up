# Race import: fitting a body onto the game's skeleton

`tools/RaceImport` turns a rigged `.glb` body (Meshy, Hi3D: the "3ds Max Biped" rig, joints `Bip001 ...`) into a body the game animates with
its own human animations: a skinned Ogre mesh linked to `male_skeleton.skeleton` or `female_skeleton.skeleton`, its DDS textures and a part
map. It reads the rig's skeleton, human body and part map from the game install, so it runs on a machine with Kenshi, never in CI; its output is committed.

## Run it

```powershell
dotnet run --project tools/RaceImport -c Release -- <model.glb> <Kenshi folder> TheRainDriedUp/items/races theraindriedup_<character> [male|female] [slim] [bust=male|female] [neck=<share>] [wings] [headless] [ears] [nohair] [skintone] [palms] [rigid] [attach=<part.glb>]...
```

- `<Kenshi folder>`: the game folder (`data/` inside it), e.g. `"C:\Program Files (x86)\Steam\steamapps\common\Kenshi"`.
- The stem names the files and must be unique across mods (the game finds files by bare name): `<modfolder lower case>_<race>`.
- `male` / `female`: the rig, default male. A female body is fitted to `female_skeleton` and measured against `human_female.mesh` and
  `FemaleMask_Default.png` (female animations and voice in game, the editor's bust sliders); a male one to `male_skeleton`, `human_male.mesh`
  and `MaleMask_Default.png`.
- `slim`: 0 to 1, default 1 ([Slimming](#slimming)); lower it for a race meant to be bulkier than its clothes.
- `bust=female`: raise the bust to the female human's height on a male fit (a single-gender woman such as the Crab Queen); default the rig's
  own human ([Bust](#bust)).
- `neck=<share>`: the neck's length as a share of the fitted one, default 1 (the Crabman uses 0.7).
- `wings`: the arms carry wing membranes ([Wings](#wings)).
- `headless`: replace the model's head with the human head, its face poses and the Greenlander's heads and hair ([Headless](#headless)).
- `ears`: `headless`, keeping the model's ears on the human head ([Ears](#ears)).
- `nohair`: `headless`, dropping the model's own hair with its head ([Headless](#headless)).
- `skintone`: the skin takes the editor's skin tone with the human head ([Skin tone](#skin-tone)); writes `<stem>_mask.dds`.
- `palms`: turn the hands palm to thigh ([Hands](#hands)); not for claws or pincers.
- `rigid`: keep the model's own shape and weights ([Rigid](#rigid)); for mechanical bodies.
- `attach=<part.glb>`: merge a static part (ears, wings, horns) into the body, placed by `tools/RaceImport/parts/<part>.json`
  ([Attachments](#attachments)); repeat for more parts, at most 4.
- Writes `<stem>.mesh`, `<stem>_d.dds`, `<stem>_n.dds` (only when the model has a normal map) and `<stem>_parts.png`; the log line gives the
  vertex count, scale, bounds, how many vertices fall in hideable parts, the wing membrane's vertices and the face poses.
- Commit the import's input (the retopologized `.glb` in `Models/<character>/`, a few MB) and its outputs; keep the original high-poly
  `.glb` outside the repo.

## Re-import every race

When a change to the tool alters fitted bodies, re-import every body it affects in the same PR, so the committed bodies match the tool:

1. Prove each body's source and options: build the tool from the commit before your change and import each source with the options in
   [character-pipeline.md "Bodies"](character-pipeline.md#6-bodies); its `.mesh` must be byte-identical to the committed one (the tool is deterministic). A body
   that differs was imported by an older tool; diff its vertices to see what else changes (the tiefling man: only the hands, from `palms`'s
   thumb fix) and say so in the PR.
2. Import every proved body with the changed tool. `rigid` bodies skip slimming and the skin conform.
3. Measure the fit before and after: per region (by each vertex's heaviest bone), the share of vertices over 0.15 units outside the human body's
   skin (the nearest point on the human's triangles, outside by the triangle's outward normal). Record the numbers in the section the change
   touches (as in [Skin conform](#skin-conform)).
4. Render the bind pose front and side, before and after, with the human body's outline over it, and check the shapes no clothing covers
   (tails, manes, claws, wing membranes) are unchanged.

## What the model needs

- One skinned mesh whose skin has the Biped joints `Bip001 Pelvis`, `Spine`, `Spine1`, `Spine2`, `Neck`, `Head`, both `Clavicle`, `UpperArm`,
  `Forearm`, `Hand`, `Thigh`, `Calf`, `Foot`, `Toe0`, and `mixamorig:HeadTop_End` above the head, or the same joints by their Mixamo names
  (`mixamorig:Hips`, `Spine`, `LeftArm`, `LeftForeArm`, `LeftUpLeg`, ...), which the import renames to the Biped's (`SourceBody`). Extra joints (fingers, tails, helpers)
  follow their nearest Biped ancestor; one without, which no vertex uses (a stray root bone), follows the pelvis. **Verified** on Leoma, Crevan, Crabman and the Crab Queen (Meshy exports).
- A base colour texture; a metallic-roughness and a normal texture are used when present.
- Any rest pose: T-pose and A-pose both fit (the game's bind pose has the arms down).

## How the fit works

1. Every vertex is skinned into the rest pose the file shows (inverse bind times joint rest transform).
2. Scale: the game's head bone height over the model's head joint above its lowest vertex.
3. Each spine, arm and leg bone is turned so it points from its joint to its child as the game's does, and stretched to the game's length
   (0.6 to 1.6 times).
4. Pelvis, spines, neck and clavicles are fitted as one rigid piece (least squares onto the game's neck, shoulder and hip joints), so the chest
   keeps its shape; per-bone fits shear it where spine and clavicle weights blend. The pelvis and spine joints are left out of that fit: the
   game's pelvis is level with its hips and a Biped export's sits above them, which dropped the chest about a unit.
5. The head lands half way from where the torso carries it to the game's head joint, so its mesh sits below its bone; with `neck` below 1 it
   then moves that share of the way from the carried neck joint. The game's neck rises
   about a unit above its shoulders and these models' barely does: placing the head on the joint stretched their necks to about three times
   their length (**Observed** on the male Leoma, Crabman and the Crab Queen: fitted neck 2.4 units, the models' own 0.8, the game's 1.4).
6. Feet keep upright and are squashed so the soles land on the ground (paws and crab feet are taller than the game's foot).
7. Each vertex is the weighted sum of its joints' fits; its weights go to the game bones its joints follow, the heaviest four kept (then
   [Human weights](#human-weights) replaces those of the torso).

**Verified**: the C# tool's meshes match the original Python prototype's (positions within 1e-5 units, identical bone weights) on Crabman.

## Slimming

The game's clothing is modelled around the rig's human body, so a body bulkier than it shows through. Around each torso and limb bone (pelvis,
spines, clavicles, upper and lower arms and legs) the tool measures the human body's furthest extent per sixth of the bone and per twelfth of a
turn around it; a fitted vertex beyond that extent plus 0.15 units (1.5 cm of cloth) keeps a fifth of its excess, its correction blended by its
bone weights. The correction fades out between 1.5 and 2.5 times the limit (beyond: an appendage such as a claw, left alone), is zero for a
vertex outside the bone's slices (a tail hanging below the pelvis), and is smoothed over the surface (12 rounds of neighbour averaging, uv
seams welded) so neighbouring vertices move alike. **Observed** (male Leoma): 39% of vertices move, by 1.3 cm on average and 9.5 cm at most.

## Skin conform

Slimming only reaches the human's widest extent per slice, so legs, hips, arms and a bulky waist still poked through trousers, skirts, coats,
sleeves and boots. After slimming, each vertex is pulled onto the human body's skin itself (the nearest point on the triangles round its six
nearest human vertices; the human's skinned triangles only): a vertex outside the skin by more than 0.05 units (5 mm of cloth) moves by its
excess, fully up to 1.0 units out and fading to nothing at 1.6 (beyond: a claw, a tail, left alone). Which skin, and how much of the pull:

- Carried more by one side's clavicle, upper arm and forearm than by the torso: that arm's skin only (the human vertices carried by its
  clavicle, upper arm, forearm or hand), scaled by its weight on those arm bones. With the whole skin, the inside of a hanging arm went to
  the flank beside it.
- Below the pelvis joint within 0.6 units of the middle line: in front of the pelvis joint, the human's pelvis skin only (its vertices carried by
  `Pelvis` or `Spine`), scaled by its torso weight; behind it, it stays. Its nearest skin is an inner thigh, and pulling onto that turned the
  male Crevan's crotch feathers inside out (**Observed**); left alone, the Wolfkin woman's groin tuft stood 0.85 units out of her trousers.
- Any other vertex: the whole skin, scaled by its weight on the pelvis, spines, bust bones, thighs, calves, feet and toes.

The hands and head stay. Tails and the vertices near them, and wing membranes, are spared as in slimming. Three passes, each smoothed (three
rounds, uv seams welded) and the next taking up what smoothing undid.

**Observed** (share of a region's vertices over 0.15 units outside the human skin, torso and legs only → with the arms and groin): Wolfkin
woman upper arms and shoulders 59 → 1%, forearms 46 → 1%, groin front 88 → 7%; Wolfkin man upper arms 49 → 3%; Crevan man upper arms
87 → 22%; Owlkind man upper arms 53 → 9%, groin front 86 → 5%. The torso pull alone (before → after): Leoma male legs 45 → 13%, feet 30 → 0%; Crevan
male hips 25 → 2%; Crab Queen hips 19 → 1%. What stays outside is tails, the manes and neck plumage, the hands and the claws.

## Bust

Every body's bust (a man's chest) is raised to its rig's human's, or the female human's with `bust=female` (**Observed**: the Leoma and
Crevan women sat theirs 2.0 and 1.75 units lower, the male Leoma 1.0, the Crab Queen 1.5 below the female human's, under the clothing's chest
and the part map's bust). The bust is the middle of the quarter-unit slice, between `Spine1` and the shoulders and 0.3 to 1.8 units beside
the middle line, whose frontmost vertex is furthest forward; its top is the bottom of the first slice above whose front is back halfway to
the collar's. The front of the torso from 0.8 below the bust to its top (at least 0.2 above) rises by the whole difference, so the bust keeps
its shape. Below, the rise falls off by a smoothstep over twice the lift (at least 1.5 units); above, almost linearly up to 0.4 above the
clavicles' joint (at least 1.4 times the lift), so the upper chest is squashed evenly and the bust's top slopes down from the collar. The rise
is scaled by the vertex's torso-bone weight and a smoothstep from the spine's depth (0) to the bust's (1), so the back, arms and legs stay.
**Observed**: a narrow Gaussian folded the chest above a 2-unit lift and stretched the bust into a cone; a smoothstep fade reaching into the
neck (whose vertices ride the head) left a ledge above the bust. Then slimming runs.

## Human weights

The editor's body sliders scale the game's bones (engine docs/animation.md "Body shape sliders"): Chest scales `Spine2`, Stomach and Waist
`Spine1`, Neck `Neck`, the bust sliders the female rig's `L Boob` and `R Boob`. **Observed** (all four Meshy Leoma and Crevan bodies): the
models' weights leave `Spine2` and `Neck` empty, carrying the chest on `Spine1` and the clavicles and the neck on the head, so Stomach and
Waist dragged the chest and Chest did nothing; the Leoma tails are bound to a thigh or a toe. On the surface with uv seams
welded:

1. Tails (before slimming): a vertex carried by a leg bone, behind depth -0.6, over 0.8 units from every human vertex and over 1.5 units from every
   foot and toe joint (a heel's talon is no tail: the Owlkind man's stretched behind his walk), goes to `Bip01 Pelvis`; so do its neighbours carried by
   the same bone, behind -0.6, over 0.3 units from the human and away from the feet (the tail's root). Slimming leaves the tail alone and
   fades out within 1.5 units of it, so the back is not pulled in around the tail's root (**Observed**, male Leoma: a notch above the tail).
2. After the skin conform, each vertex carried by the pelvis, spines, neck, clavicles, upper arms, thighs or the bust bones, or by the head below the game's head
   joint, blends to the human's weights there (the six nearest human torso, neck, head, upper-limb vertices' weights by inverse distance):
   fully within 0.5 units of the human, not at all beyond 1.2. A limb vertex takes only its own limb's weights and the torso's next to it
   (an arm: `Spine1`, `Spine2`, `Neck`; a leg: `Pelvis`, `Spine`), a torso vertex the torso's and the limbs' upper bones', never a forearm's
   or calf's. **Observed** (Crab Queen, arms hanging by her hips): taking the nearest human weights of any bone gave the arms pelvis and
   thigh weights and the hips forearm weights, so walking stretched webs between arms, hips and legs.
3. The changed vertices and two rings around them are smoothed: four rounds, each halfway to the neighbours' mean.

The bones (`L Boob` 28, `R Boob` 29) and the female rig's other handles differ from the male's (`R Clavicle` 23 against 25; the male has
`Bip01 Jaw`), so meshes are always written against their own rig's handles.

## Part map

The race's `part map male`/`female` PNG marks which body part each vertex belongs to; worn items' `hide parts` stop drawing those parts
(engine docs/characters.md "Hidden body parts"). The tool gives each fitted vertex the part of the nearest human body vertex carried by
the same bone, read from the rig's part map at that vertex's uv, and writes it at the fitted vertex's own uv (1024 × 1024,
other texels transparent). Each human part is first grown by 0.8 units (a vertex without a part takes that of the nearest marked vertex
carried by the same bone within that distance), since a bulkier body clips over a wider band. Growing across bones gave the hanging
forearms and hands the hip band, which jackets hide (**Observed**, Owlkind woman: holes at the elbows under a jacket).

**Observed** (`MaleMask_Default.png`, 256², sampled at `human_male.mesh` uvs as the game samples them): 642 of 15,504 vertices have a part:
red the upper chest and shoulder tops, white bands round the upper arms, green the belly front. `hide parts` in the base data (rebirth.mod):
Drifter's and Padded Leather Jacket 1, Sleeveless Longcoat and Dustcoat 2, Longcoat and Dustcoat 3, Ninja Gi and Rags 4, robes 5, plate
jackets, skin suits and Crab Armour 7; Holy Servant Rags, Monk Pants and hoods 0. Items with 0 hide nothing; slimming covers those.
**Observed** (`FemaleMask_Default.png` at `human_female.mesh` uvs): red the bust (1,102 vertices), white the upper-arm bands (560), green the
belly (250).

## Hands

The fit turns each bone onto the game's direction but keeps its twist, so a model whose palms face forward or down in its rest pose keeps
them so (the wolves' paws faced forward). The game's hands hang palm to thigh, their breadth running front to back (**Verified** on
`human_male.mesh` and `human_female.mesh`); the skeleton has no finger bones, so a hand is one rigid shape on `Bip01 L Hand` / `R Hand`. With
`palms` each hand is turned about its forearm so the breadth of the vertices it carries (fingers included) runs front to back, the forearm taking
half the turn so neither wrist nor elbow wrings; a turn under about 30 degrees, or a hand without a clear palm, is left alone. With `palms` a
helper joint hung off the forearm in its last quarter (Meshy's thumb) follows the hand, so the thumb turns with it in the fit and in play. The
log line gives the turns and the count of such wrist joints. Crab claws are pincers, not palms: leave the option off for them.

## Wings

Meshy binds an arm wing's membrane to helper bones and the spine, which pins it to fixed points as the arm moves. With `wings`, before the
fit (`WingRig`), every vertex past an arm's shoulder (its side of the upper arm joint, above the `Spine` joint) is bound by its place along the
arm instead: the upper arm, the forearm or the hand, blended over 15% of the forearm's length either side of the elbow and the wrist, the
hand's segment reaching 0.8 forearms past the wrist for wing tips. The membrane is what hangs below the arm further than 0.7 times the reach of
the arm's skin above it at that point (measured in eight stretches along the arm).

After the fit (`WingSpread`) each membrane vertex shares its weight between its arm bones and `Bip01 Spine2`, so the membrane spans arm and
flank like a bat's: it opens as the arm rises or reaches and closes as it falls. The model shows the wing spread (arms out), the game's bind
pose has the arms down, so the vertex is placed where that blend takes it back to the model's spread wing when the game turns each arm bone
back to the model's direction. Of the blends (0 to 1 on the arm, in hundredths) each vertex takes the one that places it nearest the arm's bones, then is drawn
within 0.25 units of them: a lowered arm hides its folded membrane inside itself, and the spread wing comes back within that much of the
model's. The membrane's weights are kept through the human weights' copy, and the skin conforms leave it alone. Kenshi's skeleton has no wing
bones, so the wings move only with the arms.

In the part map the membrane up to the wrist is white (the upper-arm bands), which every sleeved item hides (jackets, coats, robes, plate
jackets, skin suits); a membrane needs no hide part of its own. Past the wrist it keeps its nearest human part (hands show).

The model's membrane must hang from the arm alone: one joined to the torso (batman-rigged's reaches the lats) keeps that part on the body.

## Headless

With `headless` the body wears the game's human head (`HumanHead`, `HeadGraft`): the vertices of the rig's human body whose uv lies below 0,
which the body shader colours from the HEAD record's texture at uv + (0, 1) (engine docs/characters.md "Shading, poses and LOD"), with their
triangles, weights and the mesh's face poses (target 1, written to the fitted mesh with offsets only, as the game's bodies store them; 23 on
`human_male.mesh`, 29 on `human_female.mesh`). After the fit:

1. The fitted vertices within 0.8 units of the head's open neck rim (the lowest loop of open edges; the eye sockets are open too) (more than a fitted triangle) move 0.05 under the closest point of the
   human's skin and take its weights there (barycentric), so the body's neck runs into the human's along the rim with no gap.
2. The other fitted vertices whose nearest human vertex is the head's are dropped with their triangles; what the cut leaves open lies under
   the head's skin.
3. The head is appended. Its vertices are left out of the part map.

With `nohair` the model's hair goes too: the hair triangles ([Skin tone](#skin-tone) sorts them) reached from the dropped head's through
other hair triangles, so long hair hanging down the back leaves with the head; a vertex another triangle keeps stays.

Hair holes: Meshy fuses long hair to the body as one shell with no skin under it, so dropping it opens the upper back and shoulders
(**Observed**, tiefling woman: one open loop of 95 edges from y 12.3 to 17.3, see-through in game). `HairHolePatch` closes them with the human's
own skin. It takes the human triangles on the torso, neck, clavicle and upper-arm bones that are bare: no body triangle lies within 0.25 of a
corner or of the centre. Only bare regions that reach within 0.85 of one of the body's open edges count, so skin under a fuller body is not
copied. They grow 3 rings into triangles the body covers from outside, sit 0.04 under the human's skin with its normals and weights, and take
one skin texel of the body's texture, which the skin-tone mask covers. The body's skin is drawn over them where they overlap. The import log
counts them ("triangles patched under the hair"); without `nohair` the import is unchanged.

The race then keeps the Greenlander's heads, hair and random faces and uses the human face sliders ([character-pipeline.md](character-pipeline.md#7-make-it-a-race)).
The head's skin is the HEAD texture's and the body's the model's, so their tones differ at the rim unless the body is imported `skintone`.

## Ears

With `ears` the model's own ears stay (`HeadEars`): on each side, the dropped head vertices from the skull's highest point down 0.35 of the
head's height and within half that across, joined to that point through the model's surface. Their base's middle is mapped from the model's
skull (its other dropped vertices' box) to the human head's box, snapped to the head's nearest vertex and moved 0.1 inside it, and the ear is
scaled 1.4 about it, so it stands out of the skull and the hair; every ear vertex is carried by `Bip01 Head` alone. Ears fused with the
model's hair bring some of it along; hats clip them. A separate ears model is an [attachment](#attachments) instead.

## Attachments

`attach=<part.glb>` merges a static (unskinned) model into the fitted body after the head graft (`Attachment`, `Attachments`). Its spec is
`tools/RaceImport/parts/<the file's stem>.json`:

| Field | Meaning |
| --- | --- |
| `bones` | Bones it rides, top to bottom: a vertex between two joints by height is weighted between them, above or below them wholly on the end one |
| `height` | The whole model's height once placed, game units |
| `split` | Two mirrored pieces split at X = 0 (default true); each is placed as the right one and mirrored for the left |
| `anchor` | The point of each piece placed: shares of its bounds, X inner (0) to outer (1), Y bottom to top, Z back to front |
| `relativeTo` | `HeadTop` (highest vertex carried most by `Bip01 Head`, centred) or a bone's joint (its X taken as 0) |
| `at` | The anchor's place from `relativeTo`: outward, up, forward |
| `roll`, `pitch`, `yaw` | Degrees about the anchor, in that order: top outward, top back, outer edge back |
| `behind` | Each piece moved front to back until its nearest point is this far behind the body vertices `bones` carry (overrides `at`'s Z) |

A double-sided material's triangles are doubled facing back (the game culls back faces). The part's vertices are no body part in the
[part map](#part-map) (clothing never hides them) and have no source vertex (-1, like the head's). The body texture becomes an atlas
twice as wide ([Textures](#textures)): the body's uvs (not the head's) move into its left half, each part's into a 1024² tile of the right
half, two by two in `attach` order. Parts: `wolfears` (on `Bip01 Head`, at the top sides of the skull, rolled out 15°; hair may clip them),
`horns` (the tieflings' ram horns on `Bip01 Head`, rooted in the scalp's top sides from their inner end, rolled out 25° and tipped back 10°)
and `batwings` (folded, from the shoulder blades to mid thigh, `Spine2` down to `Pelvis`, yawed 45° so the outer edges fold back behind
the arms, 0.3 behind the back).

## Skin tone

The body shader multiplies the colour texture by the editor's `Skin Tone` colour where the race's `body mask` (red) and the HEAD's `mask map`
say skin (engine docs/characters.md "Shading, poses and LOD"). With `skintone` (`TextureClasses`, `SkinTone`):

1. Each triangle is sorted by its texels in the base colour (512², mean over the triangle): skin when its mean chroma (largest channel less
   smallest) is at least 0.07, else hair when its mean largest channel is at least 0.4, else other (clothing, hooves). **Observed**
   (tiefling-female.glb): skin and horns 0.10 to 0.13 chroma, the top and shorts 0.03 to 0.06, the hair below 0.03 and 0.45 to 0.8 light.
2. The skin triangles' texels (grown 4 px at 2048²) are repainted to the Greenlander heads' bare skin (`SkinTone.HumanSkin`) times their luma
   over the skin's median luma, keeping the model's shading.
3. `<stem>_mask.dds` (1024², BC1) is white over the final mesh's triangles whose three vertices came from skin (uv 0 to 1 only; the head takes
   its HEAD's mask), grown a texel; drawn at the final uvs, so with [attachments](#attachments) the skin lies in its left half and the parts
   stay black.

Head and body then share one base colour and one multiplier, so every slider position tints them alike. The race takes the mask as its `body mask`
([character-pipeline.md](character-pipeline.md#7-make-it-a-race)).

## Rigid

With `rigid` the fitted body skips slimming, the skin conform, the tail move and the human weights: every vertex keeps the weights of its
model's joints (each on the game bone its joint follows) and its fitted place. **Observed** (beepbot, a skeletal robot with an open rib cage on
a spring spine): the human weights gave the ribs clavicle and upper-arm weights, so an arm swung across the chest (`kr_charged_slash`) drew
the rib cage into a sheet; with `rigid` ribs and joints stay solid. The editor's Chest and Neck sliders do nothing on such a body (Meshy
leaves `Spine2` and `Neck` without vertices, [Human weights](#human-weights)), and clothing fits only as far as the body sits inside the
human's.

## Textures

- `_d`: base colour, alpha = gloss = (255 - roughness) / 2 (roughness 200 without a map); 2048², BC3 with mips; with
  [attachments](#attachments) 4096 × 2048, the parts in the right half.
- `_n`: the game's character layout, X in alpha and Y in green, Y flipped from glTF's +Y up; red and blue 0.
