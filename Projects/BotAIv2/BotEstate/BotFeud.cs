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

    private static readonly Dictionary<string, BotSquad> _companies = [];

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

        logger.Information(
            "{Name} has formed the war company of {Guild} against {Enemy} of {Theirs}{Why}",
            body.Name,
            ours.Name,
            enemy?.Name ?? "nobody",
            (enemy?.Guild as Guild)?.Name ?? "nobody",
            defending ? ", on our own ground" : ""
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
            + $"{Companies} war companies formed and {Rallied} members rallied to one, {Sorties} times one was sent out after an enemy nobody had seen; {BotRally.Describe()}";

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
        _companies.Clear();
        Sorties = 0;
        BotRally.Forget();
    }
}
