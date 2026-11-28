using StructureImport;

// Imports a mod's models as buildable structures, and its item models as items' looks (docs/structures.md). Arguments: the models folder
// (<Mod>/Structures; <Mod>/ItemModels beside it is read too), then the mod's folder in the game's mods folder (its name is the mod's).
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: StructureImport <models folder> <mod folder in Kenshi/mods>");
    return 2;
}

if (!Directory.Exists(args[0]) && !Directory.Exists(Importer.ItemFolderBeside(args[0])))
{
    Console.Error.WriteLine($"No folder {args[0]} or {Importer.ItemFolderBeside(args[0])}");
    return 2;
}

try
{
    foreach (string line in Importer.ImportFolder(args[0], args[1])) Console.WriteLine(line);
    return 0;
}
catch (Exception e)
{
    // One line for any failure (a bad setting, an invalid or unreadable model): the build log shows it and the build fails.
    Console.Error.WriteLine($"Structure import failed: {e.Message}");
    return 1;
}
