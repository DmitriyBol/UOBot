using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Who each guild at war has called its members onto, and how long the call stands.
///
/// <para>
/// <b>Written 10.09.2026 because a war meant nothing at all.</b> <c>BotRegard</c> could declare one, the
/// engine's own <c>Guild.IsWar</c> held it, and <c>BotRegard.AtWar</c> had not one caller anywhere in the
/// assembly — so five guilds could hate each other to the floor of the scale and go on trading. Patrick
/// watched a member of The Crown tell somebody "this is the land of The Crown, move along", get refused, and
/// walk away. His order: they are to call their guildmates and fight.
/// </para>
///
/// <para>
/// <b>A board rather than a squad, and that is the whole design decision.</b> The squad machinery is real
/// and complete, and every part of it is typed to a <c>BaseCreature</c> quarry — formation, stations, the
/// share-out of a corpse. Re-typing it for a bot would touch a dozen files to gain what one shared list of
/// "who we are all on" gains here: several members choosing the same enemy at the same time, which is what
/// calling your guildmates looks like from the outside. The squad's own work is unaffected and none of its
/// rules had to be loosened.
/// </para>
///
/// <para>
/// <b>Every call expires.</b> A quarrel that outlives the fight is a guild that hunts one bot for the rest
/// of the session; <see cref="CallMs"/> is short enough that the call has to be renewed by somebody actually
/// standing there, which is the only evidence that the fight is still happening.
/// </para>
/// </summary>
public static class BotFeud
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotFeud));

    public static bool Running { get; set; } = true;

    public static int CallMs { get; set; } = 40000;

    public static int Answer { get; set; } = 100;

    public static int Defend { get; set; } = 250;

    public static int ThreatMs { get; set; } = 60000;

    public static int WatchMs { get; set; } = 5000;

    public static long Called { get; private set; }

    public static long Answered { get; private set; }

    public static long Threatened { get; private set; }

    public static long Defended { get; private set; }

    private static readonly Dictionary<string, (BotMobile Whom, long When)> _calls = [];

    private static readonly Dictionary<string, (BotMobile Whom, long When)> _threats = [];

    private static long _watched;

    public static BotMobile Threat(Guild ours)
    {
        if (ours == null || !_threats.TryGetValue(ours.Name, out var held))
        {
            return null;
        }

        if (Core.TickCount - (held.When + ThreatMs) >= 0 || held.Whom is not { Deleted: false, Alive: true })
        {
            _threats.Remove(ours.Name);

            return null;
        }

        return held.Whom;
    }

    public static void Watch()
    {
        if (!Running || !BotRegard.Warring)
        {
            return;
        }

        var now = Core.TickCount;

        if (now - (_watched + WatchMs) < 0)
        {
            return;
        }

        _watched = now;

        foreach (var war in BotWar.Standing)
        {
            Look(war.A, war.B, now);
            Look(war.B, war.A, now);
        }

        if (_companies.Count == 0)
        {
            return;
        }

        List<string> spent = null;

        foreach (var (name, company) in _companies)
        {
            if (!Standing(company))
            {
                (spent ??= []).Add(name);

                continue;
            }

            if (BaseGuild.FindByName(name) is not Guild ours)
            {
                (spent ??= []).Add(name);

                continue;
            }

            if (_musters.TryGetValue(company, out var muster))
            {
                if (!muster.Out)
                {
                    if (Threat(ours) != null)
                    {
                        muster.Out = true;
                        SetOut++;
                    }
                    else if (!Ready(ours, company, muster, now))
                    {
                        continue;
                    }
                }
                else if (company.Count < LeastToAttack && Threat(ours) == null
                         && company.Leader?.Self is { Deleted: false } head0 && !head0.InRange(muster.Where, Assembly))
                {
                    company.Disengage("too few left in the field; back to the muster");
                    muster.Out = false;
                    muster.Since = now;
                    FellBack++;

                    logger.Information(
                        "The war company of {Guild} falls back to muster again at ({X}, {Y}) with {Count} left",
                        name,
                        muster.Where.X,
                        muster.Where.Y,
                        company.Count
                    );

                    continue;
                }
            }

            if (company.Stance == BotSquadStance.Fighting && company.Focus is { Deleted: false, Alive: true })
            {
                continue;
            }

            var next = Threat(ours) ?? On(ours);

            if (next == null && Sortie(ours) && company.Leader?.Self is { Deleted: false, Alive: true } head)
            {
                next = Foe(ours, head.Location, head.Map);

                if (next != null)
                {
                    Sorties++;
                }
            }

            if (next != null && next.Map == company.Map && Quarrel(company.Leader?.Self, next))
            {
                company.Engage(next, null);

                continue;
            }

            if (BotWar.Fighting(name) == 0)
            {
                (spent ??= []).Add(name);
            }
        }

        if (spent != null)
        {
            for (var i = 0; i < spent.Count; i++)
            {
                Disband(spent[i]);
            }
        }
    }

    private static void Look(string ours, string theirs, long now)
    {
        if (BaseGuild.FindByName(theirs) is not Guild enemy || enemy.Members == null)
        {
            return;
        }

        for (var i = 0; i < enemy.Members.Count; i++)
        {
            if (enemy.Members[i] is not BotMobile { Deleted: false, Alive: true } body || body.Map == null ||
                body.Map == Map.Internal)
            {
                continue;
            }

            var onOurs = BotLand.Holder(body.Map, body.Location) == ours;

            if (!onOurs)
            {
                var bid = BotClaim.On(body.Map, body.Location);

                onOurs = bid != null && bid.Guild == theirs && bid.From == ours;
            }

            if (!onOurs)
            {
                continue;
            }

            if (!_threats.ContainsKey(ours))
            {
                Threatened++;
                _unanswered = now == 0 ? 1 : now;

                if (BaseGuild.FindByName(ours) is Guild mine && mine.Members != null)
                {
                    for (var m = 0; m < mine.Members.Count; m++)
                    {
                        if (mine.Members[m] is IBotWilful { Resolve: { } resolve })
                        {
                            resolve.Due = true;
                        }
                    }
                }
            }

            _threats[ours] = (body, now);

            return;
        }
    }

    public static void Stood()
    {
        Defended++;

        if (_unanswered != 0)
        {
            ReactionMs += Core.TickCount - _unanswered;
            Reactions++;
            _unanswered = 0;
        }
    }

    public static long ReactionMs { get; private set; }

    public static long Reactions { get; private set; }

    private static long _unanswered;

    public static int CompanySize { get; set; } = 15;

    public static long Companies { get; private set; }

    public static long Rallied { get; private set; }

    public static long Beyond { get; private set; }

    public static long Pocketed { get; private set; }

    private static readonly Dictionary<string, BotSquad> _companies = [];

    public static int MusterMs { get; set; } = 90000;

    public static int Assembly { get; set; } = 12;

    public static int LeastToAttack { get; set; } = 3;

    public static int Straggle { get; set; } = 40;

    public static int Wave { get; set; } = 3;

    public static int WaveMs { get; set; } = 60000;

    public static long Mustered { get; private set; }

    public static long SetOut { get; private set; }

    public static long SetOutWhole { get; private set; }

    public static long FellBack { get; private set; }

    public static long Waves { get; private set; }

    /// <summary>Where a war company forms up, since when, whether it has set out, and who is waiting there for the next wave.</summary>
    private sealed class Muster
    {
        private Point3D _where;

        public Point3D Where
        {
            get => Guild != null && BotEstate.Held.TryGetValue(Guild, out var hall) && hall is { Deleted: false } ? hall.BanLocation : _where;
            set => _where = value;
        }

        public long Since;

        public bool Out;

        public string Guild;

        public readonly Dictionary<Serial, (long Arrived, long Seen)> Waiting = [];

        public long Released;

        public bool EverReleased;
    }

    private static readonly Dictionary<BotSquad, Muster> _musters = [];

    public static bool Mustering(BotSquad company) => company != null && _musters.TryGetValue(company, out var muster) && !muster.Out;

    public static bool Out(BotSquad company) => company == null || !_musters.TryGetValue(company, out var muster) || muster.Out;

    public static Point3D MusterAt(BotSquad company) =>
        company != null && _musters.TryGetValue(company, out var muster) ? muster.Where : Point3D.Zero;

    public static int Gathered(BotSquad company)
    {
        if (company == null || !_musters.TryGetValue(company, out var muster))
        {
            return 0;
        }

        var here = 0;
        var members = company.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i]?.Self is { Deleted: false, Alive: true } body && body.Map == company.Map && body.InRange(muster.Where, Assembly))
            {
                here++;
            }
        }

        return here;
    }

    public static int Needed(Guild ours, BotSquad company) =>
        Math.Clamp(BotWar.Wanted(ours?.Name), LeastToAttack, Math.Max(LeastToAttack, company?.Ceiling ?? CompanySize));

    public static int Waiting(BotSquad company) =>
        company != null && _musters.TryGetValue(company, out var muster) ? muster.Waiting.Count : 0;

    public static bool WaveGoes(BotSquad company, Mobile body)
    {
        if (company == null || body == null || !_musters.TryGetValue(company, out var muster))
        {
            return true;
        }

        var now = Core.TickCount;
        var waiting = muster.Waiting;

        if (waiting.TryGetValue(body.Serial, out var mine))
        {
            mine = (mine.Arrived, now);
        }
        else
        {
            mine = (now, now);
        }

        waiting[body.Serial] = mine;

        if (muster.EverReleased && muster.Released - mine.Arrived >= 0)
        {
            waiting.Remove(body.Serial);

            return true;
        }

        List<Serial> stale = null;
        var oldest = now;

        foreach (var (serial, seen) in waiting)
        {
            if (now - seen.Seen > 5000)
            {
                (stale ??= []).Add(serial);

                continue;
            }

            if (seen.Arrived - oldest < 0)
            {
                oldest = seen.Arrived;
            }
        }

        for (var i = 0; stale != null && i < stale.Count; i++)
        {
            waiting.Remove(stale[i]);
        }

        if (waiting.Count < Wave && now - oldest < WaveMs)
        {
            return false;
        }

        muster.Released = now;
        muster.EverReleased = true;
        Waves++;

        logger.Information(
            "A wave of {Count} of {Guild}'s war company leaves the muster at ({X}, {Y}) for the company",
            waiting.Count,
            muster.Guild,
            muster.Where.X,
            muster.Where.Y
        );

        waiting.Remove(body.Serial);

        return true;
    }

    public static Point3D MusterPoint(Guild ours, Mobile founder)
    {
        if (BotEstate.Hall(ours) is { Deleted: false } hall)
        {
            return hall.BanLocation;
        }

        var seat = BotSeat.Of(ours);

        return seat != Point3D.Zero ? seat : founder.Location;
    }

    private static bool Ready(Guild ours, BotSquad company, Muster muster, long now)
    {
        var here = Gathered(company);
        var needed = Needed(ours, company);
        var waited = now - muster.Since;

        if (here < needed && (waited < MusterMs || here < LeastToAttack))
        {
            return false;
        }

        muster.Out = true;
        SetOut++;

        if (here >= needed)
        {
            SetOutWhole++;
        }

        logger.Information(
            "The war company of {Guild} sets out from ({X}, {Y}) with {Here} gathered of {Count} in it ({Needed} wanted) after {Waited:F0}s of mustering",
            ours.Name,
            muster.Where.X,
            muster.Where.Y,
            here,
            company.Count,
            needed,
            waited / 1000.0
        );

        return true;
    }

    public static int SortieMs { get; set; } = 180000;

    public static long Sorties { get; private set; }

    public static bool Sortie(Guild ours)
    {
        if (ours == null || SortieMs <= 0)
        {
            return false;
        }

        var now = Core.TickCount;

        foreach (var war in BotWar.Standing)
        {
            if (war.Against(ours.Name) != null && now - (war.Began + SortieMs) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static BotMobile Foe(Guild ours, Point3D from, Map map)
    {
        if (ours == null || map == null || map == Map.Internal)
        {
            return null;
        }

        BotMobile nearest = null;
        var best = int.MaxValue;

        foreach (var war in BotWar.Standing)
        {
            var theirs = war.Against(ours.Name);

            var enemy = theirs == null ? null : BotGuilds.Named(theirs) ?? BaseGuild.FindByName(theirs) as Guild;

            if (enemy?.Members == null)
            {
                continue;
            }

            for (var i = 0; i < enemy.Members.Count; i++)
            {
                if (enemy.Members[i] is not BotMobile { Deleted: false, Alive: true } foe || foe.Map != map)
                {
                    continue;
                }

                if (BotDelveParty.Delving(foe))
                {
                    continue;
                }

                var gap = Math.Max(Math.Abs(foe.X - from.X), Math.Abs(foe.Y - from.Y));

                if (gap < best)
                {
                    best = gap;
                    nearest = foe;
                }
            }
        }

        return nearest;
    }

    public static void Sortied() => Sorties++;

    public static bool Standing(BotSquad company)
    {
        if (company == null || company.Count == 0)
        {
            return false;
        }

        var all = BotSquads.All;

        for (var i = 0; i < all.Count; i++)
        {
            if (ReferenceEquals(all[i], company))
            {
                return true;
            }
        }

        return false;
    }

    public static BotSquad Company(Guild ours)
    {
        if (ours == null || !_companies.TryGetValue(ours.Name, out var company))
        {
            return null;
        }

        if (!Standing(company))
        {
            _companies.Remove(ours.Name);
            _musters.Remove(company);

            return null;
        }

        return company;
    }

    public static BotDeed Rally(IBotWilful bot, Guild ours, BotMobile enemy, bool defending)
    {
        if (bot?.Self is not BotMobile body || bot is not IBotSquadMember member || ours == null || enemy == null)
        {
            return null;
        }

        var company = Company(ours);

        if (member.Squad != null && !ReferenceEquals(member.Squad, company))
        {
            return null;
        }

        if (company != null && company.Count >= company.Ceiling && !ReferenceEquals(member.Squad, company))
        {
            return null;
        }

        if (company != null && !ReferenceEquals(member.Squad, company) && !BotSquads.Reaches(company, body) && !_musters.ContainsKey(company))
        {
            Beyond++;

            return null;
        }

        if (company != null && !ReferenceEquals(member.Squad, company) && body.Map is { } map
            && BotReach.Ask(map, body.Location, company.Anchor, BotArrival.Within(BotSquad.PressReach - 1)) == BotReachVerdict.Sealed)
        {
            Pocketed++;

            return null;
        }

        Rallied++;

        return new BotRally(ours, enemy, body.Map, defending);
    }

    public static BotSquad Form(IBotSquadMember member, Guild ours, BotMobile enemy, bool defending)
    {
        if (member?.Self is not BotMobile body || ours == null)
        {
            return null;
        }

        var company = Company(ours);

        if (company != null)
        {
            return company;
        }

        if (member.Squad != null)
        {
            return null;
        }

        company = BotSquads.Form(member);

        if (company == null)
        {
            return null;
        }

        company.Charged = true;
        company.Warring = true;
        company.Ceiling = CompanySize;
        _companies[ours.Name] = company;
        Companies++;

        var muster = new Muster { Where = MusterPoint(ours, body), Since = Core.TickCount, Out = defending, Guild = ours.Name };
        _musters[company] = muster;

        if (!defending)
        {
            Mustered++;
        }

        logger.Information(
            "{Name} has formed the war company of {Guild} against {Enemy} of {Theirs}{Why}",
            body.Name,
            ours.Name,
            enemy?.Name ?? "nobody",
            (enemy?.Guild as Guild)?.Name ?? "nobody",
            defending ? ", on our own ground" : $", mustering it at ({muster.Where.X}, {muster.Where.Y})"
        );

        return company;
    }

    public static void Disband(string guild)
    {
        if (guild == null || !_companies.TryGetValue(guild, out var company))
        {
            return;
        }

        _companies.Remove(guild);
        _musters.Remove(company);

        if (Standing(company))
        {
            company.Warring = false;
            company.Charged = false;
        }
    }

    public static void Call(Guild ours, BotMobile whom)
    {
        if (ours == null || whom is not { Deleted: false })
        {
            return;
        }

        var now = Core.TickCount;

        if (_calls.TryGetValue(ours.Name, out var held) && held.Whom == whom && now - (held.When + CallMs) < 0)
        {
            _calls[ours.Name] = (whom, now);

            return;
        }

        _calls[ours.Name] = (whom, now);
        Called++;
    }

    public static BotMobile On(Guild ours)
    {
        if (ours == null || !_calls.TryGetValue(ours.Name, out var held))
        {
            return null;
        }

        if (Core.TickCount - (held.When + CallMs) >= 0 || held.Whom is not { Deleted: false, Alive: true })
        {
            _calls.Remove(ours.Name);

            return null;
        }

        return held.Whom;
    }

    public static void Came() => Answered++;

    public static void Drop(Guild ours)
    {
        if (ours != null)
        {
            _calls.Remove(ours.Name);
        }
    }

    public static bool Quarrel(Mobile ours, Mobile theirs)
    {
        if (!Running || !BotRegard.Warring || ours == null || theirs == null || ours == theirs)
        {
            return false;
        }

        if (ours.Deleted || theirs.Deleted || !ours.Alive || !theirs.Alive || ours.Map != theirs.Map)
        {
            return false;
        }

        if (!BotRegard.AtWar(ours, theirs))
        {
            return false;
        }

        if (ours.Region?.IsPartOf<Server.Regions.GuardedRegion>() == true)
        {
            return false;
        }

        return theirs.Region?.IsPartOf<Server.Regions.GuardedRegion>() != true && ours.CanBeHarmful(theirs, false);
    }

    public static string Describe() =>
        Called + Threatened == 0
            ? "no guild has called anybody onto anybody"
            : $"{Called} calls raised against an enemy at war, {Answered} members came to one somebody else had raised, "
            + $"{Threatened} times an enemy was seen on a guild's own ground and {Defended} members went to it from up to {Defend} tiles, the first setting out {(Reactions > 0 ? ReactionMs / Reactions / 1000.0 : 0.0):F1}s after the sighting on average over {Reactions}; "
            + $"{Companies} war companies formed and {Rallied} members rallied to one, {Beyond} not called to one whose leader stood more than {BotSquads.JoinReach} tiles off, {Pocketed} not called to one fighting in a pocket proved closed from where they stood, {Sorties} times one was sent out after an enemy nobody had seen; "
            + $"{Mustered} attacking companies mustered before the fight (up to {MusterMs / 1000}s, setting out with what the score asks or at least {LeastToAttack}), {SetOut} set out ({SetOutWhole} whole), {FellBack} fell back to muster again with fewer than {LeastToAttack} in the field, {Waves} waves of stragglers sent from the muster ({Wave} or {WaveMs / 1000}s); {BotRally.Describe()}";

    public static void Forget()
    {
        _calls.Clear();
        _threats.Clear();
        _watched = 0;
        Called = 0;
        Answered = 0;
        Threatened = 0;
        Defended = 0;
        ReactionMs = 0;
        Reactions = 0;
        _unanswered = 0;
        Companies = 0;
        Rallied = 0;
        Beyond = 0;
        Pocketed = 0;
        _companies.Clear();
        _musters.Clear();
        Mustered = 0;
        SetOut = 0;
        SetOutWhole = 0;
        FellBack = 0;
        Waves = 0;
        Sorties = 0;
        BotRally.Forget();
    }
}
