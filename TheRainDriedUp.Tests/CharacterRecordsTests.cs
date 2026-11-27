using Meitou.Data;
using Meitou.Data.Fcs;

namespace TheRainDriedUp.Tests;

/// <summary>
/// Each character's records are the game's Greenlander and Drifter with the body's files, no human head or hair and no kit, built from a
/// synthetic data file standing in for the game.
/// </summary>
public sealed class CharacterRecordsTests
{
    public static TheoryData<string> Names => [.. Characters.All.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void A_race_is_a_single_gender_Greenlander_with_the_body_and_no_human_head(string name)
    {
        var character = Characters.All.Single(c => c.Name == name);
        var race = new FcsRecord { StringId = character.RaceId, RecordType = FcsRecordType.Race, Name = name };

        CharacterRecords.FillRace(race, Game(), character);

        Assert.Equal(1.25f, race.Floats["walk speed"]);
        string stem = @".\mods\TheRainDriedUp\items\races\" + character.Stem;
        Assert.Equal(stem + ".mesh", race.Filenames["male mesh"]);
        Assert.Equal(stem + "_d.dds", race.Filenames["body texture male"]);
        Assert.Equal(stem + "_n.dds", race.Filenames["nm male"]);
        Assert.Equal(stem + "_parts.png", race.Filenames["part map male"]);
        Assert.True(race.Bools["single gender"]);
        Assert.Empty(race.References["heads male"]);
        Assert.Empty(race.References["hairs"]);
        Assert.Equal(0, race.Ints["morph num"]);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_character_is_a_male_Drifter_of_its_race_without_kit_or_talk(string name)
    {
        var character = Characters.All.Single(c => c.Name == name);
        var record = new FcsRecord { StringId = character.CharacterId, RecordType = FcsRecordType.Character, Name = name };

        CharacterRecords.FillCharacter(record, Game(), character);

        Assert.Equal(7, record.Ints["strength"]);
        Assert.Equal(0, record.Ints["female chance"]);
        Assert.Empty(record.References["weapons"]);
        Assert.Empty(record.References["dialogue"]);
        Assert.Equal(character.RaceId, Assert.Single(record.References["race"]).TargetStringId);
    }

    [Fact]
    public void The_records_ids_are_distinct_and_in_the_mods_file()
    {
        string[] ids = [.. Characters.All.SelectMany(c => new[] { c.RaceId, c.CharacterId })];

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.EndsWith("-TheRainDriedUp.mod", id));
    }

    /// <summary>
    /// A database holding a Greenlander and a Drifter (with a sword in its kit and a talk), from a data file written to a temporary folder.
    /// </summary>
    static GameDatabase Game()
    {
        var greenlander = new FcsRecord { StringId = CharacterRecords.Greenlander, RecordType = FcsRecordType.Race, Name = "Greenlander" };
        greenlander.Floats["walk speed"] = 1.25f;
        greenlander.Filenames["male mesh"] = "human_male.mesh";
        greenlander.Ints["morph num"] = 5;
        greenlander.References["heads male"] = [new("1-heads.mod", 100, 0, 0)];
        greenlander.References["hairs"] = [new("3-hair.mod", 100, 0, 0)];
        var drifter = new FcsRecord { StringId = CharacterRecords.Drifter, RecordType = FcsRecordType.Character, Name = "Drifter" };
        drifter.Ints["strength"] = 7;
        drifter.Ints["female chance"] = 50;
        drifter.References["weapons"] = [new FcsReference("9-test.mod", 1, 0, 0)];
        drifter.References["dialogue"] = [new FcsReference("8-test.mod", 0, 0, 0)];
        var sword = new FcsRecord { StringId = "9-test.mod", RecordType = FcsRecordType.Weapon, Name = "Sword" };

        string path = Path.Combine(Directory.CreateTempSubdirectory("theraindriedup").FullName, "test.mod");
        var file = new FcsFile();
        file.Records.AddRange([greenlander, drifter, sword]);
        FcsWriter.WriteFile(file, path);
        var order = new LoadOrder();
        order.Entries.Add(new LoadOrderEntry("test.mod", path));
        return GameDatabase.Load(order);
    }
}
