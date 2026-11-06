using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

public sealed class BotResolve
{

    public BotUrges Urges { get; } = new();

    public BotLedger Ledger { get; } = new();

    public BotDeed Deed { get; internal set; }

    public BotStake Stake { get; internal set; }

    public BotDoing Sent { get; internal set; }

    public int Nearest { get; internal set; } = int.MaxValue;

    public int RouteStamp { get; internal set; } = -1;

    public int Trudged { get; internal set; }

    public int Started { get; internal set; } = -1;

    public Point3D BecalmedAt { get; internal set; }

    public int BecalmedOff { get; internal set; }

    public long BecalmedTick { get; internal set; }

    public bool Becalmed { get; internal set; }

    public long SinceTick { get; internal set; }

    public long ReviewedTick { get; internal set; }

    public long StirredTick { get; internal set; }

    public bool Due { get; internal set; } = true;

    public bool Aside { get; internal set; }

    public long AsideTick { get; internal set; }

    public bool Struck { get; internal set; }

    public long HurtTick { get; internal set; }

    public bool Menaced { get; internal set; }

    public long MenaceTick { get; internal set; }

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

    public BotPause Under { get; internal set; }

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

    public long Aside { get; init; }

    public double Skill { get; init; }

    public string For { get; init; }

    public bool Sheltered { get; init; }

    public bool Kept { get; init; }

    public bool Freed { get; set; }

    public long FreedAt { get; set; }
}
