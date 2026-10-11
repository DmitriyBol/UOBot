using System;
using System.Collections.Generic;
using Server.Items;
using Server.Spells;
using Server.Spells.Eighth;
using Server.Targeting;

namespace Server.BotAI.V2;

/// <summary>
/// <b>The company's half of raising its fallen (02.10.2026).</b> What raises and how is <see cref="BotRaise"/>'s; this is who goes,
/// when, and how the company holds still for it.
///
/// <para>
/// A member that dies leaves the roster in the same second (<c>BotMobile.OnDeath</c> → <c>BotSquads.Leave</c> → <see cref="Detach"/>),
/// so the company keeps its fallen apart, on <see cref="_fallen"/>, for <see cref="BotRaise.HoldMs"/>. On every beat it looks for the
/// member best able to raise each of them — the engine's chance on one try, and the nearer at equal odds — within
/// <see cref="BotRaise.Reach"/>; the leader as much as anybody, because in a company of two the survivor is the leader. That member's
/// walk is written into the same slot a station is (<see cref="BotJourney.Rebase(Map, Point3D, BotArrival, string)"/>), its own
/// work is not advanced while it is at the body (<c>BotWill.Advance</c>), and the company hands it no station, no target and no
/// patient until the body is up or it is given up on: forty seconds, twenty tries, or flight. While the company's enemy stands within
/// <see cref="BotRaise.ClearReach"/> of the body the raising waits, and the revival is held for it.
/// </para>
/// </summary>
public sealed partial class BotSquad
{
    private sealed class FallenOne
    {
        public BotMobile Ghost;

        public long Fell;

        public Point3D Where;

        public Map Map;

        public BotMobile Raiser;

        public BotRaise.Means Means;

        public double Chance;

        public long Claimed;

        public int Tries;

        public int AllTries;

        public long Wound;

        public readonly HashSet<Serial> Passed = [];

        public bool EverAble;

        public bool EverNear;

        public bool Waited;

        public bool Refused;
    }

    private readonly List<FallenOne> _fallen = [];

    private int _raisings;

    private void KeepFallen(BotMobile ghost)
    {
        if (!BotRaise.Running || Disbanded || ghost is not { Deleted: false } || ghost.Map is not { } map || map == Map.Internal)
        {
            return;
        }

        for (var i = 0; i < _fallen.Count; i++)
        {
            if (ReferenceEquals(_fallen[i].Ghost, ghost))
            {
                return;
            }
        }

        _fallen.Add(new FallenOne { Ghost = ghost, Fell = ghost.FellTick, Where = ghost.Location, Map = map });

        BotRaise.NoteFallen();
        BotRaise.Await(ghost, this);
    }

    internal bool Raising(Mobile body)
    {
        if (body == null)
        {
            return false;
        }

        for (var i = 0; i < _fallen.Count; i++)
        {
            if (ReferenceEquals(_fallen[i].Raiser, body))
            {
                return true;
            }
        }

        return false;
    }

    private bool RaiseUnderway
    {
        get
        {
            for (var i = 0; i < _fallen.Count; i++)
            {
                if (_fallen[i].Raiser != null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void Raise()
    {
        if (_fallen.Count == 0)
        {
            return;
        }

        var now = Core.TickCount;

        for (var i = _fallen.Count - 1; i >= 0; i--)
        {
            var down = _fallen[i];

            if (Attend(down, now, out var why))
            {
                if (why != null)
                {
                    BotRaise.NoteUnraised(why.Value);
                }

                StepAway(down);
                BotRaise.Done(down.Ghost);
                _fallen.RemoveAt(i);
            }
        }
    }

    private static void StepAway(FallenOne down)
    {
        var raiser = down.Raiser;

        down.Raiser = null;
        down.Tries = 0;
        down.Wound = 0;

        if (raiser is { Deleted: false } && raiser.Spell is ResurrectionSpell spell)
        {
            if (raiser.Target is { } cursor)
            {
                cursor.Cancel(raiser, TargetCancelType.Canceled);
            }
            else
            {
                spell.Disturb(DisturbType.Hurt);
            }
        }
    }

    private bool Attend(FallenOne down, long now, out BotRaise.Unraised? why)
    {
        why = null;

        var ghost = down.Ghost;

        if (ghost is not { Deleted: false } || ghost.Alive || ghost.FellTick != down.Fell || ghost.Map != down.Map
            || ghost.Location != down.Where || now - down.Fell >= BotRaise.HoldMs)
        {
            why = !down.EverAble ? BotRaise.Unraised.NobodyAble
                : !down.EverNear ? BotRaise.Unraised.NobodyNear
                : down.AllTries > 0 ? BotRaise.Unraised.TriedAndFailed
                : down.Waited ? BotRaise.Unraised.FightHeld
                : BotRaise.Unraised.CarriedHome;

            return true;
        }

        if (down.Refused)
        {
            why = BotRaise.Unraised.Ground;

            return true;
        }

        if (BotRaise.Unholy(ghost))
        {
            why = BotRaise.Unraised.Outlawed;

            return true;
        }

        if (!BotRaise.Takes(ghost))
        {
            why = BotRaise.Unraised.Ground;

            return true;
        }

        if (_raisings >= BotRaise.PerCompany || !BotDelveParty.MayRaise(ghost))
        {
            why = BotRaise.Unraised.Spent;

            return true;
        }

        if (down.Raiser != null && !Keeps(down, now))
        {
            down.Passed.Add(down.Raiser.Serial);
            StepAway(down);
            BotRaise.NoteHanded();
        }

        if (down.Raiser == null && !Choose(down, now))
        {
            if (!down.EverAble && now - down.Fell >= BotRaise.JudgeMs)
            {
                why = BotRaise.Unraised.NobodyAble;

                return true;
            }

            return false;
        }

        return Work(down, now);
    }

    private bool Keeps(FallenOne down, long now)
    {
        var raiser = down.Raiser;

        if (raiser is not { Deleted: false, Alive: true } || raiser.Map != down.Map || !Has(raiser))
        {
            return false;
        }

        if (raiser.Resolve?.Deed is BotBolt or BotSalve || BotMend.Share(raiser) < DyingShare)
        {
            return false;
        }

        if (now - down.Claimed >= BotRaise.ClaimMs || down.Tries >= BotRaise.MaxTries)
        {
            return false;
        }

        if (down.Wound != 0 || raiser.Spell is ResurrectionSpell)
        {
            return true;
        }

        return BotRaise.Best(raiser, out _, out _) == down.Means;
    }

    private bool Choose(FallenOne down, long now)
    {
        BotMobile best = null;
        var bestMeans = BotRaise.Means.None;
        var bestChance = 0.0;
        var bestScore = 0.0;
        var bestAway = int.MaxValue;
        var able = false;

        for (var i = 0; i < _members.Count; i++)
        {
            if (_members[i].Self is not BotMobile { Deleted: false, Alive: true } body || ReferenceEquals(body, down.Ghost)
                || body.Map != down.Map || down.Passed.Contains(body.Serial) || Raising(body) || BotRegard.Hostile(body, down.Ghost))
            {
                continue;
            }

            var means = BotRaise.Best(body, out var chance, out _);

            if (means == BotRaise.Means.None)
            {
                continue;
            }

            able = true;

            var away = Math.Max(Math.Abs(body.X - down.Where.X), Math.Abs(body.Y - down.Where.Y));

            if (away > BotRaise.Reach || body.Resolve?.Deed is BotBolt or BotSalve || BotMend.Share(body) < DyingShare)
            {
                continue;
            }

            var score = means == BotRaise.Means.Bandage || body.Mana >= BotRaise.Mana ? chance : chance * 0.5;

            if (score < bestScore || score == bestScore && away >= bestAway)
            {
                continue;
            }

            best = body;
            bestMeans = means;
            bestChance = chance;
            bestScore = score;
            bestAway = away;
        }

        down.EverAble |= able;
        down.EverNear |= best != null;

        BotRaise.Looked(down.Ghost, best != null);

        if (best == null)
        {
            return false;
        }

        if (FightNear(down))
        {
            return false;
        }

        down.Raiser = best;
        down.Means = bestMeans;
        down.Chance = bestChance;
        down.Claimed = now;
        down.Tries = 0;
        down.Wound = 0;

        return true;
    }

    private bool FightNear(FallenOne down)
    {
        if (Stance != BotSquadStance.Fighting || Focus is not { Deleted: false, Alive: true } foe || foe.Map != down.Map
            || Math.Max(Math.Abs(foe.X - down.Where.X), Math.Abs(foe.Y - down.Where.Y)) > BotRaise.ClearReach)
        {
            return false;
        }

        down.Waited = true;
        BotRaise.NoteFightWait();

        return true;
    }

    private bool Work(FallenOne down, long now)
    {
        var raiser = down.Raiser;
        var ghost = down.Ghost;

        if (raiser == null)
        {
            return false;
        }

        BotRaise.Looked(ghost, true);

        if (!raiser.InRange(down.Where, 1) || !raiser.InLOS(ghost))
        {
            var journey = raiser.Journey;

            if (journey != null && (!journey.Active || journey.Target != down.Where))
            {
                journey.Rebase(down.Map, down.Where, BotArrival.Beside, "raise");
            }

            return false;
        }

        if (down.Wound == 0 && raiser.Spell == null && FightNear(down))
        {
            down.Claimed = now;

            return false;
        }

        ghost.Warmode = true;

        if (down.Means == BotRaise.Means.Bandage)
        {
            return Wind(down, now, raiser, ghost);
        }

        if (raiser.Target is { } cursor && raiser.Spell is ResurrectionSpell cast)
        {
            var used = cast.Scroll;
            var before = used?.Amount ?? 0;
            var mana = raiser.Mana;

            cursor.Invoke(raiser, ghost);

            var took = used != null
                ? used.Deleted || used.Amount < before
                : raiser.Mana <= mana - BotRaise.Mana / 2;

            down.Tries++;
            down.AllTries++;
            BotRaise.NoteTry(took);

            return took && Stood(down, raiser);
        }

        if (raiser.Spell != null)
        {
            return false;
        }

        if (raiser.Target is { } foreign)
        {
            foreign.Cancel(raiser, TargetCancelType.Canceled);

            return false;
        }

        if (raiser.Mana < BotRaise.Mana)
        {
            BotRaise.NoteManaWait();

            return false;
        }

        var scroll = down.Means == BotRaise.Means.Scroll ? raiser.Backpack?.FindItemByType<ResurrectionScroll>() : null;

        if (down.Means == BotRaise.Means.Scroll && scroll == null)
        {
            down.Tries = BotRaise.MaxTries;

            return false;
        }

        var spell = SpellRegistry.NewSpell(BotRaise.Spell, raiser, scroll);

        if (spell != null && spell.Cast())
        {
            BotRaise.NoteCast();
        }
        else
        {
            down.Tries++;
        }

        return false;
    }

    private bool Wind(FallenOne down, long now, BotMobile raiser, BotMobile ghost)
    {
        if (down.Wound == 0)
        {
            var cloth = raiser.Backpack?.FindItemByType<Bandage>();

            if (cloth == null)
            {
                down.Tries = BotRaise.MaxTries;

                return false;
            }

            cloth.Consume();
            down.Wound = now + BotRaise.BandageMs(raiser);

            return false;
        }

        if (now < down.Wound)
        {
            return false;
        }

        down.Wound = 0;
        down.Tries++;
        down.AllTries++;

        var healing = raiser.Skills[SkillName.Healing].Value;
        var anatomy = raiser.Skills[SkillName.Anatomy].Value;
        var took = raiser.InRange(ghost, Bandage.Range) && raiser.InLOS(ghost) && healing >= 80.0 && anatomy >= 80.0
                   && (healing - 68.0) / 50.0 > Utility.RandomDouble();

        BotRaise.NoteTry(took);

        if (!took)
        {
            return false;
        }

        raiser.CheckSkill(SkillName.Anatomy, 0.0, 120.0);
        raiser.CheckSkill(SkillName.Healing, 0.0, 120.0);

        return Stood(down, raiser);
    }

    private bool Stood(FallenOne down, BotMobile raiser)
    {
        var ghost = down.Ghost;

        if (!BotRaise.Rise(ghost, raiser, down.Means, down.Chance, down.AllTries, this))
        {
            down.Refused = true;

            return false;
        }

        _raisings++;
        down.Raiser = null;

        if (!Disbanded && Map is { } map
            && BotSquads.Enrol(this, ghost, map, Anchor, $"raised where it fell by {raiser.Name}, back in company {Id}", force: true))
        {
            BotRaise.NoteRejoined();
        }

        return true;
    }
}
