using System.Collections.Generic;
using Server.Engines.Craft;

namespace Server.BotAI.V2;

public static class BotCraftEar
{

    public static int FreshMs { get; set; } = 30000;

    public static bool Running { get; private set; }

    private static readonly Dictionary<Serial, (int Number, string Text, long When)> _last = [];

    private static readonly Dictionary<int, long> _tally = [];

    private static long _unnumbered;

    public static void Listen()
    {
        if (Running)
        {
            return;
        }

        CraftItem.Said += Hear;
        Running = true;
    }

    private static void Hear(Mobile from, TextDefinition message)
    {
        if (from is not BotMobile || message == null)
        {
            return;
        }

        var number = message.Number;

        if (number > 0)
        {
            _tally[number] = (_tally.TryGetValue(number, out var many) ? many : 0) + 1;
        }
        else
        {
            _unnumbered++;
        }

        if (number is 1044154 or 1044155)
        {
            _last.Remove(from.Serial);

            return;
        }

        _last[from.Serial] = (number, message.String, Core.TickCount);
    }

    public static string Why(Mobile bot)
    {
        if (bot == null || !_last.TryGetValue(bot.Serial, out var said) || Core.TickCount - said.When > FreshMs)
        {
            return "";
        }

        return $"; the engine's last word was {Words(said.Number, said.Text)}";
    }

    private static string Words(int number, string text) =>
        number switch
        {
            500119  => "\"you must wait to perform another action\" (500119)",
            1044037 => "\"you do not have sufficient metal to make that\" (1044037)",
            1044038 => "\"you have worn out your tool\" (1044038)",
            1044043 => "\"you failed to create the item, and some of your materials are lost\" (1044043)",
            1044153 => "\"you don't have the required skills to attempt this item\" (1044153)",
            1044157 => "\"you failed to create the item, but no materials were lost\" (1044157)",
            1044263 => "\"the tool must be on your person to use\" (1044263)",
            1044267 => "\"you must be near an anvil and a forge to smith items\" (1044267)",
            1044268 => "\"you cannot work this strange and unusual metal\" (1044268)",
            > 0     => $"cliloc {number}",
            _       => $"\"{text}\""
        };

    public static void Forget()
    {
        _last.Clear();
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "nobody is listening to the craft system";
        }

        if (_tally.Count == 0 && _unnumbered == 0)
        {
            return "the craft system has said nothing to any bot yet";
        }

        List<KeyValuePair<int, long>> heard = [.. _tally];

        heard.Sort(static (a, b) => b.Value.CompareTo(a.Value));

        List<string> parts = [];

        for (var i = 0; i < heard.Count && i < 6; i++)
        {
            parts.Add($"{heard[i].Value} × {Words(heard[i].Key, null)}");
        }

        if (_unnumbered > 0)
        {
            parts.Add($"{_unnumbered} in plain words");
        }

        return $"the craft system said to bots: {string.Join(", ", parts)}";
    }

    public static IEnumerable<(int Number, string Text, long Count)> Tallies()
    {
        List<KeyValuePair<int, long>> heard = [.. _tally];

        heard.Sort(static (a, b) => b.Value.CompareTo(a.Value));

        for (var i = 0; i < heard.Count; i++)
        {
            yield return (heard[i].Key, Words(heard[i].Key, null), heard[i].Value);
        }

        if (_unnumbered > 0)
        {
            yield return (0, "in plain words", _unnumbered);
        }
    }
}
