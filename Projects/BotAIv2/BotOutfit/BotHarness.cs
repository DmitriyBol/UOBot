using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotHarness
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHarness));

    public static double AgilityPerArmour { get; set; } = 1.0;

    public static double NimbleCare { get; set; } = 3.0;

    public const double AbsorbedPerPoint = 0.75;

    public const double BlowsPerPoint = 8.0;

    public static int GoldPerHide { get; set; } = 4;

    public static int LeastCost { get; set; } = 10;

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

        public Type Material { get; init; }

        public int Units { get; init; }

        public int PerUnit { get; init; }

        public int Making()
        {
            if (Material == null || Units <= 0)
            {
                return Cost * 2;
            }

            var stand = Math.Max(1, PerUnit);
            var unit = Math.Max(stand, BotMarket.Worth(Material, Math.Max(stand, BotShops.Lowest(Material))));

            return (int)Math.Min(int.MaxValue, Math.Max((long)Cost, (long)Units * unit) * 2);
        }

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
                        Cost = Math.Max(LeastCost, BotCraftwork.Cost(recipe) * perUnit),
                        Material = material,
                        Units = BotCraftwork.Cost(recipe),
                        PerUnit = perUnit
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
