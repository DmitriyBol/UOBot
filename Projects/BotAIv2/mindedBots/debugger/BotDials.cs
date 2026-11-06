using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Server.Text;
using Server.BotAI.V2;
using Server.Logging;

namespace Server.BotAI.Mind;

public static class BotDials
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDials));

    private static Dictionary<string, PropertyInfo> _dials;

    private static readonly Dictionary<string, (string Was, string Now, DateTime When, string Who)> _moved = new();

    private static string _journal;

    private static bool _broken;

    public static long Changes { get; private set; }

    public static int Count => Map().Count;

    public static int Moved => _moved.Count;

    public static void Open()
    {
        _broken = false;
        _moved.Clear();
        Changes = 0;

        try
        {
            var folder = Path.GetFullPath(Path.Combine(Core.BaseDirectory, "..", "logs"));

            Directory.CreateDirectory(folder);

            _journal = Path.Combine(folder, "bot-dials.log");

            File.AppendAllText(
                _journal,
                $"{Environment.NewLine}== {DateTime.Now:yyyy-MM-dd HH:mm:ss} == shard up, {Count} dials in reach =={Environment.NewLine}"
            );

            logger.Information(
                "{Count} dials can be moved while the shard runs: ask the debugger's door for \"dials <word>\", set with \"dial <Class.Name> <value>\". Every change goes to {Journal} and lasts until the shard stops",
                Count,
                _journal
            );
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The dial journal could not be opened, so no dial can be moved: {Message}", e.Message);
        }
    }

    private static Dictionary<string, PropertyInfo> Map()
    {
        if (_dials != null)
        {
            return _dials;
        }

        _dials = new Dictionary<string, PropertyInfo>();

        var assemblies = new[] { typeof(BotBeat).Assembly, typeof(BotDials).Assembly }.Distinct();

        foreach (var assembly in assemblies)
        {
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            foreach (var type in types)
            {
                if (type.Namespace?.StartsWith("Server.BotAI", StringComparison.Ordinal) != true)
                {
                    continue;
                }

                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!property.CanRead || !property.CanWrite || !Settable(property.PropertyType))
                    {
                        continue;
                    }

                    if (property.GetSetMethod() == null)
                    {
                        continue;
                    }

                    _dials[$"{type.Name}.{property.Name}".ToLowerInvariant()] = property;
                }
            }
        }

        return _dials;
    }

    private static bool Settable(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float)
        || type == typeof(bool) || type == typeof(string);

    private static string Pretty(PropertyInfo property) => $"{property.DeclaringType?.Name}.{property.Name}";

    private static PropertyInfo Find(string name, out string trouble)
    {
        trouble = null;

        var map = Map();
        var key = name.Trim().ToLowerInvariant();

        if (map.TryGetValue(key, out var exact))
        {
            return exact;
        }

        var tail = "." + key;
        var matches = map.Where(pair => pair.Key.EndsWith(tail, StringComparison.Ordinal)).ToList();

        if (matches.Count == 1)
        {
            return matches[0].Value;
        }

        trouble = matches.Count == 0
            ? $"no dial is called \"{name}\". Ask for \"dials {name}\" to see what is near it."
            : $"\"{name}\" is held by {matches.Count} classes: {string.Join(", ", matches.Select(m => Pretty(m.Value)))}. Say it in full.";

        return null;
    }

    private static string Read(PropertyInfo property)
    {
        try
        {
            var value = property.GetValue(null);

            return value switch
            {
                null     => "null",
                double d => d.ToString("0.####", CultureInfo.InvariantCulture),
                float f  => f.ToString("0.####", CultureInfo.InvariantCulture),
                bool b   => b ? "true" : "false",
                _        => value.ToString()
            };
        }
        catch (Exception e)
        {
            return $"unreadable ({e.GetBaseException().Message})";
        }
    }

    public static string Show(string filter)
    {
        var map = Map();

        if (string.IsNullOrWhiteSpace(filter))
        {
            return $"{map.Count} dials, {_moved.Count} moved this session. Ask for \"dials <word>\" — the word is"
                + " matched anywhere in Class.Name, so \"ms\" gives every clock and \"anvil\" gives the smith."
                + " \"dials changed\" lists what this session has moved and where to write it down.";
        }

        if (filter.Trim().ToLowerInvariant() is "changed" or "moved")
        {
            return Changed();
        }

        var word = filter.Trim().ToLowerInvariant();
        var hits = map
            .Where(pair => pair.Key.Contains(word, StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        if (hits.Count == 0)
        {
            return $"no dial has \"{filter}\" in its name.";
        }

        using var text = ValueStringBuilder.Create(hits.Count * 48 + 96);

        text.Append($"{hits.Count} {(hits.Count == 1 ? "dial" : "dials")} matching \"{filter}\":");

        foreach (var (key, property) in hits.Take(60))
        {
            text.Append($"{Environment.NewLine}  {Pretty(property)} = {Read(property)}");

            if (_moved.TryGetValue(key, out var move))
            {
                text.Append($"   (was {move.Was}, moved {move.When:HH:mm:ss})");
            }
        }

        if (hits.Count > 60)
        {
            text.Append($"{Environment.NewLine}  and {hits.Count - 60} more; narrow the word.");
        }

        return text.ToString();
    }

    public static string Changed()
    {
        if (_moved.Count == 0)
        {
            return "no dial has been moved this session; every number is as the shard booted with it.";
        }

        var map = Map();

        using var text = ValueStringBuilder.Create(_moved.Count * 72 + 224);

        text.Append(
            $"{_moved.Count} {(_moved.Count == 1 ? "dial has" : "dials have")} moved this session. None of it"
            + " survives a restart — to keep one, write it into its Configuration/bot-*.json by hand"
            + " (PascalCase, and DIALS.md says whether a key reaches it at all):"
        );

        foreach (var (key, move) in _moved.OrderBy(pair => pair.Value.When))
        {
            var name = map.TryGetValue(key, out var found) ? Pretty(found) : key;
            var now = found == null ? move.Now : Read(found);

            text.Append($"{Environment.NewLine}  {name}: {move.Was} -> {now}   (moved at {move.When:HH:mm:ss} by {move.Who})");
        }

        return text.ToString();
    }

    public static string Note()
    {
        if (_moved.Count == 0)
        {
            return null;
        }

        var map = Map();

        return $"{_moved.Count} {(_moved.Count == 1 ? "dial" : "dials")} moved by hand this session: "
            + string.Join(
                ", ",
                _moved.Select(pair => $"{(map.TryGetValue(pair.Key, out var p) ? Pretty(p) : pair.Key)} {pair.Value.Was}->{pair.Value.Now}")
            );
    }

    public static string Set(string name, string value, string who)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "which dial? Say \"dial <Class.Name> <value>\", or \"dials <word>\" to look one up.";
        }

        var property = Find(name, out var trouble);

        if (property == null)
        {
            return trouble;
        }

        var key = $"{property.DeclaringType?.Name}.{property.Name}".ToLowerInvariant();
        var before = Read(property);

        if (string.IsNullOrWhiteSpace(value))
        {
            var moved = _moved.TryGetValue(key, out var earlier)
                ? $" (was {earlier.Was} until {earlier.When:HH:mm:ss})"
                : "";

            return $"{Pretty(property)} = {before}{moved}";
        }

        var wanted = value.Trim();
        var putting = wanted is "reset" or "back";

        if (putting)
        {
            if (!_moved.TryGetValue(key, out var earlier))
            {
                return $"{Pretty(property)} has not been moved this session; there is nothing to put back.";
            }

            wanted = earlier.Was;
        }

        if (!Parse(property.PropertyType, wanted, out var parsed, out var why))
        {
            return why;
        }

        if (_broken)
        {
            return "the dial journal is not being written, so nothing may be moved. A change nobody can read"
                + " back afterwards is worse than no change: every later measurement would be about a shard"
                + " whose thresholds are unknown.";
        }

        try
        {
            File.AppendAllText(
                _journal,
                $"[{DateTime.Now:HH:mm:ss}] {who}: {Pretty(property)} {before} -> {wanted}{Environment.NewLine}"
            );
        }
        catch (Exception e)
        {
            _broken = true;

            return $"the dial journal refused the entry ({e.Message}), so the dial was not moved.";
        }

        try
        {
            property.SetValue(null, parsed);
        }
        catch (Exception e)
        {
            try
            {
                File.AppendAllText(
                    _journal,
                    $"           ...and the shard refused it: {e.GetBaseException().Message}{Environment.NewLine}"
                );
            }
            catch
            {

                _broken = true;
            }

            return $"{Pretty(property)} refused that: {e.GetBaseException().Message}";
        }

        Changes++;

        var after = Read(property);

        if (putting && _moved.TryGetValue(key, out var was) && after == was.Was)
        {
            _moved.Remove(key);
        }
        else
        {
            _moved[key] = (_moved.TryGetValue(key, out var first) ? first.Was : before, after, DateTime.Now, who);
        }

        BotDebugLog.Rule();
        BotDebugLog.Write($"DIAL moved by {who} - a person changing the shard, not a measurement of it");
        BotDebugLog.Block("  what moved:", $"{Pretty(property)}: {before} -> {after}");
        BotDebugLog.Rule();

        logger.Information(
            "A dial was moved by hand: {Dial} {Before} -> {After} ({Who})",
            Pretty(property),
            before,
            after,
            who
        );

        return $"{Pretty(property)}: {before} -> {after}. It holds until the shard stops; \"dials changed\""
            + " lists everything moved so far.";
    }

    private static bool Parse(Type type, string text, out object value, out string why)
    {
        value = null;
        why = null;

        var clean = text.Replace(',', '.').Trim();

        if (type == typeof(int))
        {
            if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                value = i;

                return true;
            }

            why = $"\"{text}\" is not a whole number, and that dial is one.";

            return false;
        }

        if (type == typeof(long))
        {
            if (long.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                value = l;

                return true;
            }

            why = $"\"{text}\" is not a whole number, and that dial is one.";

            return false;
        }

        if (type == typeof(double) || type == typeof(float))
        {
            if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                value = type == typeof(float) ? (float)d : d;

                return true;
            }

            why = $"\"{text}\" is not a number, and that dial is one.";

            return false;
        }

        if (type == typeof(bool))
        {
            switch (clean.ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "on":
                case "1":
                    {
                        value = true;

                        return true;
                    }
                case "false":
                case "no":
                case "off":
                case "0":
                    {
                        value = false;

                        return true;
                    }
            }

            why = $"\"{text}\" is neither true nor false, and that dial is a switch.";

            return false;
        }

        value = text;

        return true;
    }
}
