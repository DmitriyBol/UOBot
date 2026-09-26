using System;

namespace Server.BotAI.V2;

/// <summary>What an errand on the board asks for.</summary>
public enum BotQuestKind
{
    Kill,

    Gather,

    Scout
}

/// <summary>
/// One errand on the board: who posted it, what it asks, what it pays, and who holds it. See <see cref="BotQuests"/>.
/// </summary>
public sealed class BotQuest
{
    public int Id { get; init; }

    public BotQuestKind Kind { get; init; }

    public string What { get; init; }

    public Type Type { get; init; }

    public int Amount { get; init; }

    public int Done { get; set; }

    public Map Map { get; init; }

    public Point3D Where { get; init; }

    public int Held { get; set; }

    public string By { get; init; }

    public long PostedTick { get; set; }

    public BotMobile Taker { get; set; }

    public long TakenTick { get; set; }

    public int LetGo { get; set; }

    public string LetGoBecause { get; set; }

    public BotMobile LastTaker { get; set; }

    public long LetGoTick { get; set; }

    public bool Open => Taker == null;

    public bool Fulfilled => Kind == BotQuestKind.Kill ? Done >= Amount : Done > 0;

    public string Tell() =>
        Kind switch
        {
            BotQuestKind.Kill => $"#{Id}: kill {Amount} {What}{Near()}, {Done} down, {Held}gp ({By}){Holder()}",
            BotQuestKind.Gather => $"#{Id}: bring {Amount} {What} to ({Where.X}, {Where.Y}), {Held}gp ({By}){Holder()}",
            _ => $"#{Id}: scout ({Where.X}, {Where.Y}), {Held}gp ({By}){Holder()}"
        };

    private string Near() => Where == Point3D.Zero ? " anywhere" : $" near ({Where.X}, {Where.Y})";

    private string Holder() => Taker is { Deleted: false } ? $", in {Taker.Name}'s hands" : "";
}
