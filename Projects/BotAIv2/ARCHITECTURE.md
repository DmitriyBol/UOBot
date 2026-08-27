# BotAI v2 — Architecture

An autonomous population for a ModernUO shard, built as a **separate assembly** (`BotAIv2.dll`, namespace
`Server.BotAI.V2`) that the engine loads through `Data/assemblies.json`. Nothing in the engine references it,
so it stays clear of the upstream rebase path entirely.

Read this file once and the rest of the project becomes navigable. Every subsystem has its own README with the
reasoning behind its own decisions; this file is the map, the vocabulary, and the handful of rules that hold
across all of them.

- **State of the work** → `HANDOFF.md`
- **How to build and what the boot log should say** → `BUILD.md`
- **Why any individual decision is the way it is** → that subsystem's `README.md`

---

## 1. The shape in one page

A bot is a `PlayerMobile` subclass. One timer serves the whole population; each bot gets a turn on its own
schedule. On its turn a bot asks one question — *what should I be doing?* — and the answer comes from one
place: `BotWill`.

```
                    ┌──────────────────────────────────────────┐
   one timer  ──────│  BotPopulation : the bot, and its beat   │
                    └───────────────────┬──────────────────────┘
                                        │  BotWill.Decide(bot)
                    ┌───────────────────▼──────────────────────┐
                    │  BotWill : ladder → obligation → auction │
                    └───────┬───────────────────────┬──────────┘
                            │ asks every proposer   │ advances the held deed
        ┌───────────────────┴───────┐               │
        │  IBotProposer  (8 of them)│               │  BotDoing: walk / work / done / failed
        │  Harvest Craft Spells     │               │
        │  Hunt Mend Shops Peddle   │               ▼
        └───────────────────────────┘        BotMovement (a step, a path, a queue of errands)
```

Everything else is a capability those proposers reach for: `BotOutfit` (what a bot owns), `BotCombat` (is this
fight winnable), `BotShops` (buy and sell over a counter), `BotAuction` (the bots' own market, both sides),
`BotClasses` (what a kind of bot is).

**No subsystem asks another to make a decision.** They read each other's facts. That is the single rule that
makes the whole thing tractable — see §2.

---

## 2. Five rules everything rests on

Each one is a measured defect of the first version, not a preference. They are the reason this is a rewrite and
not a cleanup.

**One subsystem, one folder, and its configuration file lives in it.** In v1 every dial on the shard lived in
one `bots.json`: editing an archer's aim was editing the file that sets the population size, and a typo in
either half killed everything.

**Subsystems read each other's data and never call each other's decisions.** `BotOutfit` knows what a
`BotClass` is because a class is a description; it never asks a class what a bot should do. This is what makes
a subsystem readable on its own.

**Order is declared, not arranged.** A module states what it needs ready; the loader derives the sequence. In
v1 load order was a list of thirty calls held together by fifteen comments saying "this must come after that",
and a mistake in it was undetectable by reading. The worst cost eleven nulls in a recipe index that then
behaved plausibly for a whole session.

**A bot's state lives on the bot.** `BotBond`, `BotJourney`, `BotResolve`, the squad reference — fields on the
bot, not rows in tables keyed by serial. v1 had thirty-two such tables in thirty-two files: each needed its own
reset, each leaked when the population was torn down, and "what does this bot have" was a question that took
thirty-two files to answer.

**The shard's own rules are obeyed, not rediscovered.** The fork's `CLAUDE.md` and `dev-docs/` are the source.
See §10 — three of them changed real code.

---

## 3. The dependency order, and the four places it is broken

Fourteen modules. The loader resolves the order itself from what each one declares:

| Module | Phase | Requires |
|---|---|---|
| `Classes` | Settings | — |
| `Dashboard` | Settings | — |
| `Modules` (the frame) | — | — |
| `Movement` | World | — |
| `Combat` | — | — (no module; pure arithmetic, called directly) |
| `Auction` | World | — |
| `Will` | World | Classes |
| `Squads` | World | Classes |
| `Shops` | World | Classes, Will |
| `Harvest` | World | Classes, Will |
| `Mend` | World | Classes, Will |
| `Hunt` | World | Classes, Will, Auction |
| `Craft` | World | Classes, Will, Shops |
| `Spells` | World | Classes, Will, Shops, Auction |
| `Population` | World | Classes, Movement, Will |

The intended layering, from the bottom:

```
0  BotModules      the frame: phases, declared needs, a switch on each
1  BotClasses      data: nine kinds of bot, what they want, what they carry
2  BotOutfit       what a bot owns and what death may not take
   BotMovement     a step, a path, pockets of unreachable ground, a queue of errands
   BotCombat       strength, threat, fight-or-walk — pure arithmetic over Mobile
3  BotWill         the decision: ladder, obligation, auction, ledger
4  BotAuction      the bots' market, both sides
   BotShops        buying and selling over an NPC counter
5  BotHarvest      dig → smelt → bank
   BotCraft        buy cloth → sew → sell
   BotSpells       write scrolls → fill a book
   BotHunt         close → fight → loot
   BotMend         heal, self above everything
6  BotPopulation   the bot itself, and the population's clock
   BotDashboard    [bots — three tabs
```

**Four real inversions, named rather than hidden.** All four compile (one assembly), and each is a small debt
worth paying when it next gets in the way:

| Where | What | Why it is wrong |
|---|---|---|
| `BotAuctionConfig` → `BotDig.ListGoods` | the market's config file owns a mining dial | the knob belongs to whoever produces, not to the place they sell |
| `BotSew` → `BotDig.CounterReach` | one trade borrows another trade's number | two trades that share a number will disagree about it eventually |
| `BotShops`, `BotHarvest`, `BotHunt` → `BotPopulation.Within` | layers 4–5 reach up to layer 6 | the Britain boundary is a fact about the **world**, not about the population; it is in the wrong folder |
| `BotMend` → `BotSpells.BotGrimoire` | healing reads the spellbook | defensible — a heal is a spell — but it makes mending unavailable without magic |

Doc comments cross-reference freely (`<see cref="BotShopper"/>` in `BotOutfit`, and so on). Those are prose,
not dependencies.

---

## 4. The one currency

Every want in the project is compared in one unit: **takings per minute.**

```
worth per minute = (Δmoney + goods produced + Δskill × rate) / minutes
```

`rate` is one number — `BotYield.GoldPerSkillPoint = 500` — and it is the most important dial in the project.
Three rules around this formula matter more than the formula:

**Points are for change, not for state.** "Has 500 coins" is worth nothing; "banked 500" is worth 500. Not a
matter of taste: a bonus added to a reward provably leaves the best behaviour unchanged only when it is a
difference of potentials (Ng, Harada & Russell, 1999), and the classic price of breaking that is an agent that
discovers standing in the right place pays.

**Skill counts only in finished work.** Otherwise the best strategy on the shard is the cheapest repeatable
twitch that trains: a dummy, a spell cast at nothing, two bots sparring in a field until the server restarts.

**Death is expensive because everything else made it cheap.** The kit is bound — it survives death and comes
back on resurrection — so dying costs almost nothing by itself. `BotYield.DeathMinutes = 3` goes into the
divisor and the place is marked with caution.

**Value is measured, not assigned.** A finished obligation files its takings-per-minute in `BotLedger` under
*kind of work + patch of ground*, and next time that is the estimate. There is no model, no training, no
serialisation, and it recovers by itself: a worked-out seam stops paying and its row falls with it. A proposer
that is systematically optimistic gets corrected by the shard rather than argued with.

**Punishment is not a mechanism.** A failure is a low number in a row. v1 punished with bans — a bot judged
stuck lost its errand and was barred from trading for five minutes, i.e. punished for trading.

### The dials that decide behaviour

Everything else lives in the subsystem READMEs. These are the ones that change what the population *is*:

| Dial | Value | What it decides |
|---|---|---|
| `BotYield.GoldPerSkillPoint` | 500 | how much a population values becoming good at something |
| `BotYield.StrayFactor` | 0.3 | what a skill off the class's own vector is worth |
| `BotYield.DeathMinutes` | 3 | the price of dying |
| `BotYield.MostPerMinute` | 2000 | the ceiling on any single measurement |
| `BotAppraisal.CrowdBite` | 0.8 | how much a want is worth less because others are already doing it |
| `BotAppraisal.Inertia` / `SwitchMargin` | ×1.25 / ×1.25 | how much better a new thing must be to be worth switching to |
| `BotWill.ReviewMs` / `DwellMs` | 15 s / 30 s | how often a busy bot looks up; how long fresh work is untouchable |
| `BotLadder.FailingFraction` | 0.35 | when a bot stops weighing options and looks after itself |
| `BotThreat.Tolerance` | 1.5 | how much stronger the opposition may be before walking beats standing |
| `BotPopulation.Roam` | 200 | the boundary on **wanting**, not on walking. Temporary |

**No configuration file anywhere sets a price.** Config files set speeds and limits. The one exception is the
stand-in openers (`GoldPerIngot` 6, `GoldPerPiece` 12) which exist only until the market has traded one, and
`BotAuction.Worth` asks the market first, always.

---

## 5. The ladder

Survival is a **priority ladder**, not an appraisal: a bot that is dying does not weigh options. Everything
discretionary is appraised, so that wants compete in comparable units.

```
Dead  →  Failing  →  Hunted  →  Bound  →  Busy  →  Free
```

The auction runs on `Free` and on any rung above it that has a proposer of its own. A rung with no proposer is
honest rather than broken — the bot keeps what it is doing and the shortage is reported once, by name.

| Rung | Fact that puts a bot on it | Who answers it |
|---|---|---|
| `Dead` | not alive | the population's clock (raises the fallen) |
| `Failing` | health below `FailingFraction` | `BotMedic` — mend yourself, above all else |
| `Hunted` | being hit | nobody: "don't go looking for new work mid-fight" |
| `Bound` | obligations to a squad | nobody (squads are unwired — §11) |
| `Busy` | holding an obligation | the obligation itself |
| `Free` | none of the above | seven proposers, by appraisal |

Two deliberate departures from v1, both fixing measured defects: **flight outranks the social** (v1 put "call
for help" above "I am dying", so a bot on its last points announced a company it could not join, found nobody
able, and posted it again dozens of times); and **overload is a fact, not a rung** (as a rung it postponed the
only thing that ends the problem — carrying the ore somewhere).

---

## 6. How to add work

This is the recipe, and the whole architecture exists so that it is this short. Adding a trade **touches
nothing in `BotWill`**.

1. **A folder.** `BotSmith/`, say.
2. **A `BotDeed` subclass** — the obligation, with its own stages. It answers `Advance(bot)` with a place, or
   "work here", or an ending. It knows the sequence; the brain never learns what ore is.
3. **An `IBotProposer`** — one best offer for this bot right now, or null. The proposer owns the expensive
   question (*which vein? which shop?*) because only it can compare two of them.
4. **A config class** — `Configuration/bot-smith.json`, speeds and limits, never prices.
5. **A `BotModule`** — phase, `Requires`, and `BotWill.Offer(new BotSmith())` in `Start`.
6. **One line in `BotCore`** — `BotModules.Register(new BotSmithModule())`.
7. **A README** explaining why each number is that number.

Rules for the deed that are worth knowing before writing one:

- **The obligation owns its whole chain.** One that ends at the vein leaves a bot underground holding rock.
- **`Trains` is named, never inferred**, and credited only when the work *finishes*.
- **`Made` counts what was produced and not sold.** Anything paid for in coin is already in `Δmoney`; counting
  it in both pays the bot twice.
- **`Outlay` is what need is measured against** — not a comfort threshold. v1 compared every purse to a flat
  250 and issued 100 at birth, so the entire population read as short of money from its first second, and a
  signal that is on for everybody always is not a signal.
- **No state may wait indefinitely.** Out of materials, patient healed, quarry gone, market closed — every one
  of them ends the obligation on the beat it becomes true. "Stand and wait" is the shape of bug this project
  keeps finding in itself.
- **`Propose` may ask a real question of the world but not an expensive one.** A spatial sweep per free bot per
  beat is only acceptable where the fact genuinely cannot be remembered (see `BotHunt` — a monster walks).

---

## 7. Vocabulary

The names are unusual and consistent. Knowing them makes the code read like prose.

| Word | Means |
|---|---|
| **deed** (`BotDeed`) | one undertaking with stages, held until it finishes, fails or is dropped |
| **proposer** (`IBotProposer`) | something that knows one kind of work and can offer a piece of it |
| **doing** (`BotDoing`) | what the deed wants of the bot right now: walk, work, done, failed |
| **rung** (`BotStanding`) | where a bot is on the survival ladder |
| **takings** (`BotTakings`) | what a finished deed came to, in the one currency |
| **ledger** (`BotLedger`) | what has paid this bot, and where. All the memory of work there is |
| **bond** (`BotBond`) | what the world gave a bot at birth and what death may not take |
| **journey** (`BotJourney`) | the queue of errands; a fight stacks on top and the destination waits under it |
| **resolve** (`BotResolve`) | a bot's feelings, ledger, what it took on and why |
| **stall** (`BotListing`) | one bot's standing offer to sell one kind of thing |
| **want** (`BotWant`) | the same with the sign turned round: an offer to buy, with the money already down |
| **kit** (`BotKit`) | what a class is issued at birth |
| **vector** | how far along its own class's declared skill targets a bot has come |

---

## 8. The economy

**Gold enters the world in exactly one place: a monster's purse.** Bots are born with none, trade between them
only moves it about, and a shopkeeper's counter is where it leaves. This was discovered by reading rather than
by running, and it is worth stating as bluntly as possible: before `BotHunt` existed, every piece of work with
an `Outlay` failed on its first beat, and the only thing that could happen on the shard was digging, because
digging is free.

```
   monster ──gold──▶ fighter ──pays for──▶ crafter ──pays for──▶ gatherer
                        │                     │
                        │                     └──buys materials──▶ NPC counter  (gold leaves)
                        └──buys herbs, paper, potions─────────────▶ NPC counter  (gold leaves)

   NPC counter ──buys what nobody wanted──▶ back into a bot's purse  (gold enters, thinly)
```

Two ordering rules carry the whole thing:

**The bots' market is asked before a counter, always.** `BotShops.Buy` is compared against `BotAuction.Cheapest`
and the cheaper wins — so a shopkeeper is the **ceiling** on what a bot can charge, never the preference. And
goods only reach a counter through `BotPeddler`, whose one condition is a stall that has stood for a full stale
period and never sold: half an hour in front of the whole population, refused.

**A shortage reaches a worker without anybody sending a message.** `BotAuction.Worth(type, fallback)` asks, in
order: what somebody is offering for one with the money down; what one has actually changed hands for; then the
caller's stand-in. A producer counts its output at that price, the takings go into the ledger, and the ledger
raises its estimate of that work next time. One trip of latency and no new machinery — the same
arithmetic-from-a-shared-fact that decides everything else here.

**Nobody can be on both sides of the same kind of thing.** One number with a sign: plus is a stall, minus is a
want. Not a check — the shape of the data. This is what kills v1's measured defect where the same fifteen
ginseng and the same seventy-five gold went round in a circle between the same bots.

---

## 9. Engine facts this design is built on

Every one of these was read out of the fork's source, and several of them are the whole reason a subsystem is
shaped the way it is. This list exists so that nobody has to rediscover them.

| Fact | Where | What it decides |
|---|---|---|
| `Mobile.Combatant = target` starts a **server-side** timer that swings, rolls to hit and applies damage | `Mobile.CheckCombatTime` | a bot can fight with no client; `BotSlay` never decides a blow |
| A cast is **disturbed by damage** when the caster is a player — and every bot is a `PlayerMobile` | `Spell.OnCasterHurt` | a healer under fire cannot cast at all |
| A bandage is **not** interrupted; it *slips*, at 2 % success per blow | `BandageContext.Slip` | cloth works under fire and a spell does not → the ordering flips |
| A heal reaches **10–12 tiles** | `IRangedSpell.TargetRange` | a healer stands off; walking to melee range was a defect |
| `Bandage.Range` is **1** on a renaissance shard, 2 under AOS | `Bandage.Range` | read, never chosen |
| A heal potion is **instant and uninterruptible**, refuses a full-health patient and keeps its own cooldown | `BaseHealPotion.CanDrink` | the only mending that works while something is hitting you |
| `OnSellItems` requires `IsStandardLoot()`, and the bind marks things `Newbied` | `BaseVendor`, `Item.IsStandardLoot` | **bound gear cannot be sold, enforced by the engine** |
| Tools have 25–75 uses, spend one an attempt and are destroyed at zero | `BaseTool`, `CraftItem.Craft` | tools wear out and must be re-bought; only the weapon is bound |
| Weapons wear on every hit and are destroyed at zero | `BaseWeapon` | the fighter's demand on the crafter |
| Shop shelves refill only on an explicit `Restock()`; prices update only on `UpdateBuyInfo()` | `BaseVendor` | a shop a bot cleaned out stays empty for ever without both calls |
| NPC mages sell **only the first three circles** of scrolls (24 of 64) | `SBMage`, `circles = 3` | the other forty exist only because a bot wrote one — the whole magic economy |
| `Spellbook.OnDragDrop` returns `scroll.Deleted`, and `Item.Consume` only deletes an emptied stack | `Spellbook`, `Item.Consume` | the return value **lies** for a stack; ask the book with `HasSpell` instead |
| `Loot.RegularScrollTypes` is in art order, not spell-id order | `Loot`, `Spells/Initializer` | a map built by index is wrong for the whole first circle and looks right |
| `Item.Weight = 0.0` is a real setter | `Item.cs:474` | weightless issued gear is possible; this was the project's largest known risk |
| `CheckTool` does not require the tool in a hand | `Mining` | a pickaxe works from the pack |
| A vein's remaining ore is readable, and the swing radius comes from the definition | `HarvestDefinition.GetBank(...).Current`, `MaxRange` | emptiness is read, not guessed from failed swings |
| Optional-parameter constructors defeat `Activator.CreateInstance`; the engine's `Type.CreateInstance<T>()` fills them with `Type.Missing` | `ActivatorExtensions` | almost every stackable in the game — ore, herbs, scrolls, bandages |
| Past `40 + 3.5 × Str` stones the engine charges 5+ stamina **per step** and refuses the step at zero | `StaminaSystem` | why issued gear is weightless; three v1 bots stood still for a whole session |

---

## 10. The shard's rules, which are not ours to change

From the fork's own `CLAUDE.md` and `dev-docs/`. Three of these changed real code here.

**Tick counts are compared by subtraction only, and never against a zero sentinel.** On some hosts the counter
is the machine's uptime passed through: it starts enormous and goes negative. `dev-docs/tick-counts.md`.

**No `System.Text.StringBuilder`.** Use `ValueStringBuilder` from `Server.Text`.

**No enumeration of `World.Items` / `World.Mobiles`.** One exception exists here, in the save-cleanup path, with
its reasoning written at the call site.

**`TreatWarningsAsErrors` is true**, so any warning is a shard that does not build. Fix the cause; do not
silence it with `NoWarn`.

---

## 11. What is deliberately absent

Naming these is part of the design. Each is a decision, not an oversight.

**Squads are written and unwired.** `BotSquad` is 1 551 lines and nothing calls `BotSquads.Form`. Cooperation
is already emergent and free: `BotThreat.OurPower` counts the neighbours, so two bots in a field take on what
one would walk away from, and neither had to be told the other was there. Joining a company, when it comes,
will be an obligation with a price (a share of the takings) rather than a muster by order — in v1 twelve of
twenty bots were tied up "assisting", so the economy worked and there was nobody to take part in it. Until
then this is dead code that compiles, which is the most expensive kind: it looks maintained.

**Nothing persists across a restart.** The population is rebuilt from configuration on every world load, so
skills, spellbooks and ledgers all live one session. `BotBond` deliberately does not serialise — the engine's
entity serialiser has no per-entity refusal, so bots returning from a save are cleaned out.

**Prices are not configurable and never will be.** A file able to set the price of an ingot is a file that
decides the economy, and then the market is decoration.

**No real retreat.** "Flee" today means "stop chasing": `OnDamage` sets `Combatant` even when outmatched, so a
bot answers blows where it stands, and there is no walk-*away-from* primitive anywhere. This is the largest
remaining gap and it is the thing v1 died of — 443 deaths in a night, 104 of them one bot rising in the same
tile every half minute.

**Ground pockets are not written to disk.** On purpose: a wrong "impossible" would be invisible and permanent.

**It has never been run.** The development machine has no client files, so everything about behaviour in these
documents is reasoning rather than observation. What *is* verified is that it builds clean against the fork,
and that every engine call was read out of the fork's source first.

---

## 12. Where to look for what

| Question | Folder | Read |
|---|---|---|
| How does a subsystem get loaded, and how do I switch one off? | `BotModules` | `BotModules/README.md` |
| What is a warrior? What does a mage own? | `BotClasses` | `BotClasses/README.md` |
| What may death take from a bot? Why is issued gear weightless? | `BotOutfit` | `BotOutfit/README.md` |
| How does a bot get anywhere? Why doesn't it get stuck? | `BotMovement` | `BotMovement/README.md`, `RESEARCH.md` |
| Should this bot fight this thing? | `BotCombat` | `BotCombat/README.md` |
| Why is this bot doing this? | `BotWill` | `BotWill/README.md` |
| How do bots trade with each other? Who is short of what? | `BotAuction` | `BotAuction/README.md` |
| How does a bot buy or sell over a counter? | `BotShops` | `BotShops/README.md` |
| Where does metal come from? | `BotHarvest` | `BotHarvest/README.md` |
| Where do finished goods come from? | `BotCraft` | `BotCraft/README.md` |
| Where do spells come from, and why does a mage want them? | `BotSpells` | `BotSpells/README.md` |
| Where does **gold** come from? | `BotHunt` | `BotHunt/README.md` |
| What happens when a bot is losing? | `BotMend` | `BotMend/README.md` |
| What is a bot, mechanically, and what drives its turn? | `BotPopulation` | `BotPopulation/README.md` |
| How do I watch any of this? | `BotDashboard` | `BotDashboard/README.md` |
| Squads, if they are ever wired up | `BotSquad` | `BotSquad/README.md` |
| Who deals with ground that has already killed people? | `BotBaron` | `BotBaron/README.md` |
