using System.Collections.Generic;
using Server.Engines.Harvest;
using Server.Mobiles;

namespace Server.BotAI.V2;

public static class BotHeard
{

    public enum Word
    {

        Nothing,

        Empty,

        Taken,

        Missed,

        Adrift,

        Full,

        Broken,

        Other
    }

    public static bool Running { get; private set; }

    private static readonly Dictionary<Word, long> _tally = [];

    private static readonly Dictionary<Serial, (Word Said, long When, HarvestDefinition Of)> _last = [];

    public static int FreshMs { get; set; } = 2500;

    public static void Listen()
    {
        if (Running)
        {
            return;
        }

        HarvestDefinition.Said += Hear;
        Running = true;
    }

    public static void Forget()
    {
        if (Running)
        {
            HarvestDefinition.Said -= Hear;
            Running = false;
        }

        _last.Clear();
        _tally.Clear();
        Stale = 0;
    }

    private static void Hear(Mobile from, HarvestDefinition def, TextDefinition message)
    {
        if (from is not BotMobile { Deleted: false } bot || def == null)
        {
            return;
        }

        var said = Which(def, message);

        _last[bot.Serial] = (said, Core.TickCount, def);

        _tally.TryGetValue(said, out var many);
        _tally[said] = many + 1;
    }

    private static Word Which(HarvestDefinition def, TextDefinition message)
    {
        if (message == null)
        {
            return Word.Other;
        }

        if (Same(message, def.NoResourcesMessage))
        {
            return Word.Empty;
        }

        if (Same(message, def.DoubleHarvestMessage))
        {
            return Word.Taken;
        }

        if (Same(message, def.FailMessage))
        {
            return Word.Missed;
        }

        if (Same(message, def.TimedOutOfRangeMessage) || Same(message, def.OutOfRangeMessage))
        {
            return Word.Adrift;
        }

        if (Same(message, def.PackFullMessage))
        {
            return Word.Full;
        }

        if (Same(message, def.ToolBrokeMessage))
        {
            return Word.Broken;
        }

        return Word.Other;
    }

    private static bool Same(TextDefinition said, TextDefinition mine) =>
        mine != null && (said.Number > 0 ? said.Number == mine.Number : said.String == mine.String);

    public static Word Last(Mobile bot, HarvestDefinition about, out long since)
    {
        since = long.MaxValue;

        if (bot == null || !_last.TryGetValue(bot.Serial, out var heard))
        {
            return Word.Nothing;
        }

        since = Core.TickCount - heard.When;

        if (about != null && heard.Of != about || since > FreshMs)
        {
            Stale++;

            return Word.Nothing;
        }

        return heard.Said;
    }

    public static long Stale { get; private set; }

    public static void Clear(Mobile bot)
    {
        if (bot != null)
        {
            _last.Remove(bot.Serial);
        }
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "nobody is listening to what the harvest system says";
        }

        var empty = Count(Word.Empty);
        var taken = Count(Word.Taken);
        var missed = Count(Word.Missed);
        var adrift = Count(Word.Adrift);
        var full = Count(Word.Full);
        var broken = Count(Word.Broken);

        return $"the harvest system said: {empty} times there is nothing left here, {taken} somebody got there first, "
            + $"{missed} a missed swing, {adrift} moved too far to finish, {full} a full pack, {broken} a worn-out tool, "
            + $"{Count(Word.Other)} something else, and {Stale} sentences were passed over as being about another craft or an older swing";
    }

    private static long Count(Word said) => _tally.TryGetValue(said, out var many) ? many : 0;
}
