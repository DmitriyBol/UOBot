using System;
using System.Text.Json;
using Server.BotAI.V2;

namespace Server.BotAI.Mind;

public static class BotStoryVoice
{
    public static long Asked { get; private set; }

    public static long Read { get; private set; }

    public static long Unread { get; private set; }

    public static void Open() => BotStory.Ask = Ask;

    public static void Close() => BotStory.Ask = null;

    private static bool Ask(BotStoryAsk q, Action<string, long> then)
    {
        if (q == null || then == null || string.IsNullOrWhiteSpace(q.Facts) || !BotOllama.Free || World.WorldState != WorldState.Running)
        {
            return false;
        }

        Asked++;

        var model = !string.IsNullOrWhiteSpace(q.Model) ? q.Model
            : !string.IsNullOrWhiteSpace(BotParley.JudgeModel) ? BotParley.JudgeModel
            : BotOllama.Model;

        BotOllama.Ask(
            Role(q),
            Brief(q),
            Schema,
            false,
            (json, waited) =>
            {
                var text = Answer(json);

                if (text == null)
                {
                    Unread++;
                }
                else
                {
                    Read++;
                }

                then(text, waited);
            },
            model,
            BotParley.JudgeKeepAlive,
            BotParley.JudgeTimeoutMs
        );

        return true;
    }

    private static string Role(BotStoryAsk q) =>
        q.Kind == "day"
            ? "You keep the chronicle of an island of adventurers, guilds and towns. You are given what happened on one day, one fact a line "
              + "with its time. Retell the day as one page of plain English prose in the past tense, in the measured voice of a chronicler. "
              + "Use only the facts given: every person, guild, town, place, creature and number you write must appear in them, spelled as "
              + "they are. Add no event, cause, motive, feeling or detail that is not in them; leave out what you cannot say without inventing. "
              + $"At most {Math.Max(40, q.MostWords)} words. Answer with: text — the page."
            : "You keep the chronicle of an island of adventurers, guilds and towns. You are given the facts of one chapter of one person's "
              + "life, one fact a line, most with a date; quoted words are the person's own. Retell the chapter as one paragraph of plain "
              + "English prose in the past tense and the third person, in the measured voice of a chronicler, naming the person. Use only "
              + "the facts given: every person, guild, town, place, creature and number you write must appear in them, spelled as they are. "
              + "Add no event, cause, motive, feeling or detail that is not in them; you may quote the person's words. "
              + $"At most {Math.Max(40, q.MostWords)} words. Answer with: text — the paragraph.";

    private static string Brief(BotStoryAsk q) =>
        $"{q.Title}\nAbout: {q.Who}\nFacts:\n{q.Facts}";

    private const string Schema = "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"minLength\":40}},\"required\":[\"text\"]}";

    private static string Answer(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("text", out var text)
                   && text.ValueKind == JsonValueKind.String
                ? text.GetString()?.Trim()
                : null;
        }

        catch (Exception)
        {
            return null;
        }
    }

    public static string Describe() => $"the chronicler's model: asked {Asked}, read {Read}, unread {Unread}";
}
