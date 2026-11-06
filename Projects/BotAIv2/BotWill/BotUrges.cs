using System;

namespace Server.BotAI.V2;

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
