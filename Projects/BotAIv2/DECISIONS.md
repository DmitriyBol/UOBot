# Decisions: what was decided, what was tried, and the defects that keep coming back

Patrick's rule of 14.09.2026, after a month of "one thing, then another": every decision about how the bots work is
recorded with what it answered, what it cost and how it ended, and every defect is filed under the class it belongs to.
This file is that record. Everything before 14.09.2026 was backfilled from the project's notes of 15.08–14.09.2026;
everything after is written as it happens.

Other documents answer other questions and are not repeated here: `ARCHITECTURE.md` — the shape; `MAP.md` — where a file,
a number or a log line lives; `DIALS.md` — what a number is set to; `HANDOFF.md` — the state of the work;
`RESEARCH-decisions.md` — what the literature and other games say about commitment and roles; `NIGHT-2026-09-13.md` — one
night's rounds. Where this file and the code disagree, the code is right, and this file is corrected.

**Contents** — §0 how to use this file · §1 why problems keep coming back · §2 mechanisms and their invariants · §3 defect
classes, with every recorded instance · §4 decision log · §5 structural remedies not yet built · §6 decisions waiting for
Patrick

---

## 0. How to use this file

**Before changing a mechanism**

1. Read its section in §2 and every §4 entry that names it.
2. Name the invariants the change touches. If the change removes a guard — a veto, a threshold, a window, pricing
   (`Unpaid`), learning — name what replaces it, in the same change.
3. Find the class in §3 the change is most likely to produce and read its sign.
4. Write the §4 entry with status **attempt** before deploying.

**After measuring**

- Compare sessions only at the same age from start. Turn the knob back and see the number return. Use windows longer than
  the defect takes to form — on this shard about an hour and a half for anything economic.
- Fill in *measured* and *side effects*, including a negative result, and set the status: **active**, **superseded by …**
  or **reverted**.

**When a defect is found** — add it to its class in §3. A new instance of an old class means the class is not closed by
structure; the structural remedy belongs in §5.

**Entry format (§4.1)** — *problem* (with the number that showed it) · *options* · *decision* · *expected* · *measured* ·
*side effects* · *status* · *code*.

---

## 1. Why problems keep coming back

Not because they cannot be solved. The way the work has been done guarantees that the same classes return in new places.

1. **Instances are fixed, classes are not.** Each fix closes the case in front of it — one proposer, one threshold, one
   file — and the next deed written the same way has the same defect. The failure line "the pack would not hold the Ngp
   it drew to pay with" was gated in the guild courier on 09.09, appeared again on 10.09, and on 14.09 in restocking, where
   it failed 8,719 times in half an hour.
2. **Everything is coupled.** 843 dials, 48 proposers, and flags that change how the auction treats work (`Unpaid`,
   `Steadfast`, `Summons`, `Standing`, `Committed`, `AtCounter`). A rule that is right on its own moves the equilibrium
   next to it: removing restock's refusals on the morning of 14.09 also removed the only brake on a failing trip.
3. **Verification is shorter than the defects.** Changes are measured over 15–30 minutes on a live shard; Hale's pack took
   five hours to jam, veins run out in two to three, markets warm up over an hour. There are no regression checks, so a
   fixed defect comes back somewhere else.
4. **What was learned was not read before the next change.** The lesson "unpaid work learns nothing, its proposer is its
   only gate" was written on the night of 13→14.09; restocking was made unpaid without a gate the next morning. This file
   and §0 exist for exactly that.
5. **Topics change before they close** — wars, seats, guilds, dungeons, economy, roles — and each new system leans on
   half-finished ones.

| class | instances below | last seen | closed by structure |
|---|---|---|---|
| C1 failure re-offered unchanged | 35 | 14.09 | no — §5 S1 |
| C2 one fact, two numbers or two questions | 37 | 14.09 night | by a rule only: derive the second from the first |
| C3 wrong shape of judgement | 18 | 14.09 | partly: veto / factor / rank / flag vocabulary |
| C4 engine rules found by symptom | 47 facts | 14.09 | no — §5 S3 for packs |
| C5 instruments that lie | 42 | 14.09 | by rules only (§2.8) |
| C6 state lost or wrongly kept across restart | 13 | 14.09 night | partly |
| C7 reach, search budget, walk orders | 21 | 14.09 | partly |
| C8 remedy locked behind its own condition | 17 | 14.09 | no |
| C9 side effect at offer time | 3 | 14.09 night | yes for claims (BotOffice) and war companies |
| C10 state nobody reads; comments and names that promise | 12 | 14.09 | no |
| C11 coupled oscillation and herding | 3 | 14.09 | yes for commitment (events, not bonuses) |
| C12 two working mechanisms with no edge | 16 | 14.09 | no |

---

## 2. Mechanisms and their invariants

Each invariant was bought by a measured defect; the §3 references say which.

### 2.1 The auction — `BotWill`, `BotAppraisal`, `BotLedger`, `BotCommons`

One currency: takings per minute, `(Δmoney + goods made + Δskill × 500) / minutes`. A proposer offers one best deed;
`BotAppraisal.Weigh` scores it (estimate × fifth root of the factors × calling); the auction takes the best and holds it.

- **A1 — Goods bought are worth what they cost.** A purchase's `Made` is what was paid, measured around the payment; a
  guild's payment counts only the bot's own contribution. (C3: purchase as work; collective payment.)
- **A2 — Say which of four things a judgement is.** *Cannot happen* is a veto. *A poor idea* is a factor, and every factor
  that ordinary state can drive to zero has a floor. *Must come first* is a rank. *Paid nothing on purpose* is the
  `Unpaid` flag. A factor under a fifth root cannot say "cannot"; a veto cannot say "poor". (C3.)
- **A3 — A veto must not lock the work that cures its own condition.** Before adding a veto, find the remedy and check
  it is not subject to it. The overload veto of 08.09 blocked the walk to unload; the counter veto of 14.09 leaves unload
  alone, which was checked live. (C8.)
- **A4 — A deed that fails on its first beat is not offered unchanged at the next review.** Either the proposer asks the
  question the deed fails on, or the failure is learned. Unpaid work learns nothing, so an `Unpaid` flag arrives with a
  gate for every reason the deed can fail. (C1.)
- **A5 — A check belongs in the choice, not in the work.** Skipping a candidate is free; failing a deed is a loop. (C1.)
- **A6 — A proposer refuses everything its deed will refuse**, asking the same question with the same numbers. (C1, C2.)
- **A7 — Anything created in `Propose` is created for an offer.** The auction calls `Drop` only on the winner; losers are
  discarded. Claims made at offer time are short (`BotOffice.Offering`, 12 s); companies form on the winner's first
  `Advance`. (C9.)
- **A8 — A road that does not exist is not a price.** Unreached work writes nothing into land, commons or trade
  (`Settle(unreached)`); the road note (`Beware`) is still written. (C3.)
- **A9 — An estimate that can only be corrected by winning does not open below the winners.** (C3: forge 55.)
- **A10 — A lesson is filed under the name the chooser reads it by** (trade vs place). (C1, C10.)
- **A11 — Commitment is by events, not bonuses.** Steadfast work is held for its reckoning; only what will not wait
  (`Pressing`), a call from outside (`Summons`) or trouble in the work gets through; displaced work is put down and taken
  up again. A bonus only moves the point at which two choices trade places. (C11.)
- **A12 — What an appraisal cannot see is not undone by a rank or a veto.** Work that has already spent money in its
  middle is `Committed`. (C3: rank outranked a purchase.)

### 2.2 Deeds and proposers — the contract

- **D1 — The deed owns its whole chain, and no state waits indefinitely.** Every "stand and wait" ends on the beat it
  stops being true. (C1: frozen work.)
- **D2 — Every new gate gets a named counter in the same change**, incremented where the event happens, not behind
  another gate, and the gates add up to the proposer's `Asked`. (C5.)
- **D3 — A repeated walk order is bitwise identical.** (C7.)
- **D4 — Engine results that arrive on a timer are read on the next beat** (crafting, harvesting); an exit on "the
  material is gone" waits one swing. (C4.)
- **D5 — A deed that stands still on purpose says so** (`BotDeed.Still`), and skips are counted. (C5: progress read from a
  display string.)
- **D6 — Whatever is placed at an offset from a settled point asks about the tiles it lands on.** (C7: camps in water.)

### 2.3 Walking and reach — `BotWalk`, `BotPath`, `BotReach`, `BotRefused`, `BotBarred`, `BotQuad`

- **W1 — "Cannot get there" is proven from the far side** (a flood from the target, `BotPath.Enclose`), never from the
  bot's side, at any budget. (C7.)
- **W2 — Errands keep the full search ceiling; it is what finds a shop's door.** Walks re-issued every beat (formation
  stations, sweeps) get a short look (`StationCeilingMs` 15). (C7.)
- **W3 — Only `Refused` and `GaveUp` are proven refusals of a road.** `Stalled` in a crowd is not. (C7.)
- **W4 — Distance is a price, not a wall.** A general limit applies where the whole population pays, not to rare work.
  (C7.)
- **W5 — Height comes from the source the consumer compares with:** walking `BotStep.Settle`, building `GetAverageZ`.
  (C7.)
- **W6 — A measurement rests and clears on arrival; an assertion does not.** Decided walls are `BotBarred`, never
  `BotRefused`. (C10.)
- **W7 — Area sampling uses positive knowledge** (`BotQuad.Trodden`), not negative notes. (C7.)
- **W8 — Pursuit is excluded from "stopped closing", and a chase has a leash from where it began.** (C7.)

### 2.4 Market and money — `BotAuction`, `BotShops`, `BotUnload`, `BotShelf`, `BotYield`

- **M1 — Coin stays in the world when bots trade.** Ties between a stall and a counter go to the stall; a bot never opens
  above what a shopkeeper asks (`BotShops.Shelf`). (C2.)
- **M2 — Supply and demand must cross** (`BotAuction.Cross`); a material is ordered only if some bot's work produces it; a
  maker reads paid wants first. (C12.)
- **M3 — Only a user of a material keeps a stock of it; keep lists are quantities, never `int.MaxValue`.** (C2.)
- **M4 — Every surplus has a door:** the market, the bank box, and the ground as a last resort. Nothing is put into a pack
  past the engine's cap without asking (`TryDropItem`); anything that changes money over a counter first asks whether the
  pack can take a coin (`BotYield.Pocket`). (C4, C8.)
- **M5 — Money is where the engine spends it.** A bill under 2,000gp is paid from the backpack; purchases draw the
  shortfall from the bank into the pack first. (C4.)

### 2.5 Companies, guilds and wars — `BotSquad`, `BotGuilds`, `BotEstate`, `BotWar`, `BotFeud`

- **G1 — A Bound bot has no auction, so whoever binds it gives it work or releases it.** A company's charge is held by an
  errand; whoever charged a company ends it; a dissolved company is flagged so nobody joins it. (C8, C10.)
- **G2 — A consequence at the end of a process requires that the process can end.** Opinions are settled at a war's end;
  kills inside a war do not move opinion. (C8.)
- **G3 — One appointed hand is a veto.** Offer to every member and remove the race with a claim the work renews. (C3.)
- **G4 — A rule about a group is applied to the group** (guild muster once, after the population exists). (C3.)
- **G5 — Wars, truces, clocks, move debts and opinions survive a restart**; the engine's enemy lists are re-asserted at
  start. A restart is safe except in the middle of a hall move. (C6.)

### 2.6 Minds — `BotMindAI`

- **N1 — A mind chooses from an enumeration; its choice becomes a real deed in the same auction; its forecast is
  measured, never bid** (bid = the deed's own worth × `Insistence`). (C3.)
- **N2 — Narrow the enumeration instead of persuading; `none` first; names only from living lists.** (C3, C5.)
- **N3 — Minds are shown what the others hold, and asked at staggered times.** (C11.)
- **N4 — Prompt wording is a defect surface:** a count whose good value is zero reads as good, a word carries its scale,
  one phrase per reason. (C5.)

### 2.7 Observation — summaries, alarms, dials, Argus

- **O1 — Engine measurements are facts; a model's findings are conjectures.** `props`, `tile`, `near`, `pack`, `sight`,
  `road` are trusted; watcher findings are checked against the log before anything is changed. (C5.)
- **O2 — Summary counters are cumulative since the world loaded.** Rates are differences; sessions compare only at equal
  age; every knob is turned back before its effect is believed. (C5.)
- **O3 — Throttle lines, never counters.** Every number has a denominator taken from successes; a counter behind a gate
  measures the gate; a number nobody prints is worse than none; a ceiling that is reached says so. (C5.)
- **O4 — Verification order: the config file, then the boot line, then the code.** A background check fixes its log file
  name at launch; the clock is checked before conclusions about a log's age. (C5.)
- **O5 — An alarm carries numerator, denominator and measured window.** A population alarm can be tripped by one looping
  bot, so the first action on any alarm is to group the window's failures by bot and reason. (C5.)
- **O6 — Build with the shard stopped when `mindedBots` changed** (a running shard hides BotMindAI's errors); `do save`
  before stopping (a kill rolls the world back to the last autosave). (C5, C6.)

### 2.8 What survives a restart

| survives | lost |
|---|---|
| the world save (`Saves/Mobiles`, `Saves/Items` — Patrick's character lives there; **never delete**) | the bots themselves (purged at every load and rebuilt from configuration) |
| skills, fame, karma (`Saves/BotProgress`) | purses and packs (`BotProgress.Savings` off by Patrick's order, 08.09) |
| island map, pins, trodden ground (`Saves/BotQuads`) | ledgers and commons (what work paid where) |
| guild claims (`Saves/BotClaims`), hand-set seats (`Saves/BotSeats`) | the market (stalls and wants) |
| wars, truces, clocks, move debts, opinions (`Saves/BotWars`) | reach pockets (`BotReach`) |
| guild halls and merchants' shelves (in the world) | veins, counters, hearths (`BotGround`) |
| dungeon halls (`bot-dungeon-halls.json`), minds' lessons (`bot-minds.json`) | dials changed through the door |

---

## 3. Defect classes, with every recorded instance

Dates are 2026; "v1" is the first bot assembly, deleted on 21.08. Numbers are as measured at the time.

### C1 — A failure re-offered unchanged

**Definition.** A deed fails, and the same deed is offered to the same bot at the next review, because nothing between
the failure and the choice remembers it: the proposer does not ask the failing question, the note is written where no
chooser reads it, or the work is unpaid and learns nothing.
**Sign.** One bot, one reason, hundreds of `failed at <kind>` in minutes; a population alarm with one name under it.
**Structural remedy.** None built. §5 S1.

| date | where | what happened | fix |
|---|---|---|---|
| 17.08 v1 | buy for an order, resell | two bots sold the same shopkeepers 4,152 single items | v1 |
| 17.08 v1 | death loop | 443 deaths, Nell 104 in one tile every 20–40 s | v1 |
| 17.08 v1 | scout | survey target never reset on refusal; walked 3 h towards a point 589 tiles away | v1 |
| 25.08 | `BotQuarry` Crowded/Shunned, readers private | rescue refused 103 times on one target (hunt 7); Gerda 11 times on one skeleton | readers public |
| 25.08 | blind exit | comment promised the bot would be offered elsewhere; nothing moved it: Joss 38 takes on one zombie | `BotQuarry.Shun` on that exit |
| 25.08 | frozen work (`Work` never judged) | tailors silent 2 h, Merrick 57 min, Perri 21, Godric 17 | `BotSew.StallMs`, `BotWill.LabourMs` |
| 26.08 | recipe choice ignored the board | 4 paid armour orders stood an hour, 17 sewings passed, 0 fills | paid orders first |
| 27.08 | horse purchase as an errand | its own estimate poisoned 115 → 51 → 36 → 33/min against a mine at 260 | a reflex at the counter |
| 01.09 | `BotForage` never called `Ledger.Beware` | 281 refusals on one tile (1456,1641,20) | `Beware`: 2.78 → 0.4/min |
| 02.09 | Argus's shake bypassed `Bend` | Phyllis 31 walks to an unreachable place | `Bend` first: 31 → 3 |
| 03.09 | scouting marked squares only through `Bend` | Aldric led six bots to (1605,2115) three times | mark in `Drop` |
| 03.09 | prowl's baulk written to `BotPeril`, hunting land chosen from `BotQuad` | 119 "got no nearer than 156 tiles to (1005,1335)" in 20 min; shard value 2,221 → 739/min | mark `BotQuad` too: 119 → 3 |
| 03.09 night | "one company per quadrant" checked inside the work | 1,728 failures in 5 min | check moved to the choice |
| 04.09 night | board full (`MaxWants` 128) checked inside the work | 273 errors per 30 min; 85 of 125 purchases one bot on one scroll | `BotAuction.Full` in the choice: 273 → 7 |
| 04.09 | wants for bandages, which nothing crafts | 25,890gp of escrow frozen against 13,744gp in all purses; bid 5 → 48gp | `BotShopper.Makeable` |
| 05.09 | immobile Calla offered unload, which needs a walk | 6-min treadmill at 293 of 222 stones | list on the spot |
| 05.09 | hearth choice, two unconfirmable guards | 0 successes of 887 walks to four fires | see C10 |
| 07–08.09 | hunting land choosers never read `BotQuad.Baulk` | 1,302 of 1,544 failures "no way through" in 50 min | `BotQuad.Baulking` in both choosers |
| 07–08.09 | `BotHerbs.Bend` wrote `Beware`, `BotHerbs.Wood` never read it | failures back within the hour: 220 of 254 by minute 50 | ledger asked in the chooser |
| 07–08.09 | stall watch freed a bot and wrote nothing about the place | three bots in a row caught on one roof | open then |
| 07.09 | Baron's only work refused for paying nothing | "stroll is expected to pay −40.5/min here" | `BotDeed.Unpaid` |
| 09.09 | homeward refused for paying nothing | 416 "nothing worth doing" in 80 min, all in Britain | `Unpaid`: 416 → 22 |
| 09.09 06:09 | Quill overloaded with a load nobody buys | 143 unload failures in 5 min; alarm at 47% | last resort: put things down |
| 09.09 14:31 | Kelda: proposer asked "if you had twenty", deed asked "from what you hold" | 218 of 254 failures, ten a second | one question |
| 09.09 evening | guild courier could not hold the coins it drew | 1,955 of 2,012 failures; alarm at 9% | `BotSupplier` gate by weight |
| 10.09 | courier at a full shelf with a pack full of undelivered goods | 1,239 "the merchant would not take any more", 1,222 "the pack would not hold the Ngp" in 8 h | `BotShelf.Room`, `BotUnload.Store` |
| 13.09 | enlist radius 40 tiles against a company life of 8 s | 4,787 taken, 64 finished | `Reach` 24, `Left` 0.4 |
| 13.09 21:30 | enlist offered a company of one that the deed refused | 2,830 taken, 0 finished; shard completion 21% | same bar in the proposer |
| 13.09 | bots released underground offered island work | 858 refusals in 10 min | underground release, settle and rescue rules |
| 14.09 night | hire with two keep-backs on one fund | 4,365 failures in 43 min | `BotGuilds.Keep => BotEstate.Keep` |
| 14.09 night | sortie rally offered to bots in another company | 22 a minute "fell in with another company" | `BotFeuder.Alone` |
| 14.09 05:20 | a scribe sent to buy what it was selling | Brannoc 14 cycles a minute, Lysa Ashdown 53 in 10 min | `BotArmoury.Vending` |
| 14.09 08:27 | a silent tree re-issued to the same trip | Hale 250 swings and 0 logs; 915 full searches "to a tree" in 15 min | shun a silent tree for the trip |
| 14.09 16:38 | Hale's pack jammed; restock unpaid since that morning | 8,719 failures in 30 min — the third appearance of the courier's line | build 37: counter veto |
| 14.09 | escort after fighters it never closed on | Otho sent after Maeve 48 times, Faron 16 | `BotAccompany.ShunMs` |

### C2 — One fact, two numbers or two questions

**Definition.** Two numbers, or two calls, describe one thing, live in different places and agree by coincidence — until
one moves and the system parks in the band between them.
**Sign.** "Nothing happens", with no error; two numbers in one summary line that never meet; a proposer and its deed
disagreeing. **Remedy (rule).** Derive the second number from the first; ask one method from every place.

| date | where | what happened | fix |
|---|---|---|---|
| 18.08 v1 | poverty line = gear purse 800, born with 100 | 136 of 150 poor for ever | v1 |
| 18.08 v1 | "arrived" at 8 tiles, counted from 6 | work never counted, bots stood | v1 |
| 19.08 v1 | reagents: buy below 5, stop casting below 10 | 242 refusals in 4 min, 0 trips to a shop | second threshold derived |
| 19.08 v1 | still (brew to 10, shop below 15), cloth (buy below 50, sew above 50), bandages | loops and dead crafts | derived thresholds |
| 25.08 | standoff at flat distance, combat needs line of sight | Godric 10 of 10 fights "cannot land a blow"; melee 0 | visibility a condition of the point |
| 25.08 | recipe by skill, not by material count | one leather, sandals need four: endless refusal | `_need` from the recipe |
| 25.08 | `BotBullion` reserve 250 against a purse plateau of 400 | 16 min "cannot afford" for 10 coins | `Batch × offer + 150` |
| 25.08 | company idle cap 300 s, nothing gives a standing company work | companies stood | `BotSquad.Hunt`, `IdleCapMs` 45 s |
| 25.08 | armour "already ordered" check outside the slot loop | one unmakeable helm cancelled every order | inside the loop |
| 26.08 | `InRange(place, 3)`, then a walk to `Beside` counter furniture | 56 failures an hour at one Britain counter | walk the distance the work asks |
| 27.08 | `BotHarrower.Range` 300 against `Roam` 500 | the Baron never marched | `Range` reads `Roam` |
| 03.09 | recipe by skill, not by metal in the pack | Godric four trips in four minutes "out of metal" | `BotAnvil.Stock` |
| 03–04.09 | `Release` comment "Alongside", code `Deed != null` | 24 errands to the 10-min cap on Bound; 44 of 65 stuck "in a company" | code matches the rule |
| 04.09 | "can I hit it" asked three ways | 71 of 174 company fights "in reach, health not moving" | one verdict |
| 04.09 night | station ring (5) used as bow reach (10) | 96 "moved" ticks in 16 min | weapon reach, ring as floor |
| 04.09 night | fight clocks started at engage, finder reaches 40 tiles | 41 abandoned-and-returned per window | clocks wait for arrival |
| 04.09 | `BotShopper` stall `<` counter, `BotSeeker` `<=` | an island-made arrow could never sell on the island | `<=` everywhere |
| 04.09 | batch fixed at 20 against a reserve of 150 | craftsmen locked out of raw materials | floor |
| 04.09 | three gates around the smith | 0 forged; "241 short of metal" beside "160 have their own out on a stall" for months | `BotAnvil.Fetch` where the shortage is found |
| 04.09 | flat `DwellMs` 30 s against a 6-min forge | dropped at exactly 0.5 min; forge 5 taken, 4 dropped | `Dwell(deed)`; superseded 14.09 |
| 05.09 | `BotFlask` null read as "no herbs and no glass" | 377 "had neither" — the bucket fixed 8 h before in the same file | split bucket |
| 05.09 | opening price above the NPC's shelf | 81% of supply money left the world | `BotShops.Shelf` |
| 08.09 | muster radius 28 against darts raised to 500 the same day | 82% of musters refused "none of ours near" | `Reach` 120 + `MostBound` 0.34 |
| 08.09 | warden and harrower asked one function two questions | the Baron strolled, Mood 0% | `BotHarrower.Takeable` |
| 08.09 | dart thrown 500 tiles, search paid for 240 | strolls failing | reads `CeilingMs / MsPerTile` |
| 08.09 | plot height from `Settle`, engine compares average land Z | 113 of 113 plots `NoSurface` | `GetAverageZ` |
| 09.09 | kiting at `reach − 1` | 1,514 kites vs 860 shots | `KiteWithin` 5 |
| 09.09 | keep list `int.MaxValue` written over the class's own limit | 8 heal potions and 10 sewing kits in one pack | `SpareTools` 2, class limit |
| 10.09 | `Risk` 400 in gold compared with a lot count | 0 trips in 8 h | `Risk` 8 lots |
| 10.09 | a floor refusal written as a shard-wide verdict | 34,505 things of 56 kinds stayed in packs | measured prices only |
| 10.09 | `Enmity` −100 against a worst opinion of −36 | 0 wars | −40 |
| 11.09 | population names and mind names overlapped | two Ulrics and two Wulfrics sharing one progress record | `BotPopulation.Reserve` |
| 13.09 | peace judged by the side whose opinion moved | a war ended 7 s after it was declared | both sides above `Amity` |
| 13.09 | `MaxHalls` 4 against five guilds | the Needle could never build | 8 |
| 13.09 | enlist `Reach` 40 against `IdleCapMs` 8 s | see C1 | see C1 |
| 13–14.09 night | `BotEstate.Keep` and `BotGuilds.Keep` | 4,369 "enough" in 43 min | one keep-back |
| 14.09 night | `BotFeud.Standing` read a company as dead the second its leader died | charge removed, company disbanded, founder rose and gathered again | stands while listed and not empty |

### C3 — The wrong shape of judgement

**Definition.** A true statement about work expressed in the wrong form: a veto where a factor belongs or the reverse, a
price on work that earns nothing by design, one number serving as both forecast and bid.
**Sign.** Refusals naming the same reason across bots ("expected to pay −N"); work taken but never won; a whole trade
disappearing from the island. **Remedy (partial).** The vocabulary in §2.1 A2.

| date | where | what happened | fix |
|---|---|---|---|
| 25.08 | a mind's `Expect` was both forecast and bid | Aldric wrote "always predict zero"; 2 of 24 decisions taken | bid = worth × `Insistence` |
| 25.08 | purse factor without a floor | Cedric's empty purse forbade the sweep, the answer to "nothing to do"; stood 10 min | `LeastPurse` 0.1 |
| 27.08 | a purchase priced as work | see C1 | reflex |
| 04.09 | `BotHerbs.Made => 0` | gathering taught as free while reagents were 53% of all spending | worth reported |
| 04.09 | flat protection of fresh work against long work | forge dropped at 0.5 min | `Dwell(deed)` |
| 05.09 | scrolls bought to cast reported as a failed write into the book | 233 of 430 successes counted as failures | `BotAcquire.Purpose` |
| 07.09 | unpaid work refused for paying nothing | the Baron stood 12 min | `BotDeed.Unpaid` |
| 07–08.09 | an opening number that seals itself | forge `Prior` 55 never won in an hour; dialled to 120, first daggers at 940 and 1,300/min | 120 |
| 08.09 | unreached work priced into land, commons and trade | cooking "−309.9/min" in Britain; 9,099 "nothing worth doing" | `Settle(unreached)` |
| 08.09 | my overload veto | three crafters idle 1,677, 1,640 and 1,632 s, never reaching unload | factor, then rank |
| 08.09 | one appointed hand (the guild leader) | Busy 32, Bound 17, Free 0; no hall bought | offer to all, claim renewed by the work |
| 08.09 | two works waiting for each other (land search and money) | no hall | search from halfway to the price |
| 08.09 | a flat price on every cry for help | 45–70 abandoned works per window | price by need |
| 09.09 | collective payment read as personal loss | hall −50/min, hire −93/min; the shard learned not to build | `Made` = own contribution |
| 09.09 | a factor under a fifth root for "cannot happen" | a fiftieth of 260/min still beat half the board | rank |
| 09–10.09 | the overload rank outranked a purchase already made | supplies abandoned after payment 10 → 80 an hour | `Fits` + `Committed` |
| 14.09 night | `BotCommons.Corrected` floored every unpaid claim to a quarter | a war company's founder lost to reclaim at 41/min | unpaid claim taken as typed |
| 14.09 morning | restock made unpaid without a gate for its failure reasons | see C1, 14.09 16:38 | build 37 |

### C4 — Engine rules discovered by symptom

**Definition.** The engine enforces a rule silently — usually by a message to a client the bot does not have — and the
first sign is a behaviour, not an error. **Remedy.** Read the fork's source first; record the fact here and in
`ARCHITECTURE.md` §9.

| fact | cost when unknown | when |
|---|---|---|
| `Mobile.Player` must be set by hand; without it death deletes the bot and `Alive` is always true | bots vanished or stood dead for ever | 21.08 |
| configuration keys are PascalCase; lowercase silently gives defaults | 15 bots became 4 | 21.08 |
| configuration overrides code defaults; the boot line is the truth | wrong diagnoses from code defaults | 22.08 |
| removing an assembly whose types are in the save hangs a headless start on a y/n prompt | the world would not load | 21.08 |
| a bot standing in a `TownRegion` cannot be hostile | nothing to fight in Britain | 21.08 |
| overweight: `40 + 3.5 × Str` against body plus load; stamina per step; blocked step | three bots stood 3 h | v1 |
| the engine's A* only looks 38 tiles | greedy steps into walls | v1 |
| actions needing a target cursor need the target supplied in code | no ingot was ever smelted in v1 | v1 |
| forges and anvils are static tiles, not items | "no forge in the world" at a forge | v1 |
| `BaseOre` weight is random at creation (2–12 stones) | carrying estimates six times apart | v1 |
| a recipe's `MinSkill` is availability, not competence | 358 swings, nothing made | v1 |
| `BaseWeapon.CanEquip` checks Str/Dex for players; `EquipItem` fails silently | mages without staves | v1 |
| `Spell.Cast` returns at the start; the target request comes after the delay; `ClearHandsOnCast` disarms | spells that never landed | v1 |
| vendor shelves refill only on `Restock()`, prices on `UpdateBuyInfo()`; `OnBuyItems` takes an order whole or not at all | shops emptied for ever | v1 |
| a multiplier applied to a persistent field compounds (spawner `Count` ×1.6 per start) | 43×, int overflow, 28-min world load | v1 |
| `OnBuyItems` pays from the backpack below 2,000gp | 1,929 fruitless restocks in 30 min with money in the bank | 25.08 |
| no NPC sells raw leather; carving is free; hides become leather by scissors | no leather anywhere | 25.08 |
| `LineOfSight` lifts both ends by 14 Z; a swing needs line of sight and retries silently | standoff bots never hit | 25.08, 04.09 |
| a bow fires only 1,000 ms after the archer last moved (UOR) | companies of archers that never shot | 04.09, 09.09 |
| a cast is disturbed by damage for a `PlayerMobile` | casters in contact never cast | 04.09 |
| a scroll casts without a book or reagents when passed to `NewSpell` | — (used) | 24–25.08 |
| crafting and harvesting resolve on timers (harvest 0.9 s after the swing) | "nothing made" read on the same beat; `BotQuad.Harvested` never fired | 04.09, 09.09 |
| `BeginAction<CraftSystem>` locks each swing | swings bouncing off their own lock | 04.09 |
| lumberjacking needs the hatchet equipped; `EquipItem` refuses an occupied layer | not one log chopped in the shard's history | 04.09 |
| recipes with two resources (arrows, potions) | two whole crafts dead | 04.09 |
| heat is required per recipe (`SetNeedHeat`), not per craft system | 0 meals cooked | 05.09 |
| a failed craft consumes half the material | 38 of 52 forge failures "2 attempts, 0 made" | 05.09 |
| `Mobile.Hunger` only ever rises | a bot would eat five suppers in its life | 05.09 |
| veins run out in 2–3 h; the engine's respawn is 10–20 min from the first hit; iron rolls `CheckSkill(Mining, 0, 100)` | "island exhausted" misread | 05.09, 09.09 |
| harvest messages go through `SendLocalizedMessage`, which is not virtual | quiet swings misread for days; engine patch `HarvestDefinition.Said` | 09.09 |
| `CraftItem.IsHeatSource` is private | engine patch `CraftItem-heat-source` | by 05.09 |
| mount stamina: 3,840 steps, one back per idle second, a blocked attempt resets idleness | 97% of all "did not get there"; mount stamina switched off | 08.09 |
| house placement compares the average land Z | 113 of 113 plots refused | 08.09 |
| pre-AOS doors need keys; an ownerless house decays | locked halls | 08.09 |
| `PlayerVendor`: lots at 999 on drop, stack pricing, pre-AOS `IsOwner` is the owner only, pay timer every 2 real hours | a stack given away; merchants that would die | 09.09 |
| a container holds 125 things; `TryDropItem` joins a pile first (weight only) and needs a slot otherwise; `DropItem` asks nothing | guild counters at exactly 125 lots (10.09); Hale's pack at 197 things (14.09) | 10.09, 14.09 |
| a war makes an enemy guild's member "enemy", not innocent; no fighting in a `GuardedRegion` | war only possible inside the rules | 10.09 |
| `OrcCamp` scatters its garrison ±7 through `AddMobile` with no checks; water is `Wet` + `Impassable` | orcs in the sea | 10.09 |
| the engine cannot move a house | exile implemented as build-then-remove | 10.09 |
| `Rectangle2D.Contains` excludes the end | dungeon leaders "outside" by one tile | 11.09 |
| dungeons have no walking road from the island | parties carried down and back | 11.09 |
| `Guild.Enemies` is saved while guilds are rebuilt at start | "1,720 guilds at war with nobody" | 14.09 night |
| hidden staff above Player are invisible, walkable-through and, blessed, unharmable | Argus is safe by engine facts | 01.09 |
| a shard started from the session dies with it; a machine shutdown looks the same | silent deaths | 05.09, 11.09 |
| Ollama's thinking tokens are not in `eval_count`; two models do not fit in 12 GB | wrong latency figures | 20.08 |
| `Core.Now` is UTC, Serilog stamps local time | mismatched clocks | v1 |
| mount step delays (`walkMount` 200, `runMount` 100) were missing from the code | mounted bots too fast or too slow | 05.09 |

### C5 — Instruments that lie

**Definition.** A counter, log line, script or watcher reports something other than what it seems to measure.
**Sign.** A number that fits a beautiful hypothesis; a zero that means "never reached"; the same value twice.
**Remedy.** Rules O1–O6 in §2.7; nothing structural.

| date | where | what happened | fix |
|---|---|---|---|
| 17.08 v1 | a summary with an "other" branch | a busy economy reported as bots walking in circles | one count per goal |
| 17.08 v1 | counter scope | "asked 4963, 1 cast" counted every fighter | narrowed |
| 17.08 v1 | an honest diagnostic answering the wrong question | "engine says yes" 1,126 times; the refusal came from stamina | look between the check and the action |
| 17.08 v1 | one of two paths counted | "409 recipes known, 0 made" while a smith forged | one counter on both paths |
| 17.08 v1 | the session's own edits | 4 of 8 defects that evening were introduced that evening | 10 minutes of live log after every change |
| 24.08 | a kill diagnostic | "0 of 3 in reach, nearest −1" for a dead target | numbers only where they answer |
| 27.08 | a throttled line used as a measurement | four armour defects hidden | counters |
| 01–02.09 | Argus's findings | 10 false alarms against 2 real defects in a day, twelve forms | procedure O1 |
| 03.09 | idle line read the place after the rescue | "taking a full pack to the counter" the worst stuck for two days | fixed |
| 03.09 | `BotHomer.Describe` called from nowhere | a subsystem invisible | printed |
| 03.09 | progress judged by a display string (`{x:F1}`) | 8 paid lessons cancelled | `BotDeed.Still` |
| 04.09 | a skip counter above the time cut-off | 1,482 against at most 540 bots | moved below |
| 04.09 | "arrow orders stood on the board" | counted a fletcher's visits to one branch | reworded |
| 04.09 | the watch script's FLOW counted fills only | every percentage a third low | the market's TRADE line |
| 04.09 | Crossed/Dear frozen for five windows | stalls and wants held different kinds | read cuts |
| 04.09 | "don't chase Iman" | 25 of 38 named stucks; with the denominator 2.2% like everyone | denominator |
| 04.09 | a vein map snapshot | clean at 21:12, 457 of 512 by 23:12 | a series, not a snapshot |
| 05.09 | cumulative counters read per window | half a day of wrong comparisons | rule O2 |
| 05.09 | a counter of beats read as bots | "short of Bandage 9,637 times" | `BotMedic.Dry` |
| 05.09 | a grep pattern for stall purchases | "zero bought from stalls" | pattern |
| 05.09 | two confident wrong diagnoses before splitting a message | 233 of 430 "book would not take it" | split the reasons first |
| 05.09 | a denominator taken from failures | a sevenfold excess shrank to noise | base = finished work |
| 05.09 | a monitor that survived a context compaction as an old poller's labels | 0 matches, silence read as calm | `grep -c` every pattern |
| 05.09 | `Asked++` above a new throttle | "2631 asked" against gates summing to 500 | counter `Soon` |
| 07.09 | the first dump of the minds' state | four prompt defects, all mine | wording rules N4 |
| 07.09 | a wait script matched "0 caught swapping errands" | success reported on zero | match the event |
| 07.09 | an alarm pulse with a window equal to its beat | came every 2 min and printed "1m" | half-beat margin |
| 07–08.09 | `BotAudit.Freed`/`StillStuck` never printed | 1,120 interventions with no outcome | summary timer |
| 07–08.09 | "84% before / 23% after" | equal windows: 20% vs 23% | measure over hours |
| 07–08.09 | "51 forged on spec" (offers) | read as forging not finishing; it never started | pair of counters |
| 07–08.09 | a background check chose the freshest log when answering | 0 instead of 10 | log name fixed at launch |
| 08.09 | a limit justified by data from a sick shard | `Walkable` 240 from refusals caused by mount paralysis | re-measured |
| 08.09 | backstops at their ceiling, silent | `Most` 4,096 and `MaxSurveys` 16 read as an explored island | ceilings speak |
| 09.09 | `BotOffice.Lapsed` incremented where the flow never went | a structural zero | moved |
| 09.09 | a counter behind a gate | "227ms against 1700ms" read as bows never firing | measured on every call |
| 09.09 | the `pack` verb's first run | "Gold 340 surplus", a spare spellbook | repeats the sale's exceptions |
| 09–10.09 | one night of session ages and one-way knobs | eight wrong diagnoses; `SpareTools` and `CeilingMs` "causes" died on the reverse turn | rule O2 |
| 13.09 | "no way through" for three different walk endings | quarrel failures were chases (a road found in 0.1 ms) | endings named |
| 13.09 | `Homed` counted questions | 1,382 in 5 min against 80 births | counted in `TryPlace` |
| 13.09 | 12 h unattended | the `dying` alarm repeated 135 times, unread | monitors |
| 14.09 | `work-not-finishing` | cannot tell one looping bot from the shard (Quill, Kelda, Hale) | §5 S5 |
| 14.09 | `BotUnload.Jammed` printed as trips | counts offers, 5 on one trip | reworded, next build |

### C6 — State lost or wrongly kept across a restart

| date | where | what happened | fix |
|---|---|---|---|
| v1 | bots are serialized and purged at load | anything a bot accumulated died with a restart | `BotProgress` for skills, 25.08 |
| v1 | a multiplier applied to a saved spawner count | 43× after eight restarts | absolute bound |
| 19.08 v1 | "this square is already paid for" in memory | paid land bought again after every restart | written at the event |
| 26.08 | `Saves/Mobiles` and `Items` deleted to reset bots | Patrick's character deleted; restored from backup | never (§2.8) |
| 02.09 | every restart erases the ledgers | fixed loops revive for 10–15 min | open |
| 04.09 | a progress record dropped on a class change | 98k gold vanished with it | open (Patrick) |
| 05.09 | `BotGround` not saved | exhaustion hidden by restarts; cold starts know no counter | open, §5 S6 |
| 08.09 | purses not saved (Patrick's order) | — | by decision |
| 13.09 | `Reconcile` ended wars at start; truces and clocks in memory | a restart was an amnesty | `BotWarStore`, 14.09 night |
| 13.09 | a kill without `do save` | a hall back at its old place | `do save` first |
| 14.09 night | opinions in memory | a war "ended in peace" 15 min after a restart at 3:2 and the loser was exiled | store shape 2 |
| 14.09 night | the engine's enemy lists lost with rebuilt guilds | "1,720 guilds at war with nobody" | `Reconcile` re-asserts |
| 14.09 night | reach pockets not saved | the first company after every boot pays for two traps | open, §5 S6 |

### C7 — Reach, search budget and walk orders

| date | where | what happened | fix |
|---|---|---|---|
| v1 | the engine's 38-tile A* | greedy steps into walls | waypoints |
| v1 | an invented destination Z | arrival never counted | Z from the map |
| 25.08 | a walk order changing every beat ("distance − 1") | the route was rebuilt faster than walked; the bot never moved | stable orders |
| 25.08 | `BlindMs` seeded at construction, consumed by the walk | Merrick 36 of 38 fights "blind" | seeded in combat |
| 25–26.08 | `BotPeril.Middle()` with Z = 0 | patrols: 10 of 18 failures on high ground | `Settle` |
| 27.08 | seeing is not reaching, four times | vein behind a castle wall, the school, a patrol, a stable fence | `BotReach.Ask` in every place proposal |
| 03.09 | unreachability proven from the bot's side | 3,564 searches ended by the clock, 0 refused | `BotPath.Enclose` |
| 03.09 | the search clock priced for a crossing, spent on 3-tile chases | 469 ms/s spent, 28% starved | 0.25 ms per tile |
| 03.09 | knots of bots | four bots stood 4 min at (1344,877) | `Moving`, all eight directions |
| 03.09 | scouting across water | six bots walked for nothing | one real search before sending |
| 03.09 night | formation stations on crypt roofs | the Baron's company stood after every fight | `Within(2)` |
| 05.09 | `Nearest` by X and Y only | a fire twenty tiles up a cliff beat a fire on the road | noted |
| 07–08.09 | "no way through" | 29,858 searches, 376 s of the loop in 50 min | see C1 |
| 08.09 | mount stamina steps | 97% of all "did not get there"; 102,936 searches never returned `Sealed` | mount stamina off |
| 08.09 | area sampling against negative place notes | 249 of 350 failures still strolls | `BotQuad.Trodden` |
| 09.09 | chases with no distance limit | companies 737 tiles from home with `Roam` 200 | `BotSlay.Leash` 50 |
| 11.09 | a decided wall kept as a measurement | 70 walk failures of 2,460 to one hearth behind it | `BotBarred` |
| 13.09 | a distance-cheap search budget | 1,362 of 1,597 restock failures; a shop's counter sealed | reverted |
| 14.09 night | stations re-issued with the full ceiling | 1,534 full searches in 12 min, 475 slow ticks | `StationCeilingMs` 15 |
| 14.09 night | `Stalled` counted as a refusal | 9 teleports home in 22 min | `Refused`/`GaveUp` only |
| 14.09 | search cost rising over a day | 8.5 ms per look at night, 21 ms at 19:04; "looking for a fight" burned the ceiling 30,590 times | open |

### C8 — A remedy locked behind the condition it cures

**Sign.** The bot is in state X; the only way out of X requires not being in X. **Remedy.** Rule A3; nothing structural.

| date | where | what happened | fix |
|---|---|---|---|
| 21.08 | no starting purse | every deed with an outlay failed on its first beat; only digging was possible | `BotOutfit.Purse` |
| 25.08 | a company of one disbanded while the Baron mustered | 720 formed, 720 disbanded in 8 h | a charged company is not disbanded for size |
| 25.08 | a Bound company without a focus | bots with no source of work at all | `BotSquad.Hunt` |
| 03.09 | `Enclose` paid from the search budget it saves | 2 of 4 probes starved | paced by interval |
| 03.09 | a company survived a failed scouting; Bound skips the auction | "3 squads standing holding 15 bots" (44%) | disband in `Drop` |
| 03.09 | `Disband` released the leader only | five bots with no work, no idle clock, never sent home | `BotSquads.Disband(squad, why)` |
| 03.09 | Bound bots never offered the way home | Faron 4.3 min out of work, 330 tiles away | `BotSquad.Release` (Patrick's choice) |
| 04.09 night | prowl's charge never cleared | 94 of 136 stuck reports in 48 min | the charge is held by an errand |
| 04.09 | the smith's ring of gates | see C2 | see C2 |
| 05.09 | unload offered to an immobile bot needs a walk | Calla's treadmill | listing on the spot |
| 07.09 | unload needs a known counter; counters are learned by walking | Joss 13 min at 247 of 236 stones | offered without a counter |
| 08.09 | mount regeneration reset by blocked attempts | never recovers | mount stamina off |
| 09.09 | "cannot walk and nothing sellable" had no exit | Quill 143 failures | put things down |
| 10.09 | a war verdict needs wars that can end | none ended; the worst pair sat at −200 | kills in war do not move opinion |
| 11.09 | the guild door opened only at birth | five guilds of one member each | `BotRoster` |
| 11.09 | the island's refused-road rule underground | the first delve died in 41 s | `BotDelve.Bend` |
| 14.09 | a jammed pack refused counter work while unload was priced negative | Hale | unload unpaid when jammed |

### C9 — A side effect at offer time

| date | where | what happened | fix |
|---|---|---|---|
| 09.09 | guild offices claimed the guild at offer time for the whole length of the work | "the steward offered a hall 5 times", 3 raised; two guilds locked out 3 min each | `BotOffice` Offering 12 s / Hold |
| 09.09 | the first counter for it | incremented where the flow never reached | see C5 |
| 14.09 night | war company formed inside `Propose` | 15 formations in 10 min; "gone before it got there" | formed on the winner's first `Advance` |

### C10 — State nobody reads; comments and names that promise

| date | where | what happened | fix |
|---|---|---|---|
| 24.08 | minds' gate `Standing == Free` | never true; 0 questions asked | `>= Busy` |
| 25.08 | `BotClass.NeedsMeditation` | documented and read by nobody until armour existed | — |
| 02.09 → 14.09 | `DefendsOnly` set only by the deleted rangers' healer | healers spent 26% of their time looking for fights for twelve days | `BotHealer.Defends` |
| 03.09 | `StepAsideFor`'s answer discarded; `Walking` meant "holds a route" | a comment "proved" no deadlock from the name; four bots stood 4 min | `Moving`, answer used |
| 03.09 | a dissolved company still answering `Count`/`Ceiling`/`Map` | Doran 2 stood 45 min in it | `Disbanded` flag |
| 03.09 | company ranks yielding outside a fight | four bots stood 8 min | ranks only while fighting |
| 05.09 | `Abandon`'s comment "the ledger learns the place is bad" | `Bend` never called; the attempted fix was reverted as harmful | — |
| 05.09 | `BotShopper`'s comment listed "nobody can make it" as a refusal | the check did not exist | `Makeable` |
| 05.09 | the hearth guards | `Cautious` asked under the place name, written under the trade name; `Sealed` unreachable on the main land | named, noted |
| 07–08.09 | `BotLadder.Overloaded` | read by nobody | noted |
| 10.09 | `BotRegard.AtWar` | no caller anywhere: a declared war meant nothing | `BotFeud`, `BotQuarrel` |
| 13.09 | a company's target was whoever hit it | five of its own guild targeted in a quarter minute | `Friendly` |

### C11 — Coupled oscillation and herding

| date | where | what happened | fix |
|---|---|---|---|
| 07.09 | four identical minds asked at the same second | all four went to sew shirts | `Fellows`, `Stagger` |
| 11.09 | one measure for every guild's dungeon | five parties into the Orc Caves at once: 25 bots, 10 rooms | capacity and claims |
| 14.09 | a bonus (the two-minute dwell cap) moved the oscillation | 224 of 734 drops at 1.95–2.25 min; 80–86% took the same trade back within 10 min | commitment by events, build 34 |

### C12 — Two working mechanisms with no edge between them

**Sign.** Two healthy, growing counters and a third — the trade between them — near zero. **Remedy.** None structural;
§5 S7 for makers.

| date | where | what happened | fix |
|---|---|---|---|
| 24.08 | one call in the whole assembly raised a want | the Needs board empty for days | demand from armoury, upkeep, bullion |
| 25.08 | no bot knew how to carve | no leather existed | carving in corpse handling |
| 25.08 | nobody wore armour, so nobody wanted it | half the machinery idle | `BotArmourer` |
| 04.09 | stalls and wants never crossed | 69 sales and 9 fills with 290 stalls and 90 wants | `BotAuction.Cross` |
| 04.09 | nothing linked "the board wants feathers" to hunting birds | 261 fletchers refused "no feathers" | `BotQuarry.Sought` |
| 04.09 | gatherers sold without looking at paid wants | feathers for paid arrows sold to a shopkeeper | open Needs first |
| 04.09 | goods reached the market only through weight-triggered unload | "476 carrying enough" beside "243 could not find wood" | `BotUnload.Wanted` |
| 04.09 | a material ordered with no producer (bottles) | trade between bots 31.8% → 4.7% of the flow | reverted |
| 05.09 | the cooking chain broken in three places, each hiding the next | 0 meals | heat, meat kept for the cook, hunger clock |
| 05.09 | flat supply against skewed demand | the population covered 41% of its own reagent demand | least stocked kind: 96% |
| 08.09 | the hunter and the cook are one bot | 900 raw-meat wants, 0 supplies, 6,919gp in escrow | `Spares` respects wants |
| 08.09 | goods made to order almost nonexistent | 71 items to order against 2,952 on speculation | open |
| 10.09 | surplus had no exit | 74% of everything carried was surplus; Ulla 659 bandages | bank box, `Risk`, measured prices |
| 10.09 | eviction without a strike | "Move along", then nothing | `BotFeud` |
| 13.09 | one gathering point for the whole population | 3,597 of 3,597 resurrections at one point | `BotSeat` |
| 14.09 | goods nobody buys are still made | Hale sewed oil cloths all day: 40 price cuts, 5 take-backs | open, §5 S7 |

---

## 4. Decision log

### 4.1 Full entries, newest first

**14.09.2026 · build 38 · Argus can put a bot into the jammed state (`jam <bot>`)**
- *Problem:* build 37's fix could only be seen on a pack at the engine's cap, which takes hours to happen.
- *Decision:* a keyboard-only verb (`HandVerbs`, unreachable by any mind) that banks the pack's coin and fills the pack to
  its item cap with 0.1-stone oil cloths, so the bot is jammed and not overloaded.
- *Measured:* on Hale at 19:35:24 — 125 of 125 things, no room for a coin; 19:35:28 the sew already in hand failed once
  at the counter; the same second unload was taken at 103/min with "refused: sew goes through a counter and the pack has
  no room for a coin, at 125 of 125 things"; 19:36:43 unload finished, 113 things into the bank box; the same second mine
  was taken over "sew: after Cloth at 52/min" — sewing weighed normally again. Both directions shown.
- *Status:* active. *Code:* `mindedBots/debugger/BotHand.cs` (`Jam`).

**14.09.2026 · build 37 · Work at a counter asks whether the pack can take a coin**
- *Problem:* from 16:38 the alarm read 11–17% of work finished per half hour; one bot, Hale, failed restock 8,719 times in
  30 min with "the pack would not hold the Ngp it drew to pay with" (also sew 950, peddle 81). The market had handed back
  197 unsold oil cloths with `DropItem`, past the pack's cap of 125 things. Restock had been made unpaid that morning, so
  nothing learned from the failures. Third appearance of the line (09.09 courier, 10.09 courier), each time gated in one
  proposer only.
- *Options:* gate `BotRestock`'s proposer only (repeats the previous two fixes); a generic veto on every deed with an
  outlay (would also refuse stall and guild-merchant purchases and lessons, which never hand coin through the pack); a
  flag on the deeds that do change money over a counter, asked in the appraisal (chosen).
- *Decision:* `BotDeed.AtCounter` (restock at a shop, sew/brew/fletch/inscribe purchases, acquire's counter route, hire and
  supply before buying, peddle; a mind's deed passes its work's answer); `BotAppraisal.Weigh` vetoes it when
  `BotYield.Pocket` is false (no gold pile on top and `TotalItems ≥ MaxItems`), counter `Pocketless`; `BotListing.Return`
  gives stall goods back only as far as `TryDropItem` allows (the take-back in `BeatStalls` and the peddler's reclaim);
  the porter offers a jammed pack the trip that makes room, unpaid (`BotUnload.Jammed`). Checked against A3: the remedy
  (unload) does not go through a counter, so the veto does not lock it.
- *Expected:* no coin-bounce loops for any counter deed; jammed bots unload and resume.
- *Measured:* build 38 after 20 min — 78% of endings finished, 0 "pack would not hold" lines; live test above.
- *Side effects:* one failure of work already held when a pack jams (the veto acts at choice time). `Jammed` was worded
  as trips but counts offers (fixed in source, next build).
- *Status:* active. *Open:* about thirty `DropItem` calls still put goods into packs past the cap (loot, spoils, deliveries,
  stall purchases) — §5 S3. *Code:* `BotWill/BotDeed.cs`, `BotYield.cs`, `BotAppraisal.cs`; `BotAuction/BotListing.cs`;
  `BotPopulation/BotUnload.cs`; the eight deeds named.

**14.09.2026 · build 37 · A healer that could not keep up is not sent after the same fighter at once**
- *Problem:* escort is unpaid; Otho was sent after Maeve 48 times and Faron 16, each stint bending six times and ending
  "could not get nearer".
- *Decision:* a stint given up for falling behind sets the pair aside for `BotAccompany.ShunMs` (5 min); `BotAttendant`
  passes that fighter over (`Outrun`, `Passed`).
- *Measured:* no escort failures in build 38's first 20 min; the shun itself not yet seen firing.
- *Status:* active.

**14.09.2026 · config · The night's settings put back**
- *Problem:* `Price` 2000, `Keep` 100, `TaxShare` 0.0 and `CrafterNames` [] had been set for the night of 13→14.09.
- *Decision (Patrick):* back as they were — the keys removed from `bot-estate.json`, `bot-debugger.json` and
  `bot-mind.json`, so the code's values apply: a hall at 5,000 with 300 kept back per member, the crown's tax 0.06, five
  crafter minds (Roderic, Emeric, Ulric, Wulfric, Alaric).
- *Measured:* boot lines "a hall at 5000gp … each keeping 300gp back" and "5 minds are awake"; dials read 5000, 300, 0.06.
- *Side effects:* five crafter minds and three watchers share one Ollama slot for the first time. *Status:* active.

**14.09.2026 · build 35 · Restocking made unpaid**
- *Problem:* 721 refusals "restock is expected to pay −N" in 108 min while "nothing to write with / brew with / dig with"
  were among the commonest failures.
- *Decision:* `BotRestock.Unpaid` — priced at its typed claim, never refused for costing money.
- *Side effects:* removed the only brake on a restock that fails; no gate was added for its failure reasons, although the
  lesson was already recorded (C3). Caused the 14.09 16:38 loop.
- *Status:* active, now guarded by build 37's counter veto. The proper remedy is §5 S4.

**14.09.2026 · builds 34–36 · Commitment by events; roles**
- *Problem:* 31% of all dropped work was dropped at exactly the two-minute cap; 80–86% of dropped trades were taken back
  within 10 min; mining finished 52% and dropped 28%. Healers spent 26% of their minutes looking for fights and 10% on
  their own trade.
- *Decision:* steadfast work held for its reckoning × 1.5 (max 8 min), only events get through, displaced work paused and
  resumed (build 34); `BotCalling` own ×1.3 / other ×0.6, `DefendsOnly` restored for healers, escort (build 35); class
  book order, escort bends to keep up (build 36). Research in `RESEARCH-decisions.md`.
- *Measured:* A/B on one shard — holds on 80% finished, 8% dropped, mining 65/7; off 68%, 16%, mining 33/32; back on.
  Healers' own trade 23% → 40%. Fighters and casters unchanged.
- *Status:* active. *Open:* role-true work for fighters and casters.

**14.09.2026 night · builds 13–33 · Wars survive restarts; unpaid claims taken as typed**
- *Problem:* a restart was an amnesty (wars ended, truces and clocks forgotten, opinions reset, engine enemies lost);
  `BotCommons.Corrected` floored unpaid claims to a quarter.
- *Decision:* `BotWarStore` (wars, truces, clocks as durations, move debts, opinions); `Reconcile` re-asserts engine
  enemies; a peace names a winner only with a margin ≥ 5; unpaid claims skip the correction.
- *Measured:* two wars ran from declaration to exile in 12–13 min each without intervention; completion 71–77%.
- *Side effects:* hire began to win auctions and loop (4,365 failures in 43 min) — unpaid work has no learning.
- *Status:* active.

### 4.2 The month, by period

Status: **A** active · **S** superseded · **R** reverted · **v1** gone with the first assembly.

**Foundations and v1 (15–21.08)**

| date | decision | answered | result | status |
|---|---|---|---|---|
| 15.08 | era UOR | smallest AI surface; best-covered content | — | A |
| 15.08 | all bot code in a separate assembly; engine untouched | rebase path | two engine patches since | A |
| 15–17.08 | bots inherit `PlayerMobile` | skill gain, loot, flags come free | — | A |
| 15–17.08 | bots saved but purged at load and rebuilt | no per-entity refusal in the serializer | — | A |
| 16–17.08 | restart the shard in session and read the log | invisible defects | four found in an hour | A |
| 17–18.08 | one count per goal; refusal counters with denominators | an "other" branch hid the economy | — | A |
| 19.08 | persist at the moment of the event | Patrick kills hard | — | A |
| 21.08 | v1 deleted; v2 with one config per subsystem | Patrick's order | 0 warnings | A |
| 21.08 | home at (1440,1470) outside the town region | no fighting in a town region | — | S (seats, 13.09) |
| 21.08 | starting purse | every outlay failed on its first beat | — | A |

**v2 foundations and the first chains (22–27.08)**

| date | decision | answered | result | status |
|---|---|---|---|---|
| 24.08 | minds as a separate assembly; a mind names a trade, a real deed runs | v1 advisor lost every argument | — | A |
| 24–25.08 | demand generators (armoury, upkeep, bullion, owed) | one want in the whole system | market alive | A |
| 23–24.08 | formation measured from the target | 19 kills vs 56 draws | 6 kills vs 1 draw | A |
| 25.08 | first closed chain (carve → market → tailor) | no leather anywhere | chain closed | A |
| 25.08 | buying draws the shortfall from the bank | 1,929 fruitless restocks in 30 min | bandages bought | A |
| 25.08 | skills persist (`BotProgress`) | skills reset every morning | leather armour reachable | A |
| 25.08 | mind's bid = worth × `Insistence`; forecast measured | the forecast-bid exploit | — | A |
| 25.08 | Captain class; flags `Seasoned`, `Leads`, `Closes` | Patrick's order | — | A |
| 25.08 | armour catalogue from the engine; protection per gold | nobody wore armour | — | A |
| 25.08 | frozen-work backstops (`StallMs`, `LabourMs`) | bots silent for hours | — | A |
| 25.08 | `LeastPurse` 0.1 | a broke bot forbidden to look for work | failures 45% → 20% | A |
| 26.08 | paid orders before the hardest recipe | 0 fills with a paid board | first fill in 23 s | A |
| 27.08 | Baron class; `Sworn`, `Unpaid` (spoils), `Grieves`, `Stipend` | Patrick's order | — | A |
| 27.08 | a purchase is a reflex at the counter | the horse errand poisoned itself | — | A |
| 27.08 | every place proposal asks `BotReach` | four places seen but not reachable | — | A |
| 27.08 | documentation in English; UOBot mirror | public repository | — | A |

**Getting stuck and loops (01–04.09)**

| date | decision | answered | result | status |
|---|---|---|---|---|
| 01.09 | Argus, a watcher that is not a `BotMobile` | Patrick's order | 10 false alarms vs 2 defects on day one | A |
| 02.09 | the door (`argus-in/out.txt`) | restarts erased evidence | — | A |
| 02.09 | class targets 100 with seasoning 0.78; lesson fee 60+20, rest 20 min | Patrick's order | — | A |
| 02.09 | `BotHomeward`; `Settle` clears a dead route; `Unfixate` | no work "go home"; ghost fights | — | A |
| 03.09 | proof from the far side (`Enclose`), paced by interval | 0 pockets ever | 57% → 64% reached | A |
| 03.09 | search clock by distance of the target (0.25 ms/tile) | 28% starved | 469 → 187 ms/s | A |
| 03.09 | common exit `BotDeed.Drop` for marks and disbanding | silent endings | — | A |
| 03.09 | Bound members released from idle companies (Patrick) | four released in 6 min | turnover unchanged | A |
| 03.09 | measure the outcome, not the cost | a cheaper search cut work 11% | reverted | R |
| 03.09 night | quadrant safety by Patrick's table | danger-map ratchet | — | A |
| 04.09 | one combat verdict; the one who can strike is not moved | 41% hopeless company fights | 14–16% | A |
| 04.09 | `BotDeed.Still` | paid lessons cancelled | — | A |
| 04.09 night | population 34 → 54, combat classes; name pool 64 | Patrick's order | — | A |
| 04.09 night | charge held by an errand; one "stopped closing" rule (`TrekLimit` 600) | zombie companies; copies of one rule | 14 of 1,347 deeds | A |
| 04.09 | arrow chain (fletching, chopping) | the only consumable without a source | — | A |

**Economy (04–05.09)**

| date | decision | answered | result | status |
|---|---|---|---|---|
| 04.09 | `BotAuction.Cross` | stalls and wants never met | — | A |
| 04.09 | bottles in `BotStores` | glass for brewers | trade between bots 31.8% → 4.7% | R |
| 04.09 | `BotUnload.Wanted` | goods parked below the unload threshold | "could not find wood" 77% → 13% | A |
| 04.09 | `Dwell(deed)`, cap 120 s | forge dropped at 30 s | — | S (holds, 14.09) |
| 04.09 | `BotAnvil.Fetch` where the shortage is found | the smith's ring of gates | "own out on a stall" 160 → 4; the smith's board orders 4 → 99 | A |
| 05.09 | Patrick's night orders: `StaleMs` 10 min, jar caps, muster zone 24, needs once a minute, horses for all, mount delays | orders | between bots 7,288 → 20,485gp | A |
| 05.09 | `BotAnvil.Tries` 3 in both choices | half the material lost per miss | 8 of 10 trips with an item | A |
| 05.09 | opening price from the NPC's shelf | 81% of supply money left the world | 173/41 → 1/311 | A |
| 05.09 | least-stocked kind for gatherers | 41% own coverage | 96% | A |
| 05.09 | `BotProspect`; wood only outside the town | veins exhausted in 2–3 h | unverified | A |
| 05.09 | `MAP.md` | context spent finding files | — | A |
| 05.09 | detached shard start (Task Scheduler) | the shard died with the session | — | A |
| 05.09 | `Abandon` marks a counter through `Bend` | an assumed cause | jams 8 → 11 | R |

**Observation tools and navigation (07–08.09)**

| date | decision | answered | result | status |
|---|---|---|---|---|
| 07.09 | live dials through the door, never written to files, unreachable by minds | restarts cost hours of warm-up | 558 dials | A |
| 07.09 | the shard's own alarm channel (`alerts.ndjson`) | nobody reads a log | — | A |
| 07.09 | office minds stood down; four crafter minds (Patrick) | orders | — | A |
| 07.09 | `BotStall` tile clock and churn | a watch reset by errand swaps | — | A |
| 07.09 | `Unpaid` flag | the Baron refused his only work | — | A |
| 08.09 | `Settle(unreached)` | a road that does not exist priced as land | "nothing worth doing" 9,099 → 2 | A |
| 08.09 | mount stamina off (`modernuo.json`) | 97% of "did not get there" | stucks 3,679 → 0 | A |
| 08.09 | `BotQuad.Trodden` for area sampling | negative notes never hit | — | A |
| 08.09 | guild buys the hall; `Price` 5,000, `Keep` 300 (Patrick) | houses cost 35,250 | first halls | A |
| 08.09 | offer to every member, claim renewed by the work | one appointed hand | first hall | A |
| 08.09 | muster radius 120 with `MostBound` 0.34; help priced by need | 82% musters refused; 36 of 49 Bound | answers 59% | A |
| 08.09 | overload: veto → factor → rank (09.09) → `Committed` (10.09) | heavy bots | walking takes 155 → 28/h | A |

**Guilds, lands and war (09–11.09)**

| date | decision | answered | result | status |
|---|---|---|---|---|
| 09.09 | Patrick's guild rules (5–15, one maker); muster once | per-bot enrolment made castes | five guilds of ten | A |
| 09.09 | guild counter (shelf, supply, supplier) | Patrick's order | 4 counters, 119 lots | A |
| 09.09 | lands as a factor; regard per guild pair | Patrick's order | — | A |
| 09.09 | war on; chase leash 50; pick swing 2 s; vein empty by the engine's word (Patrick) | orders | 462 trees by the engine's word in 10 min | A |
| 09.09 | engine patch `HarvestDefinition.Said` | quiet swings unexplained | "nothing here" 93, miss 25 | A |
| 09.09 | `Made` = own contribution for guild-paid work | the shard learned not to build | — | A |
| 09.09 | mining judged below the swing throttle | "island exhausted" | mining 28% → 85% | A |
| 10.09 | surplus to the bank box; `Risk` 8 lots; measured prices | 74% of carried weight surplus | 73–78% → 43% at 105 min | A |
| 10.09 | `BotFeud`/`BotQuarrel`; `Enmity` −40 | war only on paper | wars began | A |
| 10.09 | exile of a war's loser (Patrick); kills in war don't move opinion | order; wars could not end | — | A |
| 10.09 | quadrant claims (Patrick's prices) | order | two squares in 5 min | A |
| 11.09 | crafter minds lead guilds (`BotCharter`); fifth mind | Patrick's order | orders given | A |
| 11.09 | dungeons by carrying; difficulty measured by the engine; halls map | Patrick's order | 40 kills in 5 min | A |
| 11.09 | `BotRoster` (recruit, expel) | guilds of one | — | A |
| 11.09 | `BotBarred` (Patrick: forbid the closed area) | a decided wall kept as a measurement | — | A |
| 11.09 | autostart at logon | machine shutdown killed a two-day run | — | A |

**Wars with an end, seats, the night (13–14.09)**

| date | decision | answered | result | status |
|---|---|---|---|---|
| 13.09 | `shard-status.sh`, monitors, war notes in the alarm channel (Patrick) | watching while working | — | A |
| 13.09 | `BotWar` registry: limits, truces, cooldowns, defence first (Patrick) | 3,054 bots killed by bots in 12 h | 14–18 per 20 min | A |
| 13.09 | `BotSeat` (Patrick: south, east, west) | 3,597 resurrections at one point | first 13 deaths in four squares | A |
| 13.09 | war companies (`BotFeud.Rally`) | Patrick's fifth order | companies of 15 and 12 | A |
| 13.09 | boldness: `Fit` 0.5, `Answer` 100, cry reach 60 | order | 46–98 defenders | A |
| 13.09 | speed pass: one 150 ms escalation after the far side, chase slack by plan length, `The clock:` | pathfinding two thirds of the bots' cost | — | A |
| 13.09 | search budget by distance | cheaper search | shops sealed | R |
| 13.09 night | squad of three watchers; reset = skills, halls, claims, seats; 25 kills / 5,000gp / daily clocks; allies; blue bound weapons (Patrick) | orders | — | A |
| 13.09 night | crafters ordinary bots (Patrick); hall price 2,000, keep-back 100, tax 0 (my dials for the night) | the night's watch | — | R (14.09 evening) |
| 14.09 night | the store, reconcile, peace margin, sortie, `Unbound`, station ceiling, reach asked for company targets | restart amnesty and night defects | two wars end to end | A |
| 14.09 | commitment by events; roles; unpaid restock; escort | Patrick's order | see §4.1 | A |
| 14.09 | counter veto; pack-respecting take-back; jammed unload; escort shun; `jam` | Hale's loop | see §4.1 | A |
| 14.09 | this file (Patrick) | a month of recurring classes | — | A |

---

## 5. Structural remedies not yet built

Proposed on 14.09.2026. Each would close a class rather than an instance. Waiting for Patrick's choice of order.

**S1 — A failure breaker in the auction (closes C1).** When the same bot fails the same kind of work — for the same
target or place when it has one — N times within M minutes, that kind is not offered to that bot for a cooldown,
whether the work is paid or unpaid; every trip of the breaker is counted and printed, and the alarm reads it. It
replaces eight home-made lists (`Wedged`, `Shun`, `Beware`, `BotRefused`, `_behind`, `Crowded`, `_dry`, `Baulk`) with
one rule. Risk: it can hide a real defect, so it must speak every time it trips; it must key on the target for work that
legitimately retries (hunting).

**S2 — Scenario checks through Argus after every build (closes the recurrences in C1, C2, C8).** A script that uses the
door to put the shard into known states — a jammed pack, an empty vein, a bot in a pocket, an overloaded bot, a full
board, a company whose leader dies, a declared war — and checks the expected log lines and counters. Every fixed defect
becomes a check that fails if it returns.

**S3 — One way to put a thing into a bot's pack (closes C4 and C8 for packs).** A single function that uses the engine's
own test (`TryDropItem`), falls back to the bank box, and only then to the ground, replacing about thirty `DropItem` calls
in loot, spoils, deliveries, stall purchases and returns.

**S4 — Preconditions as data, and enabling work priced by what it enables (closes C3 for supplies).** Each deed declares
what it needs — a tool, material, room in the pack, money, reach, skill — and the auction checks them uniformly before
weighing. Restocking, getting a spell and unloading are then worth a share of the work they unlock, instead of being
unpaid (`RESEARCH-decisions.md` §6.1).

**S5 — Alarms that name the bot (closes C5 for loops).** `work-not-finishing` prints the largest (bot, kind, reason)
group of the window and its share, so one looping bot never reads as the whole shard.

**S6 — Persist reach pockets and the ground map (closes C6 for navigation).** Pockets and veins, counters and hearths are
written and verified on load, so the first company after a boot does not pay for known traps and exhaustion is not hidden
by a restart.

**S7 — Makers read demand before making (closes C12 for goods).** A maker does not choose a product whose stalls stand
at their lowest price unsold, or which was taken back from the market, until something changes.

---

## 6. Decisions waiting for Patrick

- The order of §5, if any of it.
- Push the mirror branch `bots/commitment-and-roles` to GitHub (the repository is public).
- Role-true work for fighters and casters (`RESEARCH-decisions.md` §6.2).
- `Barred` boxes for the two known traps (north 1319,1047; south-west ~1133,2237), or persisting pockets (§5 S6).
- The Crown's and the Blade's seats, 62 tiles apart against a radius of 40.
- Whether losing a war also cools the loser's clock for declaring one.
- The lesson price and the captain's till (a sink); money discarded with a progress record on a class change.
- `StaleMs` as one number for two opposite jobs (seller's cut, buyer's raise); `BotStores.Reserve` 150 against poor
  crafters' purses.
- `BotAppraisal.Considerations` 5 against more than eight factors.
- `BotHarrow.Grandmasters` 15 while the population has none.
