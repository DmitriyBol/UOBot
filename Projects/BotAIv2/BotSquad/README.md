# Squads

A standing company: a leader, a few followers, a formation, scouting, and dividing the spoils.

> **This ran, and the note that used to stand here saying it never had is why the top of `MAP.md` warns
> against reading these files for facts.** `BotSquads.Form` has three callers now — a prowl that finds
> something one bot cannot take, a patrol, and the Baron's harrowing — and companies form and disband on
> every session. What follows was written before that and describes intent; where it disagrees with the
> `Companies:` line in a live log, the log is right.

| File | What is in it |
|---|---|
| `BotSquad.cs` | the squad itself: members, leader, focus, three states, anchor and axis |
| `BotSquads.cs` | the registry: forming, joining, leaving, disbanding, its own beat once a second |
| `BotFormation.cs` | where each one stands — derived, not assigned |
| `BotScatter.cs` | scouting: knots of 2–3 ten tiles apart |
| `BotSpoils.cs` | dividing by worth, gold by sum |
| `BotSquadMember.cs` | what a squad needs from a bot. Four things |
| `BotSquadConfig.cs` | `Configuration/bot-squad.json` — numbers only |
| `BotSquadModule.cs` | module, phase `World`, requires `Classes` |
| `BotEnlist.cs` | falling in with a company that is already fighting, rather than starting a fight of your own beside it |
| `BotPatrol.cs` | offers a captain the worst square on the island and a company to take there |
| `BotSweep.cs` | a company called together for a place rather than for a creature, and kept together until the place stops killing people |

## One kind of group, not two

In v1 there were two, and both were called "a group" — including in the documentation.

| | warband (`BotSocial`) | band (`BotBands`) |
|---|---|---|
| where it came from | a bot met something it could not take alone | the system raised one every minute |
| size | however many arrived | exactly five |
| a medic | if you were lucky | mandatory, and the leader |
| goal | one creature | a square of the map |
| end | the creature died | the square was cleared |

Here there is one: it forms, it lives, it marches, it scouts, it fights, it divides, and it moves on.

## Collective intelligence is arithmetic, not mail

The most valuable thing v1 found about group behaviour was buried in how it scattered across a square:

> Handing out is a **division, not a search**: the bots sort by serial, the ground is cut into a grid, each takes
> the cell at its own index. Everybody derives the same answer from the same facts, so **nobody has to be told
> anything**, and two of them never pick the same patch.

The whole design comes from that. The shared facts are the membership, the anchor and the threat axis. Each bot's
station, its scouting patch and its share of the spoils are **derived** from them, identically by everybody. No
desynchronisation, no orphaned assignments, no message queue.

The only things that remain messages are the things that are **events**: "I am being hit" (`BotSquads.Note`) and
"get off this tile" (`IBotAside`).

## No state is "stand and wait"

Three states, and in each one a bot is going somewhere:

| State | Anchor | What they do |
|---|---|---|
| `Marching` | the leader | hold formation around it |
| `Scouting` | the leader | spread out in knots of 2–3 to ten tiles |
| `Fighting` | **whoever is being hit** | the formation moves onto them, so everybody is already heading there |

**This rule was paid for in bodies.** In v1 both mustering (`DoMuster`) and settling up (`DoSettle`) began with
`Warmode = false` and standing still. And the muster point was almost always already inside the muster radius, so
"go to the muster" degenerated into "stand and look at the target". A lich strikes from eight tiles and **never
enters contact** — so not one rung of the survival ladder ever fired, because every one of them tested for
contact. Six bots stood politely in a circle while it killed them one at a time.

## Rescue is not a mechanic

There is no separate rescue. A squad has an anchor, and everybody's station is derived from the anchor. When a
member is attacked the anchor moves **onto them** — and everybody is already heading there, deciding nothing and
calling nobody. Including whoever is in a distant scouting knot at that moment: their patch was derived from the
same anchor.

The same thing answers "why is scouting safe". Spreading out is only dangerous in a world where nobody hears a
shout.

## Formation: any shape, but not any order

The shape cannot be dictated — "any shape at all". The invariant is the **order along the threat axis**:

| Role | Rank | Where that is |
|---|---|---|
| `Melee` | **+2** | in front of the anchor |
| `Ranged` | 0 | on the anchor |
| `Caster`, `Medic` | −1 | behind it |
| `Producer` | −2 | at the back |

The depth is only four tiles, on purpose. A squad has to stay within five, and a deeper formation looks good on
paper and produces a back rank that cannot hear its own front rank.

Within a rank: 0, +1, −1, +2, −2 at two tiles apart — the rank grows away from the axis in both directions rather
than trailing off in one, and there is room to walk between the files instead of asking somebody to move.

**It is built from the leader, and "in front" means in front of the leader.** If the leader is an archer, the
melee bots still take the front rank: in front of *it*. That is not a defect to be fixed but the behaviour that
was asked for.

Every station is checked twice before it is handed out, and both checks come out of v1's bill: the point has to be
somewhere a body **can stand** (height taken from the ground next to the anchor, never from a wide window — a wide
window in a built-up world returns a roof), and somewhere the bot **can reach**, verified with the same pathfinder
that will then walk it. v1's muster points were not checked at all and landed inside houses and behind the target.

## The gap a mage was standing in

The case as put: two trees with a gap between them, a mage in the gap, an enemy beyond the trees. The mage dies
there in seconds and a melee bot in minutes. So the gap belongs to the melee bot.

`BotFormation.OutranksFor` answers that with **a question about role, not about the path**: the asker's rank is
ahead of the occupant's, so the occupant yields. Nobody recognised a chokepoint; ground nearer the threat simply
belongs to whoever stands nearer the threat, and the formation has already said who that is.

It does not work the other way: a mage cannot move a melee bot. The melee bot is standing exactly where it
belongs.

The mage yields **away from the focus** (`YieldAwayFrom`) — so it does not merely vacate the tile, it vacates it in
the direction it wanted to go anyway: back behind the line, onto its own station.

## Dividing: by worth, gold by sum

v1 dealt round the circle by item count and called it "fair enough that nobody has grounds to complain" — but one
bot walked away with a katana and another with a rotten skull, and the gold went in one heap to whoever opened the
corpse.

Here: gold is cut by sum, and everything else goes to **whoever has had least**, starting with the most valuable
thing. That last detail is what makes it work: hand out the valuable things first and the small change can even
out the difference; hand them out last and it cannot.

Worth comes from an external function, `BotSpoils.Worth`, which the economy will provide. Without it everything is
worth the same and dividing degenerates into counting items — that is, into v1's rule arrived at as a **limiting
case** rather than as a design. It is now a one-line fix: `BotAuction.Worth(type, 0)` is a price.

**It is settled immediately, and the dead get nothing.** v1 held the corpse undivided until every fallen bot had
been resurrected, with the survivors standing around it — and standing around is what killed the six in a circle
round the lich. A division that waits is a state in which the squad goes nowhere, and there are no such states
here. It is a real loss for whoever died in a won fight, and it is cheaper than a second one.

**Height counts as much as distance.** In v1 every range check ignored it, and a squad member three tiles away and
twenty units up — on a crypt roof — counted as present in a fight it could not take part in.

## Leaving a fight is measured in damage

Not by a timer and not by proximity. While the target's health goes **below its previous low for this fight**, the
squad is working. Ninety seconds without a new low: withdraw. Four minutes: the ceiling.

The low for the fight, not the current health: otherwise regeneration between blows reads as progress.

**Why this is mandatory.** In v1 the fighting state had **no** time-based exit at all — it ended only in victory or
in the death of everybody. Any target that could not be killed held its company for ever, and taking part in a
squad outranks personal business. Twelve bots out of twenty were permanently "assisting": not idle but tied up.
The economy worked; there was nobody left to take part in it.

And the same rule applied to a single bot needed no code: a member that cannot reach the target loses the fighting
errand by itself — the errand queue drops it after a hundred fruitless attempts at a step, and what is underneath
it is a station.

## The size cap lives in one place

`BotSquads.Join` is the only entrance, and the check is there. In v1 the cap was checked only on the recruiting
path, while a bot that had stumbled onto the same target and called for help was added **without a check**:
companies with a declared maximum of five held twelve.

And a squad is not created by somebody who cannot fight: in v1 the call stood above "my health is going", so a bot
on its last few points announced a company it could not join, it found nobody able and disbanded in the same tick
— and the same bot posted it again.

## What the bot provides

`IBotSquadMember` — four members: `Self`, `Class`, `Journey`, `Squad`, plus `AbleToFight` from `IBotAlly`. And two
calls:

| Where | What |
|---|---|
| `OnDamage` | `BotSquads.Note(this, from)` — this is what makes the collective mind work |
| `StepAsideFor` | ask `BotSquads.ShouldYield(this, asker)`, step in `YieldAwayFrom` |

Dividing is called by whoever opens the corpse: `BotSpoils.Share(squad, this, corpse)`.

## What to check at home

In the log at startup:

```
Squads ready: up to 5 to a squad beating every 1000ms; knots of 3 at 10 tiles while sweeping;
a fight is broken off after 90000ms without the target's health falling, and capped at 240000ms
```

| Number in the summary | Healthy | What it says |
|---|---|---|
| `squads standing` | steady | jumping means squads are falling apart and re-forming |
| `formed` / `disbanded` | close together | a gap means squads are leaking |
| `times one of them was set upon` | rising | the collective mind is working |
| `tiles given up` | non-zero | yielding in gaps works; zero means either no disputes or roles are not being read |
| `corpses divided` | equal to the number of wins | dividing is not running |

With a client:

1. **Formation.** Form a squad of a melee bot, an archer and a mage; lead it. The order along the direction of
   travel must be melee in front and mage behind. Turn it towards an enemy — the formation must turn with the
   axis.
2. **An archer leader.** Form a squad whose leader is an archer. The melee bots must stand **in front of it**.
3. **The gap.** Put a mage in a narrow gap, an enemy beyond it, a melee bot behind the mage. The mage must move
   back, not towards the enemy.
4. **Rescue.** Spread the squad out to scout (10 tiles) and hit one of them. Everybody must converge on it.
5. **An immortal target.** Give the squad something that cannot be killed. After ninety seconds it must withdraw
   — not stand there until the shard restarts, as in v1.
6. **Dividing.** Kill something with gold and a couple of items with three bots nearby. The gold must split three
   ways rather than going to one.

## What is not here

- **Who forms a squad, and when.** `Form` exists and nobody calls it. In v2's terms that is a proposer on the
  `Free` rung that nobody has written, and joining will be an obligation with a price (a share of the takings)
  rather than a muster by order.
- **Agreement to fight.** `OurPower` sums everybody able nearby, but none of them is obliged to join. In v1
  agreement arose from everybody computing the same number and seeing the same thing; the same holds here, but it
  can only be verified live.
- **Prices for items.** `BotSpoils.Worth` is unfilled. It no longer has to be — `BotAuction.Worth` exists.
- **The leader stands on the anchor while scouting** and does not take a patch of its own. Its patch is computed
  and unused — stable and slightly wasteful; if it matters, the leader can be given the centre.
