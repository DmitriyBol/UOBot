namespace Server.BotAI.V2;

/// <summary>
/// Potions grouped by what they do, which is the granularity a carrying limit needs.
///
/// The engine's own <c>PotionEffect</c> distinguishes tiers — lesser, regular and greater heal are
/// three values — and a limit expressed in those terms would let a bot carry three heal potions and
/// call it one of each. A bot that is out of bottles is out of bottles regardless of which grade it
/// drank, so the limit counts families.
///
/// <see cref="Mana"/> has no engine effect behind it: mana potions do not exist in this era and are
/// this project's own item, riding on <c>PotionEffect.Refresh</c>. It is listed here because a bot
/// still has to be told how many of them it may hold, and because the warrior-mage's allowance of two
/// is the only exception to the flat limit of one that is not about healing.
/// </summary>
public enum BotPotionKind
{
    Heal,

    Cure,

    Refresh,

    Mana,

    Strength,

    Agility,

    Poison,

    Explosion
}
