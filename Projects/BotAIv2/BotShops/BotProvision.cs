using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Spells;

namespace Server.BotAI.V2;

/// <summary>
/// What a bot must carry before it goes into a fight it chose — a dungeon's delve, or a war company sent against somebody
/// else's ground — and the guild's money for what it cannot buy itself.
///
/// <para>
/// <b>Patrick's order of 26.09.2026: "supply and the purchase of every consumable needed — THIS IS COMPULSORY. A mage may
/// not go without reagents for its casts, a healer without bandages, and the other classes have no right to go without
/// bottles and bandages either. Set everybody an entry threshold."</b> Until then nothing at any door asked what a bot
/// carried: the delve, the war company and the hunt asked only its health and its class. The proving ground showed what
/// that costs once the world is awake — a healer with no bandage, mages with no reagents, archers with a dozen arrows,
/// all losing to one orcish mage.
/// </para>
///
/// <para>
/// <b>The threshold is the class's own kit, at a share.</b> Bandages: half the kit's. Bottles: one of each kind the class
/// carries. Ammunition: half the quiver it was born with. Reagents: <see cref="Casts"/> casts of its key spell — a caster's
/// strongest attack in its book, a healer's greatest heal and its cure, a warrior-mage's heal. Every share sits at or below
/// the point where the shopper already buys (<c>BotShopper.Short</c>), so a bot turned away is always a bot the shopper
/// will send for more.
/// </para>
///
/// <para>
/// <b>Asked at the doors a bot chooses to walk through, and at no other.</b> A delve's leader and every member it calls,
/// and a member of a war company going onto somebody else's ground. Not a solo hunt — that is how a poor bot earns the money
/// for its bandages, and closing it would lock the poor out of the thing that makes them less poor — and not the defence
/// of its own ground: a bot does not ask its pack before it defends its door.
/// </para>
/// </summary>
public static class BotProvision
{
    public static bool Running { get; set; } = true;

    public static double BandageShare { get; set; } = 0.5;

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

    public static bool Consumable(Type kind) =>
        kind != null && (kind == typeof(Bandage) || typeof(BasePotion).IsAssignableFrom(kind)
            || typeof(BaseReagent).IsAssignableFrom(kind) || kind == typeof(Arrow) || kind == typeof(Bolt));

    public static bool Fund(Mobile body, int cost)
    {
        var short0 = cost - BotYield.Wealth(body);

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
