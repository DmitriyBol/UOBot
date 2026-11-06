using System;

namespace Server.BotAI.V2;

public enum BotTier
{

    None,

    Metal,

    Leather,

    Ore
}

public readonly record struct BotLeaning(
    string Name,
    SkillName? Skill,
    int Weight,
    double Low = 0.0,
    double High = 0.0,
    bool Weapon = false,
    BotTier Tier = BotTier.None,
    (SkillName Skill, double Floor)[] Keeps = null
)
{

    public static BotLeaning Even(int weight) => new("even", null, weight);

    public bool IsEven => Skill == null && !Weapon;

    public double? Floor(SkillName skill)
    {
        var keeps = Keeps;

        if (keeps == null)
        {
            return null;
        }

        for (var i = 0; i < keeps.Length; i++)
        {
            if (keeps[i].Skill == skill)
            {
                return Math.Max(0.0, keeps[i].Floor);
            }
        }

        return null;
    }
}
