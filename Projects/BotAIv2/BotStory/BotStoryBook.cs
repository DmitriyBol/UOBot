using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

public sealed class BotStoryBook
{
    public BotStoryBook(string name) => Name = name;

    public string Name { get; }

    public DateTime FirstSeen;

    public DateTime LastSeen;

    public bool Born;

    public bool Female;

    public string Class;

    public string People;

    public string Town;

    public string SeenGuild;

    public string SeenRank;

    public string SeenHouse;

    public bool SeenChampion;

    public bool SeenVeteran;

    public readonly List<BotStoryNote> Notes = new(8);

    public readonly BotStoryProse[] Voiced = new BotStoryProse[BotStages.Count];

    public long? OpenedTick;

    public int Chapters;

    public int Lived;

    public int Unvoiced;

    public bool Has(string kind)
    {
        for (var i = 0; i < Notes.Count; i++)
        {
            if (Notes[i].Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    public bool Said(string text, string key, DateTime now)
    {
        var self = key ?? text;

        for (var i = Notes.Count - 1; i >= 0; i--)
        {
            var note = Notes[i];

            if ((now - note.At).TotalHours < 24 && string.Equals(note.Key ?? note.Text, self, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public void Of(BotStage stage, List<BotStoryNote> into)
    {
        into.Clear();

        for (var i = 0; i < Notes.Count; i++)
        {
            if (Notes[i].Stage == stage)
            {
                into.Add(Notes[i]);
            }
        }

        for (var i = 1; i < into.Count; i++)
        {
            var note = into[i];
            var j = i - 1;

            while (j >= 0 && into[j].At > note.At)
            {
                into[j + 1] = into[j];
                j--;
            }

            into[j + 1] = note;
        }
    }
}

public sealed class BotStoryNote
{
    public BotStage Stage;

    public string Kind;

    public string Text;

    public string Key;

    public DateTime At;

    public double Weight;

    public bool Baseline => Kind == "seen";
}

public sealed class BotStoryProse
{
    public ulong Hash;

    public string Text;

    public DateTime At;
}

public enum BotStage
{
    Youth,
    Apprenticeship,
    Guild,
    Deeds,
    Friends,
    Home,
    Family,
    OldAge
}

public static class BotStages
{
    public const int Count = 8;

    public static string Key(BotStage stage) =>
        stage switch
        {
            BotStage.Youth          => "youth",
            BotStage.Apprenticeship => "apprenticeship",
            BotStage.Guild          => "guild",
            BotStage.Deeds          => "deeds",
            BotStage.Friends        => "friends",
            BotStage.Home           => "home",
            BotStage.Family         => "family",
            _                       => "old age"
        };

    public static string Title(BotStage stage) =>
        stage switch
        {
            BotStage.Youth          => "Youth",
            BotStage.Apprenticeship => "Apprenticeship",
            BotStage.Guild          => "The guild",
            BotStage.Deeds          => "Deeds",
            BotStage.Friends        => "Friends and foes",
            BotStage.Home           => "Home",
            BotStage.Family         => "Family",
            _                       => "Old age"
        };
}

public sealed class BotStoryChapter
{
    public BotStage Stage;

    public readonly List<string> Facts = new(8);

    public string Text;

    public string Tally;

    public ulong Hash;

    public bool Lived;

    public string Voiced;

    public bool Present => Facts.Count > 0 || Tally != null;

    public void Clear(BotStage stage)
    {
        Stage = stage;
        Facts.Clear();
        Text = null;
        Tally = null;
        Hash = 0;
        Lived = false;
        Voiced = null;
    }
}

public sealed class BotStoryAsk
{

    public string Kind { get; init; }

    public string Subject { get; init; }

    public string Title { get; init; }

    public string Who { get; init; }

    public string Facts { get; init; }

    public int MostWords { get; init; }

    public string Model { get; init; }
}

public sealed class BotChronicleEntry
{
    public DateTime At;

    public string Kind;

    public string Text;

    public string Key;

    public double Weight;
}

public sealed class BotChronicleDay
{
    public string Key;

    public DateTime Date;

    public readonly List<BotChronicleEntry> Entries = new(16);

    public BotStoryProse Voiced;

    public int Dropped;

    public ulong? Hashed;
}
