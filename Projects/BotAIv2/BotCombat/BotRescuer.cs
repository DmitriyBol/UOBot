using Server.Logging;

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

    /// <summary>How far a bot will go to help. The cry has to carry at least this far to be heard.</summary>
    public static int Reach { get; set; } = 40;

    /// <summary>
    /// The share of health below which a bot is no use to anybody else.
    ///
    /// Lower than the hunter's own fitness bar. Turning up at three-quarters health to pull something off a
    /// friend is worth doing; turning up at a fifth is adding a second corpse.
    /// </summary>
    public static double FitAt { get; set; } = 0.55;

    private static bool _said;

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

        // The same question the hunter asks before setting out, for the same reason: arriving at a fight that
        // is already lost turns one death into two. Being outnumbered where you stand is a different fact
        // from what is standing round the friend, and this is the one that stops the whole population walking
        // into the same pile one at a time.
        if (BotThreat.Decide(body, BotMobile.NoticeRange) == BotStand.Outmatched)
        {
            return null;
        }

        // <b>And the question that one cannot answer, which is why the sentence above it was already
        // hedged.</b> Weighing the odds where the bot stands says nothing whatever about the pile the friend
        // is standing in — the comment above admits as much and then measures the near one anyway, because at
        // the moment of choosing, the far one is not knowable. It becomes knowable the instant somebody walks
        // over and finds out, and that is exactly what the fight writes down when it gives up on the numbers.
        //
        // Without reading it, every bot in earshot repeats the same discovery in turn and then repeats it
        // again two seconds later: one creature drew 103 refusals in a single window while the hunt, which
        // does read the note, refused it 7 times. The note lapses in two minutes, so a genuine emergency is
        // only ever deferred, never abandoned.
        if (BotQuarry.Crowded(foe))
        {
            return null;
        }

        // And the other list the fight writes: something no journey could reach is not helped by a second bot
        // walking at it from further away.
        if (BotQuarry.Shunned(foe))
        {
            return null;
        }

        Once(body, friend);
        BotCry.Noted();

        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotRescue(new BotSlay(foe, trains), friend, foe, own: false);
    }

    private static void Once(Mobile body, Mobile friend)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        // Said once, by name. "Nobody ever goes to help" and "help is offered and always outbid" look
        // identical in a log that says neither.
        logger.Information("{Name} is the first to go to somebody's aid: {Friend} called", body.Name, friend.Name);
    }

    public static void Forget() => _said = false;
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
    /// <summary>How far around itself the bot looks for whatever is on it.</summary>
    public static int Reach { get; set; } = 12;

    public string Name => "Defender";

    public BotStanding Rung => BotStanding.Hunted;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        var foe = BotThreat.Hunter(body, Reach);

        if (foe == null)
        {
            return null;
        }

        // <b>"Run when you cannot" was the stated rule and nothing anywhere enforced it.</b> The paragraph
        // above says this sits below Failing so that a badly hurt bot runs instead of turning to fight — but
        // that only decides which rung wins when both offer something, and this rung offered a fight at any
        // health at all. So a bot under the fighting floor was handed a fight, BotSlay looked at the same
        // health on its very next beat and abandoned it, and this proposed it again: Ilsa ran four complete
        // take-and-fail cycles inside two seconds, and the repetition penalty came off in hundredths while
        // the loop turned four times a second.
        //
        // Taken from BotSlay.FleeAt rather than written again, because the number that decides whether to
        // start a fight and the number that decides whether to stay in one are the same number. Written twice
        // they drift, and the gap between them is precisely where the loop lives.
        //
        // The cry is still raised below — being unable to fight back is the loudest reason to call for help.
        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotSlay.FleeAt)
        {
            BotCry.Raise(body, foe);

            return null;
        }

        // <b>The same note the rescuer reads, and leaving it out of here left half the loop standing.</b>
        // Teaching the rescuer to respect a crowded quarry silenced the bots who go to somebody else's aid
        // and did nothing whatever for the bot the crowd is actually standing on: this rung offers a healthy
        // bot the fight, the odds check throws it out on arrival, and this offers it again. Lysa turned that
        // circle thirty-seven times on one zombie inside eight minutes, several of them inside the same
        // second.
        //
        // Refusing is not standing idle. The cry goes up either way — a crowd is precisely what
        // <c>BotMuster</c> calls companies for, which is the whole reason a crowded thing is noted apart from
        // an unreachable one — and Failing outranks this rung the moment the bot is hurt enough to run. The
        // note lapses in two minutes, so this is a bot waiting for help rather than a bot giving up.
        if (BotQuarry.Crowded(foe) || BotQuarry.Shunned(foe))
        {
            BotCry.Raise(body, foe);

            return null;
        }

        // Say so first, whatever comes of the fight. A bot that can handle this alone costs the population
        // nothing by having said it was in trouble; a bot that cannot is the reason anybody is listening.
        BotCry.Raise(body, foe);

        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotRescue(new BotSlay(foe, trains), body, foe, own: true);
    }
}
