using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Spells;

namespace Server.BotAI.V2;

public static class BotProvision
{
    public static bool Running { get; set; } = true;

    public static double BandageShare { get; set; } = 0.25;

    public static int Bottles { get; set; } = 1;

    public static double AmmoShare { get; set; } = 0.5;

    public static int Casts { get; set; } = 5;

    public static long Asked { get; private set; }

    public static long Turned { get; private set; }

    public static long NoCloth { get; private set; }

    public static long NoBottle { get; private set; }

    public static long NoAmmo { get; private set; }

    public static long NoHerbs { get; private set; }

    public static long Funded { get; private set; }

    public static long FundedGold { get; private set; }

    public static long Unfunded { get; private set; }

    private static readonly Dictionary<int, Type[]> _herbs = [];

    private static readonly List<int> _spells = [];

    public static long Cut { get; private set; }

    public static bool Fit(Mobile body, out string why) => Check(body, true, out why);

    public static bool Short(Mobile body) => !Check(body, false, out _);

    public static int BandageGuess { get; set; } = 5;

    public static int BottleGuess { get; set; } = 15;

    public static int HerbGuess { get; set; } = 3;

    public static int ScrollGuess { get; set; } = 10;

    public static int Bill(Mobile body)
    {
        if (!Running || body is not BotMobile bot || bot.Class is not { } klass || bot.Backpack is not { } pack)
        {
            return 0;
        }

        var bill = 0;
        var kit = klass.Kit;

        if (kit.Bandages > 0)
        {
            var need = (int)Math.Ceiling(kit.Bandages * BandageShare) - pack.GetAmount(typeof(Bandage));

            if (need > 0)
            {
                bill += need * BotMarket.Worth(typeof(Bandage), BandageGuess);
            }
        }

        var bottles = BotOutfit.PotionsFor(klass);

        for (var i = 0; i < bottles.Count; i++)
        {
            var (kind, many) = bottles[i];
            var need = Math.Min(many, Bottles) - pack.GetAmount(kind);

            if (need > 0)
            {
                bill += need * BotMarket.Worth(kind, BottleGuess);
            }
        }

        if (kit.Reagents > 0)
        {
            Key(bot, klass, _spells);

            for (var s = 0; s < _spells.Count; s++)
            {
                var herbs = Herbs(bot, _spells[s]);

                for (var i = 0; i < herbs.Length; i++)
                {
                    var need = Casts - pack.GetAmount(herbs[i]);

                    if (need > 0)
                    {
                        bill += need * BotMarket.Worth(herbs[i], HerbGuess);
                    }
                }
            }
        }

        return bill;
    }

    public static int CastCost(Mobile bot, int spell)
    {
        if (bot == null || spell < 0)
        {
            return 1;
        }

        if (BotStrike.Scroll(bot, spell) != null)
        {
            var scroll = BotGrimoire.ScrollFor(spell);

            return Math.Max(1, scroll == null ? ScrollGuess : BotMarket.Worth(scroll, ScrollGuess));
        }

        var herbs = Herbs(bot, spell);
        var cost = 0;

        for (var i = 0; i < herbs.Length; i++)
        {
            cost += BotMarket.Worth(herbs[i], HerbGuess);
        }

        return Math.Max(1, cost);
    }

    private static bool Check(Mobile body, bool count, out string why)
    {
        why = null;

        if (!Running || body is not BotMobile bot || bot.Class is not { } klass)
        {
            return true;
        }

        var pack = bot.Backpack;

        if (pack == null)
        {
            why = "no pack";

            return false;
        }

        if (count)
        {
            Asked++;
        }

        var kit = klass.Kit;

        if (kit.Bandages > 0)
        {
            var need = (int)Math.Ceiling(kit.Bandages * BandageShare);
            var have = pack.GetAmount(typeof(Bandage));

            if (have < need && pack.GetAmount(typeof(Cloth)) > 0)
            {
                Cut += BotWeave.Bandages(body, need - have);
                have = pack.GetAmount(typeof(Bandage));
            }

            if (have < need)
            {
                why = $"{have} of {need} bandages";

                return Turn(count, ref _cloth);
            }
        }

        var bottles = BotOutfit.PotionsFor(klass);

        for (var i = 0; i < bottles.Count; i++)
        {
            var (kind, many) = bottles[i];
            var need = Math.Min(many, Bottles);

            if (need > 0 && pack.GetAmount(kind) < need)
            {
                why = $"no {kind.Name}";

                return Turn(count, ref _bottle);
            }
        }

        if (bot.Bond is { } bond && bond.Weapon is { Ammunition: { } quiver })
        {
            var granted = BotBinding.BoundCount(quiver, bond);
            var need = (int)Math.Ceiling(granted * AmmoShare);
            var have = pack.GetAmount(quiver);

            if (granted > 0 && have < need)
            {
                why = $"{have} of {need} {quiver.Name}";

                return Turn(count, ref _ammo);
            }
        }

        if (kit.Reagents > 0)
        {
            Key(bot, klass, _spells);

            for (var s = 0; s < _spells.Count; s++)
            {
                var herbs = Herbs(bot, _spells[s]);

                for (var i = 0; i < herbs.Length; i++)
                {
                    var have = pack.GetAmount(herbs[i]);

                    if (have < Casts)
                    {
                        why = $"{have} {herbs[i].Name} for {Casts} casts";

                        return Turn(count, ref _herb);
                    }
                }
            }
        }

        return true;
    }

    private static long _cloth;

    private static long _bottle;

    private static long _ammo;

    private static long _herb;

    private static bool Turn(bool count, ref long bucket)
    {
        if (count)
        {
            Turned++;
            bucket++;
            NoCloth = _cloth;
            NoBottle = _bottle;
            NoAmmo = _ammo;
            NoHerbs = _herb;
        }

        return false;
    }

    private static void Key(Mobile bot, BotClass klass, List<int> spells)
    {
        spells.Clear();

        switch (klass.Role)
        {
            case BotRole.Caster:
                {
                    var strongest = BotStrike.Strongest(bot);

                    if (strongest >= 0)
                    {
                        spells.Add(strongest);
                    }

                    break;
                }
            case BotRole.Medic:
                {
                    if (BotGrimoire.Holds(bot, BotArsenal.SpellGreaterHeal))
                    {
                        spells.Add(BotArsenal.SpellGreaterHeal);
                    }
                    else if (BotGrimoire.Holds(bot, BotArsenal.SpellHeal))
                    {
                        spells.Add(BotArsenal.SpellHeal);
                    }

                    if (BotGrimoire.Holds(bot, BotArsenal.SpellCure))
                    {
                        spells.Add(BotArsenal.SpellCure);
                    }

                    break;
                }
            default:
                {
                    if (BotGrimoire.Holds(bot, BotArsenal.SpellHeal))
                    {
                        spells.Add(BotArsenal.SpellHeal);
                    }

                    break;
                }
        }
    }

    private static Type[] Herbs(Mobile bot, int spell)
    {
        if (_herbs.TryGetValue(spell, out var herbs))
        {
            return herbs;
        }

        herbs = SpellRegistry.NewSpell(spell, bot, null)?.Reagents ?? [];
        _herbs[spell] = herbs;

        return herbs;
    }

    private static void Armed(Mobile bot, BotClass klass, List<int> spells)
    {
        Key(bot, klass, spells);

        if (klass.Role == BotRole.Caster)
        {
            return;
        }

        var attack = BotStrike.Stocked(bot);

        if (attack >= 0 && !spells.Contains(attack))
        {
            spells.Add(attack);
        }
    }

    public static int DoorFloor(Mobile body, Type kind) => Floor(body, kind, false);

    public static int Floor(Mobile body, Type kind) => Floor(body, kind, true);

    private static int Floor(Mobile body, Type kind, bool armed)
    {
        if (!Running || kind == null || body is not BotMobile bot || bot.Class is not { } klass)
        {
            return 0;
        }

        var kit = klass.Kit;

        if (kind == typeof(Bandage))
        {
            return kit.Bandages > 0 ? (int)Math.Ceiling(kit.Bandages * BandageShare) : 0;
        }

        var bottles = BotOutfit.PotionsFor(klass);

        for (var i = 0; i < bottles.Count; i++)
        {
            if (bottles[i].Item1 == kind)
            {
                return Math.Min(bottles[i].Item2, Bottles);
            }
        }

        if (bot.Bond is { } bond && bond.Weapon is { Ammunition: { } quiver } && quiver == kind)
        {
            return (int)Math.Ceiling(BotBinding.BoundCount(quiver, bond) * AmmoShare);
        }

        if (kit.Reagents > 0 && typeof(BaseReagent).IsAssignableFrom(kind))
        {
            if (armed)
            {
                Armed(bot, klass, _spells);
            }
            else
            {
                Key(bot, klass, _spells);
            }

            for (var s = 0; s < _spells.Count; s++)
            {
                var herbs = Herbs(bot, _spells[s]);

                for (var i = 0; i < herbs.Length; i++)
                {
                    if (herbs[i] == kind)
                    {
                        return Casts;
                    }
                }
            }
        }

        return 0;
    }

    public static bool Consumable(Type kind) =>
        kind != null && (kind == typeof(Bandage) || typeof(BasePotion).IsAssignableFrom(kind)
            || typeof(BaseReagent).IsAssignableFrom(kind) || kind == typeof(Arrow) || kind == typeof(Bolt));

    public static bool Fund(Mobile body, int cost)
    {
        var short0 = cost - BotYield.Spendable(body);

        if (short0 <= 0)
        {
            return true;
        }

        if (body is not BotMobile bot || bot.Guild is not Guild guild)
        {
            Unfunded++;

            return false;
        }

        var paid = 0;

        var spare = BotDues.Spare(guild);

        if (spare < short0)
        {
            BotDues.Keep(guild, short0, spare);
        }

        var drawn = BotChest.Draw(guild.Name, Math.Min(short0, spare));

        if (drawn > 0)
        {
            if (!Banker.Deposit(bot, drawn))
            {
                bot.Backpack?.TryDropItem(bot, new Gold(drawn), false);
            }

            paid += drawn;
        }

        var rest = short0 - drawn;

        if (rest > 0)
        {
            if (!BotGuilds.Stand(bot, rest))
            {
                if (paid > 0)
                {
                    BotYield.Aside(bot, -paid);
                    Funded++;
                    FundedGold += paid;
                }

                Unfunded++;

                return false;
            }

            paid += rest;
        }

        BotYield.Aside(bot, -paid);
        Funded++;
        FundedGold += paid;

        return true;
    }

    public static string Describe() =>
        !Running
            ? "no supplies are asked at any door"
            : $"supplies: asked {Asked} times at a door and {Turned} turned away ({NoCloth} for bandages, {NoBottle} for bottles, "
            + $"{NoAmmo} for ammunition, {NoHerbs} for reagents); the guild paid {Funded} restocks, {FundedGold}gp in all, "
            + $"and could not pay {Unfunded}";

    public static void Forget()
    {
        Asked = 0;
        Turned = 0;
        _cloth = _bottle = _ammo = _herb = 0;
        NoCloth = NoBottle = NoAmmo = NoHerbs = 0;
        Funded = 0;
        FundedGold = 0;
        Unfunded = 0;
    }
}
