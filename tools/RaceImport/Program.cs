using RaceImport;

// Fits a Biped .glb body onto one of the game's human skeletons (docs/race-import.md). Arguments: the model, the game folder (its
// skeleton, human body and part map are read), the output folder, the files' stem (e.g. theraindriedup_raiden), then optionally the rig
// (male or female, default male), the slimming strength (0 to 1, default 1), bust=male|female, neck=<share> and the flags and parts of
// docs/race-import.md "Run it".
if (args.Length < 4 || ImportOptions.Parse(args.Skip(4)) is not { } options)
{
    Console.Error.WriteLine("Usage: RaceImport <model.glb> <Kenshi folder> <output folder> <stem> [male|female] [slim 0-1] [bust=male|female] [neck=<share>] [wings] [headless] [ears] [nohair] [skintone] [palms] [rigid] [attach=<part.glb>]...");
    return 2;
}

try
{
    Console.WriteLine(BodyImporter.Import(args[0], args[1], args[2], args[3], options));
    return 0;
}
catch (Exception e) when (e is InvalidDataException or IOException or KeyNotFoundException)
{
    Console.Error.WriteLine($"Race import failed: {e.Message}");
    return 1;
}
