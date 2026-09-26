using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What a bot ought to be wearing, worked out from what this shard can actually make and what it costs.
///
/// <para>
/// <b>The catalogue is asked of the engine, never written down here.</b> A table of armour types with their
/// protection and their price would be right on the day it was typed and wrong the first time anybody edits
/// an armour definition or a recipe — and wrong <em>silently</em>, which is the expensive kind. So this
/// walks the craft systems the shard already runs, keeps every recipe whose product is a piece of armour,
/// builds exactly one of each, reads its numbers off the object, and throws it away. Everything below is a
/// fact the engine gave up: protection, the agility it costs, the strength it wants, whether a caster can
/// meditate in it, and how much material one takes.
/// </para>
///
/// <para>
/// <b>Only what can be made, because a want nobody can fill is worse than no want.</b> Coming from the
/// recipes rather than from the item types means a piece is on this list if and only if some bot on this
/// island could in principle forge or sew it — and the skill it needs comes along with it, so "nobody is
/// good enough yet" is a fact the board can be told rather than a mystery on it.
/// </para>
///
/// <para>
/// <b>And the answer is harm stopped per gold — over the piece's whole life, not per blow.</b> Ranked on
/// armour rating alone every bot wants plate, and twenty bots saving for plate is twenty bots not buying
/// arrows. Ranked on rating per gold, which is what this did first, everybody buys leather — and that is
/// wrong for precisely the bots that need armour most, because a piece is not bought by the point, it is
/// bought by the afternoon. Plate carries more rating <em>and</em> more durability, so it stops nearly three
/// times the harm a leather tunic does before either is scrap. Both numbers are the engine's own.
/// </para>
/// </summary>
public static class BotHarness
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHarness));

    public static double AgilityPerArmour { get; set; } = 1.0;

    public static double NimbleCare { get; set; } = 3.0;

    public const double AbsorbedPerPoint = 0.75;

    public const double BlowsPerPoint = 8.0;

    public static int GoldPerHide { get; set; } = 4;

    public static int LeastCost { get; set; } = 10;

    /// <summary>One kind of armour, as the engine describes it.</summary>
    public sealed class Piece
    {
        public Type Kind { get; init; }

        public Layer Where { get; init; }

        public double Rating { get; init; }

        public int Agility { get; init; }

        public int Strength { get; init; }

        public int Lasts { get; init; }

        public double Absorbs => Rating * AbsorbedPerPoint * Lasts * BlowsPerPoint;

        public ArmorMeditationAllowance Meditation { get; init; }

        public bool Male { get; init; }

        public bool Female { get; init; }

        public bool Fits(Mobile body) => body == null || (body.Female ? Female : Male);

        public SkillName Craft { get; init; }

        public double Needs { get; init; }

        public int Cost { get; init; }

        public override string ToString() =>
            $"{Kind.Name} ({Rating:F0} armour × {Lasts} wear = {Absorbs:F0} damage stopped for {Cost}gp, {Craft} {Needs:F0})";
    }

    private static readonly List<Piece> _pieces = [];

    public static IReadOnlyList<Piece> Pieces => _pieces;

    public static IReadOnlyList<Layer> Layers { get; } =
    [
        Layer.InnerTorso,
        Layer.Pants,
        Layer.Arms,
        Layer.Gloves,
        Layer.Helm,
        Layer.Neck
    ];

    private static bool Covers(Layer where)
    {
        for (var i = 0; i < Layers.Count; i++)
        {
            if (Layers[i] == where)
            {
                return true;
            }
        }

        return false;
    }

    public static int RetryMs { get; set; } = 30000;

    private static long _surveyedTick;

    private static bool _surveyed;

    public static void Survey()
    {
        _pieces.Clear();

        Read(DefBlacksmithy.CraftSystem, SkillName.Blacksmith, typeof(IronIngot), BotDig.GoldPerIngot);
        Read(DefTailoring.CraftSystem, SkillName.Tailoring, typeof(Leather), GoldPerHide);

        _pieces.Sort(static (a, b) => a.Cost.CompareTo(b.Cost));

        _surveyedTick = Core.TickCount;
        _surveyed = _pieces.Count > 0;

        if (!_surveyed)
        {
            logger.Warning("No armour could be surveyed yet: the craft systems may not be built. Trying again in {Wait}ms", RetryMs);

            return;
        }

        logger.Information(
            "Armour surveyed: {Count} pieces this shard could make, from {Cheap} up to {Dear}",
            _pieces.Count,
            _pieces.Count == 0 ? "nothing" : _pieces[0].ToString(),
            _pieces.Count == 0 ? "nothing" : _pieces[^1].ToString()
        );
    }

    private static void Read(CraftSystem system, SkillName craft, Type material, int perUnit)
    {
        if (system == null)
        {
            return;
        }

        var recipes = system.CraftItems;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];

            if (!BotCraftwork.Simple(recipe, material) || recipe.ItemType == null)
            {
                continue;
            }

            var made = Sample(recipe.ItemType);

            if (made == null)
            {
                continue;
            }

            try
            {
                if (made is not BaseArmor armour || !Covers(armour.Layer))
                {
                    continue;
                }

                _pieces.Add(
                    new Piece
                    {
                        Kind = recipe.ItemType,
                        Where = armour.Layer,
                        Rating = armour.ArmorRating,

                        Agility = Math.Max(0, -armour.DexBonus),
                        Strength = armour.StrRequirement,

                        Lasts = Math.Max(1, (armour.InitMinHits + armour.InitMaxHits) / 2),
                        Meditation = armour.MeditationAllowance,
                        Male = armour.AllowMaleWearer,
                        Female = armour.AllowFemaleWearer,
                        Craft = craft,
                        Needs = BotCraftwork.Requirement(recipe, craft),
                        Cost = Math.Max(LeastCost, BotCraftwork.Cost(recipe) * perUnit)
                    }
                );
            }
            finally
            {
                made.Delete();
            }
        }
    }

    private static Item Sample(Type kind)
    {
        try
        {
            return Activator.CreateInstance(kind) as Item;
        }
        catch (MissingMethodException)
        {
            var made = Defaulted(kind);

            if (made != null)
            {
                return made;
            }

            logger.Warning(
                "{Kind} has no constructor that can be called without arguments, so it is left out of the armoury",
                kind.Name
            );

            return null;
        }
        catch (Exception e)
        {
            logger.Warning("{Kind} could not be built to be measured, so it is left out of the armoury: {Why}", kind.Name, e.Message);

            return null;
        }
    }

    private static Item Defaulted(Type kind)
    {
        var ctors = kind.GetConstructors();

        for (var i = 0; i < ctors.Length; i++)
        {
            var wants = ctors[i].GetParameters();

            if (wants.Length == 0)
            {
                continue;
            }

            var args = new object[wants.Length];
            var all = true;

            for (var j = 0; j < wants.Length; j++)
            {
                if (!wants[j].HasDefaultValue)
                {
                    all = false;

                    break;
                }

                args[j] = wants[j].DefaultValue;
            }

            if (!all)
            {
                continue;
            }

            try
            {
                if (ctors[i].Invoke(args) is Item made)
                {
                    return made;
                }
            }
            catch (Exception e)
            {
                logger.Warning("{Kind} threw while being built for the armoury: {Why}", kind.Name, e.Message);

                return null;
            }
        }

        return null;
    }

    public static int Purse(BotMobile bot)
    {
        var resolve = bot?.Resolve;

        if (resolve == null)
        {
            return 0;
        }

        var earned = (int)(resolve.Beaten(Core.TickCount) * GoldPerBlow);

        return Math.Max(LeastPurse, earned);
    }

    public static int LeastPurse
    {
        get
        {
            var dearest = 0;

            for (var i = 0; i < _pieces.Count; i++)
            {
                var piece = _pieces[i];

                if (piece.Craft == SkillName.Tailoring && piece.Cost > dearest)
                {
                    dearest = piece.Cost;
                }
            }

            return dearest > 0 ? dearest * 2 : 120;
        }
    }

    public static int GoldPerBlow { get; set; } = 200;

    public static long Misfits { get; private set; }

    public static Piece Best(BotMobile bot, Layer where, Func<SkillName, double> skill) =>
        Best(bot, where, skill, int.MaxValue);

    public static Piece Best(BotMobile bot, Layer where, Func<SkillName, double> skill, int purse)
    {
        if (!_surveyed && Core.TickCount - _surveyedTick >= RetryMs)
        {
            Survey();
        }

        Piece best = null;
        var bestWorth = 0.0;

        for (var i = 0; i < _pieces.Count; i++)
        {
            var piece = _pieces[i];

            if (piece.Where != where)
            {
                continue;
            }

            if (!piece.Fits(bot))
            {
                Misfits++;

                continue;
            }

            if (skill != null && piece.Needs > skill(piece.Craft))
            {
                continue;
            }

            if (piece.Cost > purse)
            {
                continue;
            }

            var worth = Worth(bot, piece);

            if (worth > bestWorth)
            {
                bestWorth = worth;
                best = piece;
            }
        }

        return best;
    }

    public static double Worth(BotMobile bot, Piece piece)
    {
        var klass = bot?.Class;

        if (klass == null || piece == null || piece.Rating <= 0.0)
        {
            return 0.0;
        }

        if (klass.NeedsMeditation && piece.Meditation != ArmorMeditationAllowance.All)
        {
            return 0.0;
        }

        if (piece.Strength > bot.RawStr)
        {
            return 0.0;
        }

        var cares = klass.Role == BotRole.Ranged ? NimbleCare : 1.0;
        var agility = Math.Clamp(
            1.0 - piece.Agility * AgilityPerArmour * cares / Math.Max(1, bot.RawDex),
            0.0,
            1.0
        );

        return piece.Absorbs * agility / piece.Cost;
    }

    public static double Ablest(SkillName craft)
    {
        var bots = BotPopulation.Bots;
        var best = 0.0;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Alive: true })
            {
                continue;
            }

            if (craft == SkillName.Tailoring && BotThread.Kit(bot) == null)
            {
                continue;
            }

            var able = bot.Skills[craft].Base - BotCraftwork.Margin;

            if (able > best)
            {
                best = able;
            }
        }

        return best;
    }

    public static string Describe() =>
        _pieces.Count == 0
            ? "no armour has been surveyed yet"
            : $"{_pieces.Count} kinds of armour this shard can make, {Covering()} of the {Layers.Count} places a bot can cover, {Misfits} passed over as cut for the other sex";

    private static int Covering()
    {
        var covered = 0;

        for (var i = 0; i < Layers.Count; i++)
        {
            for (var j = 0; j < _pieces.Count; j++)
            {
                if (_pieces[j].Where == Layers[i])
                {
                    covered++;

                    break;
                }
            }
        }

        return covered;
    }
}
