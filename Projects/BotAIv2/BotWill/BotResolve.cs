using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// Everything one bot has resolved, feels and has learned. Lives on the bot, like its journey and its bond.
///
/// <para>
/// <b>All of it in one object, and that is the point.</b> In the first version a bot's state was thirty-two
/// dictionaries keyed by serial in thirty-two files: each needed its own reset, each leaked when the
/// population was torn down, and the question "what is this bot up to" was answered by reading a file per
/// possible answer. A deleted bot takes this with it, and there is nowhere else to look.
/// </para>
///
/// <para>
/// Only <see cref="BotWill"/> writes to it. Everything here is readable by a gump, a command or a log line
/// without asking permission, which is the other half of the same lesson: the first version's decisions
/// were unobservable, so the question "why did the brain not take that plan" had no answer at all — and it
/// turned out to be the question that mattered, because it had not taken 85 of 135 of them.
/// </para>
/// </summary>
public sealed class BotResolve
{
    public BotUrges Urges { get; } = new();

    public BotLedger Ledger { get; } = new();

    public BotDeed Deed { get; internal set; }

    public BotStake Stake { get; internal set; }

    public BotDoing Sent { get; internal set; }

    public int Nearest { get; internal set; } = int.MaxValue;

    public int Trudged { get; internal set; }

    public int Started { get; internal set; } = -1;

    public long SinceTick { get; internal set; }

    public long ReviewedTick { get; internal set; }

    public long StirredTick { get; internal set; }

    public bool Due { get; internal set; } = true;

    public bool Aside { get; internal set; }

    public long AsideTick { get; internal set; }

    public bool Struck { get; internal set; }

    public long HurtTick { get; internal set; }

    public BotStanding Standing { get; internal set; }

    public List<string> Offered { get; } = [];

    public long OfferedTick { get; internal set; }

    public static int BruiseHalfLifeMs { get; set; } = 1800000;

    private double _blows;

    private long _blowTick;

    private bool _bruised;

    public void Bruise(long now)
    {
        _blows = Faded(now) + 1.0;
        _blowTick = now;
        _bruised = true;
    }

    public double Beaten(long now) =>
        !_bruised ? 0.0 : Faded(now) / (BruiseHalfLifeMs / 60000.0);

    private double Faded(long now)
    {
        var since = now - _blowTick;

        return since <= 0 || _blows <= 0.0 ? _blows : _blows * Math.Pow(0.5, since / (double)BruiseHalfLifeMs);
    }

    public BotStanding Took { get; internal set; } = BotStanding.Free;

    public string Because { get; internal set; }

    public string Empty { get; internal set; }

    public double TakenAt { get; internal set; }

    public double Expected { get; internal set; }

    public bool Bent { get; internal set; }

    public BotPause Paused { get; internal set; }

    internal string DroppedKind { get; set; }

    internal long DroppedTick { get; set; }

    private readonly string[] _recent = new string[6];

    private int _recentAt;

    internal void Remember(string line)
    {
        _recent[_recentAt] = line;
        _recentAt = (_recentAt + 1) % _recent.Length;
    }

    public IEnumerable<string> Recent()
    {
        for (var i = 1; i <= _recent.Length; i++)
        {
            var line = _recent[(_recentAt - i + _recent.Length) % _recent.Length];

            if (line != null)
            {
                yield return line;
            }
        }
    }

    public override string ToString() =>
        Deed == null
            ? $"nothing on, {Urges}"
            : $"{Deed}, {Urges}, because {Because}";
}

/// <summary>
/// Steadfast work put down for something that would not wait, and everything needed to take it up again as if
/// the interruption had not been counted against it.
///
/// <para>
/// <b>The takings are the part that has to be put right.</b> What a piece of work came to is measured as the
/// difference between the bot now and the bot when it began, so a rescue that earned forty gold in the middle
/// of a mining trip would otherwise be booked to the mine — and the minutes spent on it too, which would make
/// the trip look slow. <see cref="Wealth"/> and <see cref="Skill"/> are the bot at the moment it put the work
/// down; the stake is moved by whatever changed while it was away.
/// </para>
/// </summary>
public sealed class BotPause
{
    public BotDeed Deed { get; init; }

    public BotStanding Took { get; init; }

    public BotStake Stake { get; init; }

    public long Since { get; init; }

    public long At { get; init; }

    public string Because { get; init; }

    public double TakenAt { get; init; }

    public double Expected { get; init; }

    public int Wealth { get; init; }

    public double Skill { get; init; }

    public string For { get; init; }
}
