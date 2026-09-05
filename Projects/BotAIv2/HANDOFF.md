# Handoff: the state of BotAI v2 on 05.09.2026

Twenty-three folders, 212 source files, about 70,000 lines. **It builds clean against the fork** —
`TreatWarningsAsErrors=true` there, so clean means genuinely clean — into `BotAIv2.dll` (689 KB) and
`BotMindAI.dll` (210 KB).

This file is the state of the work: what is running, what is measured, what is known to be wrong, and what to
do next. For where anything lives, `MAP.md`. For what a number is set to, `DIALS.md`. For how the thing is put
together, `ARCHITECTURE.md`. For what it is at all, `README.md`.

> **One engine patch is now required.** `engine-patches/CraftItem-heat-source.patch` — seventeen lines
> exposing two questions `CraftItem` already answers privately. Without it this assembly does not compile.

---

## What is running

Fifty-three bots outside Britain on Felucca. Over a thirty-five minute session:

```
Will: 2223 taken on, 1731 finished, 244 failed, 195 dropped, 0 died doing it;
      44 times nothing was worth doing
The market: 249 of 1024 stalls holding 1431 things worth 8128gp and 82 of 512 wants
      for 97 things with 5686gp down; 196 sales and 67 fills for 5626gp
Money: 49 purses that were earned: poorest 0gp, middling 221gp, fattest 548gp,
      10772gp between them with 7216gp of it in pockets
```

78% of everything taken on finishes. Nothing died doing its work. Forty-four beats out of 2223 found nothing
worth doing at all, which is the number that used to be the whole problem.

**Trades that close end to end**, each measured on a live shard rather than reasoned about:

| trade | reading |
|---|---|
| smithing | `14 stints at an anvil ended: 12 with 30 pieces beaten out, 1 out of metal` |
| tailoring | `423 asked: 135 took an order off the board, 234 sewed on spec` |
| cooking | `452 put something on … 112 stacks of raw meat kept back … 29 meals eaten` |
| alchemy | brews on spec, blocked on herbs — see below |
| inscription | writes scrolls, and casters buy them off the market |
| mining, woodcutting, herb picking | all three cut, and as of today all three sell |

---

## What was fixed on 05.09.2026, and how it was found

**Cooking was broken in three separate places**, each of which alone made the chain read dead, and each
invisible until the one before it was fixed. The requirement for fire lives on the *recipe*
(`SetNeedHeat`) rather than on the system's `CanCraft`, and the engine refuses in silence. The hunter listed
the meat before the cook ever saw it. And `Mobile.Hunger` is a one-way ratchet — `Food.FillHunger` adds and
refuses at twenty, nothing anywhere subtracts — so a bot would have eaten five suppers in its life and been
full for ever. Fixed, and cooking now runs from carcass to eaten meal.

**Smithing was not underpriced, it was under-supplied.** The ledger had it at 110–1178 a minute already. What
was wrong: a failed swing eats half the material (`ConsumeRes` with `isFailure`), so a smith setting out with
exactly one piece's worth of metal missed twice and walked home. `BotAnvil.Tries` is three now, used in both
the recipe choice and the metal choice, and the shape changed from three stints in ten producing something to
eight in ten.

**Woodcutting and herb picking had no ending at all.** They swung, counted and stopped, so the goods rode
home in a pack: `133 of 212 fletchers could not find wood` while woodcutters walked past them carrying it, and
`herbs` was the second commonest thing anybody did beside a brewer reading `607 had the glass but no herbs`.
Neither trade was broken — there was no edge between them. `BotAuction.Offer` is the shared ending now: a
funded order first, a stall second, and a working handful kept back. First window after it, against nought
before: 2327 reagents onto stalls.

Then the other half of the same trade: those reagents opened at five where a herbalist sells garlic at three,
and `BotShopper` takes whichever of stall and counter is cheaper — so every caster that wanted one walked
past 1986 bot-owned reagents and paid a shopkeeper. Picked reagents now open at the shelf price, measured off
the engine rather than declared. The other five opening prices were audited against what actually sits unsold
and are correct: potions open at fourteen under a shelf of fifteen and fall to six because supply outruns a
demand capped at five a bot, which is the market working rather than failing.

**233 of 430 scroll purchases were successes filed as failures.** `BotSeeker` buys a scroll to write into a
book and `BotArmoury` buys one to throw; both built the same undertaking, which tried to put every scroll into
a book. Warriors stocking magic arrows exactly as designed were recorded as having failed, and the ledger
priced the trade off those reports. Split by `BotAcquire.Purpose`. Underneath it was a real refusal running
the other way: a caster that knew a spell could never buy a scroll of it to throw.

Worth keeping about *how* that was found: two confident diagnoses came first and both were wrong, and the
second was written and deployed before the line failed to move. What settled it in two minutes was making the
message name which of its four ways it had failed.

**Where the island's supply money went.** Herb picking reached the market and 1986 reagents went onto stalls
at five gold, against a herbalist's three — and `BotShopper` takes whichever of stall and counter is cheaper,
so every caster walked past the population's whole supply and paid the world. `173 sent to a shopkeeper, 41
to a cheaper stall` became `1 sent to a shopkeeper, 311 to a cheaper stall`. Opening prices are now measured
off the engine through `BotShops.Shelf` rather than guessed. The other five openings were audited against
what actually sits unsold and left alone.

**A keep-back that blocks a paid order is a hoard.** The woodcutter held twenty logs for its own fletching
while a fletcher's funded order for exactly twenty stood unfilled — most cutters are gatherers and carry no
fletcher's tools. Both the wood and the herb keep-backs now ask whether *this* bot can use the thing, which
is what `BotOven.Spares` has always asked about the cook's meat. `0 logs to an order and 0 onto a stall`
became `15 to an order and 45 onto a stall`, and the fletcher's `could not find wood` went from 133 of 212
to nought.

**Read the summary as running totals.** Every counter in it is cumulative since boot — the `Forget()` that
would reset it runs only on a world reload. Proved on three consecutive summaries of one run: brew asks
293 / 554 / 857. Rates are differences between summaries; two runs compare only at the same age. This was
assumed the other way round while reading today's measurements, and the one conclusion it weakened is the
brewer's, noted immediately below.

**The brewer's herbs were nobody's errand.** `BotShopper` buys reagents for a build whose *kit* declares
them — every caster, no crafter — so the one bot carrying a mortar was never sent for the half of its trade
it cannot gather. The tool decides now, and the reagents come from `BotFlask.Needs` — the two the draughts actually burn rather
than the caster's eight, which at one kind an errand would have been forty minutes of shopping before the
brewer reached either of its own.

**Both were built on a reading that turned out to be wrong, and neither was the cause of anything.** The
number that prompted them — "had the glass but no herbs", steady at 82–86% of new asks across eight
consecutive windows — was measured properly once the bucket was split, and it came back **79 of 79 at the
cap**. Not one lacked a reagent. Not one lacked the skill. A brewer holding five heal and five cure across
its pack and its own stall has made everything the population will take and is standing off for ten minutes,
which is what the cap was ordered to do. The steadiness was the tell and it was read as its opposite: no real
shortage sits at 85% for eight windows.

Both changes are kept — a brewer wanting what it burns is right on its own terms, and reaching it by its tool
rather than its class name is the rule the rest of the assembly follows — and the commits say plainly that
they fixed nothing.

**What the brewer is actually short of is glass**, exactly as its own file has said all along, and glass is
the one material with no producer: it trickles back a bottle at a time from whoever drinks a potion, and
ordering it by the armful was tried on 04.09 and took four fifths off the shard's trade in half an hour. The
line now reads `0 had the glass and not the reagent`, which is the whole answer.

---

## What is open

**Fletching takes no orders, and that is answered rather than open.** `0 took an order off the board` in
every session on record, and the reason is that nobody wants arrows: `BotArms.Quiver` counts empty quivers
and has read nought every session, because archers are born with arrows, buy them, and pick roughly four in
ten back out of whatever they shot. So the trade can only ever sell on spec, and the board path has never
been exercised — which is worth remembering the day something makes archers spend faster than they recover.

**Fletching is now blocked entirely on feathers.** Wood is solved — `could not find wood` went from 133 of
212 to nought — and the whole trade moved onto the other half: `43 asked to fletch, 43 had no feathers`, with
no bird killed in the window and the keep-back never firing. Not urgent, because arrows have no demand (see
above), and **not** the glass mistake either: two feather orders were raised and both were filled, so nothing
is freezing escrow. It is simply a trade with no supply and no customer.

**Being watched: whether a funded order steers a hunt.** `BotQuarry` chooses a kill by what the carcass
carries, and there is now a paid want for ribs. `60 kills were chosen because the board wanted what the
carcass carries` over 35 minutes before any meat order existed, against 4 over 6 minutes after — the same
rate inside the noise, on windows too different to compare. Six meat wants were filled. If that rate does not
climb over a long session, the ask is not reaching the hunter.

**Open, with numbers and no conclusion: is the meat bounty costing the shard its ore?** Letting a funded
order steer a hunt works — the top quarry over the same twenty-two minutes went from ettin 16, mongbat 11,
cougar 10 to grizzly 10, troll 9, ettin 8, horse 7, black bear 6, and the steering count went from a plateau
at 45 to 128 / 341 / 575 / 1219. In the same comparison smithing collapsed:

| | before | after |
|---|---|---|
| forge stints taken | 7 | 1 |
| mine stints taken | 47 | 25 |
| `short of metal` | 70 | 149 |
| ground swept | 11 sweeps, 512 seams | 3 sweeps, 319 seams |

The smith is **not** outbid — `0 with metal but not enough for any recipe`, and forge appeared as the
runner-up four times in a whole run. It has nothing to work. The seam map is built by bots walking and is
never saved, so less walking is less ore, and the obvious story is that meat-bearing quarry grazes nearer the
towns than undead and giants do.

**That story does not fit the rest of the evidence.** Path searches fell (12852 → 9737) while tiles examined
rose (347M → 416M): the population is walking *less often and further*, not closer. One run against one run,
on a quantity already known to vary with wherever bots happened to wander.

**Half an hour later the same run answers most of it, and the answer is warm-up.** Sweeping resumed —
`3 sweeps: 319 seams` stood for five summaries and then went to `6 sweeps: 371 seams` — mining recovered from
25 stints to 61, forge from 1 to 3, and the shortage itself is decelerating. Read as a series, which is the
only way a cumulative counter can be read:

```
short of metal:  48  91  129  149  171  184
     added:        +43 +38  +20  +22  +13
```

So the collapse was a young shard with an empty seam map rather than a price paid for the bounty. Still worth
a long run before it is called settled.

And the weak joint it exposed stands whatever the answer: **the ore map is a by-product of somebody else's
route.** It is built by bots walking, never saved, and a trade whose supply depends on where a different trade
happened to wander will keep producing this shape — a craft that looks broken on a young shard and cures
itself an hour later, with nothing anywhere saying which it is doing.

**Supply drifts back to the shopkeepers as a run ages, and it has now done it twice.** Pricing a picked
reagent at the shelf moved supply buying from `173 to a shopkeeper, 41 to a stall` to `1 / 311`. That holds on
a young shard and then decays: on one run it reached `1879 / 1944`, and on the next it crossed over at
`1206 to a shopkeeper, 1179 to a stall` after about an hour. Two runs, same shape, so it is not a fluke of
one.

The plain reading is that stalls open full of what the gatherers have just listed, the population empties them
faster than the gatherers refill, and the rest is bought over a counter — which is money leaving the world,
increasingly so the longer the shard lives.

**Measured, and the answer is capacity rather than a defect.** The stall stock over one run:

```
stalls:   162  219  249  282  320  353  368  391      rising
things:  2091 3003 2170 2067 2014 1586 1110  863      falling by 71%
worth:  12940 17363 9562 7700 6702 5417 4350 4574
```

More sellers holding less each. The goods are not evaporating — 505 sales and fills against 79 listings
forgotten — they are being bought: 2491 reagents listed over the run against 1213 purchases off stalls and a
net loss of 2140 things. Demand outruns supply, the stalls empty, and the rest of the shard's shopping goes
over a counter, which is money leaving the world. Purses fall while turnover rises, which is the same fact
seen from the other end.

**That was not it either, and the real answer is composition rather than capacity.** Two distributions, laid
side by side for the first time — what the population buys over a counter, against what its own gatherers put
on stalls:

| | bought at a counter | listed by gatherers |
|---|---|---|
| SulfurousAsh | **1320** | 301 |
| Bloodmoss | 393 | 135 |
| Nightshade | 314 | 215 |
| Ginseng | 243 | 191 |
| Garlic | 61 | **259** |
| BlackPearl | 16 | **257** |

Demand is lopsided fourfold; supply was perfectly flat, because the pick was
`Kinds[Utility.Random(Kinds.Length)]` — an eighth of each. So the ash, over half of everything wanted, ran
dry at once and was bought over a counter, while the garlic and pearl nobody wanted made up the thousand
unsold. Neither number says anything alone: the stalls are full, sales are brisk, the gatherers are working.
It is only visible as a pair.

`BotHerbs` now picks the kind with least on the stalls. Measured on the first fifteen minutes of each run:

| | before | after |
|---|---|---|
| bought off stalls | 1051 | 538 |
| bought at a counter | **798** | **25** |
| reagent demand met by the population | 57% | **96%** |

Counter buying fell thirty-twofold, and the turnover fell with it because a bot no longer buys twice — the
first purchase used to be the wrong kind.

**And the volume behind it, now that composition is out of the way — the island supplies about a ninth of its
own reagents by construction.** Herb picking is gated per bot by `BotClass.HerbIntervalMs`:

| class | interval | of 53 |
|---|---|---|
| `Gatherer` | 15 min | 2 |
| `Sage` | 30 min | 1 |
| `Mage`, `WarriorMage` | 45 min | 12 |

Fifteen bots may pick at all, and at those intervals that is `2×4 + 1×2 + 12×1.33 ≈ 26` trips an hour, about
9 herbs a trip, so roughly **230 reagents an hour against a demand near 2700**. Not a defect and not
something code fixes: it is two settings — the intervals and the class mix — and both are Patrick's.

**A warning that goes with those numbers.** Every one of the fifteen is free at boot, so a fresh shard makes
its whole first hour's worth of trips in the first four minutes and then goes quiet for fifteen to forty-five.
Read at twenty minutes that looks exactly like gathering having stopped, and it was read that way here before
the intervals were checked. The counter series says it plainly — `trips offered: 158 158 158 170` — and the
170 is the two Gatherers coming round.

**Why the stalls and not the shortage tally.** `BotShopper` counts what bots have been short of and was the
obvious source; it is cumulative for the life of the shard, so a reagent that ran dry an hour ago still reads
as the scarcest thing on the island and the gatherers would have chased it for ever. Stock on the stalls is a
fact about now and puts itself out.

**Open defect: 36 funded orders for metal, none of them ever filled.** Smiths put money down for ingots,
miners list ingots — 62 listings of copper, iron and bronze — and the two have not met once in a run:

```
36 ordered metal        0 metal wants filled        0 ingots bought off a stall
```

Meanwhile `544 of 763` smiths answer "short of metal" and no stint has ended in the last fifty minutes,
with mining perfectly healthy at 106 stints and the seam map fully built at 512 seams. It is not the auction:
forge asks 340/min when it does ask, against hunting's 223, and appears as runner-up seven times in a whole
run. It has nothing to work with.

**Narrowed as far as reading allows, and the first thing found is that the line itself is false.**
`BotBullion` increments `Ordered` *before* it builds the errand, so "36 put the order to the population"
counts intentions rather than wants. Not one want for an ingot was raised in the whole run — 460 fills, none
for metal, and no `wants N Ingot` line anywhere. It is the counter-that-names-one-cause-and-catches-all
again, in a file this project has already corrected twice for it.

`BotOrder.For` refuses only when the wants board is full, and it was at 96 of 512. So the errand is built and
then never appears in the auction, as winner or as runner-up, while leather orders from `BotUpkeep` at the
same price are taken twenty-six times each.

**Two candidates, neither accepted:**

1. The metal errand loses every auction. It asks about 5/min like the leather ones, but leather orders are
   raised by bots with nothing better to do while metal is wanted by crafters who also hunt and mine.
2. Something drops it before the auction, and nothing in the log shows that.

**How to tell them apart, and it is one instrument:** make `BotBullion` count wants actually *raised* rather
than errands returned, and give the refusal its own bucket. Everything else here is guesswork until that
line tells the truth — and the shard's own error log already says the consequence out loud:
`No shopkeeper buys Bronze Ingot, and no bot wants it either; it will sit on the market`.

**The market moves one way.** `StaleMs` marks a seller's price down on a timer and never moves a buyer's bid
up: of 191 price cuts in one window, one moved towards an actual bid and none moved up, with
`15410 stalls had no bid to move towards`. One dial doing two opposite jobs. **Left deliberately for
Patrick.**

**Cooking supply is thin, and the reason is the island rather than the trade.** 94% of asks answer "no meat
worth cooking", and the keep-back works — 112 stacks in a session, only 3 sold past the cap. Counted since:
about half of what gets hunted carves into nothing. The commonest quarry in one session was zombie 15,
skeleton 15, troll 13, ettin 7, ogre 6 against boar 6, bear 5, wolf 4, sheep 4, hind 4.

What to watch now that a funded want for ribs exists: `BotQuarry` already steers a kill by what the carcass
carries — `14 kills were chosen because the board wanted what the carcass carries` before there was ever a
meat order on the board. If that number does not climb, the ask is not reaching the hunter and the two are
not meeting.

**The smith's new threshold costs less than it first looked.** `BotAnvil.Tries` moves rounds into
`with metal but not enough for any recipe they can work`, a bucket that used to read nought. First reading
was 82 of 428 asks; measured again on a settled shard it is **13 of 194**, under 7%. Against that, stints
producing something went from three in ten to eight in ten. The number has still only been chosen and never
tuned, and the pair to watch while tuning it is that bucket against `stints … with N pieces beaten out`.

**Twenty of sixty `Forget()` methods are unreachable from any module's `Reset()`.** Their counters never go
back to nought — not even on a world reload, which is the one event that resets the other forty.
`BotHerbalist.Forget` was one of them and is now wired; the rest are listed by walking the call graph from
each module's `Reset()`:

```
grep -l Module.cs, take each public override void Reset(), collect X.Forget*() calls,
follow those through the bodies of the Forget methods they name, and diff against
every public static void Forget*() declared in the assembly
```

Left alone deliberately. The practical weight is small — the summary is cumulative anyway, and a world
reload inside a session is rare — and wiring twenty resets blind risks double-resetting counters whose scope
nobody has checked. It is a consistency problem worth one careful pass, not a defect worth a blind sweep.

**Three dials are `code only`.** `BotAnvil.Tries`, `BotOven.Keeps`, `BotBake.Keeps` — `BotCraftSettings` was
written when sewing was the only craft and was never widened. They cannot be turned without a rebuild.

**A shard died silently at 12:54:59 on 05.09**, sixteen minutes after starting: no error, no exception, no
shutdown line, a clean save five minutes earlier, and the log stopping mid-second. One occurrence. If it
happens again at a similar uptime it is a pattern and worth chasing.

---

## Documents, and what they are worth

`MAP.md` and `DIALS.md` are generated from the source by `regen-map.py` and cannot drift. `README.md`,
`ARCHITECTURE.md`, `BUILD.md`, `INSTALL.md` and the subsystem `README.md` files are written by hand and do
drift — on 05.09 four of them described a shard that no longer existed and fourteen listed fewer files than
their folders held. All corrected, and `regen-map.py` now prints a line for any README whose file table has
fallen behind, because the way it accumulated was silently.

**When a document and a live log disagree, the log is right.**

---

## Working order

Changes go into the fork, shakedown happens on a live shard, and what has actually run is copied to
[DmitriyBol/UOBot](https://github.com/DmitriyBol/UOBot), whose paths mirror this one exactly.

The pattern this project keeps returning to: a mechanism that looks broken is usually **two numbers that never
met**, and the engine **refuses in silence** — it answers a refusal by sending a message to a screen the bot
has not got. `MAP.md` §4 lists the shapes those defects take, and every one of them was paid for.
