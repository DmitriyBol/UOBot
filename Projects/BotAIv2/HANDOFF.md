# Handoff: the state of botAiv2 on 21.08.2026

Sixteen folders — fifteen subsystems and the loading frame. **It builds clean against the fork: 0 warnings, 0
errors** (`BotAIv2.dll`, 239 KB). The fork sets `TreatWarningsAsErrors=true`, so clean here means genuinely clean,
and the build is now a five-second feedback loop rather than a one-off event.

For how it is put together, read `ARCHITECTURE.md`. For how to build it and what the boot log should say, read
`BUILD.md`. This file is the state of the work: what is done, what is known to be missing, and what the last
review found.

## What happened in this session

**Magic** (`BotSpells/`) as both a trade and an appetite, the **demand side of the market** (funded wants), a
**third dashboard tab**, producers pricing their output from the market — and **hunting** (`BotHunt/`), because
the review uncovered the thing that mattered most: **there was no gold in this world at all.**

Not one line created a coin. A bot is born with none, trade between bots moves it about, a shopkeeper's counter is
where it leaves. So everything with an `Outlay > 0` failed on its first beat, and the only thing that could happen
was digging, because digging is free. The faucet is a monster's purse; all the other gold is that same gold one
step further from the field. `BotShops` also learned to **sell** (`OnSellItems`), selling became work with one
condition for going, and a fighter came to want what a crafter makes — a blade wears down in a fight and is
destroyed at zero.

Then **mending** (`BotMend/`), which populated `Failing` — the last empty rung of the ladder. While nothing
fought, an empty `Failing` was harmless; the day hunting arrived it started meaning "go back to the skeleton".

Plus **ten defects found by review**: four in code that was already here and already built clean, and six in what
was written this session. §4.

---

## 1. What exists

| Folder | Files | Lines | What it is |
|---|---|---|---|
| `BotModules/` | 3 | 399 | the loading frame: phases, declared dependencies, a switch on each |
| `BotClasses/` | 18 | 1548 | nine classes — data and limits, no behaviour |
| `BotOutfit/` | 4 | 906 | issuing the kit, binding the weapon, zero weight, the ammunition count |
| `BotMovement/` | 10 | 2800 | pathfinding, pockets of ground, the queue of errands, the moment of the step |
| `BotCombat/` | 1 | 307 | strength, threat, the decision to fight or walk |
| `BotSquad/` | 8 | 1551 | squads: leader, formation, scouting, spoils — **written, unwired** |
| `BotWill/` | 14 | 2692 | the decision: ladder, obligations, auction, ledger |
| `BotHarvest/` | 6 | 1801 | dig → smelt → bank |
| `BotShops/` | 7 | 1313 | NPC trade both ways, and selling as work. **Where the world's money enters and leaves** |
| `BotCraft/` | 5 | 673 | buy cloth → sew → sell |
| `BotAuction/` | 5 | 2056 | the market, both sides: stalls and **funded wants** |
| `BotSpells/` | 8 | 1517 | **write scrolls, fill a book** |
| `BotHunt/` | 5 | 659 | **close, fight, loot. The only faucet of gold** |
| `BotMend/` | 6 | 961 | **mending: yourself above all, others as work. Populates `Failing`** |
| `BotPopulation/` | 5 | 1349 | the bot on `PlayerMobile`, the population's clock, birth and raising the fallen |
| `BotDashboard/` | 2 | 581 | `[bots`: the population, its market, and its shortages |
| root | 1 | 98 | `BotCore` — the entry point |

108 files and 21,211 lines of code. Plus twenty-one documents: `ARCHITECTURE.md`, `BUILD.md`, this file, one
README per subsystem, and `BotMovement/RESEARCH.md` (the v1 movement analysis, with measurements).

**Eight proposers, from six subsystems:** mine, sew, inscribe, acquire, hunt, mend (twice, on two rungs), restock,
peddle.

---

## 2. What a first run should do

Four bots are born in Britain, dress themselves and go to work. They dig ore, smelt it, put the metal on the
market and take to a counter whatever nobody wanted. The crafter buys cloth and sews. The mage writes scrolls,
keeps the first of each kind for its own book and sells the rest. The healer buys the spells it cannot write.
Anybody hurt patches itself up — spell first if it can cast, cloth under fire, a bottle below forty per cent — and
a caster will walk over to patch up somebody worse off. Whoever is whole enough goes looking for something it can
beat, kills it, goes through the corpse, and that is where every coin on the shard comes from.

`[bots` shows all of it, one row per bot, plus the market and the shortages.

---

## 3. Known unknowns

- **It has never been run.** It builds; there is no live run — the development machine has no client files (maps,
  art) and the server will not come up without them. Everything about behaviour is still reasoning.
- **No human has read** `BotWill`, `BotHarvest`, `BotPopulation`, `BotAuction`, `BotShops`, `BotCraft`,
  `BotSpells`, `BotHunt`, `BotMend` or `BotDashboard`. The compiler accepted them. What *has* happened is the
  review in §4, which does not replace reading.
- **Only the first draft of `BotMovement` was reviewed.** It found eleven defects (all fixed, written up in
  `BotMovement/README.md`), but the fixes themselves, the rework of the road into a queue, `BotCombat` and
  `BotSquad` have not been read by anybody.
- **Nothing calls `BotSquads.Form`** — 1551 lines that have never run. Who forms a squad and when is a decision:
  in v2's terms, a proposer on the `Free` rung that nobody has written. Dead code that compiles is the most
  expensive kind, because it looks maintained.
- **No real retreat.** "Flee" means "stop chasing": `OnDamage` sets `Combatant` even when outmatched, so a bot
  answers blows where it stands, and there is no walk-*away-from* primitive. This is the largest remaining gap and
  it is what v1 died of — 443 deaths in a night, 104 of them one bot rising in the same tile every half minute.
- **Nothing casts offensively.** Books fill up; spells are spoken only by `BotMend`. So reagent demand is
  proportional to **climbing Inscribe** rather than to using magic, and it will end when the climb does. Perpetual
  herb demand arrives with combat magic or alchemy; there is no brewing in v2 at all.
- **Gathering herbs is not bot work, deliberately.** In this era reagents are shop goods and no skill picks them.
  The gatherer's reagent talent is declared in the class data and unimplemented: making it work would mean
  inventing a mechanic instead of using the engine's.
- **Six of eight potion families are declared and unused.** Only Heal and Cure are issued and drunk. The rest are
  buffs and weapons with nothing to use them.
- **No armour in the kit at all.** That is the next step together with smithing — before it, armour would be a need
  a shopkeeper satisfies, i.e. a new gold sink rather than a crafter's income.
- **Metal still has no buyer among the bots.** Scrolls do, as of `BotSpells`. Until smithing exists, ingot prices
  will creep to their floor — which is correct market behaviour, not a breakage.
- **`BotSpoils.Worth` is unfilled**, so dividing spoils degenerates into counting items. It is now a one-line fix:
  `BotAuction.Worth(type, 0)` is a price.
- **`Cell` folds Z into bands of 20**, as the engine's own search does: a bridge at Z=25 and the road under it at
  Z=12 land in one A\* node. Same as v1, but the band now also keys the pocket registry. If oddities with bridges
  and balconies show up live, look here.
- **Pockets of ground are not written to disk.** Deliberate: a wrong "impossible" is invisible and permanent.
- **Nothing survives a restart.** Skills, books, ledgers and the market all live one session.
- **The numbers are unverified and there is nothing to verify them with but a run:** 500 gold a skill point, a
  third for a skill off the vector, ×1.25 margins, 0.8 on crowding, 3 minutes for death, 6 for an ingot, 12 for a
  tailor's piece, 60/min for writing, a slice of 5, `Inscribe 80` for the mage, `12 + 10×(circle−1)` as the opening
  bid for a scroll above the third circle. That last is the only extrapolation in the magic subsystem: up to the
  third circle it is the engine's own ladder, read out of `SBMage`.
- ~~`Item.Weight = 0.0`~~ — **closed.** `Projects/Server/Items/Item.cs:474` is a real setter, so
  `BotBinding.Weightless` compiles and works. This was the project's largest known risk and it is gone.

---

## 4. What the review of 21.08 found

Ten places. Four of them were in code that was already here and already built clean — that is, the compiler let
them through, and a live run would have shown them as "odd behaviour" rather than as an error. This is what the
line "the compiler accepted them, no human has read them" is worth.

### In the older code

**Six reagents of eight were issued.** `BotOutfit` and `BotShopper` did not know bloodmoss or mandrake root. The
six were chosen to match exactly the three starting spells, and that was right until the book grew. With a full
book, without those two, clumsy, agility, cunning, strength, bless, teleport, unlock, wall of stone, arch cure,
greater heal, lightning, mana drain and recall cannot be cast — and almost nothing can be inscribed.

**A tool ran out and nobody bought another.** The engine gives a fresh tool 25–75 uses, spends one an attempt and
destroys it at zero (`BaseTool.BreakOnDepletion => true`; `CraftItem.Craft` decrements). A crafter swinging every
three seconds was out of a sewing kit after two and a half minutes of work — and out of a trade for good.
**Nothing in the log would have looked wrong:** the proposer simply stops offering the work, exactly as it does
for a bot that never had a kit.

The first fix was wrong — I made the tool unwearable. **Patrik's decision, 21.08.2026: only the weapon is bound**
(plus its ammunition, the staff and the book); a tool wears through, drops into the corpse and is bought again.
Better for three reasons: the shard gains a reason to keep toolmakers, a trade gains a floor under its costs, and
death gains one more price. The defect was never the wearing out but that **nobody noticed the loss**: now
`BotOutfit.ToolsFor(klass)` is one list asked by two callers, and `BotShopper` buys a replacement at the highest
priority, above bandages. The hatchet, incidentally, is sold by **the weaponsmith only** — the narrowest supply on
the shard; the tinker stocks nearly everything else.

**A tie in the opening points was resolved by enumeration order.** The hundred points of character creation go to
the three highest targets, and `List.Sort` is unstable. The comments in `BotWarrior` and `BotCrafter` have warned
about this from the beginning (in v1 `Swords` and `Tactics` were both at 70; half the warriors spent their best
points on Tactics and could not hit anybody) — but the warnings were the only protection. A tie now breaks on
declaration order: a class writes its skills in the order they matter to it.

**The market could not split a single stack.** `BotListing.Portion` used `Activator.CreateInstance`, which looks
for a genuinely parameterless constructor — and almost everything stackable in the game is declared
`Foo(int amount = 1)`, which has none. So ore, reagents, scrolls and bandages **all** fell through to the "whole
objects only" fallback, honestly documented as a rare case. It now uses the engine's `Type.CreateInstance<T>()`,
which fills optional parameters with `Type.Missing`.

### In the new code

**A raised offer became unpayable, and that killed the whole mechanism.** A want can buy `escrow / offer` things. A
want for one scroll with exactly one scroll's money down, whose offer rose 15 % on the market's beat, could
suddenly buy nothing — it would have raised itself out of existence on the first beat and no supplier would have
seen it. The beat now asks what the offer **would become**, tops up the difference from the buyer's purse, and only
then lifts it; a bot with nothing to outbid with cannot claim to have outbid, and its want is dropped with the
money returned.

**Writing into a book abandoned the rest of the stack.** `Spellbook.OnDragDrop` consumes **one** scroll from a
stack. The code did `continue` after a successful write — so the other two scrolls reached neither the market nor a
want and rode around in the pack for ever.

**The seeker asked the world sixty-four times per decision.** The first `BotSeeker` walked every spell and asked,
for each, who sells it — and that question walks every remembered shop and reads its whole stock list. Per beat,
per bot. That is v1's cost model in a new hat, and the proposer interface forbids it in as many words. Now: one gap
at a time and one question of the world.

**And the same in choosing what to write.** `Choose` asked "does the book know this spell" for every candidate, and
every such question walked the pack looking for the book. The book is fetched once, and the order of checks was
rearranged so that herbs are only counted for recipes that can still win on difficulty.

**Delivered goods could hang in the market for ever.** A want held handed-over goods until the buyer came to
collect — and collecting pays nothing per minute, because the goods are already bought and paid for. So a bot with
a trade would always rather do its trade, and a scroll written to order would have stood in the market for the life
of the shard. **A state with no guaranteed exit is the same thing "stand and wait" was in v1.** The market now
hands goods over on its own beat and holds them only while a pack will not take them, which is what the holding was
written for.

**A scribe that wrote a spell into its own book went on asking for it.** The escrow sat there for a spell the bot
already knew, until the market gave up at the ceiling — hours. Writing into the book now drops its own want, as a
purchase does.

**A target on a bot is not necessarily ours.** The first `BotSalve` filled in whatever target it found with the
patient — and harvesting also puts a cursor up, so it could have pointed a mining target at a wounded friend.
Now only the target whose cast this same work started gets filled in.

### The engine trap that was avoided

`Spellbook.OnDragDrop` writes the spell, consumes one scroll and returns `scroll.Deleted` — and `Item.Consume` only
deletes when the stack runs out. So a scroll from a stack of two **is written into the book and reports failure**,
and a caller believing the answer will try for ever against a book that already knows the spell. Exactly the same
shape as the loot flag on a merged stack of arrows: the engine answers about the object and the question was about
the contents. `BotGrimoire.Write` asks the book afterwards.

---

## 5. What to watch first on a live run

In order of how expensive it is to be wrong.

1. **`Bot modules, World: 11 of 11 started`.** The last part of that line — "so many that should be running and
   are not" — is the one number answering "did everything come up". Everything else is downstream of it.
2. **`Read 64 of 64 scrolls`.** Fewer than 64 and the scroll-to-spell map is incomplete, and the whole magic
   subsystem goes quiet without a single error.
3. **The first gold.** `finished hunt: 2 things and 43gp off a skeleton`, and then the `purse` column in `[bots`
   ceasing to be zero. If it is zero for everybody after ten minutes, look for
   `Nothing within 30 tiles ... is worth fighting` — that is the faucet being dry, and everything with an outlay
   fails behind it.
4. **The reload line from shops:** `N things bought for Xgp, M sold for Ygp`. **The difference is the health of the
   economy**, in one subtraction. v1's was −110,900 over a night and nobody could say where.
5. **The first spell learned.** `learned CureScroll for 22gp` from the healer — the first bot to have acquired
   rather than made something.
6. **The first trade between two bots.** `filled ...'s want for 1 ... and was paid Ngp`.
7. **Does anybody die repeatedly in the same tile?** That is v1's death loop and the reason `FleeAt` exists. A
   failed hunt should mark the place with caution.
8. **`[bots` → Needs.** Empty for the first hour is normal: circles 1–3 are on a shelf. The first row appears when
   somebody's book reaches the fourth circle.
9. **Ingot prices.** They must creep to the floor while there is no smithing. If they do not, somebody is buying
   them and it is worth finding out who.
10. **`nothing was worth doing`** in the brain's census. For the warrior, archer and brawler it will rise, and that
    is correct — they have no pickaxe, so mining is not offered, and hunting is all they have. For the mage and the
    healer it should not.

---

## 6. The v1 evidence this was designed against

Every item below is a measured defect rather than an opinion. `BotWill/` was written against this list, and each
mechanism's README names the item it came from. Keep the list: it is what to check v2 against if it ever starts
drifting.

### What not to do

**Do not write one method.** `BotBrain.ChooseGoal` in v1 was **1209 lines** inside a file of 7985 that referenced
57 other modules. Every change to any behaviour was a change to that file.

**Do not reconsider the decision every tick.** Noticed from the symptom "a bot in state Trade walking a
graveyard". It was honestly trading, one tick at a time: two steps, a skeleton ten tiles away, back to hunting.
**A long journey is impossible in principle under that design** — any intention longer than a second is overwritten
by the nearest temptation.

**Do not put a call for help above your own health.** In v1 the "dangerous, calling for help" rung stood **above**
"my health is going". A bot on its last few points announced a company it could not join; it found nobody able and
disbanded in the same tick — and the same bot posted it again, dozens of times over. Flight must outrank the
social.

**Do not judge bots that are standing still.** Trading at a counter, working at a forge, mining. In v1 a bot at a
counter was measured against the distance to a home it was not going to, and after 25 seconds it got "stuck", a
cancelled errand and a five-minute ban on trading — **for trading**.

**Do not let waiting be uninterruptible.** A bot must learn it has been hit through `OnDamage` rather than by
looking: a caster strikes from eight tiles and never enters contact, so checks for "in contact" never fire.

### What motivates a bot

**Motivation is deficits, and that is also its trap.** Every motive in v1 was a shortage, and shortages get
satisfied: 38 bots out of 51 ended up on patrol with drive frozen at 0.62. Not a defect — **they had run out of
things to want.**

Which is why the takings-per-minute formula is a derivative: a mastered occupation stops paying by itself. And the
measurement that spoiled v1's version of this: a hundred starting coins against a comfort threshold of 250 meant
**the whole population was born "pinched"**, and a signal that is on for everybody always is not a signal.

### The slow tier (a model), if it ever returns

v1's `PLAN.md` §10 and `evidence-20260820/`. The transport worked flawlessly — 1281 requests, 0 drops, 4 s, 4 % of
the game loop. The learning loop never closed, and the reasons are nameable:

- **0 of 119 predictions borne out.** 89 % of plans ended in an arrival and were never reviewed, so almost only
  failures reached review.
- **85 of 135 reviewed plans the brain never took** — the model's suggestion lost to any errand the brain had, and
  the model did not know. **The slow tier must have a vote.** `IBotProposer` is that vote, by construction.
- **A prediction must be measured on the horizon it was given for.** A journey that ends in an arrival does not
  refute a promise about trading.
- **The lessons degenerated into noise:** all ten of one bot's rules were rephrasings of one, and it was learning
  about the guard "the brain did not take the plan" rather than about the world.
- **The wording of the prompt is a defect surface.** "Carrying 39 of 215 stones" turned into a plan to sell rocks.

### What not to judge too early

**The economy was negative.** Over one night v1's world lost 110,900 gold: 67k mined against 156k spent at vendors
plus 21k in commission. The median bot held 57gp against a poverty line of 800. The population degenerated into 116
traders to 14 fighters.

While a shopkeeper takes more than the ground gives, **any motivation mechanic will produce destitute traders**.
The brain can be written, but it cannot be judged by the population's wealth until the money balances. In v2 that
is now one subtraction — see §5, item 4.
