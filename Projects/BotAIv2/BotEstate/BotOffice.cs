using System.Collections.Generic;
using Server.Guilds;

namespace Server.BotAI.V2;

public static class BotOffice
{

    public static int OfferedMs { get; set; } = 12000;

    public static long Lapsed { get; private set; }

    public static long Kept { get; private set; }

    private static readonly Dictionary<(string Office, string Guild), long> _claims = [];

    private static readonly Dictionary<(string Office, string Guild), long> _pending = [];

    private static readonly List<(string Office, string Guild)> _stale = [];

    public static bool Busy(string office, Guild guild)
    {
        if (guild == null)
        {
            return false;
        }

        Sweep();

        return _claims.TryGetValue((office, guild.Name), out var until) && Core.TickCount - until < 0;
    }

    public static void Offering(string office, Guild guild)
    {
        if (guild == null)
        {
            return;
        }

        var key = (office, guild.Name);
        var now = Core.TickCount;

        _claims[key] = now + OfferedMs;
        _pending[key] = now;
    }

    public static void Hold(string office, Guild guild, int claimMs)
    {
        if (guild == null)
        {
            return;
        }

        var key = (office, guild.Name);

        _claims[key] = Core.TickCount + claimMs;

        if (_pending.Remove(key))
        {
            Kept++;
        }
    }

    public static void Release(string office, Guild guild)
    {
        if (guild == null)
        {
            return;
        }

        var key = (office, guild.Name);

        _claims.Remove(key);
        _pending.Remove(key);
    }

    private static void Sweep()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var now = Core.TickCount;

        _stale.Clear();

        foreach (var (key, made) in _pending)
        {
            if (now - (made + OfferedMs) >= 0)
            {
                _stale.Add(key);
            }
        }

        for (var i = 0; i < _stale.Count; i++)
        {
            _pending.Remove(_stale[i]);
            Lapsed++;
        }
    }

    public static string Describe() =>
        $"{Kept} guild errands were taken up out of the offers made and {Lapsed} offers lapsed unclaimed";

    public static void Forget()
    {
        _claims.Clear();
        _pending.Clear();
        _stale.Clear();
        Lapsed = 0;
        Kept = 0;
    }
}
