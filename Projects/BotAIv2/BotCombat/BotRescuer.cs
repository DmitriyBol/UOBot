using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a free bot the chance to go to somebody's aid.
///
/// <para>
/// <b>The half of "we are a company" that no amount of formation could supply.</b> A squad already carries
/// its own to trouble: the anchor moves onto whoever was hit and every station is derived from it, so the
/// rest are walking before anything is decided. That works beautifully and only for bots who are already in
/// the same squad — which, on a population that spends its day scattered across three hundred tiles at
/// separate trades, is almost nobody. Fourteen of fifteen bots could watch the fifteenth die four screens
/// away and none of them would ever be asked the question.
/// </para>
///
/// <para>
/// It is offered rather than ordered, on the <c>Free</c> rung like every other kind of work, and it is priced
/// so that it wins against digging and loses to nothing much. What makes it actually interrupt a trip to the
/// forge is <see cref="BotDeed.Pressing"/> — see <see cref="BotRescue"/>.
/// </para>
/// </summary>
public sealed class BotRescuer : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRescuer));

    public static int Reach { get; set; } = 60;

    public static double FitAt { get; set; } = 0.55;

    private static bool _said;

    public static long Outmatched { get; private set; }

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

        if (BotQuarry.Crowded(foe))
        {
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
        BotDefender.Forget();
    }
}

/// <summary>
/// Hitting back, for a bot that something is presently hitting.
///
/// <para>
/// <b>On the <c>Hunted</c> rung, which had nothing on it at all.</b> The rung exists to say that a bot has no
/// business shopping for work while something is chewing on it, and <c>BotWill</c> has been complaining into
/// the log that nobody proposes anything for it — so the bot simply held whatever it was doing. For a warrior
/// that hardly shows: the engine's own reflex swings back. For a healer it is the whole problem, because a
/// healer's work is to stand still over somebody, it has no combatant of its own, and it will do that until
/// it dies with a staff in its hands it never once raised.
/// </para>
///
/// <para>
/// It sits below <c>Failing</c> in the ladder, so a bot that is badly hurt still runs rather than turning to
/// fight. Fight back while you can afford to; run when you cannot.
/// </para>
/// </summary>
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

    private static BotDeed Stand(IBotWilful bot, Mobile body, BaseCreature foe)
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

        if (!body.InRange(foe.Location, StandReach) && foe.AI is not (AIType.AI_Mage or AIType.AI_Archer))
        {
            return null;
        }

        if (BotFugitive.Calm(body, bot.Resolve) || BotQuarry.Shunned(foe) || BotQuarry.Crowded(foe)
            || BotThreat.Decide(body, BotMobile.NoticeRange) == BotStand.Outmatched)
        {
            return null;
        }

        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotRescue(new BotSlay(foe, trains) { ToTheEnd = true }, body, foe, own: true);
    }

    public static string DescribeStands() =>
        $"{StoodOn} fights carried on under {BotSlay.FleeAt:P0} of the bot's health by a bot with what was killing it on top of it and no flight on offer, "
        + $"{UnderOwnShare} looks at a bot struck between {BotSlay.FleeAt:P0} and its own flight share by its nature answered as under the floor rather than with a fight that gives itself up";

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

            return Stand(bot, body, on);
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

            return Stand(bot, body, foe);
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

            return null;
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
