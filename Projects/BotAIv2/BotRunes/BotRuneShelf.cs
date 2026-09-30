using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>One place a guild holds a rune for: what it is called, what kind of place, and where the rune puts a body down.</summary>
public sealed class BotRunePlace
{
    public string Name;

    public string Kind;

    public Map Map;

    public Point3D Spot;

    public string By;

    public DateTime When;

    public int Uses;

    public override string ToString() => $"{Name} ({Spot.X}, {Spot.Y})";
}

/// <summary>
/// What each guild holds runes for, kept across restarts: the guild's knowledge of the places its mages have stood.
///
/// <para>
/// <b>The knowledge, not the runes.</b> A rune is an item in somebody's pack or in the library's book, and a restart
/// raises the population afresh with empty packs; what a guild knows is kept here instead, by guild name, and the
/// library's book is written from it (<see cref="BotRuneLibrary"/>) — the same thing <c>BotProgress</c> does for skills.
/// A place is one per name: the seat marked again after the hall moved replaces the old seat.
/// </para>
/// </summary>
public static class BotRuneShelf
{
    private static readonly Dictionary<string, List<BotRunePlace>> _shelves = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, int> _versions = new(StringComparer.Ordinal);

    private static readonly List<BotRunePlace> _none = [];

    public static long Evicted { get; private set; }

    public static IReadOnlyList<BotRunePlace> Of(string guild) =>
        guild != null && _shelves.TryGetValue(guild, out var places) ? places : _none;

    public static int Version(string guild) => guild != null && _versions.TryGetValue(guild, out var v) ? v : 0;

    public static BotRunePlace Held(string guild, Map map, Point3D at, int within)
    {
        var places = Of(guild);
        BotRunePlace best = null;
        var nearest = int.MaxValue;

        for (var i = 0; i < places.Count; i++)
        {
            var place = places[i];
            var away = BotRunes.Apart(place.Spot, at);

            if (place.Map == map && away <= within && away < nearest)
            {
                nearest = away;
                best = place;
            }
        }

        return best;
    }

    public static BotRunePlace Named(string guild, string name)
    {
        var places = Of(guild);

        for (var i = 0; i < places.Count; i++)
        {
            if (string.Equals(places[i].Name, name, StringComparison.Ordinal))
            {
                return places[i];
            }
        }

        return null;
    }

    public static BotRunePlace File(string guild, string name, string kind, Map map, Point3D spot, string by)
    {
        if (guild == null || name == null || map == null)
        {
            return null;
        }

        if (!_shelves.TryGetValue(guild, out var places))
        {
            _shelves[guild] = places = [];
        }

        var place = Named(guild, name);

        if (place == null)
        {
            place = new BotRunePlace { Name = name };
            places.Add(place);
        }

        place.Kind = kind;
        place.Map = map;
        place.Spot = spot;
        place.By = by;
        place.When = Core.Now;

        while (places.Count > Math.Max(1, BotRunes.Shelf))
        {
            var drop = -1;

            for (var i = 0; i < places.Count; i++)
            {
                var other = places[i];

                if (ReferenceEquals(other, place) || other.Kind == "seat")
                {
                    continue;
                }

                if (drop < 0 || other.Uses < places[drop].Uses || other.Uses == places[drop].Uses && other.When < places[drop].When)
                {
                    drop = i;
                }
            }

            if (drop < 0)
            {
                break;
            }

            places.RemoveAt(drop);
            Evicted++;
        }

        _versions[guild] = Version(guild) + 1;

        return place;
    }

    public static void Used(string guild, Map map, Point3D spot)
    {
        var place = Held(guild, map, spot, BotRunes.Near);

        if (place != null)
        {
            place.Uses++;
        }
    }

    public static int Places
    {
        get
        {
            var many = 0;

            foreach (var places in _shelves.Values)
            {
                many += places.Count;
            }

            return many;
        }
    }

    public static string Describe()
    {
        if (_shelves.Count == 0)
        {
            return "no guild holds a rune";
        }

        using var line = Server.Text.ValueStringBuilder.Create(256);

        line.Append($"{Places} places held by {_shelves.Count} guilds (");

        var first = true;

        foreach (var (guild, places) in _shelves)
        {
            line.Append(first ? "" : ", ");
            line.Append($"{guild} {places.Count}");
            first = false;
        }

        line.Append($"), {Evicted} dropped off a full shelf");

        return line.ToString();
    }

    internal static void Save(IGenericWriter writer)
    {
        writer.WriteEncodedInt(_shelves.Count);

        foreach (var (guild, places) in _shelves)
        {
            writer.Write(guild);
            writer.WriteEncodedInt(places.Count);

            for (var i = 0; i < places.Count; i++)
            {
                var place = places[i];

                writer.Write(place.Name);
                writer.Write(place.Kind);
                writer.Write(place.Map);
                writer.Write(place.Spot);
                writer.Write(place.By);
                writer.Write(place.When);
                writer.WriteEncodedInt(place.Uses);
            }
        }
    }

    internal static int Load(IGenericReader reader)
    {
        _shelves.Clear();
        _versions.Clear();

        var read = 0;
        var guilds = reader.ReadEncodedInt();

        for (var g = 0; g < guilds; g++)
        {
            var guild = reader.ReadString();
            var many = reader.ReadEncodedInt();
            List<BotRunePlace> places = [];

            for (var i = 0; i < many; i++)
            {
                var place = new BotRunePlace
                {
                    Name = reader.ReadString(),
                    Kind = reader.ReadString(),
                    Map = reader.ReadMap(),
                    Spot = reader.ReadPoint3D(),
                    By = reader.ReadString(),
                    When = reader.ReadDateTime(),
                    Uses = reader.ReadEncodedInt()
                };

                if (place.Name != null && place.Map != null && place.Map != Map.Internal)
                {
                    places.Add(place);
                    read++;
                }
            }

            if (!string.IsNullOrEmpty(guild) && places.Count > 0)
            {
                _shelves[guild] = places;
                _versions[guild] = 1;
            }
        }

        return read;
    }

    public static void Forget()
    {
        Evicted = 0;
    }
}

/// <summary>What each guild holds runes for, kept between boots. Shape 1. See <see cref="BotRuneShelf"/>.</summary>
public sealed class BotRuneStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRuneStore));

    private const int Shape = 1;

    private static BotRuneStore _store;

    public static void Configure() => _store ??= new BotRuneStore();

    public BotRuneStore() : base("BotRunes", 15)
    {
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        BotRuneShelf.Save(writer);
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape < 1 || shape > Shape)
        {
            logger.Warning("The saved runes are shape {Found} and this build reads {Wanted}; the guilds start with none", shape, Shape);

            return;
        }

        var read = BotRuneShelf.Load(reader);

        if (read > 0)
        {
            logger.Information("Runes: {Read} places the guilds hold runes for read back from the save", read);
        }
    }
}
