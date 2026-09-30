using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// One task a town sets a guild, and the catalogue of the kinds it may set: each one something the shard can check by itself.
///
/// <para>
/// <b>Patrick's order of 29.09.2026, point 3: the town gives the guild a unique task; each success lowers the town's prices for
/// that guild by half a percent, each failure raises them by as much.</b> A task the code cannot check is a task whose success
/// is whatever the model says it was, so the catalogue holds only what is measured:
/// </para>
///
/// <list type="bullet">
/// <item><b>clear</b> — the worst square within reach of the town's hall that reads unsafe, and that the guild's might can face,
/// is to read at least <see cref="ClearGain"/> better (up to neutral) by the deadline, with the guild's own members having stood
/// in it for <see cref="ClearPresence"/> minutes between them. Read off <c>BotQuad</c>, which every kill, crossing and death
/// already writes.</item>
/// <item><b>guard</b> — the guild's members are to spend <see cref="GuardMinutes"/> minutes between them inside the town's
/// walls. Counted once a look, member by member.</item>
/// <item><b>supply</b> — the guild's members are to put <see cref="SupplyUnits"/> of a material the market wants onto the bots'
/// own market, on stalls or into standing orders. Counted where the goods go out (<c>BotAuction.List</c> and
/// <c>BotAuction.Fill</c>), never where they are made.</item>
/// </list>
///
/// <para>
/// <b>A task is a price, not a command</b>, as every order on this shard is (<c>BotCharter</c>): the town charges the guild
/// with it — a muster at the square or the hall, or a request on the guild's board for the material — and the members' own
/// auctions decide whether they go. A task nobody took up fails at its deadline, and that is a fact about the guild.
/// </para>
///
/// <para>
/// <b>Unique</b>: a square or a material another guild is already working for the same town is not offered again while that
/// task stands.
/// </para>
/// </summary>
public sealed class BotTownTask
{
    public enum Kinds
    {
        Clear,
        Guard,
        Supply
    }

    public static double Hours { get; set; } = 2.0;

    public static double ClearGain { get; set; } = 0.10;

    public static double ClearPresence { get; set; } = 10.0;

    public static double ClearOdds { get; set; } = 2.0;

    public static double GuardMinutes { get; set; } = 90.0;

    public static int SupplyUnits { get; set; } = 20;

    public static readonly string[] Materials = ["ore", "logs", "hides", "feathers", "meat", "herbs", "wool"];

    public string Id { get; init; }

    public Kinds Kind { get; init; }

    public string Guild { get; init; }

    public BotTowns.Town Town { get; init; }

    public Map Map { get; init; }

    public Point3D Target { get; init; }

    public string Material { get; init; }

    public double Need { get; init; }

    public double Done { get; set; }

    public double Presence { get; set; }

    public double StartReading { get; init; }

    public long Began { get; set; }

    public long Deadline { get; set; }

    public string Charge { get; set; }

    public string Offer =>
        Kind switch
        {
            Kinds.Clear => $"clear — make the {BotQuad.Band(StartReading)} ground at ({Target.X}, {Target.Y}) read at least {Goal:F2} (it reads {StartReading:F2}), your members standing in it {ClearPresence:0} minutes between them",
            Kinds.Guard => $"guard — keep your members inside {Town?.Name}'s walls for {Need:0} minutes between them",
            _ => $"{Id} — put {Need:0} {Material} on the bots' market, on stalls or into standing orders"
        };

    public double Goal => Math.Min(BotQuad.Neutral, StartReading + ClearGain);

    public string Progress =>
        Kind switch
        {
            Kinds.Clear => $"{Presence:0} of {ClearPresence:0} minutes stood in it, reading {BotQuad.Safety(Map, Target):F2} of {Goal:F2}",
            Kinds.Guard => $"{Done:0} of {Need:0} member-minutes",
            _ => $"{Done:0} of {Need:0} {Material}"
        };

    public static List<BotTownTask> Options(Guild guild, BotTowns.Town town, Point3D hall, BotTownReport report)
    {
        List<BotTownTask> found = [];
        var map = BotPopulation.Home;
        var now = Core.TickCount;
        var might = BotWar.Might(guild.Name);

        if (report.Worst is { } worst && BotQuad.Stand(worst) is var stand && stand != Point3D.Zero
            && BotQuad.Muscle(report.WorstReading) * ClearOdds <= might && !BotBurgh.Taken(town, stand, null))
        {
            found.Add(new BotTownTask
            {
                Id = "clear",
                Kind = Kinds.Clear,
                Guild = guild.Name,
                Town = town,
                Map = map,
                Target = stand,
                Need = ClearPresence,
                StartReading = report.WorstReading,
                Began = now
            });
        }

        found.Add(new BotTownTask
        {
            Id = "guard",
            Kind = Kinds.Guard,
            Guild = guild.Name,
            Town = town,
            Map = map,
            Target = hall,
            Need = GuardMinutes,
            Began = now
        });

        List<(string Material, int Score)> scarce = [];

        for (var i = 0; i < Materials.Length; i++)
        {
            var material = Materials[i];

            if (BotBurgh.Taken(town, Point3D.Zero, material))
            {
                continue;
            }

            var kinds = Samples(material);
            var stocked = 0;
            var wanted = 0;

            for (var k = 0; k < kinds.Length; k++)
            {
                stocked += BotAuction.Stocked(kinds[k]);
                wanted += BotAuction.Best(kinds[k]) > 0 ? 1 : 0;
            }

            scarce.Add((material, 2 * wanted + (stocked == 0 ? 1 : 0)));
        }

        scarce.Sort((a, b) => b.Score.CompareTo(a.Score));

        for (var i = 0; i < scarce.Count && i < 2; i++)
        {
            found.Add(new BotTownTask
            {
                Id = $"supply-{scarce[i].Material}",
                Kind = Kinds.Supply,
                Guild = guild.Name,
                Town = town,
                Map = map,
                Target = hall,
                Material = scarce[i].Material,
                Need = SupplyUnits,
                Began = now
            });
        }

        return found;
    }

    private static Type[] Samples(string material) =>
        material switch
        {
            "ore" => [typeof(IronOre), typeof(IronIngot)],
            "logs" => [typeof(Log), typeof(Board)],
            "hides" => [typeof(Hides), typeof(Leather)],
            "feathers" => [typeof(Feather)],
            "meat" => [typeof(RawRibs), typeof(RawBird), typeof(RawLambLeg), typeof(RawChickenLeg)],
            "herbs" => [typeof(BlackPearl), typeof(Bloodmoss), typeof(Garlic), typeof(Ginseng), typeof(MandrakeRoot), typeof(Nightshade), typeof(SulfurousAsh), typeof(SpidersSilk)],
            _ => [typeof(Wool), typeof(Cloth), typeof(BoltOfCloth)]
        };

    public static string MaterialOf(Type kind) =>
        kind == null ? null
        : typeof(BaseOre).IsAssignableFrom(kind) || typeof(BaseIngot).IsAssignableFrom(kind) ? "ore"
        : kind == typeof(Log) || kind == typeof(Board) || typeof(Log).IsAssignableFrom(kind) || typeof(Board).IsAssignableFrom(kind) ? "logs"
        : typeof(BaseHides).IsAssignableFrom(kind) || typeof(BaseLeather).IsAssignableFrom(kind) ? "hides"
        : kind == typeof(Feather) ? "feathers"
        : kind == typeof(RawRibs) || kind == typeof(RawBird) || kind == typeof(RawLambLeg) || kind == typeof(RawChickenLeg) || kind == typeof(RawFishSteak) ? "meat"
        : typeof(BaseReagent).IsAssignableFrom(kind) ? "herbs"
        : kind == typeof(Wool) || kind == typeof(Cotton) || kind == typeof(Flax) || kind == typeof(Cloth) || kind == typeof(BoltOfCloth) || kind == typeof(UncutCloth) ? "wool"
        : null;

    public static void Hearing(BotHearing m)
    {
        var guild = BotGuilds.Named(m.Guest);

        m.Report = BotTownReport.Of(m.Town, m.Place, guild);
        m.Tasks = Options(guild, m.Town, m.Place, m.Report);

        var ids = new string[m.Tasks.Count];

        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = m.Tasks[i].Id;
        }

        m.Allowed = ids;
        m.Lines.Clear();

        var fill = BotDuke.Fill(m);

        m.Lines.Add((BotSpeaker.Witness, BotDuke.Phrase("parley:town-open", m.Witness, fill)));
        m.Lines.Add((BotSpeaker.Envoy, BotDuke.Phrase("parley:town-ask", m.Envoy, fill)));
        m.Lines.Add((BotSpeaker.Witness, BotDuke.Phrase("parley:town-await", m.Witness, fill)));

        using var b = Server.Text.ValueStringBuilder.Create(1024);

        b.Append(m.Report.Facts);
        b.Append("Tasks you may set it (answer with the id before the dash):\n");

        for (var i = 0; i < m.Tasks.Count; i++)
        {
            b.Append($"- {m.Tasks[i].Offer}\n");
        }

        b.Append($"A task is due in {Hours:0.#} hours. Done, the town's prices for this guild fall by {BotBurgh.Step:P1}; failed, they rise by as much.\n");

        m.Brief = b.ToString();
    }

    public static BotVerdict Rule(BotHearing m, string because)
    {
        BotTownTask pick = null;

        for (var i = 0; i < m.Tasks.Count && pick == null; i++)
        {
            if (m.Tasks[i].Kind == Kinds.Clear)
            {
                pick = m.Tasks[i];
            }
        }

        for (var i = 0; i < m.Tasks.Count && pick == null; i++)
        {
            if (m.Tasks[i].Kind == Kinds.Supply)
            {
                pick = m.Tasks[i];
            }
        }

        pick ??= m.Tasks[0];

        return new BotVerdict
        {
            Outcome = pick.Id,
            Report = m.Report.Said,
            Speech = Words(pick, m.Guest),
            Reason = $"by rule ({because}): {pick.Kind.ToString().ToLowerInvariant()} first, as the town's state reads"
        };
    }

    public static string Words(BotTownTask task, string guild) =>
        task.Kind switch
        {
            Kinds.Clear => $"{guild}: make the ground at {task.Target.X},{task.Target.Y} safe again, within {Hours:0.#} hours.",
            Kinds.Guard => $"{guild}: keep watch inside our walls, {task.Need:0} minutes between you, within {Hours:0.#} hours.",
            _ => $"{guild}: bring {task.Need:0} {task.Material} to the market within {Hours:0.#} hours."
        };

    public bool Check(long sinceMs)
    {
        var guild = BotGuilds.Named(Guild);

        if (guild?.Members == null || Map == null)
        {
            return false;
        }

        var minutes = Math.Clamp(sinceMs / 60000.0, 0.0, 5.0);

        if (Kind != Kinds.Supply)
        {
            var key = BotQuad.Key(Map, Target);
            var there = 0;

            for (var i = 0; i < guild.Members.Count; i++)
            {
                if (guild.Members[i] is not BotMobile { Deleted: false, Alive: true } member || member.Map != Map)
                {
                    continue;
                }

                if (Kind == Kinds.Guard ? Town?.Bounds.Contains(member.Location) == true : BotQuad.Key(Map, member.Location) == key)
                {
                    there++;
                }
            }

            if (Kind == Kinds.Guard)
            {
                Done += there * minutes;
            }
            else
            {
                Presence += there * minutes;
            }
        }

        return Kind switch
        {
            Kinds.Clear => Presence >= ClearPresence && BotQuad.Safety(Map, Target) >= Goal,
            _ => Done >= Need
        };
    }
}
