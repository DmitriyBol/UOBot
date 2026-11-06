using System;
using System.IO;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotBoot
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBoot));

    public static double Least { get; set; } = 0.5;

    public static int Mobiles { get; private set; }

    public static int Items { get; private set; }

    public static int Best { get; private set; }

    public static bool Truncated { get; private set; }

    private static string File => Path.GetFullPath(Path.Combine(Core.BaseDirectory, "..", "logs", "bot-boot.txt"));

    public static void Check()
    {
        Mobiles = World.Mobiles.Count;
        Items = World.Items.Count;
        Best = 0;
        Truncated = false;

        try
        {
            if (System.IO.File.Exists(File))
            {
                foreach (var line in System.IO.File.ReadAllLines(File))
                {
                    if (line.StartsWith("best=", StringComparison.Ordinal) && int.TryParse(line[5..].Trim(), out var best))
                    {
                        Best = best;
                    }
                }
            }
        }
        catch (Exception e)
        {
            logger.Warning("The boot record could not be read: {Message}", e.Message);
        }

        if (Best > 0 && Mobiles < Best * Least)
        {
            Truncated = true;

            var say = $"the world loaded with {Mobiles} mobiles and {Items} items against {Best} mobiles at the best boot on record: "
                      + "the save is probably truncated — stop the shard and restore Saves from Backups/Automatic or Archives/Hourly before anything else";

            logger.Error("{Say}", say);
            BotAlarm.Raise("world", say, Mobiles, Best, "boot");
        }
        else
        {
            var say = $"the world loaded with {Mobiles} mobiles and {Items} items ({Best} mobiles at the best boot on record)";

            logger.Information("{Say}", say);

            if (BotAlarm.Up("world"))
            {
                BotAlarm.Clear("world", say, Mobiles, Best, "boot");
            }
            else
            {
                BotAlarm.Note("world", say, Mobiles, Best, "boot");
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, $"best={Math.Max(Best, Mobiles)}{Environment.NewLine}last={Mobiles}{Environment.NewLine}items={Items}{Environment.NewLine}at={DateTime.Now:O}{Environment.NewLine}");
        }
        catch (Exception e)
        {
            logger.Warning("The boot record could not be written: {Message}", e.Message);
        }
    }

    public static string Describe() =>
        Best == 0 && Mobiles == 0
            ? "the boot has not been checked"
            : $"the boot loaded {Mobiles} mobiles and {Items} items against {Best} at the best boot on record{(Truncated ? " — READ AS TRUNCATED" : "")}";
}
