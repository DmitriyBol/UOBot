using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Friendships and feuds between two bots, as one number each way: what this bot thinks of that one, from −100 to +100.
///
/// <para>
/// <b>Patrick's idea of 07.10.2026: "personal friendships and feuds between bots — the one who saved you is a friend who
/// answers your cry first; the one who killed you in a war is a personal enemy you seek out for a duel".</b> Fed only by
/// things that happened between the two, each from the hook where it happens: saved from a creature (+<see cref="Saved"/>,
/// and the rescuer +<see cref="Saver"/> back), raised where it fell (+<see cref="RaisedBy"/>), healed (+<see cref="Healed"/>),
/// a kill side by side in one company (+<see cref="Beside"/> each way), a fair sale hand to hand (+<see cref="Traded"/> each
/// way); killed in a war (<see cref="WarKilled"/>), murdered or robbed (<see cref="Murdered"/>), made to pay under threat
/// (<see cref="Extorted"/>). A duel fought out settles a feud (+<see cref="Settled"/> each way).
/// </para>
///
/// <para>
/// <b>Everything decays, slowly, in the shard's running time</b>: every <see cref="DecayEveryMs"/> each regard moves
/// <see cref="DecayShare"/> of the way to nought (at least <see cref="DecayLeast"/>); a grudge decays at half that pace in a
/// vindictive bot and twice it in a forgiving one. A regard under one is forgotten. At most <see cref="MostTies"/> ties a bot,
/// the strongest kept: a new one that is weaker than all twelve is not written.
/// </para>
///
/// <para>
/// <b>What the ties change, each counted.</b> A friend (either way at <see cref="FriendAt"/> or more) is ranked before
/// everybody else when a dire cry picks who goes (<c>BotCry.Summon</c>) and when a mood line picks who answers
/// (<c>BotSocial.Heard</c>). An enemy (this bot's regard at <see cref="DuelAt"/> or less) may be sought out and challenged to a
/// duel (<see cref="BotGrudge"/>), and a bot that thinks <see cref="SpurnAt"/> or less of a speaker does not answer its mood.
/// Public <see cref="Regard(Mobile, Mobile)"/> is what the guild elections read.
/// </para>
/// </summary>
public static class BotTies
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTies));

    public static bool On { get; set; } = true;

    public static int MostTies { get; set; } = 12;

    public static double FriendAt { get; set; } = 40.0;

    public static double DuelAt { get; set; } = -60.0;

    public static double SpurnAt { get; set; } = -30.0;

    public static double Saved { get; set; } = 45.0;

    public static double Saver { get; set; } = 20.0;

    public static double RaisedBy { get; set; } = 45.0;

    public static double Raiser { get; set; } = 10.0;

    public static double Healed { get; set; } = 8.0;

    public static double Beside { get; set; } = 0.5;

    public static double Traded { get; set; } = 2.0;

    public static double WarKilled { get; set; } = -75.0;

    public static double Murdered { get; set; } = -80.0;

    public static double Extorted { get; set; } = -40.0;

    public static double Settled { get; set; } = 40.0;

    public static int DecayEveryMs { get; set; } = 600000;

    public static double DecayShare { get; set; } = 0.01;

    public static double DecayLeast { get; set; } = 0.2;

    public static int FriendPull { get; set; } = 1000;

    public static long Formed { get; private set; }

    public static long Forgotten { get; private set; }

    public static long Crowded { get; private set; }

    public static long FriendsMade { get; private set; }

    public static long EnemiesMade { get; private set; }

    public static long FriendsAnswered { get; private set; }

    public static long CriesAnswered { get; private set; }

    public static long FriendsFirstSocial { get; private set; }

    public static long Spurned { get; private set; }

    private static long _decayTick;

    private static bool _decaySeen;

    public static int Regard(Mobile a, Mobile b) =>
        a == null || b == null || ReferenceEquals(a, b) ? 0 : Regard(a.Name, b.Name);

    public static int Regard(string a, string b) =>
        (int)Math.Round(BotLives.Find(a)?.Tie(b)?.Regard ?? 0.0);

    private static double Exact(Mobile a, Mobile b) =>
        a == null || b == null ? 0.0 : BotLives.Find(a.Name)?.Tie(b.Name)?.Regard ?? 0.0;

    public static bool Friends(Mobile a, Mobile b) =>
        On && a is BotMobile && b is BotMobile && !ReferenceEquals(a, b) && (Exact(a, b) >= FriendAt || Exact(b, a) >= FriendAt);

    public static bool Enemy(Mobile a, Mobile b) => On && Exact(a, b) <= DuelAt;

    public static BotTie Move(Mobile a, Mobile b, double by, string why)
    {
        if (!On || a is not BotMobile || b is not BotMobile || ReferenceEquals(a, b) || by == 0.0)
        {
            return null;
        }

        var life = BotLives.Of(a);

        if (life == null || string.IsNullOrEmpty(b.Name))
        {
            return null;
        }

        var tie = life.Tie(b.Name);

        if (tie == null)
        {
            if (life.Ties.Count >= Math.Max(1, MostTies))
            {
                var weakest = 0;

                for (var i = 1; i < life.Ties.Count; i++)
                {
                    if (Math.Abs(life.Ties[i].Regard) < Math.Abs(life.Ties[weakest].Regard))
                    {
                        weakest = i;
                    }
                }

                if (Math.Abs(life.Ties[weakest].Regard) >= Math.Abs(by))
                {
                    Crowded++;

                    return null;
                }

                life.Ties.RemoveAt(weakest);
                Forgotten++;
            }

            tie = new BotTie { Name = b.Name };
            life.Ties.Add(tie);
            Formed++;
        }

        var was = tie.Regard;

        tie.Regard = Math.Clamp(was + by, -100.0, 100.0);

        if (was < FriendAt && tie.Regard >= FriendAt)
        {
            FriendsMade++;

            logger.Information("{Name} counts {Other} a friend now: regard {Was:F0} to {Now:F0} — {Why}", a.Name, b.Name, was, tie.Regard, why);
        }
        else if (was > DuelAt && tie.Regard <= DuelAt)
        {
            EnemiesMade++;

            logger.Information("{Name} counts {Other} a personal enemy now: regard {Was:F0} to {Now:F0} — {Why}", a.Name, b.Name, was, tie.Regard, why);
        }

        life.Rewound();

        return tie;
    }

    public static void Mutual(Mobile a, Mobile b, double by, string why)
    {
        Move(a, b, by, why);
        Move(b, a, by, why);
    }

    public static int CryRank(Mobile helper, Mobile crier, int off) => Friends(helper, crier) ? off - FriendPull : off;

    public static void Answered(Mobile helper, Mobile crier)
    {
        CriesAnswered++;

        if (!Friends(helper, crier))
        {
            return;
        }

        FriendsAnswered++;

        logger.Information("{Helper} answers its friend {Crier}'s cry first: regard {Regard} and {Back} back", helper?.Name, crier?.Name, Regard(helper, crier), Regard(crier, helper));
    }

    public static double SocialRank(Mobile speaker, Mobile near, double distance, bool count = true)
    {
        if (!Friends(speaker, near))
        {
            return distance;
        }

        if (count)
        {
            FriendsFirstSocial++;
        }

        return distance - FriendPull;
    }

    public static bool Spurns(Mobile near, Mobile speaker)
    {
        if (!On || Exact(near, speaker) > SpurnAt)
        {
            return false;
        }

        Spurned++;

        return true;
    }

    public static (BotTie Friend, BotTie Enemy) Strongest(BotLife life)
    {
        BotTie friend = null;
        BotTie enemy = null;

        if (life == null)
        {
            return (null, null);
        }

        for (var i = 0; i < life.Ties.Count; i++)
        {
            var tie = life.Ties[i];

            if (tie.Regard >= FriendAt && (friend == null || tie.Regard > friend.Regard))
            {
                friend = tie;
            }

            if (tie.Regard <= SpurnAt && (enemy == null || tie.Regard < enemy.Regard))
            {
                enemy = tie;
            }
        }

        return (friend, enemy);
    }

    public static int Decay(long now)
    {
        if (!_decaySeen)
        {
            _decaySeen = true;
            _decayTick = now;

            return 0;
        }

        if (!On || now - _decayTick < Math.Max(60000, DecayEveryMs))
        {
            return 0;
        }

        _decayTick = now;

        var forgotten = 0;

        foreach (var life in BotLives.All)
        {
            if (life.Ties.Count == 0)
            {
                continue;
            }

            var grudge = (life.Traits & BotTrait.Vindictive) != 0 ? 0.5
                : (life.Traits & BotTrait.Forgiving) != 0 ? 2.0
                : 1.0;

            for (var i = life.Ties.Count - 1; i >= 0; i--)
            {
                var tie = life.Ties[i];
                var pace = tie.Regard < 0.0 ? grudge : 1.0;
                var step = Math.Max(DecayLeast, Math.Abs(tie.Regard) * DecayShare) * pace;

                tie.Regard = tie.Regard > 0.0 ? Math.Max(0.0, tie.Regard - step) : Math.Min(0.0, tie.Regard + step);

                if (Math.Abs(tie.Regard) < 1.0)
                {
                    life.Ties.RemoveAt(i);
                    forgotten++;
                }
            }

            life.Rewound();
        }

        Forgotten += forgotten;

        return forgotten;
    }

    public static (int Friends, int Enemies, int Ties) Census()
    {
        var friends = 0;
        var enemies = 0;
        var ties = 0;

        foreach (var life in BotLives.All)
        {
            for (var i = 0; i < life.Ties.Count; i++)
            {
                ties++;

                if (life.Ties[i].Regard >= FriendAt)
                {
                    friends++;
                }
                else if (life.Ties[i].Regard <= DuelAt)
                {
                    enemies++;
                }
            }
        }

        return (friends, enemies, ties);
    }

    public static string Describe()
    {
        var (friends, enemies, ties) = Census();

        return !On
            ? "ties are off"
            : $"{ties} ties held ({friends} friendships at {FriendAt:F0}+ and {enemies} personal enmities at {DuelAt:F0}- one way each, at most {MostTies} a bot); "
              + $"{Formed} formed, {FriendsMade} friends made, {EnemiesMade} enemies made, {Forgotten} forgotten or pushed out, {Crowded} too weak to write; "
              + $"{FriendsAnswered} of {CriesAnswered} bots sent to a dire cry went as the crier's friend, {FriendsFirstSocial} friends put first to answer a mood, {Spurned} moods not answered for a grudge";
    }

    public static void Forget()
    {
        Formed = 0;
        Forgotten = 0;
        Crowded = 0;
        FriendsMade = 0;
        EnemiesMade = 0;
        FriendsAnswered = 0;
        CriesAnswered = 0;
        FriendsFirstSocial = 0;
        Spurned = 0;
        _decaySeen = false;
    }
}
