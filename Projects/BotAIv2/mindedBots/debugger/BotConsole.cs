using System;
using System.Collections.Generic;
using System.IO;
using Server.BotAI.V2;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.Mind;

public static class BotConsole
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotConsole));

    public static int ListenMs { get; set; } = 2000;

    private static string _in;

    private static string _out;

    private static long _triedTick;

    private static bool _broken;

    public static long Asked { get; private set; }

    public static void Open()
    {
        try
        {
            _in = Path.Combine(Core.BaseDirectory, "argus-in.txt");
            _out = Path.Combine(Core.BaseDirectory, "argus-out.txt");

            File.WriteAllText(_out, $"[{DateTime.Now:HH:mm:ss}] {BotVigil.Name} is listening at {_in}{Environment.NewLine}");

            logger.Information(
                "The debugger's door is open: write a line into {In} and the answer appears in {Out}. Words it knows: state, bot <name>, idle, trades, fighting, rollcall, memory, note <text>, think <question>, hands, do <command>, dials <word>, dial <Class.Name> <value>",
                _in,
                _out
            );
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The debugger's door could not be opened: {Message}", e.Message);
        }
    }

    public static void Listen(long now)
    {
        if (_broken || _in == null || now - _triedTick < ListenMs)
        {
            return;
        }

        _triedTick = now;

        string[] lines;

        try
        {
            if (!File.Exists(_in))
            {
                return;
            }

            var text = File.ReadAllText(_in);

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            File.WriteAllText(_in, "");

            lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception e)
        {
            logger.Warning("The debugger's door stopped taking questions: {Message}", e.Message);

            return;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.Length == 0)
            {
                continue;
            }

            Asked++;

            try
            {
                Answer(line);
            }
            catch (Exception e)
            {

                Say($"that question threw: {e.Message}");
            }
        }
    }

    private static void Answer(string line)
    {
        var (verb, rest) = Split(line);

        switch (verb)
        {
            case "state":
                Say(BotVigil.Digest());

                return;

            case "bot":
                Say(BotVigil.Row(rest));

                return;

            case "trades":
                Say(BotVigil.TradeTable());

                return;

            case "fighting":
                Say(BotVigil.CombatTable());

                return;

            case "idle":
                Say(BotVigil.Loitering());

                return;

            case "rollcall":
                Say(BotAudit.Last);

                return;

            case "memory":
                Say(BotDebugMemory.Recite() ?? "Nothing remembered yet.");

                return;

            case "note":
                BotDebugMemory.Ask("console", rest);
                BotDebugLog.Rule();
                BotDebugLog.Write("NOTE from the console — a person speaking, not a measurement");
                BotDebugLog.Block("  what was said:", rest);
                BotDebugLog.Rule();
                Say($"noted, and it goes in front of me at my next report: {rest}");

                return;

            case "do":
                {
                    var (order, args) = Split(rest);
                    var answer = BotHand.Run("the console", order, args, "asked by hand through the door", true);

                    Say(answer ?? "nothing to do.");
                }

                return;

            case "hands":
                Say(
                    BotHand.Describe() + $". I know: {string.Join(", ", BotHand.Verbs)}. {BotHand.Manual}"
                    + $" By hand only: {BotHand.ByHand}"
                );

                return;

            case "dials":
                Say(BotDials.Show(rest));

                return;

            case "dial":
                {
                    var (which, wanted) = Split(rest);

                    Say(BotDials.Set(which, wanted, "the console"));
                }

                return;

            case "chart":
                Say(BotHalls.Describe()
                    + (BotHalls.Charting == null ? "" : $"; I am in {BotHalls.Charting} now, at ({BotHalls.Probing.X}, {BotHalls.Probing.Y})"));

                return;

            case "think":

                if (BotVigil.Consider(rest, Say))
                {
                    Say($"thinking about: {rest} — the answer will follow here and in the log.");
                }
                else
                {
                    Say("the model is busy or the question was empty; nothing was asked.");
                }

                return;

            default:
                Say(
                    $"I do not know the word \"{verb}\". I know: state, bot <name>, idle, trades, fighting, rollcall,"
                    + " memory, note <text>, think <question>, hands, do <command>, dials <word>, dial <Class.Name> <value>."
                );

                return;
        }
    }

    private static (string Verb, string Tail) Split(string line)
    {
        var space = line.IndexOf(' ');

        return space < 0
            ? (line.ToLowerInvariant(), "")
            : (line[..space].ToLowerInvariant(), line[(space + 1)..].Trim());
    }

    public static void Say(string what)
    {
        if (_broken || _out == null || what == null)
        {
            return;
        }

        try
        {
            File.AppendAllText(_out, $"[{DateTime.Now:HH:mm:ss}] {what}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The debugger's answer file stopped taking lines: {Message}", e.Message);
        }
    }
}
