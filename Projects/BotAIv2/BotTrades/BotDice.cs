using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotDice
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDice));

    public static bool Running { get; set; } = true;

    public static double GameChance { get; set; } = 0.08;

    public static int StakeMin { get; set; } = 5;

    public static int StakeMax { get; set; } = 20;

    public static double WatchChance { get; set; } = 0.5;

    public static long GamesCalled { get; private set; }

    public static long GamesTaken { get; private set; }

    public static long RefusedPoor { get; private set; }

    public static long GamesPlayed { get; private set; }

    public static long Tied { get; private set; }

    public static long Voided { get; private set; }

    public static long GoldMoved { get; private set; }

    public static long WatchersReacted { get; private set; }

    public sealed class Game
    {
        public BotMobile First;

        public BotMobile Second;

        public int Stake;

        public bool Taken;

        public bool Rethrown;
    }

    public static bool Play(BotTavern tavern, List<BotMobile> talkers)
    {
        if (!Running || tavern == null)
        {
            return false;
        }

        if (tavern.Dice is { } game)
        {
            Advance(tavern, game, talkers);

            return true;
        }

        if (talkers == null || Utility.RandomDouble() >= GameChance)
        {
            return false;
        }

        BotMobile first = null;
        BotMobile second = null;
        var guests = 0;

        for (var i = 0; i < talkers.Count; i++)
        {
            var bot = talkers[i];

            if (bot is not { Deleted: false, Alive: true } || ReferenceEquals(bot, tavern.Keeper))
            {
                continue;
            }

            guests++;

            if (first == null)
            {
                first = bot;
            }
            else if (second == null)
            {
                second = bot;
            }
            else if (Utility.Random(guests) is var pick && pick < 2)
            {
                if (pick == 0)
                {
                    first = bot;
                }
                else
                {
                    second = bot;
                }
            }
        }

        if (first == null || second == null)
        {
            return false;
        }

        var stake = Utility.RandomMinMax(Math.Max(1, StakeMin), Math.Max(StakeMin, StakeMax));
        var fill = new Dictionary<string, string> { ["stake"] = stake.ToString() };

        if (!BotCampTalk.Say(first, "tavern:dice:call", null, second, fill))
        {
            return false;
        }

        tavern.Dice = new Game { First = first, Second = second, Stake = stake };
        GamesCalled++;

        return true;
    }

    internal static void Drop(BotTavern tavern)
    {
        if (tavern?.Dice is { Taken: true })
        {
            Voided++;
        }

        if (tavern != null)
        {
            tavern.Dice = null;
        }
    }

    private static void Advance(BotTavern tavern, Game game, List<BotMobile> talkers)
    {

        if (!Seated(talkers, game.First) || !Seated(talkers, game.Second))
        {
            if (game.Taken)
            {
                Voided++;
            }

            tavern.Dice = null;

            return;
        }

        if (!game.Taken)
        {
            if (Pocket(game.First) < game.Stake || Pocket(game.Second) < game.Stake)
            {
                BotCampTalk.Say(game.Second, "tavern:dice:poor", null, game.First, null);
                RefusedPoor++;
                tavern.Dice = null;

                return;
            }

            if (BotCampTalk.Say(game.Second, "tavern:dice:take", null, game.First, null))
            {
                game.Taken = true;
                GamesTaken++;
            }

            return;
        }

        var rollA = Utility.Dice(2, 6, 0);
        var rollB = Utility.Dice(2, 6, 0);

        if (rollA == rollB)
        {
            if (!game.Rethrown)
            {
                game.Rethrown = true;
                BotCampTalk.Say(game.First, "tavern:dice:tie", null, game.Second, null);

                return;
            }

            BotCampTalk.Say(game.First, "tavern:dice:tie", null, game.Second, null);
            Tied++;
            tavern.Dice = null;

            return;
        }

        var winner = rollA > rollB ? game.First : game.Second;
        var loser = ReferenceEquals(winner, game.First) ? game.Second : game.First;
        var roll = new Dictionary<string, string>
        {
            ["rollA"] = Math.Max(rollA, rollB).ToString(),
            ["rollB"] = Math.Min(rollA, rollB).ToString(),
            ["stake"] = game.Stake.ToString()
        };

        BotCampTalk.Say(winner, "tavern:dice:roll", null, loser, roll);

        if (!BotMarket.Charge(loser, game.Stake, out _))
        {
            BotCampTalk.Say(winner, "tavern:dice:void", null, loser, null);
            Voided++;
            tavern.Dice = null;

            return;
        }

        winner.Backpack?.DropItem(new Gold(game.Stake));

        BotYield.Aside(loser, game.Stake);
        GoldMoved += game.Stake;
        GamesPlayed++;

        BotCampTalk.Say(loser, "tavern:dice:paid", null, winner, new Dictionary<string, string> { ["stake"] = game.Stake.ToString() });

        logger.Information(
            "Dice at {Table}: {Winner} {RollW} against {Loser} {RollL}; {Stake}gp changes hands",
            tavern.Name,
            winner.Name,
            Math.Max(rollA, rollB),
            loser.Name,
            Math.Min(rollA, rollB),
            game.Stake
        );

        for (var i = 0; i < talkers.Count; i++)
        {
            var watcher = talkers[i];

            if (watcher is { Deleted: false, Alive: true } && !ReferenceEquals(watcher, winner) && !ReferenceEquals(watcher, loser)
                && Utility.RandomDouble() < WatchChance)
            {
                if (BotCampTalk.Say(watcher, "tavern:dice:watch", null, winner, null))
                {
                    WatchersReacted++;
                }

                break;
            }
        }

        tavern.Dice = null;
    }

    private static bool Seated(List<BotMobile> talkers, BotMobile bot)
    {
        if (talkers == null || bot is not { Deleted: false, Alive: true })
        {
            return false;
        }

        for (var i = 0; i < talkers.Count; i++)
        {
            if (ReferenceEquals(talkers[i], bot))
            {
                return true;
            }
        }

        return false;
    }

    private static int Pocket(Mobile bot) => bot?.Backpack?.GetAmount(typeof(Gold)) ?? 0;

    public static string Describe() =>
        !Running
            ? "dice are off"
            : $"dice: {GamesCalled} games called, {GamesTaken} taken ({RefusedPoor} refused, pockets short), {GamesPlayed} played and paid ({Tied} tied, {Voided} voided), "
              + $"{GoldMoved}gp changed hands, {WatchersReacted} guests watched aloud";

    public static void Forget()
    {
        GamesCalled = 0;
        GamesTaken = 0;
        RefusedPoor = 0;
        GamesPlayed = 0;
        Tied = 0;
        Voided = 0;
        GoldMoved = 0;
        WatchersReacted = 0;
    }
}
