# Combat: a decision, not a mechanic

There is only one question here — **is it worth stopping**. The fight itself is the engine's:
`Warmode = true` and `Combatant = target` are enough, and it swings the weapon on its own while the skill checks
inside it train skills exactly as they do for a live player. That is a first-version finding that never once
needed reworking.

| File | What is in it |
|---|---|
| `BotThreat.cs` | strength, threat, our side, and the decision to fight or walk on |
| `BotArms.cs` | whether a bot has anything in its hands, asked at the moment it matters |
| `BotBolt.cs` | getting away from whatever is killing it |
| `BotCry.cs` | somebody of ours is being killed and has said so out loud |
| `BotFugitive.cs` | offers a bot whose health is going the one thing that was missing from that rung: leaving; not again for a few seconds after getting clear unless something hits it (`CalmMs`) |
| `BotPeril.cs` | where the shard is dangerous, learned from the only two facts that actually say so: where bots are being hit, and where they are dying; two deaths lately keep ordinary work out (`Lethal`), three close the ground to everything but flight (`Closes`; four until build 65), each fading by half in forty minutes (twenty until build 65); `Tell` answers Argus's `peril` (build 50) |
| `BotRescue.cs` | going to somebody's aid, or hitting back at whatever is hitting you |
| `BotRescuer.cs` | offers a free bot the chance to go to somebody's aid; and `BotDefender`, hitting back at whatever is hitting a bot — never offered when the work in hand is already fighting that creature (`BotDeed.Foe`), nor when the odds round the bot already call the fight off (`Outnumbered`, build 51) |
| `BotBrawl.cs` | one bot fighting another as a piece of work — a duel, a robbery, a manhunt, a defence — pressed on the bot rather than won at auction; the caller's rule says when it is over; counted on the first beat, not in the constructor (build 69, 72) |
| `BotDuel.cs` | a pair of brawls with one rule between them: over when one is down, yields at a share of health or the clock runs out; both are disarmed when it is decided; `Between` makes a duel no crime (build 69) |

No configuration file: the only dial is `Tolerance`, and it lives in code for now. When there is a second one
there will be a file.

## What happens when a bot is hit on the road

```
hit
  │
  ├─ threat ≤ 1.5 × our strength  →  BotStand.Fight
  │     journey.Interrupt(map, the strongest, Beside, "fight")
  │     the destination stays in the queue underneath the fight
  │     the monster dies → Prune() lifts the fight → the bot is already looking at the destination
  │
  └─ threat > 1.5 × our strength  →  BotStand.Outmatched
        the obligation is not touched at all; the bot walks on and hits back as it goes
        the square where it nearly died → journey.AvoidDanger(...)
        same goal, different way
```

Three things here are worth noticing.

**Nobody "remembers" the destination.** It does not go into a variable and get restored — it simply stays in the
queue under the fight. Returning to it is not code: it is what happens when there is nothing on top any more. In
the first version fleeing was a **goal** and overwrote the errand, so a bot stood in a field after a fight with
no memory of what it had set out to do.

**The fight lifts itself.** `BotErrand.Lapsed` is true when the pursued creature is dead or deleted, and
`BotJourney.Prune()` is called first thing on every step. Nobody has to notice that the monster died.

**Fight whoever matters.** `Strongest`, not nearest. This cost the first version an evening: a party formed
against whatever was closest, and in a graveyard the closest thing is always a skeleton — six bots would declare
a party against a skeleton, commit instantly (a skeleton is trivial), go and chop it up, while the lich that was
actually killing them went on casting.

## How strength is computed

```
Power(m) = max(1, HitsMax) × (average damage + Magery / 2)
```

**Both halves are needed.** By health an ogre and a lich are nearly equal — 108 against 111 — while the lich
hits two and a half times harder and casts. Reference values measured in v1: **bot 1116, ogre 1080, lich 6937**.

**Maximum health, never current.** "Will we win this fight" does not become a different question because
somebody has taken a couple of hits. Mixing the two produced oscillation: a party took damage, its "strength"
collapsed, and it disbanded in the middle of a fight it was winning.

**Bystanders count at 0.4.** Adding everybody at full strength grossly overestimates a fight: you fight one
thing, the others arrive in turn, hold back, or never engage at all. At full count a graveyard's ordinary
residents were added to whatever had walked in, and a company that could have handled a newcomer refused,
because it was summing the scenery too.

**What cannot be reached still counts as a threat.** "I cannot hit it" and "it cannot hit me" are different
statements. The wraith on the crypt roof casts down perfectly well, and a bot that called the place safe on that
basis would stand in it and die.

## The threshold: why 1.5

This is a measured v1 number, not taste. What falls on each side:

| Case | Numbers | Decision |
|---|---|---|
| one bot against a spectre in a graveyard | 2120 against 1116 = **1.90** | walks on unless it has company |
| six against a lich | 6937 against 6696 = **1.04** | stop and fight |
| one bot against an ogre | 1080 against 1116 = **0.97** | fight |

A spectre is a caster: 53 health × (9 damage + 31 from magic). The graveyard undead are twice as strong as a
lone bot, and that is the shard's data rather than a defect — which is why a call for help there is honest.

## The decision is binary and immediate

No soft utility, no mustering, no hesitation. This comes straight out of the first version's most expensive
defect: six bots stood politely in a circle waiting for a muster that had already happened, while a lich killed
them one at a time. A caster strikes from eight tiles and **never enters contact**, so not one rung of the
survival ladder ever fired — every one of them tested for contact.

**Standing still is not allowed at any numbers.** Either fight or walk.

## Who reads the danger map

`BotPeril` is written from `OnDamage` and `OnDeath` and decays on a twenty-minute half-life. Until 14.09.2026 it was
read by captains choosing where to sweep, by the Baron choosing ground to harrow, and by the hunters' tie-break — and
not by the auction, whose only caution was each bot's own ledger, keyed by trade and remembering disappointment rather
than harm. That evening a field north-east of Britain filled with plague beasts and their spawn after a revel sent
companies into it, and twenty-eight bots died there in twenty minutes on offers to unload, sell, cook, mine and fetch
their own corpses that all read "safe 1.00".

So the dead are now kept apart from the blows, faded on the same half-life, and `BotPeril.Lethal` sums them over a
destination's square and the eight around it. `BotAppraisal` refuses work there at `KeepOutDeaths` (two), unless the
work goes where the fighting is on purpose or is a call from outside. Blows alone do not count: a hunting ground reads
high on them without anybody dying. Work in the square the bot already stands in is left alone, and running away is
never refused. The table is not saved, so a restart forgets the field until two more bots die in it.

## What is not here yet

- Who exactly `Combatant` is, and what to do when the target changes. In v1 the rule was: the target is whoever
  is **doing damage**, not whoever is nearest.
- Group agreement. `OurPower` sums everybody able nearby, but none of them is obliged to join. In v1 this was
  handled by the decision being **seen and taken by everybody who can see it** — agreement arises from everybody
  computing the same number.
- A check that a fight is even happening: v1 measured neither a timer nor proximity but the **target's damage** —
  while its health goes below its previous low for the fight, the party is working; 90 seconds without a single
  point, disband. A creature healing faster than it is hit is not a fight, it is a way of life.
- `BotHunt` now calls into this file to pick a quarry (`Best`, using `Power`, `Hostile` and `OurPower`), so the
  arithmetic here is what decides who goes looking for a fight, not just who stops for one.
