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

**Woodcutting and herb picking had no ending at all.** They swung, counted, and stopped, so the goods rode
home in a pack: `133 of 212 fletchers could not find wood` while woodcutters walked past carrying it, and
`herbs` was the second commonest thing anybody did while the brewer read `607 had the glass but no herbs`.
Neither trade was broken — there was no edge between them. `BotAuction.Offer` is the shared ending now
(a funded order first, a stall second), and both gathering trades keep a working handful back for themselves.
**Deployed at 14:00; not yet measured.**

---

## What is open

**Fletching takes no orders, ever.** `0 took an order off the board` in every session on record. That is
probably correct rather than broken — nobody wants arrows, because `0 found with an empty quiver` — but it has
never been confirmed, and if archers ever do run dry the path has not been exercised once.

**The market moves one way.** `StaleMs` marks a seller's price down on a timer and never moves a buyer's bid
up: of 191 price cuts in one window, one moved towards an actual bid and none moved up, with
`15410 stalls had no bid to move towards`. One dial doing two opposite jobs. **Left deliberately for
Patrick.**

**Cooking supply is thin.** 94% of asks answer "no meat worth cooking". The keep-back works — 112 stacks in a
session, and only 3 sold past the cap — so the shortage is in what the population kills, not in what it does
with it. Unmeasured: how many kills are actually animals.

**The smith's new threshold costs something.** `BotAnvil.Tries` turned 82 potential rounds into
`with metal but not enough for any recipe they can work`. Success went from 29% to 86% and pieces made went
up, so the trade is ahead — but the number has not been tuned, only chosen.

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
