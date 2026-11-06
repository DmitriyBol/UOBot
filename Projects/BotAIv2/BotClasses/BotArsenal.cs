using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

public static class BotArsenal
{

    public const int SpellHeal = 3;

    public const int SpellMagicArrow = 4;

    public const int SpellWeaken = 7;

    public const int SpellCure = 10;

    public const int SpellGreaterHeal = 28;

    public const int SpellHarm = 11;

    public const int SpellProtection = 14;

    public const int SpellFireball = 17;

    public const int SpellArchCure = 24;

    public const int SpellArchProtection = 25;

    public const int SpellLightning = 29;

    public const int SpellEnergyBolt = 41;

    public const int SpellResurrection = 58;

    public const int StartingAmmunition = 150;

    public static Type Potion(BotPotionKind kind) =>
        kind switch
        {
            BotPotionKind.Heal => typeof(LesserHealPotion),
            BotPotionKind.Cure => typeof(LesserCurePotion),
            _ => null
        };

    public static IReadOnlyList<BotPotionKind> Draughts { get; } =
    [
        BotPotionKind.Heal,
        BotPotionKind.Cure
    ];

    public static IReadOnlyList<BotWeaponOption> Melee(double target) =>
    [
        new(typeof(Katana), SkillName.Swords, target),
        new(typeof(Broadsword), SkillName.Swords, target),
        new(typeof(VikingSword), SkillName.Swords, target),
        new(typeof(WarMace), SkillName.Macing, target),
        new(typeof(WarFork), SkillName.Fencing, target),
        new(typeof(Kryss), SkillName.Fencing, target)
    ];

    public static IReadOnlyList<BotWeaponOption> MeleeWithStaff(double target) =>
    [
        new(typeof(Katana), SkillName.Swords, target),
        new(typeof(Broadsword), SkillName.Swords, target),
        new(typeof(VikingSword), SkillName.Swords, target),
        new(typeof(WarMace), SkillName.Macing, target),
        new(typeof(WarFork), SkillName.Fencing, target),
        new(typeof(QuarterStaff), SkillName.Macing, target)
    ];

    public static IReadOnlyList<BotWeaponOption> Bow(double target) =>
    [
        new(typeof(Bow), SkillName.Archery, target, typeof(Arrow), StartingAmmunition)
    ];

    public static IReadOnlyList<BotWeaponOption> BowOrCrossbow(double target) =>
    [
        new(typeof(Bow), SkillName.Archery, target, typeof(Arrow), StartingAmmunition),
        new(typeof(Crossbow), SkillName.Archery, target, typeof(Bolt), StartingAmmunition)
    ];

    public static BotWeaponOption Sidearm(double target) => new(typeof(Dagger), SkillName.Fencing, target);
}
