using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// What a mark does when a robbery has beaten it down to a quarter of its health.
///
/// <para>
/// <b>Patrick's rule of the evening of 18.09.2026.</b> "If the victim's health falls below 25% of the whole, it is
/// given a choice — run, or throw down part of its pack for the robbers to take, or fight back and maybe die, because
/// while they are fighting somebody may come to help."
/// </para>
///
/// <para>
/// <b>The choice is reasoned rather than rolled</b>, and Patrick's own sentence says what the reasoning is. Help
/// coming is the thing that makes fighting worth it, so a mark with anybody of its own within <see cref="Helpers"/>
/// tiles fights, and so does one that is still a match for the robber. Otherwise it is a question of what it can
/// reach: a ward within <see cref="Bolt"/> tiles is worth running for, and if there is none, its pack buys its life.
/// A mark with nothing to give has nothing to buy with and runs anyway — which is how this ends up being three
/// answers rather than two.
/// </para>
///
/// <para>
/// Asked once per robbery. The mark that pays keeps the rest of its pack and its life, the robber keeps what it was
/// given, and the robbery ends there: that is the whole point of paying.
/// </para>
/// </summary>
public static class BotQuarter
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotQuarter));

    public static bool Running { get; set; } = true;

    public static double Share { get; set; } = 0.25;

    public static int Helpers { get; set; } = 20;

    public static double Stands { get; set; } = 0.9;

    public static int Bolt { get; set; } = 60;

    public static int RunMs { get; set; } = 10000;

    public static double Toll { get; set; } = 0.5;

    public static long Asked { get; private set; }

    public static long Fought { get; private set; }

    public static long Fled { get; private set; }

    public static long Paid { get; private set; }

    public static long PaidGold { get; private set; }

    public static long PaidThings { get; private set; }

    public static long Penniless { get; private set; }

    public enum Answer
    {
        Fight,
        Flee,
        Pay
    }

    public static bool Beaten(Mobile mark) =>
        Running && mark is { Deleted: false, Alive: true } && mark.HitsMax > 0
        && mark.Hits <= mark.HitsMax * Math.Clamp(Share, 0.05, 0.95);

    public static Answer Choose(BotMobile robber, BotMobile mark)
    {
        Asked++;

        if (mark is not { Deleted: false, Alive: true } || mark.Map is not { } map || map == Map.Internal)
        {
            Fought++;

            return Answer.Fight;
        }

        if (Helped(mark, robber, map) || robber == null || BotThreat.Now(mark) >= BotThreat.Now(robber) * Stands)
        {
            Fought++;

            logger.Information(
                "{Name} is down to {Hits} of {Max} and fights on against {Robber}",
                mark.Name,
                mark.Hits,
                mark.HitsMax,
                robber?.Name ?? "its robber"
            );

            return Answer.Fight;
        }

        if (Warded(map, mark.Location))
        {
            Fled++;
            BotWill.Press(mark, new BotBolt(map, robber.Location, RunMs), "beaten down, and a town within a run");

            logger.Information(
                "{Name} is down to {Hits} of {Max} and runs for the town from {Robber}",
                mark.Name,
                mark.Hits,
                mark.HitsMax,
                robber.Name
            );

            return Answer.Flee;
        }

        var coin = Toss(mark, robber, out var things);

        if (coin <= 0 && things <= 0)
        {
            Penniless++;
            Fled++;
            BotWill.Press(mark, new BotBolt(map, robber.Location, RunMs), "beaten down, with nothing to buy its life with");

            return Answer.Flee;
        }

        Paid++;
        PaidGold += coin;
        PaidThings += things;

        BotVoice.Aloud(mark, $"Take it, {robber.Name}! Take it and let me be!");

        logger.Information(
            "{Name} is down to {Hits} of {Max} and throws {Gold}gp and {Things} things to {Robber} for its life",
            mark.Name,
            mark.Hits,
            mark.HitsMax,
            coin,
            things,
            robber.Name
        );

        return Answer.Pay;
    }

    private static bool Helped(BotMobile mark, BotMobile robber, Map map)
    {
        foreach (var other in map.GetMobilesInRange<BotMobile>(mark.Location, Math.Max(1, Helpers)))
        {
            if (other == mark || other == robber || other is not { Deleted: false, Alive: true, Hidden: false }
                || BotUnderworld.Member(other) || BotOutlaw.Outlaw(other))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool Warded(Map map, Point3D at)
    {
        for (var i = 0; i < 8; i++)
        {
            var x = at.X + (i is 0 or 1 or 7 ? Bolt : i is 3 or 4 or 5 ? -Bolt : 0);
            var y = at.Y + (i is 1 or 2 or 3 ? Bolt : i is 5 or 6 or 7 ? -Bolt : 0);

            if (BotOutlaw.Guarded(map, x, y, at.Z))
            {
                return true;
            }
        }

        return false;
    }

    private static int Toss(BotMobile mark, BotMobile robber, out int things)
    {
        things = 0;

        if (mark.Backpack is not { } pack || robber?.Backpack is not { } into)
        {
            return 0;
        }

        var share = Math.Clamp(Toll, 0.05, 1.0);
        var coin = (int)(pack.GetAmount(typeof(Gold)) * share);

        if (coin > 0 && pack.ConsumeTotal(typeof(Gold), coin) && !into.TryDropItem(robber, new Gold(coin), false))
        {
            mark.AddToBackpack(new Gold(coin));
            coin = 0;
        }
        else if (coin <= 0)
        {
            coin = 0;
        }

        List<Item> loose = [];

        for (var i = 0; i < pack.Items.Count; i++)
        {
            if (pack.Items[i] is { Deleted: false, Movable: true } item && item is not Gold
                && !BotBinding.IsBound(item, mark.Bond))
            {
                loose.Add(item);
            }
        }

        var many = (int)Math.Ceiling(loose.Count * share);

        for (var i = 0; i < loose.Count && things < many; i++)
        {
            if (into.TryDropItem(robber, loose[i], false))
            {
                things++;
            }
        }

        return coin;
    }

    public static string Describe() =>
        $"{Asked} marks beaten to a quarter and given the choice: {Fought} fought on, {Fled} ran ({Penniless} of them with nothing to give), {Paid} bought their lives for {PaidGold}gp and {PaidThings} things";

    public static void Forget()
    {
        Asked = 0;
        Fought = 0;
        Fled = 0;
        Paid = 0;
        PaidGold = 0;
        PaidThings = 0;
        Penniless = 0;
    }
}
