using Meitou.Data.Fcs;

// Writes an empty .mod file at each path given: a code-only mod's folder still needs one for the game to list it. The play build (Play.csproj)
// calls it for mods whose .mod is missing and that have no data generator.
foreach (string path in args)
{
    var file = new FcsFile { Author = "Meitou mod", Description = $"{Path.GetFileNameWithoutExtension(path)}: code only." };
    FcsWriter.WriteFile(file, path);
    Console.WriteLine($"Wrote empty {path}");
}

return 0;
