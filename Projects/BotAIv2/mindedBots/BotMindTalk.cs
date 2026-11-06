using System.Collections.Generic;

namespace Server.BotAI.Mind;

public static class BotMindTalk
{

    public static int Keep { get; set; } = 6;

    public static int HoldsMs { get; set; } = 240000;

    public static int MostLetters { get; set; } = 120;

    public static int SpeakEveryMs { get; set; } = 25000;

    private static readonly List<(string Who, string What, long Tick)> _said = [];

    public static long Lines { get; private set; }

    public static bool Post(string who, string what)
    {
        if (string.IsNullOrWhiteSpace(who) || string.IsNullOrWhiteSpace(what))
        {
            return false;
        }

        var line = what.Replace('\n', ' ').Replace('\r', ' ').Trim();

        if (line.Length > MostLetters)
        {
            line = line[..MostLetters];
        }

        for (var i = _said.Count - 1; i >= 0; i--)
        {
            if (_said[i].Who == who)
            {
                if (string.Equals(_said[i].What, line, System.StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                break;
            }
        }

        _said.Add((who, line, Core.TickCount));
        Lines++;

        while (_said.Count > Keep)
        {
            _said.RemoveAt(0);
        }

        return true;
    }

    public static IEnumerable<(string Who, string What, int SecondsAgo)> Heard(string listener)
    {
        for (var i = 0; i < _said.Count; i++)
        {
            var (who, what, tick) = _said[i];
            var since = Core.TickCount - tick;

            if (who == listener || since >= HoldsMs)
            {
                continue;
            }

            yield return (who, what, (int)(since / 1000));
        }
    }

    public static void Forget()
    {
        _said.Clear();
        Lines = 0;
    }
}
