using System;

namespace Server.BotAI.V2;

/// <summary>
/// The two things a bot feels, and why there are only two.
///
/// <para>
/// <b>Wanting things is not what drives this population — getting better at something is.</b> The first
/// version made every motive a shortage, and shortages get filled: by the end of an hour thirty-eight of
/// fifty-one bots were patrolling with their drive stuck at 0.62. That was not a defect in the arithmetic.
/// They had run out of things to want. So the value of work here is the <em>rate</em> at which it produces
/// skill and money — see <see cref="BotYield"/> — which cannot be satisfied and put away, because it falls
/// to nothing on its own as the work stops teaching the bot anything. That is the mechanism, and this file
/// is only what is left over.
/// </para>
///
/// <para>
/// <b>Boredom is what is left over on the empty side.</b> It is the one thing that grows while nothing
/// happens, so it cannot be settled and forgotten, and it exists for the case the ledger cannot price: a bot
/// with nothing on offer at all. It does not compete with work — it changes how work is chosen, by making a
/// bored bot discount the thing it has been doing over and over and by shortening the margin it demands
/// before trying something else.
/// </para>
///
/// <para>
/// <b>Need is what is left over on the full side, and it is a fact rather than a feeling</b>: how short the
/// purse is of what this bot was about to try to do. Not a comfort line — the first version compared every
/// purse against a flat 250 while handing every bot 100 at birth, so the entire population read as short of
/// money from its first second, and a signal that is on for everybody always is not a signal.
/// </para>
/// </summary>
public sealed class BotUrges
{
    public static double BoredomPerMinute { get; set; } = 0.10;

    public static double ReliefPerHundred { get; set; } = 0.25;

    public static double Restless { get; set; } = 0.5;

    private long _stampTick;

    private bool _stamped;

    private long _barrenTick;

    public double Boredom { get; private set; }

    public double Need { get; private set; }

    public bool IsBarren { get; private set; }

    public double Since(long now)
    {
        if (!_stamped)
        {
            _stamped = true;
            _stampTick = now;

            return 0.0;
        }

        var minutes = (now - _stampTick) / 60000.0;

        _stampTick = now;

        return minutes > 0.0 ? minutes : 0.0;
    }

    public void Idle(double minutes) =>
        Boredom = Math.Clamp(Boredom + minutes * BoredomPerMinute, 0.0, 1.0);

    public void Held(double minutes)
    {
    }

    public void Paid(double worth)
    {
        if (worth <= 0.0)
        {
            return;
        }

        Boredom = Math.Clamp(Boredom - worth / 100.0 * ReliefPerHundred, 0.0, 1.0);
    }

    public void Weigh(int wealth, int outlay) =>
        Need = outlay <= 0 ? 0.0 : Math.Clamp(1.0 - (double)wealth / outlay, 0.0, 1.0);

    public void Barren(long now)
    {
        if (IsBarren)
        {
            return;
        }

        IsBarren = true;
        _barrenTick = now;
    }

    public void Fruitful() => IsBarren = false;

    public double BarrenMinutes(long now) =>
        IsBarren ? Math.Max(0.0, (now - _barrenTick) / 60000.0) : 0.0;

    public bool IsRestless => Boredom >= Restless;

    public override string ToString() => $"boredom {Boredom:F2}, need {Need:F2}";
}
