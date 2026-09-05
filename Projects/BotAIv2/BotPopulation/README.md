# Population: the bot, and the beat

The object six subsystems were waiting for, and the clock that drives them. **This is the folder from which the
project first does anything at all.**

| File | What is in it |
|---|---|
| `BotMobile.cs` | the bot itself: a `PlayerMobile` that holds the bond, the journey and the resolve, and can take one turn |
| `BotBeat.cs` | the population's clock: one timer, each bot with its own due time |
| `BotPopulation.cs` | who exists: purging saved bots, birth, placement, raising the fallen |
| `BotPopulationConfig.cs` | `Configuration/bot-population.json` — the only configuration file with working values in it |
| `BotPopulationModule.cs` | module, phase `World`, requires `Classes`, `Movement`, `Will` |
| `BotHomeward.cs` | walking back to where the population lives, when there is nothing else to do and the bot is a long way from it |
| `BotProgress.cs` | what a bot has become, kept across restarts: its skills, its fame, its karma and its savings |
| `BotPurse.cs` | what a bot keeps in its pocket, and what it puts away the moment it is standing somewhere it can |
| `BotReclaim.cs` | going back for what death took |
| `BotStall.cs` | notices a bot that has stopped getting anywhere, and says so as an error |
| `BotUnload.cs` | going to the counter when the pack is getting heavy: coin into the account, everything spare onto the market |

---

## Why `PlayerMobile` and not `BaseCreature`

Because **this project's entire measure of work rests on that choice.** Inheriting from `PlayerMobile` gives
every player system without reimplementation: skill gain from use, fame and karma, a bank, criminal flags, a
lootable corpse. Skill gain is the important one: the decision layer appraises work by the skill it produced, and
here the skill is raised by the **engine**, after its own check. On a `BaseCreature` there would be nothing to
measure and the metric would have to be invented — and an invented metric is the first thing an agent breaks.

The price: a `PlayerMobile` has no think loop, so `BotBeat` provides the turn. And one more consequence carries
weight: nothing has a `NetState`, and `Mobile.Move` skips movement throttling when `NetState == null` — so a
bot's pace is set **only** by how often it is asked.

---

## One turn is three calls

```csharp
BotWill.Decide(this);
var result = BotWalk.Advance(this, Journey, Runs);
BotWill.Note(this, result);
```

The order is the whole contract between the subsystems. Decide first, because a decision may put a new
destination at the bottom of the journey. Then one step towards whatever is on the journey now. Then report
**what that step did**: two of its outcomes — "proved there is no way" and "not moving from this spot" — are facts
the decision layer has no other way of seeing, and caution about places is built out of them.

---

## The beat cannot be slower than the step

This is the tick's one measured defect in v1, and here it is closed by construction rather than by care.

There the period was `interval × phases` — two numbers in a configuration file whose product nobody checked — and
it drifted to 800 ms against a 400 ms step. The bots were not stuck, and nothing in the log looked wrong: they
simply walked at half a pedestrian's pace for a whole session, because you cannot step more often than you are
asked.

Here a bot's next due time is set from `BotWalk.StepDelayMs` at the moment its turn is handed out. **The product
that could be spoiled does not exist.** The timer's interval (100 ms) is only the schedule's resolution: how
precisely due times are honoured. The spread is not configurable either — bots are born with due times laid out
one step apart, so the step's work falls across the beats evenly. That was what "phases" were for, and this way
it needs no configuration and cannot be set to a value that strangles movement.

---

## Who is born, and where

The population is **rebuilt from configuration on every world load** rather than restored from the save. Not out
of laziness: a bot's state lives in objects — the bond, the journey, the ledger of takings — and none of them is
worth a save format. Loading a saved bot would mean building all of that again anyway, and the only thing that
would come back intact is its pack, so issuing the kit a second time would produce a bot with two of everything.
So bots arriving from a save are deleted.

`PurgeSaved` is **the only place in the assembly that walks the whole world**, and that is deliberate: the
shard's rules forbid enumerating `World.Mobiles` in favour of spatial queries, but there is no query for
"everywhere", this happens once per world load, and the alternative is a population that doubles on every
restart.

The birthplace is Britain — and specifically the point the **shard's own location list**
(`Data/Locations/felucca.json`) calls Britain, not a coordinate somebody remembers. That matters more than it
looks: the first bot that wants work sweeps the ground around itself for veins, fires and counters — so the
birthplace decides what the population is able to do at all.

What puts a bot on the ground is the engine rather than a guess: `Map.CanSpawnMobile`, the same test the shard's
own spawners use, including the region's opinion and a search for a floor within a couple of units of height. A
bot placed inside a wall is a bot whose first act is to prove there is no way out.

---

## The starting set — four bots

Two gatherers, a crafter and a warrior. The first three are born with a pickaxe (and the pickaxe is what decides
who can dig). **The warrior is there precisely because it has nothing to do:** it is living proof that "nothing
was worth doing" in the census is a fact about the world rather than a broken bot.

This is the only configuration file in the project written with working values rather than empty ones: elsewhere
"empty" means "keep the number the code has", and here an empty class list means "there are no bots at all".

---

## What a bot must be told rather than notice

A bot learns it has been hit from `OnDamage`, not by looking. A caster strikes from eight tiles and never enters
contact, so any check of the form "is there something next to me" never fires — in v1 six bots stood in a ring
while a lich killed them one at a time, and not one rung of their ladder ever triggered.

`BotThreat` then decides, and the decision is binary: put the journey aside and deal with it, or walk on and hit
back as you go. Standing still is not allowed at any numbers. The target is **the strongest** within range, not
whoever hit last: in a graveyard the nearest thing is always a skeleton, and v1's parties reliably went for the
skeleton while the lich kept casting.

The interruption is queued **once per target**: otherwise every blow would add another errand, and four blows
would fill the queue with the same fight four times over.

---

## Death and rising

Death: `BotBinding.TrimAmmunition` (the corpse is the only place where the ammunition count can still be
trimmed), then `BotWill.Died` counts how the work ended, then leaving the squad and clearing the journey.

Rising: a minute later `BotPopulation.Revive` brings the bot home and raises it — home first, because a ghost
raised where it was killed is a bot standing inside whatever killed it. The kit comes back in
`OnAfterResurrect`.

Somebody has to do this: nothing else in the project resurrects anybody, and a dead bot is a ghost for the rest
of the shard's life. The minute is not decoration — death has to cost something, and to a bot it costs time. The
decision layer counts the same price in its own units (`BotYield.DeathMinutes`); here it is in wall-clock.

---

## What to check with a client

1. `Population raised: 4 bots at (1592, 1680, 10) on Felucca; …` — and beside it the clock's line with the
   interval and the step.
2. Walk into Britain and look at them: dressed, carrying a pickaxe, moving. If they are standing still, look for
   `took on` in the log: either they took no work (and the reason is written there) or they took some and cannot
   reach it.
3. `finished mine: … N ingots put away` — the chain reached the end. That is the first real proof that all the
   subsystems are working at once.
4. Kill one and wait a minute: it should get up and come home with its kit.
5. `bots.population.enabled = false` — the shard becomes exactly what it was before this folder: subsystems
   loaded and nobody to use them. The cleanest A/B for "is this the bots or is this the shard".

---

## Known rough edges

**Nobody musters a squad.** `BotSquads.Form` is still called by nothing, so formation, scouting and sharing are
written and do not run. That is the next proposer, not a defect.

**Clothes are bound.** Not for looks: an unbound shirt is lootable, and a population that has died twice stands
around naked.

**The `Failing` rung is served** as of `BotMend/` — a bot whose health is going now mends itself above all other
work, spell first if it can cast and cloth if it cannot, and drinks a bottle below forty per cent.

**The engine surface was read out of the fork:** `PlayerMobile(Serial)` and the parameterless constructor,
`Serialize/Deserialize` (the fork is on the legacy pattern, not the source generator),
`OnDamage/OnDeath/OnAfterResurrect/OnAfterDelete`, `Mobile.Resurrect`, `MoveToWorld`, `Map.CanSpawnMobile` (the
overload that searches for Z), `Map.TryParse`, `World.Mobiles`,
`Race.RandomSkinHue/RandomHair/RandomHairHue`, `Utility.RandomDyedHue/RandomMinMax/Random`, `StatCap`,
`RawStr/RawDex/RawInt`, `HairItemID/HairHue`, `Warmode`, `Combatant` (a `Mobile`), `Female`, `Body`.
