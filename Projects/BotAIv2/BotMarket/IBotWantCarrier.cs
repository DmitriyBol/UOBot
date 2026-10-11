using System;

namespace Server.BotAI.V2;

/// <summary>
/// Work that has reserved units on a spoken want and is bringing them: the carrier's walk (<see cref="BotDeliver"/>) and the
/// trader's haul (<c>BotHaul</c>). The market's sweep (<see cref="BotWant.Sweep"/>) keeps a reservation only while the
/// courier's work in hand, or the work it has put down, is one of these and still carries that want.
///
/// <para>
/// <b>01.10.2026, build 385 (traders).</b> The sweep asked for a <see cref="BotDeliver"/> by type, so any other work that
/// reserved units had its reservation swept on the next beat and the want stood open to a second carrier for the same
/// goods. One interface instead of a second type test, so the market's core names no trade outside itself.
/// </para>
/// </summary>
public interface IBotWantCarrier
{
    bool Carries(BotWant want);

    bool Stale { get; }

    Type Goods { get; }
}
