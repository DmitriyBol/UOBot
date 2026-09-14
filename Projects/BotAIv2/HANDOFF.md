# Handoff: the state of BotAI v2 on 05.09.2026

Twenty-three folders, 212 source files, about 70,000 lines. **It builds clean against the fork** —
`TreatWarningsAsErrors=true` there, so clean means genuinely clean — into `BotAIv2.dll` (689 KB) and
`BotMindAI.dll` (210 KB).

This file is the state of the work: what is running, what is measured, what is known to be wrong, and what to
do next. For where anything lives, `MAP.md`. For what a number is set to, `DIALS.md`. For how the thing is put
together, `ARCHITECTURE.md`. For what it is at all, `README.md`.

> **One engine patch is now required.** `engine-patches/CraftItem-heat-source.patch` — seventeen lines
> exposing two questions `CraftItem` already answers privately. Without it this assembly does not compile.

> **Newest first.** The section directly below is 14.09.2026. Everything after it is the state on 05.09.2026 and
> the nights that followed, kept as it was written.

---

## 14.09.2026, evening — the night's settings put back, a loop closed at the shared question, and a decision log

**The night's settings are back to the code's values** at Patrick's word: `Price`, `Keep`, `TaxShare` and
`CrafterNames` are out of `bot-estate.json`, `bot-debugger.json` and `bot-mind.json` — a hall at 5,000 with 300 kept
back per member, the crown's tax at 0.06, five crafter minds. Checked on the boot lines and through the door.

**What the alarm was.** From 16:38 the channel held `work-not-finishing`, down to 11–17% per half hour. It was one bot.
Hale's pack held 197 oil cloths nobody bought, handed back by the market with `DropItem` past the engine's cap of 125
things; after that no coin could be drawn into the pack to pay at a counter, or paid into it for a sale, and restocking —
made unpaid that morning, so learning nothing from failing — failed 8,719 times in half an hour. It was the third time
this failure line had been seen: the guild's courier on 09.09 (1,955 of 2,012 failures) and on 10.09 (1,222 in eight
hours), each time gated in one proposer only.

**Build 37** asks the question at the shared level. `BotDeed.AtCounter` marks work that changes money over a counter;
`BotAppraisal.Weigh` refuses it when `BotYield.Pocket` says the pack has no room for a coin (`Pocketless`);
`BotListing.Return` hands a stall's goods back only as far as `TryDropItem` allows; the porter offers a jammed pack the
trip that makes room, unpaid (`BotUnload.Jammed`, which counts offers). A healer that could not keep up with a fighter is
not sent after the same fighter for five minutes (`BotAccompany.ShunMs`; Otho had been sent after Maeve 48 times).
**Build 38** gives Argus a keyboard-only verb, `jam <bot>`, that puts a bot into Hale's state. Measured on Hale at 19:35:
the counter work was refused, unload was taken at 103/min, 113 cloths went into the bank box within 75 seconds, and sewing
was weighed normally again the same second — one failure (the work already in hand) against thousands. Build 38 after
20 minutes: 78% of endings finished, no "pack would not hold" line, no escort failures.

**Not deployed yet:** the `Will:` line still calls `Jammed` "trips begun" although it counts offers; the corrected
wording is in source and goes out with the next build.

**`DECISIONS.md` exists from tonight, at Patrick's order:** the mechanisms and their invariants, twelve defect classes with
every recorded instance, the month's decisions with what they answered and how they ended, and the structural remedies
that would close classes rather than instances. It is read before a mechanism is touched.

---

## 14.09.2026 — a choice kept, a role played, and the research behind both

Patrick's order that morning: the price of a choice and whether a bot keeps to it; research into how autonomous
characters decide, aimed at decisions that are correct, logical and true to a bot's role; and this repository
brought up to date with the code carrying only its class summaries. The research, the measurements and the
reasoning are in **`RESEARCH-decisions.md`**; this is the state it left.

**What was measured first** (build 33, 108 minutes): 31% of all dropped work was dropped at exactly the two-minute
protection cap, and four bots in five took the same trade up again within ten minutes; mining finished 52% of its
trips and abandoned 28%. The stall watch read a pick's swing counter as errands swapped. A woodcutter could swing
293 times at nothing. Members holding a claimed square were cancelled for standing still. Healers spent 26% of their
time looking for fights, because the medic's rule had gone with the rangers. The auction refused 1,008 unloads and
721 restocks for "costing money".

**Build 34 — commitment.** `BotDeed.Steadfast` work is held for its own reckoning (`CommitStretch` ×1.5, capped at
eight minutes); only events get through a hold — `Pressing`, `BotDeed.Summons`, or trouble in the work; what an
event displaces is put down and taken up again (`BotPause`); every drop says what it cost; the census writes
`Resolve:`; Argus has `resolve <bot>` and `resolves`. Same build: churn counted by identity, woodcutting given up by
swings, the muster `Still`. First twenty-two minutes against the same age of build 33: drops 12% → 8%, mining
44/37 → 74/5 (finished/dropped %), cooking 63/21 → 79/7, unloading 75/19 → 97/3.

**Build 35 — roles and logic.** `BotCalling` prices a class's own trade ×1.3 and another class's ×0.6 and writes
`Roles:`; healers keep `DefendsOnly` again and have a place to be (`BotAccompany`, standing by an engaged fighter);
getting a spell and standing for the guild are steadfast; a trip to the shops is unpaid work, no longer refused for
what it costs. Argus has `roles`.

**Build 36 — a class's own book, and a healer that keeps up.** A class names the spells it learns first
(`BotClass.BookFirst`): a healer with fifteen gold had been measured buying six curses while short of Greater Heal.
Standing by a fighter bends up to six times when the fighter walks away (`BotAccompany.KeepUp`) — the first three
stints on build 35 had all ended inside ten seconds — and healers look sixty tiles for one. First thirty minutes:
healers' own trade 40% of their minutes against 23% in the morning, 45 stints standing by (40 ended with the fight),
the rest of the population as before; commitment 79% of endings finished, 154 better offers refused inside a hold.
One healer was found fighting bare-handed with no staff in its pack at 11:43 and killed — the first such report of
the day, on a supply run rather than beside a fighter; the `Arms:` line counts them, and it is worth watching.

**Open, in the order `RESEARCH-decisions.md` §6 gives:** enabling work priced by what it enables (restock is only
the first of them); role-true work for fighters and casters, whose own trade is 10–16% of their minutes; reacting to
a nearer opportunity during a walk; a census of the safety rules and whom they bind; weighted randomness among
near-equal offers; minds that name how long they mean to keep a choice.

**The night's settings, put back at Patrick's word on the evening of 14.09.2026:** the keys are out of
`bot-estate.json`, `bot-debugger.json` and `bot-mind.json`, so the code's own values apply again — a hall at 5,000 with
300 kept back per member, the crown's tax at 0.06, and five crafter minds. Still Patrick's: the Crown and Blade seats
62 tiles apart against a radius of 40; whether losing a war should also cool the loser's clock for declaring one.

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

**Open, and the largest thing the broken error grep was hiding — but read the qualification before acting on
it.** Twenty of thirty-nine errors in a run are a bot carried home off ground the search calls `TooBig`:

```
Oswin the Mage could get nowhere at all from (1391, 1442, 10) and has been carried
home to (1441, 1466, 0); the ground it was on is TooBig, its tile allows DF of the
eight directions
```

Fourteen distinct bots of fifty-three in thirty-six minutes — the Captain, the Baron, the Sage, mages,
archers, gatherers. Four more read `can reach nothing … and it is standing at home`, and five are a bot that
has not moved or changed what it is doing for minutes, mostly the Baron with `"nothing"` as its occupation.

**It starts at the twenty-eighth minute of the run, not at boot.** While the population works near home
nothing happens; as it spreads out, bots reach ground the search calls `TooBig` and have to be carried back.

**`TooBig` is a designed outcome, not a failure**, and the first version of this note called it one. See
`BotPath.StrandedCells` — 25000 cells, ten times the ordinary bound — and the paragraph beside it: the look
is only made once a bot has already had a dozen roads refused from one tile, and `TooBig` means the pocket
was too large to *prove* impassable inside its 150ms, so carrying the bot home is the insurance firing
correctly. It was set at the ordinary bound for an hour on 03.09 and one trap at (1757, 976) took fourteen
bots in eighteen minutes; that is what the larger bound bought.

**So the open question is narrower than "movement is broken".** Why does a bot reach a position where a dozen
roads in a row are refused — when in fourteen of the twenty cases its own tile allowed all eight directions
(`FF`)? The code's neighbouring message says the same thing in its own words: *this is not bad ground, look
at what is refusing the roads.*

Distances from home: min 14, median 127, max 547 — scattered across the map rather than the one bad place
03.09 had. **And the raised share is flat across that spread**, which rules out a single bad neighbourhood:

```
      <30 tiles from home   24 strandings   46% raised
   30-100                   32              69%
  100-250                   42              43%
     >250                   10              40%
```

Against a 6.8% baseline every band is between six and ten times its share. The two dozen that happen within
sight of home are not a separate at-home defect — they are the same one, on the raised ground that happens
to be near home.

**At five-tile resolution there is no trap either: the densest bin on the island holds four errors in three
hours**, and the runners-up hold three. Whatever took fourteen bots at (1757, 976) in eighteen minutes on
03.09 is simply not present. Both resolutions say the same thing, and it is the central claim for the larger
half of the defect: **the walk fails, the places are innocent.**

**Two different populations, and they want different answers — one measurement separated them.**

```
stranded at Z=0:   16        "has not moved": 11 cases,
stranded above:    13            every one of them with 0 of ours within 2 tiles
   Z = 4, 10, 34, 36, 40
```

Half the strandings are on raised ground, and **not one** of the frozen bots had another bot beside it.

**With the denominator it stops being a hint and becomes the finding.** Over 3238 pieces of work the
population stands on flat ground 92% of the time and on raised ground 8%. Its strandings split 50/50. That is
raised ground failing **more than six times its share**, on 36 cases — and the earlier note here said "nearly
half are on a hill", which on its own means nothing at all, because it never asked how much of the shard's
day is spent on hills.

This project already carries the note for what raised ground does: an invented Z is a place nothing can stand
on, working on the flat and failing on a hill. That is where the scattered strandings point, and it is no
longer a guess.

## The danger map does reopen, once in fourteen

Left on Patrick's list since 03.09 as a possible one-way ratchet: the island line reads
`N shut and 0 reopened since the shard came up` for hours on end, and quadrants shut for being too quiet
never seemed to come back.

**They do.** At 3h25m into the 05.09 run the line turned over to `14 shut and 1 reopened`. So the mechanism
is not missing and the question changes shape: not "why never", but "why once in fourteen, and only after
three hours". Those are different defects and they want different measurements — a missing path versus a
threshold set too far out.

Worth knowing before anyone spends an evening looking for a reopen that was there all along.

## Read this number first: the population's work rate halves over a run

**The single most important measurement of 05.09, and it was taken last because nothing on this page was
pointing at it.** Per five-minute window, over 2h50m:

```
                    finished   failed   fail%
first 5 windows         1482      170     10%
last 10 windows         2263     4979     69%
```

**Finished work halves and failures multiply by twenty.** Not a plateau, not a step: a climb across the whole
run. Every other finding on this page is a contributor to this line or is noise beside it.

**And count is the wrong unit — use time.** A failure is one deed won at auction, begun, and ended without
its result; the bot takes new work immediately. 97% of them earn nothing at all (0 coin, 0 made, 0 skill),
and they are *short*: 0.27 min against 0.48 for a finished one. So the count ratio flatters the failures into
looking more numerous than they are costly. Measured as working time:

```
first 25 min    1322 bot-min working    215 failing    14% of the time produces nothing
last 11 min      121 bot-min working    273 failing    69%
whole run       3914 bot-min working   3617 failing    48%
```

**Half the population's working time produces nothing, and two thirds of it by the end.** That is the number
to quote. The count-based figures below are correct but read worse than the truth in the early windows and
better in the late ones.

**Take the aggregate, not the last window.** Individual windows swing hard — the last ten read
`68 72 74 77 79 45 82 82 76 53` — and an earlier draft of this note quoted "77-82%" off two consecutive bad
ones. Every number in it was true and the impression it left was not. The honest current figure is 69%.

**Two things it settles.** The climb starts at window 5-7, and the hearth defect starts at window 10 — so the
hearths are a large contributor but **something is already degrading before them**. And "cooking output is
flat" — true of `BotCook.Offered`, which holds near 350 — must not be read as the population being flat. It
is not.

**How to take it, in one line:**
```bash
grep -oE "Will: [0-9]+ taken on, [0-9]+ finished, [0-9]+ failed" $L | sed -E 's/[^0-9]+([0-9]+)[^0-9]+([0-9]+)[^0-9]+([0-9]+)/  /' | awk 'NR>1{dd=$2-pd; df=$3-pf; if(dd+df>0) printf "%d %d %.0f%%
", dd, df, df*100/(dd+df)} {pd=$2;pf=$3}'
```
Cumulative totals hide it completely: read as a running figure the fail rate looks like a slow drift to 56%,
and the halving of output does not appear at all.

### The movement defect reaches the economy, and it looks like bots going broke

Purses fell five windows running (15928 -> 13702gp) and a bot reached 0gp. **No money left the world.** It
moved into escrow on the wants board, and the board's prices are climbing:

```
things wanted    gp down    gp/thing
        507        12193        24.0
        454        14381        31.7
        364        14647        40.2
        289        18062        62.5
```

**Fewer goods wanted, at 2.6x the price, with more money locked against them.** Purses plus escrow is flat
(13702 + 18062 = 31764 against 31376 ninety minutes earlier), so the drop in purses is not a leak.

The mechanism is the ledger doing its job on top of a broken one: work fails, measured yield falls, the price
per unit has to rise before anyone will take the work, and the money to back it comes out of purses. It is a
feedback loop from the movement defect into the economy, and it will read as poverty to anyone watching
purses alone.

**Do not treat rising want prices as a market to fix.** They are a symptom with the same root as everything
above.

**Open, unexplained, and left that way deliberately: counter buying fell about tenfold in the last quarter
hour of the run** — real `X bought N Thing from Y` events per five minutes went `11 13 4 5 7 1 1`, and the
`Trade:` line's own delta went `293 45 108 121 0 1 0`. Selling to shopkeepers carried on, so bots still reach
counters.

Four things checked and none of them it:

- **Not an instrument.** Real purchase events fell with the counter, so the summary is telling the truth.
- **Not reach.** Selling continues at the same counters in the same windows.
- **Not affordability.** `cannot afford one` grows steadily at about 50 a window all run, not in a step, and
  the metal equivalent sits flat near 10. The fattest purse among those who cannot afford is frozen at 189gp
  — it is a cumulative maximum, so it says nothing about now.
- **Not a shift to stalls.** Stall purchases held flat (30, 23, 24 per quarter hour) while counter buying fell.

Recorded without a conclusion on purpose. The next candidate worth a counter is whether supply thresholds are
simply met — `33597 were short of nothing` out of 43775 looks — which would make this the population going
quiet rather than going wrong.



## The largest single loss in the shard: twelve hearths, several unreachable, never struck off

**Found 05.09 late, by asking what the recent failures actually say. It is bigger than everything else on
this page put together, and it is in code added the same day.**

The shard's own cook line, at 2h35m:

```
62882 asked to cook: 8913 put something on, 35113 had no meat worth cooking,
0 had meat but no recipe their skill would carry,
18856 had both and no fire they could get to (of 12 known)
```

**18856 bots held meat and the skill to cook it and could not reach a fire.** Two of every three cooks that
were otherwise ready. And the island holds only **twelve** known hearths.

In a 30-minute window, `no way through to (x, y, z)` is **1218 of 2567 work failures — 47% of everything
that failed.** Their destinations, against a 5.9% baseline of raised ground taken from successful work:

```
raised destinations   372 of 1218   30.5%    5.2x the population's share
84 distinct raised targets, of which the top eight take 67% of raised failures
```

And the top eight are all one thing:

```
216 failed at cook: off to a fire to cook Ribs   (1448, 1615, 20)
210 failed at cook: off to a fire to cook Ribs   (1648, 1601, 20)
208 failed at cook: off to a fire to cook Ribs   (1549, 1680, 30)
210 failed at cook: off to a fire to cook Ribs   (1353, 1779, 15)
```

**It is not the invented-Z defect, and that was the first guess.** `NoteHearth` takes `tile.Z` and
`NoteItemHearths` takes `item.GetWorldLocation()` — both read the world, neither computes a height. The
hearth really is at Z=20. It is on a ledge, a roof, an upper floor: a bot cannot stand on the fire tile, and
on raised ground there is no reachable neighbour at that height either.

**Not one of the four was ever reached — 0 successes against 887 attempts.** That is what rules out the
other candidate: a fire that is merely crowded would still show successes between the failures.

**There are two guards against exactly this, both sit in the chosen path, and neither can fire.** The first
version of this note said `BotGround` had no such memory at all; that was a bad grep (`Shun|Unreachable|Bad`)
against code that spells it `Cautious` and `BotReach.Ask`.

1. `ledger?.Cautious(kind, on, where)` — **the code's own comment says it has never once fired**, because
   `BotWill.Settle` files the caution under the undertaking's name ("cook") and `Nearest` asks under the
   place's name ("hearth"). Both keys are right and they never meet. The comment records the same defect
   costing 76 walks to one counter in 11 minutes on 25.08.
2. `BotReach.Ask(...) == BotReachVerdict.Sealed` — cannot fire here by construction. The enum has three
   values, and `Sealed` means *a pocket walked to its edges*. These fires stand on the island's main
   landmass, which is never walked to its edges inside the budget — that is what every `TooBig` in the error
   log is saying. **So the night's two findings are one: `TooBig` is the reason the unreachable-place guard
   is silent.**

**And the distance that picks the winner ignores Z entirely** — `Nearest` takes `Math.Sqrt` over X and Y
only. A fire twenty tiles up a cliff measures as near as one on the road, so the bad ones win on merit.

This is the [[bot-note-written-but-not-read]] family twice over: one note written under a key nobody reads,
one guard whose question cannot be answered in the affirmative for any place that matters.

**Proposed fix, not deployed, and the diagnosis points at the cheapest one.** Test standability when the
hearth is *recorded*, not when it is walked to: a fire with no standable neighbouring tile is a fire nobody
can ever cook at, it costs one check per hearth for the life of the shard, and it needs no memory, no
timer and no reversibility argument. Failing that, the honest repair is guard 1 — make `Settle` file the
caution under the place's name as well as the trade's — which fixes counters and forges in the same stroke,
since the comment says they have been broken the same way since 25.08.

A per-place failure count is the weakest of the three: it pays for the walk before it learns anything, and
with twelve hearths it has to be reversible or a passing obstruction retires a good fire forever.

**Why it was not fixed on the spot:** the fix needs a rebuild and a restart, and the restart would destroy
the run that produced these numbers — the observation that was asked for. It also wipes the survey, which is
never saved, so the twelve hearths would have to be rediscovered before the fix could be judged at all.

### And it is not only the hearths: the shard's own guard lines read zero

The same 20-minute window that carries 482 cook failures carries **2197 `no way through` in all**, and every
trade that degraded names the same cause — herbs 345 of 346, prowl 810 of 857, peddle all of its:

```
  to a map coordinate   1947
  to a named person      173      (Kepa, Caine, Ximena, Odon - shopkeepers)
  to a creature           77
```

**And the two guard lines report nothing, across the whole run:**

```
0 counters passed over for having no way through to them
0 with no forge in reach
```

A guard that has refused nothing in 2h50m, while 173 walks to a counter fail in twenty minutes. `Nearest` is
shared by counters, forges and hearths, so this is one defect wearing three coats — and the code's own
comment already said so for counters and forges, with a date on it.

**Measured, and the answer is the inconvenient one: they are two defects, not one.** The discriminator is
free — a place taken off a list repeats, a place chosen fresh does not:

```
trade    failures   distinct targets   repeats   worst single target
cook         2817                 12    234.8x   270 attempts
herbs        1577               1556      1.0x     3
prowl        3251               2920      1.1x   192
```

**Cook is twelve places struck at 235 times each. Herbs and prowl are fifteen hundred and three thousand
fresh places, struck at once.** So repairing `Nearest` covers cook, counters and forges — the bad-list defect
— and leaves the larger number entirely alone. The two need separate work:

1. **A bad list.** `Nearest` offers known places nothing can reach and neither guard can fire. Cook alone is
   2817 failures a run.
2. **A bad walk.** Herbs and prowl are refused at thousands of *distinct* destinations, one attempt each.
   Nothing is choosing badly here — the walk itself fails at fresh ground all over the island, and this is
   the half that ties to `TooBig` and to raised ground.

Do not let the first repair's numbers be read as progress against the second.

### Confirmed on a second, longer run — and the two populations turn out to be one

A 90-minute run (`session-2026-09-05_19-44`) reproduces the baseline exactly and sharpens both figures.
The denominator is **successful work only** — `finished ... near (x, y, z)`, the places bots stand when the
day goes right:

```
baseline, work that succeeded    808 flat   59 raised    6.8% raised
stranded (could get nowhere)      21 flat   22 raised     51% raised   n=43   7.5x its share
frozen  (has not moved)             5 flat   13 raised     72% raised   n=18  10.6x its share
```

**The frozen bots are more concentrated on raised ground than the stranded ones, not less.** The note above
splits them into "two different populations that want different answers" on the strength of the crowding
reading. That split does not survive the larger sample: both failures live on the same 6.8% of the ground,
and the frozen one lives there harder. Treat them as one defect with two endings — the search gives up and
carries the bot home, or the bot holds an order it can never arrive at and stops.

**A trap worth writing down, because it cost a measurement mid-check.** Taking the denominator from *all*
non-error lines gives 22.5% raised, and against that the excess looks like a forgettable 2x. That population
is poisoned: it is mostly failure lines (`no way through to`, `made no progress towards`), which are
themselves 32.9% raised because failures are what raised ground produces. Measuring the excess against a
denominator already made of the thing being measured is how a 7x defect reads as noise. The baseline has to
come from **successes**.

**The crowding lead below is therefore only about the six at the camp.** It was written first and as the
lead; it is not.

**But the two are linked, and the link is the rescue.** The at-home messages cluster in time with the
strandings: Calla and Bryn got theirs in the same two minutes that a dozen bots were carried in. Field
stranding → rescue → a crowded camp → stranding at the camp. Fixing the scattered half would drain the other
one, and fixing the camp without it would only move the queue.

**The at-home subset.** Four of the errors are a different sentence: *can reach nothing from
(x, y), and it is standing at home*. Six distinct bots — Alden, Aldric, Cedric, Neriah, Oswin, Talia — and
the coordinates cluster in `1434–1446 × 1465–1476`, which is the camp. Neriah was carried home to
(1434, 1476) at 20:51 and two minutes later could reach nothing *from that spot*. Home is where fifty-three
bodies congregate, and the code's own message beside it says what to suspect: *this is not bad ground, look
at what is refusing the roads*. Bots refusing each other's roads fits every part of it, including why a
rescue drops a bot somewhere it cannot leave.

**How to test it:** count our own bodies within a few tiles when the message fires. If crowding is the
answer, the rescue destination is the bug — carrying a stranded bot into the one place guaranteed to be full.

**And it arrives in a wave, which the crowding story would predict.** Errors per ten minutes of one run:

```
0-9   2      40-49   6
10-19 2      50-59  14
20-29 5      60-69  16
30-39 2      70-79  15
```

Eightfold at fifty minutes against the first half hour, and then **it stays there**. It does not damp.

This paragraph first read the last bucket as a collapse to 2 and concluded the mechanism puts itself out.
That bucket was simply not finished yet — the run had not reached the end of it. Half an hour later it had
filled to 15. **A partial last bucket read as a completed one**, which is the same mistake as reading a young
shard as a settled one, and it changes the whole answer: a plateau and a decay want different fixes. Not touched: this is `BotMovement`, which `ARCHITECTURE.md` calls the part
that accumulated more measured defects than everything else together.

**Open, and separate from the movement work although it hides inside the same errors: the Baron has no work
to do.** Of 67 errors in a run, three bots hold seventeen — Baldric 8, Aldric 5, Cedric 4, against a fair
share of about 1.3 each. The three offices, at six, four and three times their share.

But they are not the same failure. Aldric and Cedric are stranded like everybody else. **Six of Baldric's
eight are `has not moved`, every one with the occupation `"nothing"`** — for four, seven, fourteen and
seventeen minutes at a stretch. He is not stuck. He is idle.

Why, from his own summary line:

```
1663 times a Baron was asked: 1663 found nowhere reading at or below -0.30
1663 times a Baron was asked to walk his rounds: 0 were offered unknown ground
3145 times a Baron was asked: 3145 were offered a town
48285 offers withheld from classes sworn elsewhere
```

**The harrowing threshold never fires.** Not once in a run does any square read at or below `-0.30`; the
danger map does not get that bad. And ordinary work is withheld from him because he is sworn to the office —
that is the design, and it is what makes the threshold load-bearing. What is left is the town walk, offered
3145 times, and he still sits with nothing for a quarter of an hour at a time.

Two numbers that never meet, in the family this project keeps paying for: the harrow bar against what the
danger map actually reaches. First thing to check is the distribution of square readings against `-0.30`.

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

---

# Night of 09–10.09.2026 — packs, the carrying rank, and six wrong diagnoses

Patrick's order was: watch the shard overnight, fix what is critical, and concentrate on what the bots carry
— "not a ton of rubbish each; they have a bank for that". The example was one bot holding eight heal potions,
ten sewing kits, and piles of leather and cloth.

## What is now true that was not

**The allowance was about the pack and every place that read it asked about the stack.** Four sites in
`BotUnload` tested `item.Amount <= allowed`, item by item. A sewing kit does not stack, so each one is an
amount of one against an allowance of two and every one of them was kept; a tailor with five bundles of ten
leather kept all fifty, because no single bundle was over twenty. The intent is written three lines above
the sale in that same file — *"keeping to that number and listing the rest is the whole of the difference
between a population of hoarders and a market"* — and the code had never done it. All four now share
`BotUnload.Over`, which carries a running total per kind.

**How it was found is worth more than the fix.** The pack census printed `Pickaxe 253 (121 surplus)` while
the seller passed over every one of them: two readings of one allowance, disagreeing. The instrument was the
one that happened to be right.

**Tools and potions had no ceiling at all** — `int.MaxValue` in the same table, which is not a cap but a
promise never to let go. Potions now use the number the class already declared and that was being
overwritten (`klass.PotionLimit`, which `BotOutfit.PotionsFor` has always returned and `Needed` threw away);
tools use `BotUnload.SpareTools`, two — one to work with, one for when it wears through.

**Weight that is not the bot's own is now a reason to walk to a counter** (`BotUnload.Hoard`, forty stones).
The other four reasons ask whether the bot is uncomfortable, rich, wanted or exposed. Ore is heavy and cheap
and slipped past all of them: 652 stones of iron across the population, every ounce surplus by the shard's
own rules.

**A new instrument: the `Packs:` line.** What the whole population is carrying, how much is over the
allowance, and the six heaviest kinds. Every other weight instrument here is about bots already in trouble;
a pack fills with rubbish while the bot is comfortably under its ceiling.

**A new hand for Argus: `pack <bot>`** — what it carries, what it may keep of each, what is surplus.

**The school publishes its clock** (`BotSchool.Left`). The captain owned it and nobody deciding to come could
see it, so about twenty bots per cold start walked to a lesson that ended before they arrived.

**Argus chooses where a revel stands.** Both callers passed `Point3D.Zero`; the model is now asked for
`x`/`y`, its direction is always kept and only a distance outside 30–120 tiles of the population is
corrected. Blocked ground no longer cancels the camp silently — `Footing` rings outward and `Unpitched`
counts the failures.

## What was broken in the making, and by whom

Making the carrying ceiling a **rank** rather than a discount was right (overloaded bots taking work that
needs a step: 155/hour → 28) and it had a second cost that took hours to see: a courier that goes over its
ceiling *by buying the guild's lot* had the errand taken off it on the spot and sold what the guild had just
paid for. Supply errands dropped after the money was spent went from 10/hour to 80. Cured in two halves —
`BotSupplier.Fits` (do not send a courier for more than it can carry) and `BotDeed.Committed` (work that has
already spent money is not weighed against a better price).

## Open, with numbers

- **Things the market will not take can never leave a pack by selling.** Measured tonight: unload errands
  finishing with *"5 the market would not take; the porter counted 5 worth leaving"* — the two numbers agree,
  the listing floor of 2gp refuses them, and only `Dump` can shift them, which runs only for a bot that
  cannot walk. This is the second root of "a ton of rubbish" and it is untouched.
- **A cap released all at once is a crowd.** Six casters simultaneously carried exactly eight surplus heal
  potions to the one shopkeeper who buys them and jammed on his tile; seven of nine stall reports that
  window were that errand. A destination has no crowding factor, though a kind of work does.
- **Blank scrolls, 140 stones across the population**, uncapped on purpose and defensible — but it is weight.
- **`BotPorter` has five reasons to offer nothing and counts one.** Everything about why counter trips
  collapsed is guesswork until it counts all five.

## The methodological lesson, which cost most of the night

**Six diagnoses were confidently wrong, every one of them from comparing unlike windows.** Sessions here run
from ten minutes to an hour, and the shard warms up non-linearly: the completion band climbs 26% → 54% →
65% → 76% over an hour, and the market gave 379 stall sales in its first twenty minutes and 1146 in the whole
hour — it *slowed down*. Comparing a young session with an old one proves whatever you like.

What actually works is a **live dial on a running shard**: same world, same minute, one number moved. It cost
me one more mistake to learn the rest of it — a band that rises after a dial moves has not been explained
until the dial is moved back and the band falls again. Mine did not fall, which means the recovery was
warm-up and not the dial.

Falsified along the way, so nobody spends the time again: unloading on the spot instead of at a counter;
`Committed` turning drops into failures; a surge in brewing; side effects in the census (`Kit()` is a pure
lookup everywhere); the island running out of ore; and the walk failures, which normalised to 21% of prowls
and not the 50% the raw count suggested.

**And twice a new counter lied before the shard did** — the census printed "49 packs hold 0 stones"
(`Item.TotalWeight` is the weight of an item's *contents*; `PileWeight` is the stack's own), and the pack
reader called 340 gold and a spellbook surplus because it did not repeat the two exclusions the sale makes.
Both looked like findings.

## Tested and innocent: the path-search budget

`BotPath.CeilingMs` was raised 60 → 240 on the running shard and the walk failures fell fourfold — "could
get nowhere at all" 4 → 1, "got no nearer" 17 → 4 in matched ten-minute windows. It looked like the answer
to four things at once: counter trips collapsing, coin staying in pockets, the market going quiet, and the
pack surplus refusing to fall.

**Moving it back to 60 did not bring the failures back** — 0 and 4 in the next window. The improvement was
the session warming up, not the dial, and the same trap had already caught the tool-cap bisect an hour
earlier. The budget is not the cause of anything measured that night; do not spend the time again.

The rule this produced, which is the most useful thing in this section: **a number that improves after a
dial moves has not been explained until the dial is moved back and it worsens again.** One direction is
never evidence on a shard that warms up over an hour.

## The second root of "a ton of rubbish", with the number

Lowering `BotUnload.Hoard` from 40 to 15 on the running shard did **not** shed anything: the pack surplus
went 58% → 53% → 61% → 65% while the completion band held at 70%. The trigger only *offers* the errand; it
cannot make the market take what it refuses.

**881 things of 29 kinds were worth less than the 2gp listing floor and stayed in the pack** in one
forty-five-minute session, against 287 things successfully listed. Unload errands finish saying so in as
many words — *"12 the market would not take; the porter counted 1 worth leaving"*.

So surplus below the floor has **no way out of a pack at all**: the market refuses it, and `Dump` — which
would drop it — runs only for a bot that cannot walk. It accumulates for the life of the shard. Spined
leather led the census at 582 stones, all of it surplus.

Three ways out, each with a different price, and the choice is Patrick's:

1. **Sell it to a shopkeeper.** They buy what the market will not price. The peddler already walks to them;
   the unload errand does not. Most economically honest, most work.
2. **Drop it when the market refuses at a counter**, not only when immobile. Cheapest change; litters the
   world, and the litter has to decay.
3. **Lower the listing floor.** One dial; fills the market with penny lots, which is what the floor was put
   there to prevent.

Not done, deliberately: it is a design decision with a real cost either way, and it was three in the morning.

## Two more, found at four in the morning and not touched

**A company member that goes overloaded is a monument, and the carrying rank cannot reach it.**
`BotLadder.Standing` returns `Bound` before `Busy`, and the auction is skipped for a bound bot — so the one
errand that would free it is never offered. Two caught in one session, both holding *"nothing"*:
Bertram at 188 of 184 stones with 0 stamina, Hale at 140 of 131 with 0 stamina, both in a company. This is
exactly the state the rank was written to prevent (see `BotAppraisal.StoppedShare` and `BotDeed.Standing`),
sitting behind a gate the rank does not see. Either the standing errand has to be reachable on the Bound
rung, or the company has to let a bot that cannot walk go.

**Deliveries are taken absurdly far.** The thirty-two stall reports of that session sit at a median of 203
tiles from home and a maximum of 857, and twenty-four of them are *"taking N of something to somebody"* —
one bot walked two hundred tiles to hand over a single war mace. Distance is priced in `BotAppraisal` as a
ratio of working time to walking time, which is right for a dig and wrong for a delivery whose whole content
is the walk. Nothing here is broken; the price is simply not the one a courier should be paid.

## The errors alarm is measuring the stall watch's cadence

Background error rate on this shard is **0.5–0.8 per minute**, steady across three sessions of 62, 81 and
122 minutes. The `errors` alarm fires at five in sixty seconds — so it should never fire, and it fired five
times in one night.

Every one of those bursts was `BotStall` output, and it lands **in a single second**: seven errors at
02:04:52, four at 03:17:37, four at 02:05:22. The watch runs on its own timer and reports everything it has
found at once, so an alarm that counts lines per minute is counting the watch's reporting cadence rather
than the shard's health. The stall reports are also already counted by `BotStall` itself and printed on the
`Standing still:` line, so they reach the operator twice — once as information and once as an alarm.

Not changed, because it is instrument policy rather than a defect: either the watch spaces its reports, or
the errors rule stops counting lines that another rule already owns. Whichever is chosen, an alarm that
fires every twenty minutes on ordinary events is one nobody reads by morning.

## Corrections to the section above, made the same night

**The rescues are not elevated.** At matched age (+60 minutes) "has been carried home" reads 9, 9 and 6
across the 20:26, 22:13 and 01:43 sessions — the session carrying all of that night's changes has the
*fewest*. An earlier reading of "0 in three sessions against 3 tonight" was taken at +22 minutes and was
small-sample noise. Do not re-open it on that evidence.

**And the crowding has a second and worse address than the potion shopkeeper: the population point itself.**
Two bots reported "can reach nothing from (1442, 1465) — standing at home, this is not bad ground, look at
what is refusing the roads" in the same second. Forty-nine bots return to one tile; what refuses the roads
at home is each other. The potion jam at Delano and this are one defect with two addresses — a destination
has no crowding factor, though a kind of work does.

**Sized before anyone acts on it:** seven of these in four hours, seven different bots, two or three an hour,
no trend, and each resolves itself. Calling it "worse than the potion jam" on the strength of two reports in
one second was wrong — the jam was six bots at once and it repeated. Home crowding is real, rare and
self-limiting; the shopkeeper jam is the one that costs work.

**A filter note, because it cost a wrong sentence.** "Every error burst is the stall watch" is true, but the
watch speaks in at least three shapes: *"has not moved or changed"*, *"has not left X for N minutes while
taking and dropping"*, and *"can reach nothing … standing at home"*. A grep for the first two under-counts
and makes a burst look mixed.

## The third and largest root: a permanent verdict from one unmeasured guess

At five hours the packs held 5443 stones, 4305 of them surplus, and **2126 of those were spined leather —
thirty-nine per cent of everything the population was carrying, in one commodity.** It appears in the log
fifty-four times and every one of them is the census line. Never listed, never bought, never sold, never
wanted.

`BotAuction.List` adds a kind to `_worthless` the first time any bot offers one below `Floor` (2gp), and
`Sellable` skips a worthless kind outright from then on — for every bot, for the rest of the session. So the
kind is never counted as surplus, never carried to a counter, never disposed of, and `Rifle` goes on putting
more of it into packs off every corpse.

**The trap is where the price comes from.** `Worth` answers with the market's own price only once somebody
has bid or bought; until then it hands back the caller's guess. The first listing attempt therefore happens
at an unmeasured guess — and that one guess condemns the kind permanently. Tainted wool, bone piles and raw
ribs are in the same state behind it.

This is the largest of the three roots by weight and the cheapest to argue about: nothing here is a
threshold that wants tuning, it is a verdict taken on one sample and never revisited. Whatever is done —
re-test a condemned kind after a while, refuse to condemn until a price has actually been observed, or stop
picking up what has been condemned — it should be done before the other two.

## Fixed at seven in the morning: an arrow inside the ground

The band fell from a steady 76% to **28%** in one five-minute window. Grouping the failures first — the rule
that has paid for itself every time — put 406 of the window's 440 on three archers: Brannoc 235, Aric 120,
Bertram 51. All of them on **one tile**: `(1457, 1461, -15)`, seventeen tiles from home and fifteen below it.
Three hundred and fifty-five attempts in five minutes at a single spent arrow lying inside the ground.

`BotGleaner.Propose` took the nearest lying arrow and offered a walk to it with **no reachability question at
all**. An arrow that has fallen through the floor stays the nearest arrow for ever, so every failure put the
same one straight back on offer. `BotPicker` had the identical hole for corpses — fifty of the same window's
failures.

Both now ask `BotReach.Ask` before offering, exactly as `BotStudent` has always done, and count the refusal
(`BotGleaner.Sealed`, `BotPicker.Sealed`). The gleaner had no summary line of its own, so its counter is
printed on the picker's — a number nobody prints is a number nobody has.

**Proved for one of the two, and say which.** After an hour: glean failures **0**, pickings failures **1**,
against 355 in five minutes before — and the band back to **78%**. But `BotPicker.Sealed` read **4** and
`BotGleaner.Sealed` read **0**, so the picker's check demonstrably declined four corpses while the gleaner's
never declined anything. The loop is gone; the gleaner fix is not what removed it, because it never fired.
Its hole may simply not have been triggered this session — no spent arrow happened to lie in a sealed
pocket. Treat the gleaner's guard as written and untested.

## The crowding defect is not an incident, it is the shape of every restock

Third sighting, and this one is the dominant stall shape of its session: **six of the seven stalls in the
07:27 session were "after paper"**, and all six bots stood inside an eight-by-nine tile patch — Delwyn,
Emrys, Garrow, Hale, Hollis and Isolde, six mages, one errand, one destination, four minutes each.

The same picture as the potion jam at Delano (six casters, one shopkeeper) and the two bots that could not
leave the population point. Whenever a need arises for a whole class at once — and a cap or a supply release
makes it arise for a whole class at once — the entire class walks to the single shop that sells the thing
and blocks itself in front of it.

`BotAppraisal` discounts a kind of work by how many others are already doing it (`CrowdBite`, four fifths).
Nothing discounts a *place*. That asymmetry is the whole defect, and it will get worse as the pack work
above makes more bots set out to buy and sell rather than fewer.

Worth fixing before the "sell the junk to a shopkeeper" option in the section above, which would otherwise
add a fourth address to the same jam.

## 10.09.2026, morning: the leftovers on the market

Patrick's order: small things sit on the auction unsold for a very long time; consider letting Argus buy
them out as an event, or decide with him what is better.

**What was actually wrong.** A stall's price is cut by `CutStep` once per `StaleMs` down to
`LeastMultiple` — a quarter of what it opened at. Then `Cut` returns false and the beat reached `continue`,
for ever. Only an **empty** stall was ever forgotten (`ForgetMs`), so a pitch holding something the
population had walked past at every price stood until the shard stopped, holding one of `MaxListings`
places while it did. Nothing anywhere could see this: `BotAuction.Describe()` went to a gump nobody opens
and to the world reload, which is the same "which is to say nowhere" as `BotGround.Describe` before it.

**Done.** The market has a line in the summary now (`Market:`), with a new reading beside it — how many
stalls have stood past `StuckMs` (thirty minutes, three price cuts), what they hold and what the oldest is.
And a stall that has reached its lowest ask and stood past that is **handed back to its owner**: the goods
rejoin the seller's pack and take the road the peddler already walks, to a shopkeeper — the buyer of last
resort this world already has, paying coin that comes from outside the bot economy.

Measured over one hour: **88 stalls taken off the board, 194 things returned, 0 that could not be handed
back**, and the standing backlog fell to **4 stalls holding 10 things at 52gp**. The cost is real and small:
"the stall was empty by the time it got here" went from 3 to 7 over matched fifty-minute windows — sometimes
a stall is pulled from under a buyer already walking to it. Eighty-eight places freed for four wasted walks.

**Argus was asked and agreed: do not buy the leftovers.** His words: it "would not address the root cause",
and "any issues would likely be resolved by adjusting the proposers or trade thresholds rather than buying
leftover items". Weight that as agreement rather than as proof — his stated evidence was generic ("the bots
are on their feet", "no signs of economic collapse") and did not engage with the figures he was given. The
argument against a buyout stands on its own: his treasury is a tax on the guilds, so buying junk would spend
their money to hide the one signal that says nobody wants the thing, and would teach the population that
making unwanted things pays. See `captains-till-not-a-faucet`.

**And a defect found on the way to asking him.** `BotVigil.Consider` dropped a console question whenever the
single Ollama slot was busy — and it is busy nearly always, because four thinking crafters share it and ask
several times a minute. Three questions in a row were lost, while the prompt that method builds opens with
"SOMEBODY AT THE KEYBOARD IS ASKING YOU THIS, AND IT COMES BEFORE ANYTHING ELSE HERE". The sentence promised
first place and the gate gave last. A question is now held and asked the moment the slot frees, ahead of the
revel and the ordinary look, counted as `BotVigil.Held`. That is what made the answer above possible.

### The residue, diagnosed but not fixed

An hour on the new build: **97 stalls reclaimed, 292 things returned, 0 failures** — and **8 stalls still
standing past thirty minutes, holding 152 things at 345gp**, the oldest 51 minutes. Few stalls, many things:
these are not forgotten trinkets, they are large piles.

**Why they escape.** The reclaim fires when `BotListing.Cut` answers "no lower", and the floor it compares
against is `Anchor * LeastMultiple`. A seller restocking its own pitch raises the anchor — the market line
reads **697 prices raised against 1919 cut** in that hour, and the same kinds are cut over and over (Lesser
Heal Potion 134 times, Leather 121, Thigh Boots 119). A stall that is topped up therefore always has room to
cut again, never reaches "no lower", and is never handed back.

**The obvious cure and why it was not shipped.** Reclaim on age and no sale — `now - ListedTick >= StuckMs
&& now - DealtTick >= StuckMs` — regardless of whether a cut is still possible. That subsumes the present
condition and would take the piles too. Its blast radius is the whole market, and judging it needs a session
plus the forty-minute warm-up; there was not that much time left before the shard was to be left running
unattended, and the known cost of the present change had already doubled ("the stall was empty by the time
it got here", 7 → 17 over matched windows). Shipping an unmeasured market-wide change immediately before
walking away is the trade this project has paid for before.

### The Baron started marching, and that is what the "dying" alarm was

At 13:33 the alarm reported **10 bots dead at their work in five minutes out of 49 alive** — a kind that had
not fired once all night. It was a harrowing: Faron called sixteen volunteers to (2055, 1035), "where 6 have
died", marched them, and they were killed retreating.

**Not a grinder.** Twenty deaths in the session against twenty "should rise again" lines, and the population
line reads 49 bots, 48 on their feet, 1 waiting to be revived. The cost is a purse each time
(`Perri died doing flee: -165 coin`), not a body.

**The change worth noticing is upstream of the deaths.** Over matched two-hour windows: **seven companies
marched in this session against none in the 22:13 session**, and all ten deaths came from them. The Baron's
whole purpose is finally happening where it previously did not. Cause deliberately unattributed — plausibly
bots have more free time now that fewer errands fail and fewer stand loaded, but that is a story, not a
measurement, and this file has enough of those from one night.

Worth watching over a long unattended run: whether the death rate stays recoverable when harrowings become
routine, and whether the purse lost per death is a meaningful drain on the economy.

### Seven hours later: which half of the reachability guard actually works

Free-flight run of 7h19m, and the four counters settle it:

| | gleaner (arrows) | picker (corpses) |
|---|---|---|
| `Sealed` — the reach ledger (`BotReach.Ask`) | **0** | **0** |
| `Baulked` — the place this bot was refused before | **132** | **75** |

**The reach ledger fired not once in seven hours. The baulk did all the work, two hundred and seven times.**
Glean failures over the whole run: **2**. Pickings: **8** — against 355 in five minutes before the repair.

So the morning's first guard was the wrong question asked well: `BotReach.Ask` answers about pockets that
enclosure searches have proved closed — ground a bot is *standing in* — and the problem was always an
unreachable *destination*. It costs a dictionary lookup and it is harmless, so it stays; but nothing should
be built on it, and anything else with this shape wants the baulk, not the ledger.

**A loop found in the same reading and not diagnosed.** Hale failed `acquire` 477 times over the run, 387 of
them "could not put the money down for it" and **376 inside the 16:00 hour**. The affordability veto in
`BotAppraisal` and the escrow charge both count purse plus bank, so the obvious pocket-versus-bank
explanation is wrong. It is self-limiting — Hale holds 809gp now, is brewing, contentment 1.00 — which makes
it a different animal from the glean loops, which were permanent. Diagnosing it wants a counter on why
`BotAuction.Ask` returned null, not more reading: the errand already learned once (see the note on
`BotAcquire.Board`) that one null hides two causes.
