# Hunt: where the money in this world comes from

The first work in the project that **brings new gold into the world**. Everything else only moves it about.

| File | What is in it |
|---|---|
| `BotQuarry.cs` | what is worth fighting, and where what is left of it is lying |
| `BotSlay.cs` | an obligation: close → fight → go through the corpse |
| `BotHunter.cs` | the proposer: who is whole enough to want a fight |
| `BotHuntConfig.cs` | `Configuration/bot-hunt.json` |
| `BotHuntModule.cs` | module, phase `World`, requires `Classes`, `Will`, `Auction` |

---

## Why this became urgent

Before this folder **there was no gold in the world at all.** Not "little" — not one line that creates a coin: a
bot is born with none, trade between bots moves it about, and a shopkeeper's counter is where it leaves. The
consequence is arithmetic: `BotShops.Buy` computes what is affordable as `purse / price`, which at zero returns
zero, so **everything with an `Outlay` failed on its first beat** — sewing, writing, filling a book, restocking.
The only thing that could happen was digging, because digging is free.

A monster's purse is the only faucet. Everything else is that same gold one step further from the field: a
fighter pays a smith for a blade, the smith pays a gatherer for ore, a caster pays a scribe for a scroll.

---

## The fighting is entirely the engine's, and that is the key fact

`Mobile.Combatant = target` **starts a server-side timer by itself** (`CheckCombatTime`, every 0.01 s) which
checks range and line of sight, swings the weapon, rolls to hit, applies damage and wears the blade down. No
client is involved and there is nothing to simulate.

So this file never decides a blow. It decides three things: who to go for, when to run, and what to take off the
corpse.

## Who: the biggest thing we can take

Everything the judgement needs is already written in `BotCombat`. `Power` is toughness × damage, `Hostile`
answers what counts as a target at all, `OurPower` sums our side **including the neighbours**, `Tolerance` is 1.5.

There is exactly one new question: of the things I could beat, which is the biggest. Not the nearest — distance
is already priced by the appraisal's nearness factor, so a hunter that preferred the closest rat would be paid
twice for being lazy.

The threshold is **the same tolerance the flight decision uses**. Not elegance: a bot must not walk towards
something it would immediately run away from.

**And cooperation here is free.** It is judged against `OurPower` rather than against the bot alone, so two bots
in the same field take on what one of them would have walked away from — and neither had to be told the other was
there. No party, no leader, no message: the same arithmetic-from-a-shared-fact that decides everything else here.

## When to run: 40 %, and that the threshold exists matters more than its value

Before this session the "health is going" rung was served by nobody — so the brain's answer to failing health was
"hold on to what you are doing", and what this bot is doing is dying. `BotMend/` serves it now, but flight still
lives **here**: mending under blows is pointless, and the decision to break off a fight can only be taken by the
work that is running it.

In v1 this cost 443 deaths in a night, 104 of them one bot rising in the same tile every twenty to forty seconds.
Not once in all 443 did "too dangerous to get up" fire. So the flight decision lives inside the obligation, and
an obligation has to be willing to give itself up.

A failure marks the place with caution — exactly the right record: **this patch of ground kills me.**

The threshold for setting out (`FitAt` 80 %) is above the threshold for fleeing (`FleeAt` 40 %) on purpose:
without the gap a bot that has just escaped is immediately offered the same fight by the same arithmetic.

## What to take: everything, and to the market rather than to a counter

There is no "treasure or rubbish" test here and none is needed. **Everything picked up goes to the bots' own
market**, and a stall that nobody bought from in half an hour is carried to a shopkeeper by `BotPeddler` — the
same road produced goods travel.

One bot's junk is another's material, and the only thing that can tell the difference is the market. So the
market is asked, every time, before a counter is.

The limit is **weight, not value**. Loot is not bound, so it weighs, and a hunter that empties a corpse into a
full pack is a hunter that cannot carry its own takings home. What is left stays on the corpse for whoever comes
past.

Gold goes into the purse and is counted as gold; goods are counted as `Made`. Neither is counted twice.

---

## The boundaries, and why they are deliberately narrow

Thirty tiles of sight inside two hundred tiles of `Roam`. v1's death loop cannot be built at those boundaries: a
bot is never far from the place it will get up.

The price of that is stated plainly: **the monsters near a town are poor, so the faucet will be thin.** Named
hunting grounds — a graveyard, a dungeon mouth — are the next step, and they will need a separate defence of
"do not get up where you are being killed", because that is exactly where v1 died.

## The one honestly expensive line

The proposer makes a **real spatial query every time** a free bot asks. This is the only exception to "a question
of the world must not be an expensive one", and it is unavoidable: a vein stands still and can be remembered, a
shopkeeper stands still, but a monster walks and respawns — a remembered one is a lie within a minute. What is
remembered instead is **which patch pays**, and the thing that remembers it is the ledger, whose business facts
about ground are.

---

## What appeared outside this folder along with it

**`BotShops` learned to sell.** `Buys` asks the shopkeeper itself (`IsSellable`/`GetSellPriceFor` — about a
specific object, because that is the only question the engine answers), `Buyer` finds the nearest one, and `Sell`
goes through `OnSellItems`. The takings are **measured** rather than summed from price tags: the engine decides
how much of an order it honours. And it requires `IsStandardLoot()`, while the bind marks things `Newbied` — so
**the weapon and the book cannot be sold at the engine level**, a free guarantee on the "not merchandise"
promise.

**`BotPeddle`/`BotPeddler`** — selling as work, with one condition for going: a stall that has never sold one and
has stood for `StaleMs`. Half an hour in front of the whole population and nobody took it. No "is this junk"
test, no table of worthless things. And it can only ever offer what the bot **itself decided to sell**, so tools,
herbs and paper cannot be sold — they are never on a stall.

**A fighter came to want what a crafter makes.** A weapon wears on every landed hit and the engine destroys it at
zero; `BotShopper` now notices a missing blade (at the highest priority, above tools) and spent arrows, and
`BotRestock` gained a "buy it off another bot" route. Whichever is cheaper, and **a shopkeeper is the ceiling
rather than the preference** — which is precisely why a fighter's gold goes to a smith instead of out of the
world.

`IBotWilful` gained `Bond`, because otherwise "buy another of what I had" is unanswerable: the class offered six
blades and the roll handed over one, together with the skill that swings it.

---

## What to check with a client

1. `The hunt is on: ...` at startup, and `Hunt` among the eleven `World`-phase modules.
2. `took on hunt`, then `fighting a skeleton`, then `finished hunt: 2 things and 43gp off a skeleton`. That is the
   first gold in the project's history.
3. `[bots`, the `purse` column — it must stop being zero. If it is zero for everybody after ten minutes, look for
   the `Nothing within 30 tiles ... is worth fighting` line.
4. The shops line on reload: `N things bought for Xgp, M sold for Ygp`. **The difference between those two
   numbers is the health of the economy**, in one subtraction.
5. Kill a bot by hand near a monster and check that it does not come back to die in the same tile: a failed hunt
   marks the place with caution.
6. `bot-hunt.json` → `"FleeAt": 0.0` — the bot stops running away. That is the A/B for v1's death loop; switch it
   on only to see it.

## What is not here

**Squads.** `BotSquads.Form` is still called by nobody — the decision is solo hunting, with the cooperation
`OurPower` gives for free. Joining a company as an obligation with a price comes after solo hunting has been
measured.

**Magic in a fight.** A caster walks into melee. The book fills up, spells are not cast, and reagents are spent
only on writing.

**Armour.** There is none in the kit at all. That is the next step, together with smithing — before it, armour
would be a need a shopkeeper satisfies, i.e. a new gold sink rather than a crafter's income.
