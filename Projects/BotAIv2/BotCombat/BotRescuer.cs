using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

public sealed class BotRescuer : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRescuer));

    public static int Reach { get; set; } = 60;

    public static double FitAt { get; set; } = 0.55;

    private static bool _said;

    public static long Outmatched { get; private set; }

    public static long PassedCrowded { get; private set; }

    public string Name => "Rescuer";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * FitAt)
        {
            return null;
        }

        var (friend, foe) = BotCry.Nearest(body, Reach);

        if (friend == null || foe == null)
        {
            return null;
        }

        if (BotDungeon.Under(foe.Location))
        {
            BotDefender.Below++;

            return null;
        }

        if (BotThreat.Decide(body, BotMobile.NoticeRange) == BotStand.Outmatched)
        {
            return null;
        }

        if (BotQuarry.Crowded(foe) && !BotCry.IsDire(friend))
        {
            PassedCrowded++;

            return null;
        }

        if (BotQuarry.Shunned(foe))
        {
            return null;
        }

        if (BotThreat.OverrunThere(body, friend, foe, BotMobile.NoticeRange))
        {
            BotQuarry.Crowd(foe);
            Outmatched++;

            return null;
        }

        Once(body, friend);
        BotCry.Noted();
        BotCry.Answering(friend);

        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotRescue(new BotSlay(foe, trains), friend, foe, own: false, rescuer: body);
    }

    private static void Once(Mobile body, Mobile friend)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information("{Name} is the first to go to somebody's aid: {Friend} called", body.Name, friend.Name);
    }

    public static void Forget()
    {
        _said = false;
        Outmatched = 0;
        PassedCrowded = 0;
        BotDefender.Forget();
    }
}

public sealed class BotDefender : IBotProposer
{

    public static int Reach { get; set; } = 12;

    public BotDefender(bool last = false) => _last = last;

    private readonly bool _last;

    public string Name => _last ? "LastStand" : "Defender";

    public BotStanding Rung => _last ? BotStanding.Failing : BotStanding.Hunted;

    public static bool Stands { get; set; } = true;

    public static int StandReach { get; set; } = 2;

    public static long StoodOn { get; set; }

    private static BotDeed Stand(IBotWilful bot, Mobile body, BaseCreature foe, bool last)
    {
        if (!Stands || body.HitsMax <= 0 || foe == null)
        {
            return null;
        }

        var deed = bot.Resolve?.Deed;

        if (bot.Squad != null || deed is BotBolt || deed is { Committed: true } || ReferenceEquals(deed?.Foe, foe))
        {
            return null;
        }

        var onTop = body.InRange(foe.Location, StandReach);

        if (last && !onTop && foe.AI is not (AIType.AI_Mage or AIType.AI_Archer))
        {
            return null;
        }

        if (BotFugitive.Calm(body, bot.Resolve))
        {
            return null;
        }

        if (!onTop && BotQuarry.Shunned(foe))
        {
            StoodShunned++;

            return null;
        }

        if (BotHaven.Guarded(body.Map, body.Location))
        {
            StoodTown++;

            return null;
        }

        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotRescue(new BotSlay(foe, trains) { ToTheEnd = true }, body, foe, own: true);
    }

    public static long StoodTaken { get; private set; }

    public static long StoodCaster { get; private set; }

    public static long StoodHealthy { get; private set; }

    public static long StoodDied { get; set; }

    public static long StoodTown { get; private set; }

    public static long StoodShunned { get; private set; }

    public static long StoodOnOdds { get; set; }

    public static void Stood(Mobile body, Mobile foe)
    {
        StoodTaken++;

        if (BotSquad.Caster(foe))
        {
            StoodCaster++;
        }

        if (body is { HitsMax: > 0 } && body.Hits >= body.HitsMax * BotTraits.FleeAt(body, false))
        {
            StoodHealthy++;
        }
    }

    public static string DescribeStands() =>
        $"{StoodOn} fights carried on under {BotSlay.FleeAt:P0} of the bot's health by a bot with what was killing it on top of it and no flight on offer, "
        + $"{UnderOwnShare} looks at a bot struck between {BotSlay.FleeAt:P0} and its own flight share by its nature answered as under the floor rather than with a fight that gives itself up; "
        + $"{StoodTaken} stands taken for want of a flight by a bot refused the fight for the odds ({StoodCaster} against a caster, {StoodHealthy} above the bot's own flight share, {StoodOnOdds} that the odds would have ended on the fight's first beat, {StoodDied} died in them), {StoodTown} not taken on guarded ground (the guards called instead), {StoodShunned} not taken for a thing no road reaches that was not on top of the bot{(Stands ? "" : " (off)")}";

    public static long UnderOwnShare { get; private set; }

    public static long Already { get; private set; }

    public static long Outnumbered { get; private set; }

    public static long Leading { get; private set; }

    public static long Assailed { get; private set; }

    public static long Below { get; set; }

    public static long ToTown { get; private set; }

    public static void Forget()
    {
        UnderOwnShare = 0;
        Already = 0;
        Outnumbered = 0;
        Leading = 0;
        Assailed = 0;
        Below = 0;
        ToTown = 0;
        StoodOn = 0;
        StoodTaken = 0;
        StoodCaster = 0;
        StoodHealthy = 0;
        StoodDied = 0;
        StoodTown = 0;
        StoodShunned = 0;
        StoodOnOdds = 0;
    }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (_last)
        {
            if (body.HitsMax <= 0 || body.Hits >= body.HitsMax * BotTraits.FleeAt(body, false) || BotThreat.Hunter(body, Reach) is not { } on)
            {
                return null;
            }

            if (bot is IBotSquadMember { Squad: { } own } one && (ReferenceEquals(own.Leader, one) || BotDungeon.Under(body.Location)))
            {
                return null;
            }

            return Stand(bot, body, on, last: true);
        }

        var assailant = BotOutlaw.Assailant(body, Reach);

        if (assailant != null && bot.Resolve?.Deed is not BotBrawl)
        {
            if (body.HitsMax > 0 && body.Hits < body.HitsMax * BotSlay.FleeAt)
            {
                return null;
            }

            Assailed++;

            var against = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

            return new BotBrawl(
                assailant,
                BotBrawl.Defence,
                against,
                (b, w) => assailant is not { Deleted: false, Alive: true }
                    ? $"{assailant?.Name} is down"
                    : !BotOutlaw.Assailing(assailant, b?.Self) && w.Elapsed > 15000
                        ? $"{assailant.Name} broke off"
                        : null
            );
        }

        var foe = BotThreat.Hunter(body, Reach);

        if (foe == null)
        {
            return null;
        }

        if (bot is IBotSquadMember { Squad: { } company } member && ReferenceEquals(company.Leader, member))
        {
            Leading++;

            return null;
        }

        if (bot is IBotSquadMember { Squad: not null } && BotDungeon.Under(body.Location))
        {
            Below++;

            return null;
        }

        var ownShare = BotTraits.FleeAt(body, false);

        if (body.HitsMax > 0 && body.Hits >= body.HitsMax * BotSlay.FleeAt && body.Hits < body.HitsMax * ownShare)
        {
            UnderOwnShare++;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * ownShare)
        {
            BotCry.Raise(body, foe, dire: true);

            return Stand(bot, body, foe, last: false);
        }

        if (BotQuarry.Crowded(foe) || BotQuarry.Shunned(foe))
        {
            BotCry.Raise(body, foe, dire: true);

            return null;
        }

        if (BotThreat.Decide(body, BotMobile.NoticeRange) == BotStand.Outmatched)
        {
            BotQuarry.Crowd(foe);
            Outnumbered++;
            BotCry.Raise(body, foe, dire: true);

            if (BotFugitive.ToTown(bot, body, foe) is { } run)
            {
                ToTown++;

                return run;
            }

            return Stand(bot, body, foe, last: false);
        }

        BotCry.Raise(body, foe);

        if (ReferenceEquals(bot.Resolve?.Deed?.Foe, foe))
        {
            Already++;

            return null;
        }

        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotRescue(new BotSlay(foe, trains), body, foe, own: true);
    }
}
