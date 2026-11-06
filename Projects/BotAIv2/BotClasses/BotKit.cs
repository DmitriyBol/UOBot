using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

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
