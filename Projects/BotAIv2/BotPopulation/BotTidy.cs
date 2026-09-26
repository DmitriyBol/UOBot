using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// What a bot carries and wears, kept in order: an unidentified thing is identified — off a corpse, in the pack or worn — and
/// the robe the engine dresses a risen body in is taken off.
///
/// <para>
/// <b>Item Identification at a hundred, and nothing ever used it.</b> Patrick's order of 08.09.2026 gave every bot the skill
/// at a hundred (<see cref="BotMobile.Common"/>) so that a population that loots could tell what it had picked up; nothing on
/// the shard ever asked for the skill, so a magic sword off a corpse was "an unidentified longsword" in the pack, on the
/// stall and on a guild's counter for ever. Patrick, 16.09.2026, 23:1x: a bot holding an unidentified thing identifies it.
/// With the check the engine's skill makes on its target (<see cref="ItemIdentification"/>):
/// <c>CheckTargetSkill(ItemID, item, 0, 100)</c>, which at a hundred cannot fail, and the flag it sets.
/// </para>
///
/// <para>
/// <b>At the corpse, because the pack is only where loot passes through.</b> Build 98 used the skill the way a player
/// does — use it, then point the target it raises at the thing — on whatever lay in the pack every five seconds, and
/// counted nothing in its first quarter of an hour: <c>BotSlay.Rifle</c> lists everything it lifts on the market in the
/// same breath, and a listing takes the thing out of the world (<c>BotListing.Add</c>). So a thing is appraised as it comes
/// off the corpse, before it is listed, and the look on the beat covers what is worn as well as what is carried — a magic
/// blade bought off a stall goes straight into the hand (<c>BotMobile.Rearm</c>). The check without the skill's own
/// target: no bandage's or spell's cursor is taken, and a corpse with three magic things has all three appraised rather
/// than one a second.
/// </para>
///
/// <para>
/// <b>The death robe.</b> <c>PlayerMobile.Resurrect</c> dresses every body that rises in a grey robe, and nothing a bot does
/// ever took it off, so the population wore them after every death. Patrick, the same evening: throw them away, sell them to
/// a shopkeeper or cut them into bandages — "they look ugly". The engine lets no scissors near a death robe and no
/// shopkeeper buys a newbied thing, so a bot carrying scissors cuts it into <see cref="RobeBandages"/> bandages — a rule of
/// this shard, not of the engine — and any other throws it away.
/// </para>
/// </summary>
public static class BotTidy
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTidy));

    public static bool Running { get; set; } = true;

    public static int LookEveryMs { get; set; } = 5000;

    public static int RobeBandages { get; set; } = 4;

    public static long Identified { get; private set; }

    public static long Unsure { get; private set; }

    public static long RobesCut { get; private set; }

    public static long RobesThrown { get; private set; }

    public static void Keep(BotMobile bot)
    {
        if (!Running || bot is not { Deleted: false, Alive: true })
        {
            return;
        }

        Shed(bot);
        Identify(bot);
    }

    private static void Shed(BotMobile bot)
    {
        if (bot.FindItemOnLayer(Layer.OuterTorso) is not DeathRobe { Deleted: false } robe)
        {
            return;
        }

        var pack = bot.Backpack;

        if (RobeBandages > 0 && pack?.FindItemByType<Scissors>() != null)
        {
            robe.Delete();

            var bandages = new Bandage(RobeBandages);

            if (!pack.TryDropItem(bot, bandages, false))
            {
                bandages.Delete();
            }

            RobesCut++;

            logger.Information("{Name} cut its death robe into {Bandages} bandages", bot.Name, RobeBandages);

            return;
        }

        robe.Delete();
        RobesThrown++;

        logger.Information("{Name} took off its death robe and threw it away", bot.Name);
    }

    private static void Identify(BotMobile bot)
    {
        var now = Core.TickCount;

        if (!bot.Appraised)
        {
            bot.Appraised = true;
            bot.AppraisedTick = now;

            return;
        }

        if (now - bot.AppraisedTick < LookEveryMs)
        {
            return;
        }

        bot.AppraisedTick = now;

        var worn = bot.Items;

        for (var i = 0; i < worn.Count; i++)
        {
            if (Unidentified(worn[i]))
            {
                Appraise(bot, worn[i], "that it wears");

                return;
            }
        }

        var pack = bot.Backpack;
        var thing = pack == null ? null : Search(pack, 0);

        if (thing != null)
        {
            Appraise(bot, thing, "in its pack");
        }
    }

    public static bool Appraise(Mobile bot, Item thing, string where)
    {
        if (!Running || bot is not { Deleted: false, Alive: true } || !Unidentified(thing))
        {
            return false;
        }

        if (!bot.CheckTargetSkill(SkillName.ItemID, thing, 0, 100))
        {
            Unsure++;

            return false;
        }

        ((IIdentifiable)thing).Identified = true;
        Identified++;

        logger.Information("{Name} identified {Thing} {Where}", bot.Name, Tell(thing), where);

        return true;
    }

    private static bool Unidentified(Item thing) =>
        thing switch
        {
            BaseWeapon { Deleted: false, Identified: false } weapon => Magic(weapon),
            BaseArmor { Deleted: false, Identified: false } armor => Magic(armor),
            _ => false
        };

    private static Item Search(Container container, int depth)
    {
        var items = container.Items;

        for (var i = 0; i < items.Count; i++)
        {
            if (Unidentified(items[i]))
            {
                return items[i];
            }

            switch (items[i])
            {
                case Container { Deleted: false } inner when depth < 2:
                    {
                        var found = Search(inner, depth + 1);

                        if (found != null)
                        {
                            return found;
                        }

                        break;
                    }
            }
        }

        return null;
    }

    private static bool Magic(BaseWeapon weapon) =>
        weapon.DamageLevel > WeaponDamageLevel.Regular
        || weapon.AccuracyLevel > WeaponAccuracyLevel.Regular
        || weapon.DurabilityLevel > WeaponDurabilityLevel.Regular
        || weapon.Slayer != SlayerName.None;

    private static bool Magic(BaseArmor armor) =>
        armor.ProtectionLevel > ArmorProtectionLevel.Regular || armor.Durability > ArmorDurabilityLevel.Regular;

    private static string Tell(Item thing) =>
        thing switch
        {
            BaseWeapon w => $"a {w.GetType().Name} ({w.DamageLevel}, {w.AccuracyLevel}, {w.DurabilityLevel}{(w.Slayer != SlayerName.None ? $", {w.Slayer}" : "")})",
            BaseArmor a => $"a {a.GetType().Name} ({a.ProtectionLevel}, {a.Durability})",
            _ => thing.GetType().Name
        };

    public static string Describe() =>
        !Running
            ? "bots do not keep their kit"
            : $"{Identified} things identified with Item Identification (off corpses, in packs and worn; {Unsure} the check did not pass); death robes: {RobesCut} cut into bandages, {RobesThrown} thrown away";
}
