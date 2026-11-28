# Agent instructions

The Rain Dried Up: a Kenshi mod for MeitouClient (mod folder `TheRainDriedUp/`, file stem prefix `theraindriedup_`). GPL-3.0-or-later.
`CLAUDE.md` is canonical; keep `AGENTS.md` identical.

## The mod

- `TheRainDriedUp/`: the code mod (`meitou.json`, entry `TheRainDriedUpMod`). Its version lives only in `meitou.json`; bump it in any change
  players get. Records are added in the data phase (`CharacterRecords`) with ids `<n>-TheRainDriedUp.mod`; characters are listed in
  `Characters.cs`. The F10 row opens `RecruitWindow`; `RecruitSystem` spawns.
- `TheRainDriedUp.Tests/`: xunit, no game install. Add tests with every change. `dotnet test`.
- `Play` builds the engine and the mod, deploys the dll, manifest and `items/` to the game, and runs the structure import (docs/play.md).
- `Directory.Build.rsp` stamps engine references as the oldest supported release (`-p:AssemblyVersion=<floor>.0`); it, `gameVersion`'s floor
  in `meitou.json` and the engine pin move together (a nightly counts as the release it leads to).

## Importing models

- Skill `.claude/skills/model-import` is the procedure; the docs are in `docs/` (README "Importing models" lists them).
- Buildings and items: `tools/StructureImport` (docs/structures.md, docs/model-prep.md). Characters: retopology then `tools/RaceImport`
  (docs/character-pipeline.md). Budgets: buildings 5,000 to 20,000 triangles at 2048², items under 5,000 at 1024², characters about
  7,000 with one material and one 2048² atlas (vanilla `human_male.mesh`: 5,168, body only).
- Commit each model's import input (the in-budget `.glb`, its `.json`, the folder's `ids.json`, a joint map), the original it was
  retopologized from (`Models/<character>/source/`, so the work can be redone), and, for characters, the RaceImport output in
  `TheRainDriedUp/items/races/`.
- Add each new character body to docs/character-pipeline.md "Bodies" (input, retopo options, import options, triangles) and its
  re-import command, so it can be redone.
- Never commit Kenshi's own files (meshes, skeletons, textures) or content dumps.

## Engine

- `engine/` is the MeitouClient submodule (VlcekM/MeitouClient), pinned to an upstream tag. The C# tools build against its `Meitou.Data`. Never
  edit or commit inside it; move the pin to a newer tag when a tool needs it, and rebuild the tools.
- `git pull` does not move submodules: `git submodule update --init` after a pull.

## Docs

- Short: facts and instructions, one place per fact, no progress notes. Update the doc a change affects in the same commit.
- Setup guides: no `cd` unless the directory changes; bold each directory change; with more than two directories, list them first as
  PowerShell variables and use the variables in every command.
