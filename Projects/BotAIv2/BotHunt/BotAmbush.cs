using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotAmbush
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotAmbush));

    public static bool Running { get; set; } = true;

    public static double Times { get; set; } = 3.0;

    public static int MeleeStunMs { get; set; } = 5000;

    public static int ArrowStunMs { get; set; } = 5000;

    public static int BackstabMs { get; set; } = 6000;

    public static long Backstabs { get; private set; }

    public static int BareBlow { get; set; } = 5;

    public static long Struck { get; private set; }

    public static long ByArrow { get; private set; }

    public static long Felled { get; private set; }

    public static long Dealt { get; private set; }

    public static long Missed { get; private set; }

    public static int Strike(Mobile robber, Mobile victim, bool backstab = false)
    {
        if (!Running)
        {
            return 0;
        }

        if (robber is not { Deleted: false, Alive: true } || victim is not { Deleted: false, Alive: true }
            || robber.Map == null || robber.Map != victim.Map)
        {
            Missed++;

            return 0;
        }

        var weapon = robber.Weapon as BaseWeapon;
        var arrow = weapon is BaseRanged;
        var swing = weapon?.ComputeDamage(robber, victim) ?? BareBlow;
        var damage = (int)Math.Round(Math.Max(1, swing) * Times);

        robber.RevealingAction();
        robber.DoHarmful(victim);

        var before = victim.Hits;

        victim.Damage(damage, robber);

        var stun = backstab ? BackstabMs : arrow ? ArrowStunMs : MeleeStunMs;

        if (stun > 0 && victim.Alive)
        {
            victim.Paralyze(TimeSpan.FromMilliseconds(stun));
        }

        Struck++;
        Dealt += Math.Max(0, before - victim.Hits);

        if (backstab)
        {
            Backstabs++;
        }

        if (arrow)
        {
            ByArrow++;
        }

        if (!victim.Alive)
        {
            Felled++;
        }

        logger.Information(
            "{Name} {How} {Victim} for {Damage} with {Weapon}, {Left} hit points left of {Before}, held {Stun}s",
            robber.Name,
            backstab ? "backstabbed" : "struck out of hiding",
            victim.Name,
            damage,
            weapon?.GetType().Name ?? "bare hands",
            Math.Max(0, victim.Hits),
            before,
            stun / 1000.0
        );

        return victim.Alive ? stun : 0;
    }

    public static string Describe() =>
        $"{Struck} blows struck out of hiding ({ByArrow} of them with a bow, {Backstabs} of them backstabs), {Dealt} hit points taken, {Felled} victims left dead by the blow alone, {Missed} that found nothing to strike";

    public static void Forget()
    {
        Struck = 0;
        ByArrow = 0;
        Backstabs = 0;
        Felled = 0;
        Dealt = 0;
        Missed = 0;
    }
}
