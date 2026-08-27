# Harvest: the first work in the world

Dig, smelt, bank it. The first subsystem that gives the population something to want — and the test of whether
the brain works at all.

| File | What is in it |
|---|---|
| `BotOre.cs` | ore: what is in a hill, what digs it, one swing, and what ore becomes in a fire |
| `BotGround.cs` | what the population knows about places: one sweep yields veins, fires and counters |
| `BotDig.cs` | an obligation with three legs: vein → fire → counter |
| `BotMiner.cs` | the proposer: one offer to whoever has a pickaxe |
| `BotHarvestConfig.cs` | `Configuration/bot-harvest.json` |
| `BotHarvestModule.cs` | module, phase `World`, requires `Classes` and `Will` |

---

## Why a chain and not just "dig"

**Ore is worth nothing to anybody.** No counter buys it and no bot wants it, and a miner that comes home with a
pack full of rock has produced exactly nothing — which is what v1's miners did all night, because the goal ended
at the vein. So the work here is not "dig" but **dig, carry it to a fire, and put the metal somewhere safe**, and
it is not finished until the last of those has happened.

That is why this chain came first: it exercises everything the brain can do at once. Stages that survive
interruption. A named skill credited only in finished work. Goods that are worth something **before** they are
sold. And a definition of "done" that is **not where the work happened**. Hunting would have tested none of the
last three.

---

## The work is the engine's

Digging goes through the shard's `HarvestSystem` — the same call a player's double-click makes — so the swing,
the skill check, the ore that comes out and the exhaustion of the vein are all real. A bot that digs is a miner,
not a bot that was credited with ore. And that is what makes the thing this project measures measurable: the
skill gain here is the engine raising a number, not us deciding a number should rise.

Four things v1 learned expensively, all four accounted for here.

**Ore is not uniform, and the shard will say which kind.** `GetVeinAt` seeds a stable draw from the bank's
coordinates, so every 8×8 block has its ore type fixed for the life of the shard, and iron is only half of them.
The complaint "my miners bring nothing but iron" had exactly this cause: they were digging the nearest rock. **Ask
with bank coordinates, not tile coordinates** — `GetVeinAt` divides before it asks, so a tile asks the bank
sixty-four times further away, which reads as ore scattered at random instead of ore in veins.

**Sand is not mining.** It is worked by masters with a hundred points and a flag nobody has, and a bot swinging
at a beach gets a message nobody can read and nothing in the pack. Britain is surrounded by sand, and in v1 both
gatherers dug it all night: the ground passed every check the bot could make and produced not one ingot in eight
hours. The test is one line: the tile definition must be **exactly** `OreAndStone`.

**A fire has to be pointed at, not stood next to.** Ore is smelted by double-clicking it and targeting a forge:
the double-click only opens a target and waits for a player. A bot has no client, so in v1 **not one ingot was
ever smelted** — every pile of ore opened a target that hung there until the next one replaced it. The answer is
to fill in the target ourselves, which is exactly what a client does.

**Weight kills.** A pack holds about twelve ore before the engine starts charging stamina per step, and at zero
it refuses the step outright. In v1 three bots stood that way for a whole session while the log confirmed six
hundred times that the step was allowed. It was allowed — stamina was not in the message. So digging ends by
weight, not by count.

---

## The sweep: how the population learns about places

A bot in town cannot see the mine and a bot in the mine cannot see a forge — so knowledge of places cannot come
from looking. It has to be walked once and remembered.

**What one pass looks for:** veins (every fourth tile — mountains are large), fires (every tile — a forge is one
tile wide) and counters (one spatial query for the whole sweep, because bankers are mobiles).

**The sweep goes round the bot, not round a list of towns.** v1 swept vendor clusters and missed the only town
that mattered: the centre of Britain's cluster is the mean of the shopkeepers' spawn points, and it lands inside
a wall 246 tiles from the smithy. The result was eight forges across four facets and **none** on Felucca, where
the entire population lived. A bot asking where it can work starts a sweep around itself — and missing the place
where the bots actually are is then impossible.

**Forges come in two kinds**, which is another reason nothing was ever forged in v1: a player-placed one is an
item and a query finds it; every town one is a **static tile**, part of the map, and no query finds it at all. On
this shard Felucca's smithies turned out to be items while the other facets' were statics, so both kinds have to
be looked for.

None of this is written to disk or survives a world reload: every point in these lists is a fact about a world
that has just been replaced.

---

## How the takings are counted

A finished chain's takings are `Δmoney + goods produced + Δskill × rate`, over minutes. For a miner:

- **Δmoney = 0.** The chain brings in no coin at all, and `Coin = 0` says so honestly: metal in the bank is
  wealth the population can use, but it is not money until somebody has bought it.
- **goods produced** = ingots × `BotAuction.Worth(iron, GoldPerIngot)`. The market is asked first — what somebody
  is offering with the money down, then what iron has actually changed hands for — and `GoldPerIngot` (6) is the
  last resort when the shard has never had an opinion. That ordering is how a shortage reaches a miner: nobody
  tells it to dig, a want for metal raises what its metal counts for, and the ledger raises its estimate of
  digging next time.
- **Δskill** is the only part that is genuinely measured, and it is also the largest: half a point of Mining per
  trip at a rate of 500 outweighs all the metal. That the skill dominates is not an accident of the numbers; it
  is what this population is for.

Skill is credited **only if the chain reached `Done`**, so "train on a dummy" does not work here: the gain has to
arrive alongside metal in the bank.

---

## What is closed, and by what

| Hole | What closes it |
|---|---|
| dig for ever and seize up from the weight | digging ends at `FillFraction` of the engine's own ceiling |
| stand at an exhausted vein | the engine's remaining-ore count is read, and six empty swings write the tile off |
| learn that "the vein is bad" when the forge was at fault | the proposer offers no chain until a fire and a counter are known |
| walk in circles to an unreachable vein | `Bend` picks another vein, but no more than three times |
| mint money on deposit | the gold is taken out of the pack **before** `Banker.Deposit`, because the engine's deposit adds to an account without touching what the depositor carries |
| get paid twice for one bar | what goes into a funded want is paid in coin, so it comes back off `Made` |

---

## What to check with a client

1. At startup, the `Harvest ready:` line with the ingot rate and the bid for a trip.
2. Any bot's first bid prints `Swept 160 tiles around ...` with the numbers: how many veins, fires and counters.
   **Zero fires is the most important thing that can appear there** — it means nobody will offer the chain, and
   there will be a separate line saying so by name.
3. Then watch the brain's pairs: `took on mine: ...` with the estimate, and
   `finished mine: ... N ingots put away`. The second is the proof that the chain reached the end rather than
   stopping at the vein.
4. After a few trips the estimates should diverge between veins: that is the ledger, not a configuration file.

---

## Known rough edges

**A failure at the fire marks the vein.** The ledger is keyed by "work + patch", and the patch comes from the
vein (rightly: what is being learned is "digging here pays"). So if the ore never made it into a fire, the
caution lands on the vein although the forge was at fault. It fades in five minutes, but it is an inaccuracy.

**No more than sixteen sweeps per world.** After that, bots that have walked beyond the swept ground will find
nothing — and there is one line in the log saying so.

**The pickaxe decides who mines, not the class.** A gatherer is born with a pickaxe and a hatchet; anybody with a
pickaxe in the pack is a miner while it lasts. Deliberate: v1 had a list of archetypes permitted to work, and
adding a class silently excluded it from working.

**Every engine call was read out of the fork:** `Mining.System.OreAndStone.GetVeinAt`, `system.GetDefinition`,
`StartHarvesting`, `ore.OnDoubleClick` plus `bot.Target.Invoke`, `map.Tiles.GetStaticTiles`,
`GetStaticAndMultiTiles`, `GetMobilesInRange<Banker>`, `Banker.Deposit`, `pack.ConsumeTotal`, `box.DropItem`,
`Utility.InRange`, `Mobile.InRange`, `HarvestDefinition.GetBank(...).Current`, `MaxRange`.
