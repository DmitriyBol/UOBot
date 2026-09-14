using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// What the world hands a bot at birth, and on what terms.
///
/// Pure description: nothing here creates an item. The class says what a bot of its trade needs, and
/// a single granting step turns that into objects — which is the difference between this and the
/// first version, where the starting kit was a switch statement inside the bot itself and every
/// question about "what does a smith actually own" had to be answered by reading control flow.
///
/// <para>
/// <b>Everything granted is bound.</b> Bound means two things: the item weighs nothing, and death
/// does not take it. A bot's working tools are what let it start again after being killed, and a
/// hammer left in a corpse turns one bad fight into the end of a career. Weightlessness matters for a
/// reason the first version measured: three bots spent an entire session pinned in place because ore
/// plus tools crossed the engine's overload threshold, and past it every step costs stamina until
/// there is none and the step is refused outright.
/// </para>
///
/// <para>
/// <b>Stacks are bound differently from things.</b> A hammer is one object and the engine can keep it
/// through death by itself. A quiver cannot be handled that way: stacks merge, so a hundred bound
/// arrows and fifty bought ones become one stack of a hundred and fifty with a single loot flag, and
/// whichever flag wins is wrong. Ammunition is therefore bound by remembered <em>count</em>, and it
/// rides on the weapon that fires it — see <see cref="BotWeaponOption.AmmunitionCount"/>.
/// </para>
/// </summary>
public sealed class BotKit
{
    public IReadOnlyList<BotWeaponOption> Melee { get; init; } = [];

    public IReadOnlyList<BotWeaponOption> Ranged { get; init; } = [];

    public BotWeaponOption? Sidearm { get; init; }

    public bool Staff { get; init; }

    public IReadOnlyList<Type> Tools { get; init; } = [];

    public int Bandages { get; init; } = 20;

    public int Reagents { get; init; }

    public IReadOnlyList<Type> Armour { get; init; } = [];

    public IReadOnlyList<int> Spells { get; init; } = [];
}
