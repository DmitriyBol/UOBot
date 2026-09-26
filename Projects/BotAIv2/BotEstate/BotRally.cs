using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Falling in with the guild's war company and fighting with it, as a company, until the company is done.
///
/// <para>
/// <b>Patrick's order of 13.09.2026, the fifth of the evening: when one guild attacks another, a company
/// forms and there is a real battle.</b> Until this a war was a hundred duels: every member offered a
/// quarrel of its own, walked at its own enemy and fought alone, and the squad — the formation, the medics
/// behind it, the casters, the share-out — existed only against monsters. The squad's fighting code never
/// cared what it fought: it sets a combatant and asks the engine whether the blow is lawful, and in a war
/// it is. What it lacked was a way to be formed for a war.
/// </para>
///
/// <para>
/// <b>This errand is the way.</b> The first member to answer a call or a threat forms the guild's war
/// company and points it at the enemy (<c>BotFeud.Rally</c>); every member after it is handed this, walks
/// to the company and joins it; and from then on the company's own beat does the fighting — station,
/// formation, blow, bandage, spell — while this errand does nothing but stand in the ranks. It runs
/// <c>Alongside</c> the company like a hunt's enlistment does, and it ends when the company does.
/// </para>
///
/// <para>
/// <b>Not the enlistment errand, though it is nearly one.</b> <c>BotEnlist</c> refuses a company of one,
/// which is right for a hunt and wrong for the bot that just formed the company: the founder would be
/// refused its own company in the same second. This one holds as long as the company holds.
/// </para>
/// </summary>
public sealed class BotRally : BotDeed
{
    public const string Trade = "rally";

    public static double Prior { get; set; } = 400.0;

    public static double DefendPrior { get; set; } = 700.0;

    public static double WorkMinutes { get; set; } = 3.0;

    public static long Stood { get; private set; }

    public static long Late { get; private set; }

    public static long Marched { get; private set; }

    public static long Kept { get; private set; }

    public static int Drift { get; set; } = 8;

    private readonly Guild _guild;

    public Guild Guild => _guild;

    private readonly BotMobile _enemy;

    private readonly Map _map;

    private readonly bool _defending;

    private BotSquad _company;

    private bool _joined;

    private Point3D _march;

    private bool _waved;

    public BotRally(Guild guild, BotMobile enemy, Map map, bool defending)
    {
        _guild = guild;
        _enemy = enemy;
        _map = map;
        _defending = defending;
    }

    public override string Kind => Trade;

    public override bool Summons => true;

    public override Map Map => _map;

    public override Point3D Where => _company?.Anchor ?? _enemy?.Location ?? Point3D.Zero;

    public override double Expects => _defending ? DefendPrior : Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override bool Unpaid => true;

    public override bool Alongside => true;

    public override bool Still => _joined;

    public override bool Pressing(IBotWilful bot) => _defending;

    public override string Stage =>
        (_defending ? "defending our ground: " : "")
        + (_joined ? $"in the war company of {_guild?.Name}, {_company?.Count} of us" : $"rallying to the war company of {_guild?.Name}");

    public override bool Bend(IBotWilful bot) => false;

    public override void Drop(IBotWilful bot)
    {
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (bot is not IBotSquadMember member)
        {
            return BotDoing.Failed("not the sort of thing that joins companies");
        }

        if (_company == null)
        {
            _company = BotFeud.Company(_guild);

            if (_company == null)
            {
                if (_enemy is not { Deleted: false, Alive: true })
                {
                    return BotDoing.Done("the enemy was down before a company could be formed");
                }

                _company = BotFeud.Form(member, _guild, _enemy, _defending);

                if (_company == null)
                {
                    return member.Squad != null
                        ? BotDoing.Done("fell in with another company on the way")
                        : BotDoing.Failed("could not form the war company");
                }
            }

            if (BotFeud.Mustering(_company))
            {
            }
            else if (_company.Focus is { Deleted: false, Alive: true } held && !ReferenceEquals(held, _enemy))
            {
                Kept++;
            }
            else
            {
                _company.Engage(_enemy, ReferenceEquals(member.Squad, _company) ? member : null);
            }
        }

        if (!BotFeud.Standing(_company))
        {
            if (_joined)
            {
                Stood++;

                return BotDoing.Done("the war company is done");
            }

            Late++;

            return BotDoing.Failed("the war company was gone before it got there");
        }

        if (member.Squad != null)
        {
            if (!ReferenceEquals(member.Squad, _company))
            {
                return BotDoing.Done("fell in with another company on the way");
            }

            _joined = true;

            if (ReferenceEquals(_company.Leader, member) && BotFeud.Mustering(_company) && _company.Stance != BotSquadStance.Fighting)
            {
                var at = BotFeud.MusterAt(_company);

                if (at != Point3D.Zero && !body.InRange(at, 2))
                {
                    return BotDoing.Walk(_map, at, BotArrival.Within(2), $"to the muster at ({at.X}, {at.Y})");
                }

                return BotDoing.Work($"mustering the war company at ({at.X}, {at.Y}): {BotFeud.Gathered(_company)} of {BotFeud.Needed(_guild, _company)}");
            }

            if (ReferenceEquals(_company.Leader, member)
                && _company.Focus is { Deleted: false, Alive: true } focus
                && focus.Map == _map
                && !body.InRange(focus.Location, BotSquad.PressReach))
            {
                if (_march == Point3D.Zero || !Utility.InRange(_march, focus.Location, Drift))
                {
                    if (_march == Point3D.Zero)
                    {
                        Marched++;
                    }

                    _march = focus.Location;
                }

                return BotDoing.Walk(_map, _march, BotArrival.Within(BotSquad.PressReach - 1), $"at {focus.Name}");
            }

            _march = Point3D.Zero;

            return BotDoing.Work($"in the war company, {_company.Count} of us");
        }

        var anchor = _company.Anchor;

        if (!body.InRange(anchor, BotSquad.PressReach))
        {
            if (!_waved && BotFeud.Out(_company) && !body.InRange(anchor, BotFeud.Straggle))
            {
                var at = BotFeud.MusterAt(_company);

                if (at != Point3D.Zero)
                {
                    if (!body.InRange(at, BotFeud.Assembly))
                    {
                        return BotDoing.Walk(_map, at, BotArrival.Within(BotFeud.Assembly - 2), $"to the muster at ({at.X}, {at.Y}) for the next wave");
                    }

                    if (!BotFeud.WaveGoes(_company, body))
                    {
                        return BotDoing.Work($"waiting at the muster for the next wave: {BotFeud.Waiting(_company)} of {BotFeud.Wave}");
                    }

                    _waved = true;
                }
            }

            return BotDoing.Walk(_map, anchor, BotArrival.Within(BotSquad.PressReach - 1), "to the war company");
        }

        if (!BotSquads.Join(_company, member))
        {
            Late++;

            return BotDoing.Failed("the war company had no room by the time it arrived");
        }

        _joined = true;

        return BotDoing.Work($"fell in with the war company, {_company.Count} of us");
    }

    public static string Describe() =>
        Stood + Late + Marched + Kept == 0
            ? "nobody has rallied to a war company"
            : $"{Stood} rallies stood through to the company's end, {Late} arrived to find no company, {Marched} marches led at an enemy out of reach, {Kept} rallies left their company on the enemy it already had";

    public static void Forget()
    {
        Stood = 0;
        Late = 0;
        Marched = 0;
        Kept = 0;
    }
}
