using Meitou.Data.Fcs;

namespace StructureImport;

/// <summary>
/// Puts the structures' records into the mod's <c>.mod</c> beside whatever else it holds (a data generator's records, or nothing): the records of
/// an earlier import are replaced, everything else is kept.
/// </summary>
public static class ModFileMerger
{
    /// <summary>
    /// Writes <paramref name="records"/> into the data file at <paramref name="path"/> (created when missing), replacing an earlier import's.
    /// </summary>
    public static void Merge(string path, IReadOnlyList<FcsRecord> records)
    {
        string modFile = Path.GetFileName(path);
        var file = File.Exists(path)
            ? FcsReader.ReadFile(path)
            : new FcsFile { Author = "Meitou mod", Description = $"{Path.GetFileNameWithoutExtension(path)}: structures imported from models." };

        RemoveEarlierImport(file, modFile);
        foreach (var record in records) Add(file, record);
        Renumber(file);
        FcsWriter.WriteFile(file, path);
    }

    /// <summary>
    /// Removes the import's records and its unlock links; a research change left with nothing in it goes too.
    /// </summary>
    static void RemoveEarlierImport(FcsFile file, string modFile)
    {
        file.Records.RemoveAll(r => RecordIds.IsImported(r.StringId, modFile));
        foreach (var record in file.Records.Where(r => r.RecordType == FcsRecordType.Research))
        {
            if (!record.References.TryGetValue("enable buildings", out var links)) continue;

            links.RemoveAll(l => RecordIds.IsImported(l.TargetStringId, modFile));
            if (links.Count == 0) record.References.Remove("enable buildings");
        }

        file.Records.RemoveAll(r => r.RecordType == FcsRecordType.Research && IsEmptyChange(r));
    }

    /// <summary>
    /// Adds a record; a change to a record the file already changes is folded into it (its links appended).
    /// </summary>
    static void Add(FcsFile file, FcsRecord record)
    {
        var existing = file.Records.FirstOrDefault(r => r.StringId == record.StringId);
        if (existing is null)
        {
            file.Records.Add(record);
            return;
        }

        foreach (var (list, links) in record.References)
        {
            if (!existing.References.TryGetValue(list, out var into)) existing.References[list] = into = [];
            into.AddRange(links.Where(l => !into.Contains(l)));
        }
    }

    /// <summary>
    /// Whether a record changes nothing: no name, fields, links or instances.
    /// </summary>
    static bool IsEmptyChange(FcsRecord r) =>
        r.Name.Length == 0 && r.Bools.Count == 0 && r.Floats.Count == 0 && r.Ints.Count == 0 && r.Vector3S.Count == 0 && r.Vector4S.Count == 0
        && r.Strings.Count == 0 && r.Filenames.Count == 0 && r.References.Count == 0 && r.Instances.Count == 0;

    /// <summary>
    /// Numbers the records 1, 2, ... in file order and sets the file's next id after them. This renumbers a generator's records too, which is
    /// harmless: the game keys records by string id, and the numeric id only builds string ids in files older than format 7 (engine
    /// docs/formats/fcs-mod.md).
    /// </summary>
    static void Renumber(FcsFile file)
    {
        for (int i = 0; i < file.Records.Count; i++) file.Records[i].Id = i + 1;
        file.NextId = Math.Max(file.NextId, file.Records.Count + 1);
    }
}
