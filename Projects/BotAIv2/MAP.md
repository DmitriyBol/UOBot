# BotAI v2 — Map

**Read this instead of searching.** `ARCHITECTURE.md` says why the design is the way it is; this file says
*where things are*, so that a question can be answered by opening one or two files rather than by walking a
tree of 212 of them. Every table here is a lookup. Nothing here explains a decision.

The tables in §2 are generated from each file's own class summary. When a file's purpose changes, its doc
comment is the thing to change; this file is regenerated from those, never edited row by row.

- Where a thing is → **here**, §2: one block per subsystem, and every file in it
- What a number is set to → `DIALS.md`, generated beside this one
- The shape of the whole thing → `ARCHITECTURE.md`
- Why a decision is the way it is → that subsystem's `README.md`

**A warning about those READMEs.** They are written by hand and they fall behind. On 05.09.2026 four of
them described a shard that no longer existed — nine classes where the engine reports thirteen, three
dashboard tabs where it opens five, "nothing calls `BotSquads.Form`" where three things do, and a Baron
"not raised" who has been raised for days — and fourteen listed fewer files than their folders held.
All of that is corrected, and `regen-map.py` now prints a line for any README whose file table has drifted
again, because the way it accumulated was silently. Read them for reasoning; read §2 below, which is
generated from the source, for facts.

---

## 0. Running it

| | |
|---|---|
| Start | `./start-shard.ps1` from the repo root — hidden, waits until it reports listening |
| Stop | `taskkill /F /IM ModernUO.exe` |
| Log | `logs/session-<yyyy-MM-dd_HH-mm>.log`, one file per start |
| Port | `127.0.0.1:2593` |
| Build | `dotnet build ModernUO.slnx -c Release` |
| Configuration | `Distribution/Configuration/bot-*.json`, one file per subsystem |
| Rebuild §2 and `DIALS.md` | `python regen-map.py` from this folder |

Three facts worth having in front of you rather than rediscovering:

**A running shard holds the assemblies.** Building while it runs still *compiles* — it fails only at the copy
into `Distribution/Assemblies`. So `dotnet build … | grep "error CS"` is a valid syntax check against a live
shard; `Ошибок: 2` with nothing but `MSB3021`/`MSB3027` means the code is fine and the shard is up.

**Configuration keys are PascalCase and a wrong key is silent.** A lowercase key is not an error and not a
warning — the value is simply the code default. The only proof of what a dial is actually set to is the boot
lines the modules write at startup.

**Restarting resets what was surveyed.** The ore map, the hearth list and the counter list are built by bots
walking around and are not saved. A restart therefore *cures* "the island has run out of ore" and makes any
measurement about exhaustion impossible for the first two or three hours. Never delete
`Distribution/Saves/Mobiles` or `Items` — Patrick's own character lives there.

---

## 1. Reading a summary line

Every five minutes (`BotBeat.SummaryMs`, `BotSquads.SayEveryMs`, both 300000) the population writes one block
of lines. Each line is assembled from `Describe()` on the classes that own the counters, and each is reset by
the matching `Forget()`. **A number in the summary is always a static counter on one class**, so the line
prefix is the fastest route into the code there is.

| line begins | assembled in | fed by |
|---|---|---|
| `Will:` | `BotWill/BotWill.cs` | itself — the census: what the whole population is doing right now |
| `Companies:` | `BotSquad/BotSquads.cs` | `BotMuster`, `BotEnlist`, `BotSpoils`, `BotSquad` |
| `Arms:` | `BotSquad/BotSquads.cs` | `BotCry`, `BotArms`, `BotArmoury`, `BotMedic` |
| `Bows:` | `BotSquad/BotSquads.cs` | `BotBolt` |
| `Needs:` | `BotSquad/BotSquads.cs` | `BotUpkeep`, `BotBullion`, `BotSmith`, `BotTailor`, `BotFletcher`, `BotAlchemist`, `BotCook`, `BotStores` |
| `The ground:` | `BotSquad/BotSquads.cs` | `BotGround`, `BotWoodsman`, `BotStable` |
| `Getting about:` | `BotPopulation/BotBeat.cs` | `BotPath`, `BotStep`, `BotJourney` |
| `The market:` | `BotPopulation/BotBeat.cs` | `BotAuction`, `BotHaggle`, `BotListing` |
| `What we know:` | `BotPopulation/BotBeat.cs` | `BotLedger` |
| `Money:` | `BotPopulation/BotBeat.cs` | `BotPurse` |
| `The island:` | `BotPopulation/BotBeat.cs` | `BotQuad`, `BotHunter` |
| `At death's door:` | `BotPopulation/BotBeat.cs` | `BotMobile`, `BotShopper` |
| `Standing still:` | `BotPopulation/BotBeat.cs` | `BotStall`, `BotHomer` |
| `Gathering:` | `BotPopulation/BotBeat.cs` | `BotForager`, `BotHerbalist`, `BotPicker`, `BotOutfit` |
| `Trade:` | `BotPopulation/BotBeat.cs` | `BotShops`, `BotPeddler`, `BotQuarry`, `BotPopulation` |
| `The captain:` | `BotDrill/BotDrillModule.cs` | `BotDrill`, `BotSchool`, `BotLesson`, `BotAttend`, `BotArmourer` |
| `The Baron:` | `BotBaron/BotBaronModule.cs` | `BotHarrow`, `BotRounds`, `BotStroll`, `BotStipend` |
| `Minds:` | `mindedBots/BotMinds.cs` | `BotMind`, `BotMindChoice`, `BotOllama` |

`Spells ready:` is written once at boot by `BotSpells/BotSpellsModule.cs`, not every five minutes.

Per-bot lines, which are the other half of the log:

| line shape | written by | says |
|---|---|---|
| `X took on <trade>: <stage>: N/min = …` | `BotWill/BotWill.cs` | the auction was held and this won; the factors are the whole reckoning |
| `X finished <trade>: … — <why>` | `BotWill/BotWill.cs` | a round ended having produced something |
| `X failed at <trade>: … — <why>` | `BotWill/BotWill.cs` | a round ended having produced nothing; `<why>` is the deed's own words |
| `over <trade>: <stage> at N/min` | `BotWill/BotWill.cs` | what the winner beat — the runner-up and its price |

---

## 2. Every file, and the one thing it decides

<!-- Generated from each file's class summary by `regen-map.py`. Change the doc comment, then
     rerun the script; never edit a row here. -->

<!--SECTION2:BEGIN-->

### root — the way in

One file. `BotCore.Configure` and `BotCore.Initialize` are found by the engine's reflection the same way it finds content, and everything this assembly does hangs off them: the stores that must exist before the world is read, the regeneration hooks, and the registration of every module in the order they are listed there.

- **Trap.** `BotCore.Configure` runs after `RegenRates.Configure` by loader ordering rather than by contract. If that ever inverts, meals stop quickening recovery — they do not break anything.

| file | decides |
|---|---|
| `BotCore.cs` | The way in. |

### `BotModules/` — the loading frame

Holds the subsystems, works out what order to start them in, starts them, and says what happened. Two phases: `Settings` runs before the world exists and may only read numbers and files; `World` runs after it is in memory and may ask where things are. A module can be switched off from its own configuration file without touching anything else.

- **Trap.** A subsystem that asks the map a question in the `Settings` phase gets a world that is not there yet. Phase is the first thing to check when something is null only at boot.

| file | decides |
|---|---|
| `BotModule.cs` | One switchable, ordered piece of the population. |
| `BotModules.cs` | Holds the modules, works out the order, starts them and says what happened. |
| `BotPhase.cs` | When a module is allowed to run. |

### `BotPopulation/` — what a bot is, and the clock

`BotMobile : PlayerMobile` is the bot. One timer serves the whole population and gives each bot a turn on its own schedule — `BotBeat`, which is also where four of the summary lines are written. This folder also owns what survives a restart (`BotProgress`: skills, fame, karma, savings) and the three pieces of work that belong to no trade: walking home, going back for what death took, and taking a full pack to a counter.

**Module** `BotPopulationModule` · **Config** `bot-population.json` · **Writes** `Getting about:` · **Writes** `The market:` · **Writes** `What we know:` · **Writes** `Money:`

- **Trap.** A bot must be Player-flagged or death deletes it outright, and a dead bot counts as alive and silently drops out of the beat.
- **Trap.** `BotProgress` is the only place skills persist. Zeroing it is not the same as touching the world save, and the world save holds Patrick's own character — never delete it.

| file | decides |
|---|---|
| `BotBeat.cs` | The population's clock. |
| `BotHomeward.cs` | Walking back to where the population lives, when there is nothing else to do and the bot is a long way from it. |
| `BotMobile.cs` | An autonomous inhabitant of the shard. |
| `BotPopulation.cs` | Who exists. |
| `BotPopulationConfig.cs` | What Configuration/bot-population.json is allowed to say. |
| `BotPopulationModule.cs` | The population as a module: reads who should exist, deletes whoever came back from the save, raises the rest, and starts the clock. |
| `BotProgress.cs` | What a bot has become, kept across restarts: its skills, its fame, its karma and its savings. |
| `BotPurse.cs` | What a bot keeps in its pocket, and what it puts away the moment it is standing somewhere it can. |
| `BotReclaim.cs` | Going back for what death took. |
| `BotStall.cs` | Notices a bot that has stopped getting anywhere, and says so as an error. |
| `BotUnload.cs` | Going to the counter when the pack is getting heavy: coin into the account, everything spare onto the market. |

### `BotWill/` — the decision

Every turn a bot asks one question and this answers it. Three stages: the ladder says which rung the bot is on from facts alone, obligations are taken before anything is auctioned, and then every proposer registered by every other subsystem offers work which is priced per minute and the best offer wins. Prices are *measured* — `BotLedger` corrects a trade's opening guess by what the work really paid — rather than assigned by weights. Every `took on` / `finished` / `failed at` line in the log is written here, and the factors printed beside the price are the whole reckoning.

**Module** `BotWillModule` · **Config** `bot-will.json` · **Writes** `What we know:`

- **Trap.** A multiplier that can reach zero is a veto: every factor needs a floor.
- **Trap.** A deed that answers `Work` for ever, or answers with the same walk for ever, is immortal and invisible. Both have happened; both cost a session.
- **Trap.** The walk order a deed returns must be stable between ticks, because arrival is compared against it. A destination recomputed every tick resets the route every tick.

| file | decides |
|---|---|
| `BotAppraisal.cs` | What a score was made of. |
| `BotCommons.cs` | What the population as a whole has found out about what pays where. |
| `BotDeed.cs` | One undertaking: a piece of work with stages, held until it finishes, fails or is dropped. |
| `BotDoing.cs` | What an undertaking wants of the bot at this moment. |
| `BotLadder.cs` | Which rung the bot is on, from facts only. |
| `BotLedger.cs` | What this bot has learned about what pays. |
| `BotResolve.cs` | Everything one bot has resolved, feels and has learned. |
| `BotStanding.cs` | Which rung of the survival ladder a bot is standing on. |
| `BotUrges.cs` | The two things a bot feels, and why there are only two. |
| `BotWill.cs` | The decision. |
| `BotWillConfig.cs` | What Configuration/bot-will.json is allowed to say. |
| `BotWillModule.cs` | Deciding as a module: reads its numbers, turns itself on, and says what the population will be judging work by. |
| `BotYield.cs` | How a piece of work ended. |
| `IBotProposer.cs` | The supply side of the auction: something that knows about one kind of work and can offer a bot a piece of it. |
| `IBotWilful.cs` | What deciding needs a bot to be. |

### `BotMovement/` — path, road and step

Getting a bot from where it is to where the work is. The most expensive part of the assembly and the one with the longest history of measured defects — `RESEARCH.md` beside it is the analysis the current design came out of. A journey is a destination, a queue of things put aside to do first, and the plan currently being walked.

**Module** `BotMovementModule` · **Config** `bot-movement.json` · **Writes** `Getting about:`

- **Trap.** A `Point3D` whose Z came out of arithmetic is a place nothing can stand on: it works on flat ground and fails on a hill. Settle it against the map.
- **Trap.** Arriving is judged by asking the engine whether the work would be accepted here, never by the distance to a remembered point.

| file | decides |
|---|---|
| `BotArrival.cs` | What counts as having got there. |
| `BotAvoid.cs` | Ground a single plan is to keep out of. |
| `BotErrand.cs` | One thing a bot is trying to get to. |
| `BotJourney.cs` | What a bot is trying to get to, what it has put aside to do first, and the plan it is walking now. |
| `BotMovementConfig.cs` | What Configuration/bot-movement.json is allowed to say. |
| `BotMovementModule.cs` | Movement as a module: reads its numbers, lets the population walk, and puts its counters back on a world reload. |
| `BotPath.cs` | What a search concluded. |
| `BotReach.cs` | What the reach ledger can say about a journey before anybody searches for it. |
| `BotStep.cs` | One tile, and everything the engine knows about stepping off it. |
| `BotWalk.cs` | Something standing in the way that can be asked to move. |

### `BotClasses/` — what a kind of bot is

Thirteen classes, one file each, plus a registry. A class is **a description and a set of limits with no behaviour** — it decides nothing and commands nobody. `BotWill` reads a class the way it reads the map: as a fact about the world. Four of the thirteen cast; three produce; the rest fight, shoot or heal.

**Module** `BotClassModule` · **Config** `bot-classes.json`

- **Trap.** Adding a class means a file here and a line in the registry. Nothing else in the assembly holds a list of permitted archetypes, and nothing should — the tool a bot carries is what decides which trades it is offered.

| file | decides |
|---|---|
| `BotArcher.cs` | The bow and nothing else, and the only class that can triple a hit. |
| `BotArchitect.cs` | The bot that is paid by the health of the market rather than by any errand in it. |
| `BotArsenal.cs` | The weapons of the era, named once, each beside the skill that swings it. |
| `BotBaron.cs` | The one bot on the shard that is not trying to make a living. |
| `BotBrawler.cs` | Fights with its hands, and is therefore never holding anything it has to put down. |
| `BotCaptain.cs` | The one bot on the shard that exists for the others rather than for itself. |
| `BotClass.cs` | What a bot is: its build, what it is born owning, what it may carry, and the one thing it can do that nobody else can. |
| `BotClassConfig.cs` | What Configuration/bot-classes.json is allowed to say. |
| `BotClassModule.cs` | The class layer as a module: reads its file, then says what the nine came out as. |
| `BotClasses.cs` | Every class, built once, looked up by name. |
| `BotCrafter.cs` | Metal, cloth and leather. |
| `BotGatherer.cs` | Ore and timber, and the only bot that can find a reagent in the grass. |
| `BotHealer.cs` | The green staff. |
| `BotKit.cs` | What the world hands a bot at birth, and on what terms. |
| `BotMage.cs` | A spellbook, a blue staff, and no metal. |
| `BotPotionKind.cs` | Potions grouped by what they do, which is the granularity a carrying limit needs. |
| `BotRole.cs` | What a class contributes to a group, as opposed to what it is called. |
| `BotSage.cs` | The captain's opposite number, for the half of the population a captain cannot teach. |
| `BotWarrior.cs` | The plain fighter. |
| `BotWarriorArcher.cs` | Shoots, and has a knife for when that stops working. |
| `BotWarriorMage.cs` | Plate, a blade, and spells anyway. |
| `BotWeaponOption.cs` | One weapon a class may be born holding, together with the skill that makes it land, how far the bot will train that skill, and whatever the weapon needs in order to work at all. |

### `BotOutfit/` — what a bot owns

Turns a class's kit into items actually in a pack, decides what death has no right to take, and owns the parts of a bot that are property rather than behaviour: its bond with a weapon, its harness, its armour, and its horse.

**Config** `bot-population.json` · **Writes** `The ground:`

- **Trap.** `Mobile.EquipItem` does not replace an occupied layer, it refuses. Whatever wants a hand has to free it first, and has to put back what it took.

| file | decides |
|---|---|
| `BotBinding.cs` | What "bound" means, in one place. |
| `BotBond.cs` | What one bot was given, and therefore what death may not take from it. |
| `BotBrawlerGloves.cs` | The gloves a brawler fights in. |
| `BotCasterStaff.cs` | The staff a caster leans on. |
| `BotHarness.cs` | What a bot ought to be wearing, worked out from what this shard can actually make and what it costs. |
| `BotOutfit.cs` | Turns a class's kit into things a bot is actually holding. |
| `BotStable.cs` | Buying a horse, and calling it up. |
| `BotSteed.cs` | A horse a bot carries in its pack and calls up when it has somewhere to be. |

### `BotHarvest/` — ore, wood, herbs, and the survey

Where raw material comes from, and — more used than the digging — the survey of the ground that the whole population navigates by. `BotGround` sweeps the tiles around wherever bots go and remembers four things: seams of ore, forges with an anvil beside them (`Fires`), every kind of fire the engine will cook over (`Hearths`), and counters. Everything that needs a *place* asks this.

**Module** `BotHarvestModule` · **Config** `bot-harvest.json` · **Writes** `The ground:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotMiner` | Miner | Free | `BotDig`, `BotProspect` |
| `BotWoodsman` | Woodsman | Free | `BotChop` |

- **Trap.** The survey is built by walking and is not saved. A restart empties it — which silently cures "the island has run out of ore" and makes any measurement about exhaustion meaningless for the first two or three hours.
- **Trap.** An axe must be worn for the engine to accept a swing; a pick works from the pack. That difference is why mining worked from the first day and no log was ever cut.

| file | decides |
|---|---|
| `BotChop.cs` | Cutting wood: walk to the nearest tree and swing until the pack has enough or the tree has nothing left. |
| `BotDig.cs` | Dig, melt, put away. |
| `BotForage.cs` | Picking up the reagents the world leaves lying about, and putting them on the board. |
| `BotGround.cs` | One remembered patch of workable rock, and what is in it. |
| `BotHarvestConfig.cs` | What Configuration/bot-harvest.json is allowed to say. |
| `BotHarvestModule.cs` | Getting a living out of the ground, as a module: reads its numbers and offers the trade to the decision layer. |
| `BotHerbs.cs` | A walk into the woods that comes back with herbs. |
| `BotMiner.cs` | Offers a mining trip to anybody carrying a pick. |
| `BotOre.cs` | Ore: what is in a hill, whether this bot can get it out, how it is dug, and what it becomes. |
| `BotProspect.cs` | A walk out past the last swept ground, so that there is rock on the board somewhere nobody has been. |
| `BotTimber.cs` | What a woodcutter needs to know: the axe, the trees within reach, and how much wood is worth a trip. |
| `BotWoodsman.cs` | Offers a bot with an axe a trip to the woods, and only when somebody wants the wood. |

### `BotCraft/` — turning materials into goods

Five trades that make things: the smith at a forge, the tailor out of leather or cloth, the alchemist over a bottle, the fletcher out of shafts and feathers, and the cook at any fire. Each is a proposer that decides whether there is a piece of work, and a deed that walks to the place, swings until something comes of it, and puts the result either into the order it was made for or onto the market. `BotCraftwork` is the shared half — choosing a recipe, swinging, counting what appeared.

**Module** `BotCraftModule` · **Config** `bot-craft.json` · **Writes** `Needs:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotAlchemist` | Alchemist | Free | `BotBrew` |
| `BotCook` | Cook | Free | `BotBake` |
| `BotFletcher` | Fletcher | Free | `BotFletch` |
| `BotSmith` | Smith | Free | `BotForge` |
| `BotTailor` | Tailor | Free | `BotSew` |

- **Trap.** Crafting is asynchronous: `CraftItem.Craft` starts a timer. Count what the *last* swing produced at the top of the next tick, never after the swing you just made.
- **Trap.** The engine refuses in silence, because it answers a refusal by sending a message to a screen the bot does not have. Requirements live on the *recipe* (`SetNeedHeat`, `SetNeedOven`, `SetNeedMill`), not on the system's `CanCraft`.
- **Trap.** A failed attempt eats half the material. A round set up for exactly one item is a round that ends in `out of metal` after two misses — see `BotAnvil.Tries`.

| file | decides |
|---|---|
| `BotAlchemist.cs` | Offers the mortar to anybody carrying one, and offers the board's orders first. |
| `BotAnvil.cs` | What the smith's trade knows that no other trade does: its system, its skill, its hammer, its metal, and the one thing about blacksmithing that is genuinely different — it cannot be done just anywhere. |
| `BotBake.cs` | One turn at the skillet: raw meat in the pack becomes suppers, and what the cook does not keep goes out to the market. |
| `BotBrew.cs` | Brewing: buy the glass if it is short of it, work the mortar, and hand the bottles to whoever put the money down. |
| `BotCook.cs` | Offers a turn at the skillet to anybody carrying one and some meat. |
| `BotCraftConfig.cs` | What Configuration/bot-craft.json is allowed to say. |
| `BotCraftModule.cs` | Making things, as a module. |
| `BotCraftwork.cs` | The part of making things that is the same whatever is being made. |
| `BotFlask.cs` | Brewing: what a potion is made of, and the swing that makes it. |
| `BotFletch.cs` | Making arrows: buy the wood if it is short of it, cut the shafts, feather them, and hand them to whoever put the money down. |
| `BotFletcher.cs` | Offers a bot with a fletching tool and a handful of feathers a turn at making arrows, and offers it the board's orders first. |
| `BotFletching.cs` | What a fletcher needs to know: the craft system, the tool, and the two-step chain that turns a log and a feather into an arrow. |
| `BotForge.cs` | Beating iron into something, at a forge, and handing it to whoever asked for it. |
| `BotMeal.cs` | A bot eating a cooked meal: what it does to the bot, and for how long. |
| `BotOven.cs` | Cooking: what a meal is made of, and who can make one. |
| `BotSew.cs` | Buy cloth, make something of it, put it on the market. |
| `BotSmith.cs` | Offers a bot with a hammer and some metal a turn at an anvil, and offers it the board's orders first. |
| `BotTailor.cs` | Offers the needle to anybody carrying a sewing kit. |
| `BotThread.cs` | Sewing: what can be made out of cloth, and the swing that makes it. |

### `BotHunt/` — the only new gold in the world

Everything else on this shard moves money about; this brings it in. Choosing what is worth fighting, prowling for it, killing it, carving it and going through what it left. Carving is folded into looting rather than made a choice, because the engine charges nothing for it and there is no decision in it worth a bot's turn.

**Module** `BotHuntModule` · **Config** `bot-hunt.json` · **Writes** `Companies:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotHunter` | Hunter | Free | `BotProwl`, `BotSlay` |
| `BotMuster` | Muster | Free | `BotBand` |

- **Trap.** Everything lifted off a corpse is listed for sale except the bot's own ammunition and a cook's raw meat. Anything else a trade needs as *input* will be sold before that trade ever sees it.

| file | decides |
|---|---|
| `BotBand.cs` | Calling a company together for something one bot cannot take, and seeing it through. |
| `BotGlean.cs` | Picking spent ammunition up off the ground. |
| `BotHuntConfig.cs` | What Configuration/bot-hunt.json is allowed to say. |
| `BotHuntModule.cs` | Fighting for a living, as a module. |
| `BotHunter.cs` | Offers a fight to any bot healthy enough to want one. |
| `BotMuster.cs` | Offers a bot the chance to call a company against something it must otherwise walk past. |
| `BotPickings.cs` | Going through something this bot killed without meaning to. |
| `BotProwl.cs` | Going to look for a fight, when there is nothing to fight where the bot is standing. |
| `BotQuarry.cs` | Finding something worth fighting, and finding what it left behind. |
| `BotSlay.cs` | Close, fight, go through what is left. |

### `BotShops/` — buying and selling over a counter

A capability for every bot rather than a trade of its own: reagents for a caster, bandages for anybody, metal for a smith, and whatever the population would not buy goes back over the same counter for coin. Also the board of standing orders — what a bot has put money down for and has not received yet.

**Module** `BotShopsModule` · **Config** `bot-shops.json` · **Writes** `Needs:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotBullion` | Bullion | Free | — |
| `BotPeddler` | Peddler | Free | `BotPeddle` |
| `BotShopper` | Shopper | Free | `BotRestock` |
| `BotUpkeep` | Upkeep | Free | — |

- **Trap.** The engine pays out of the pack, and a bot only walks to a bank above a threshold. Three files once closed on each other so that money existed and could not be spent.

| file | decides |
|---|---|
| `BotBullion.cs` | A crafter with money buys its metal instead of going and digging it. |
| `BotNeeds.cs` | How often a bot reconsiders what it is short of. |
| `BotOrder.cs` | Putting an order on the board, and going back for it when somebody has filled it. |
| `BotPeddle.cs` | Taking what the population would not buy to somebody who will. |
| `BotPeddler.cs` | Offers a trip to a counter to any bot holding a stall the population has ignored. |
| `BotRestock.cs` | Going to a shop and buying what the bot has run out of. |
| `BotShopper.cs` | Offers a trip to the shops to any bot that has run out of something its class needs. |
| `BotShops.cs` | Buying things from the shopkeepers. |
| `BotShopsConfig.cs` | What Configuration/bot-shops.json is allowed to say. |
| `BotShopsModule.cs` | Trading with the shopkeepers, as a module. |
| `BotStores.cs` | A crafter short of the raw material of its trade, putting the order to the population. |
| `BotUpkeep.cs` | Asking the population, by name, for a replacement for something that is wearing out. |

### `BotAuction/` — the bots' own market

Both sides of trade between bots. A stall is a standing offer of one kind of thing at one price; a want is money already down for something a bot needs. Prices move on what actually sold and what actually got filled, not on a table. This is where the crafts sell to and where the orders that make crafting worth doing come from.

**Module** `BotAuctionModule` · **Config** `bot-auction.json` · **Writes** `The market:`

- **Trap.** A stall and a want can both be healthy and never meet. When trade is low the question is not whether either side works but whether there is an edge between them.

| file | decides |
|---|---|
| `BotAuction.cs` | The bots' own market. |
| `BotAuctionConfig.cs` | What Configuration/bot-auction.json is allowed to say. |
| `BotAuctionModule.cs` | The market as a module: reads how fast bots may change their minds, and starts its own beat. |
| `BotHaggle.cs` | A seller looking at what buyers are offering for what it has out, and moving its price towards them. |
| `BotListing.cs` | One bot's standing offer of one kind of thing: what it is, how much of it is left, what it is asking, and what it has learned from selling it. |
| `BotWant.cs` | One bot's standing offer to buy one kind of thing: what it wants, how many, what it is paying, and the money it has already put down. |

### `BotSpells/` — a book that grows

Scrolls, reagents and spellbooks: the first work in the project whose output no shopkeeper sells, and the first buyer on the bots' market that is itself a bot. A caster wants spells it does not have, a scribe makes them, and the armoury decides what a book is short of.

**Module** `BotSpellsModule` · **Config** `bot-spells.json` · **Writes** `Arms:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotArmoury` | Armoury | Free | — |
| `BotScribe` | Scribe | Free | `BotInscribe` |
| `BotSeeker` | Seeker | Free | — |

| file | decides |
|---|---|
| `BotAcquire.cs` | Getting hold of one spell the book is short of: off a shelf, off somebody's stall, or by asking the population for it and putting the money down. |
| `BotArmoury.cs` | Offers any bot with a mana pool the chance to lay in a few attack scrolls. |
| `BotGrimoire.cs` | What a book holds, what it is short of, and how a scroll becomes a spell in it. |
| `BotInscribe.cs` | Buy paper, write scrolls, keep what the book is short of and sell the rest. |
| `BotQuill.cs` | Writing scrolls: what can be written, what it takes, and the attempt itself. |
| `BotScribe.cs` | Offers the pen to anybody carrying one. |
| `BotSeeker.cs` | Offers a caster the next spell its book is short of, by whichever route exists for it. |
| `BotSpellsConfig.cs` | What Configuration/bot-spells.json is allowed to say. |
| `BotSpellsModule.cs` | Magic as a trade and as an appetite, as a module. |
| `BotStrike.cs` | Casting at something, as opposed to casting at somebody who is hurt. |

### `BotCombat/` — is it worth stopping

The fight itself belongs to the engine — `Warmode` and `Combatant` are enough, and its own skill checks train skills exactly as they do for a player. What is here is the judgement around it: is this thing worth fighting, is this fight being lost, is somebody calling for help, and how a shooter keeps its distance.

**Writes** `Arms:` · **Writes** `Bows:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotFugitive` | Fugitive | Failing | `BotBolt` |
| `BotRescuer` | Rescuer | Free | `BotRescue`, `BotSlay` |

- **Trap.** Three separate reasons a bot fails to land a blow on something it is standing next to — no line to it, a shooter that moved too recently, a broken cast — and none of the three says anything in the log unless a counter is put there.

| file | decides |
|---|---|
| `BotArms.cs` | Whether a bot has anything in its hands, asked at the moment it matters. |
| `BotBolt.cs` | Getting away from whatever is killing it. |
| `BotCry.cs` | Somebody of ours is being killed and has said so out loud. |
| `BotFugitive.cs` | Offers a bot whose health is going the one thing that was missing from that rung: leaving. |
| `BotPeril.cs` | Where the shard is dangerous, learned from the only two facts that actually say so: where bots are being hit, and where they are dying. |
| `BotRescue.cs` | Going to somebody's aid, or hitting back at whatever is hitting you. |
| `BotRescuer.cs` | Offers a free bot the chance to go to somebody's aid. |
| `BotThreat.cs` | One of this population, counted when adding up our side of a fight. |

### `BotMend/` — bandages

The smallest subsystem here, and the only thing that puts a bot on the `Failing` rung. A bot binds its own wounds, and a surgeon binds somebody else's.

**Module** `BotMendModule` · **Config** `bot-mend.json` · **Writes** `Arms:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotMedic` | Medic | Failing | `BotSalve` |
| `BotSurgeon` | Surgeon | Free | `BotSalve` |

| file | decides |
|---|---|
| `BotMedic.cs` | Offers a bot the chance to look after itself, and it is the only thing on the rung that says so. |
| `BotMend.cs` | Mending: what a bot can heal with, who needs it, and the two ways of doing it. |
| `BotMendConfig.cs` | What Configuration/bot-mend.json is allowed to say. |
| `BotMendModule.cs` | Looking after each other, as a module. |
| `BotSalve.cs` | Patching somebody up — itself or somebody else, by spell if it can and by cloth if it cannot. |
| `BotSurgeon.cs` | Offers whoever can mend the worst-hurt bot within sight — itself included — as ordinary work. |

### `BotSquad/` — standing companies

A leader, a few followers, a formation, and dividing what the fight left. Companies are formed from three places — a hunt that found something too big for one, a patrol, and the Baron's harrowing — and dissolve when there is nothing left to fight. This folder also *assembles* five of the summary lines out of counters that mostly live elsewhere.

**Module** `BotSquadModule` · **Config** `bot-squad.json` · **Writes** `Companies:` · **Writes** `Arms:` · **Writes** `Bows:` · **Writes** `Needs:` · **Writes** `The ground:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotPatrol` | Patrol | Free | `BotSweep` |

- **Trap.** A bot on the `Bound` rung takes no work of its own: the auction is switched off for it. A company with no charge is therefore a company of idle bots.
- **Trap.** The subsystem's own README still says nothing calls `BotSquads.Form`. It has three callers and companies form on every session.

| file | decides |
|---|---|
| `BotEnlist.cs` | Falling in with a company that is already fighting, rather than starting a fight of your own beside it. |
| `BotFormation.cs` | Where each member of a squad ought to be standing, worked out rather than assigned. |
| `BotPatrol.cs` | Offers a captain the worst square on the island and a company to take there. |
| `BotScatter.cs` | How a squad stands on ground that has nothing left on it: broken into small knots, spread out, covering the place instead of standing in it. |
| `BotSpoils.cs` | Dividing what a squad took. |
| `BotSquad.cs` | What a squad is doing. |
| `BotSquadConfig.cs` | What Configuration/bot-squad.json may say. |
| `BotSquadMember.cs` | What a squad needs a bot to be. |
| `BotSquadModule.cs` | Squads as a module: reads its numbers, starts the squads' own beat, and clears the board on a world reload. |
| `BotSquads.cs` | Every squad on the shard, and the four things that can happen to one: it forms, somebody joins, somebody leaves, it dies. |
| `BotSweep.cs` | A company called together for a place rather than for a creature, and kept together until the place stops killing people. |

### `BotDrill/` — the captain

One class exists for the others rather than for itself. A captain holds a field, teaches whoever pays the fee, marches a company at ground the danger map says is bad, and puts armour orders on the board for bodies that have none.

**Module** `BotDrillModule` · **Config** `bot-drill.json` · **Writes** `The captain:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotArmourer` | Armourer | Free | — |
| `BotDrill` | Drill | Free | `BotAttend`, `BotLesson` |

- **Trap.** A lesson is worth about fifteen times what a rescue is in skill gained, so any rule that keeps captains from teaching is a rule against the population levelling at all.

| file | decides |
|---|---|
| `BotArmourer.cs` | Puts an order on the board for the best piece of armour this bot is not wearing. |
| `BotAttend.cs` | The student's half of a class: pay, walk to the field, find your place in the block, and stay in it. |
| `BotDrill.cs` | Offers a captain an afternoon of teaching, when there is anybody on the island worth teaching. |
| `BotDrillModule.cs` | What Configuration/bot-drill.json may say. |
| `BotLesson.cs` | The captain's half of a class: take the field, wait for whoever comes, then pace the ranks for an hour saying things. |
| `BotSchool.cs` | The training field: where it is, who is standing on it, where each of them stands, and what an hour of being shouted at is actually worth. |

### `BotBaron/` — the ground that has already killed people

Every other class answers *how does this bot get by*. The Baron answers the one question nobody else on the island asks, because there is no profit in it: who goes back to the places that have proved they kill. He raises a levy for it, walks his rounds, tours the towns, and pays a stipend out of his own account.

**Module** `BotBaronModule` · **Config** `bot-baron.json` · **Writes** `The Baron:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotHarrower` | Baron | Free | `BotHarrow` |
| `BotStroll` | Stroll | Free | `BotRounds` |

| file | decides |
|---|---|
| `BotBaronModule.cs` | What Configuration/bot-baron.json may say. |
| `BotHarrow.cs` | Six bots taken to ground that has killed people, and kept there until it has been emptied or the afternoon is gone. |
| `BotHarrower.cs` | Offers the Baron the ground that has killed the most people, and five bots to take there. |
| `BotRegalia.cs` | What the Baron wears, and the one promise all seven pieces make: they do not wear out. |
| `BotRounds.cs` | The Baron walking his town, because nowhere has taken anybody lately. |
| `BotStipend.cs` | The one purse on this shard that is not earned, and the argument for allowing exactly one. |
| `BotStroll.cs` | Offers the Baron his own town to walk through. |

### `BotQuad/` — the island as squares

The map cut into squares thirty tiles across, each carrying one number: how safe the population has found it to be. It is written by everything that dies or kills and read by the captain, the Baron and anything choosing where to go. Also the frontier — the nearest square nobody has ever stood in — and the scouting that fills it in.

**Writes** `The captain:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotScoutmaster` | Scoutmaster | Free | `BotScout` |
| `BotWarden` | Warden | Free | `BotScout` |

- **Trap.** This one *is* saved between restarts, unlike the ground survey.

| file | decides |
|---|---|
| `BotMarkers.cs` | Writes what the population knows about the island into the client's own world-map pins. |
| `BotQuad.cs` | The island cut into squares thirty tiles across, each carrying one number: how safe the population has found it to be. |
| `BotQuadStore.cs` | Keeps the island's reputation across restarts. |
| `BotScout.cs` | A captain taking a paid party out to ground nobody has ever stood in. |
| `BotScoutmaster.cs` | Offers a captain the nearest ground nobody has ever stood in. |
| `BotWarden.cs` | The Baron walking his own ground: the nearest square nobody has stood in, alone if need be. |

### `BotRanger/` — livery

One file: the King's Rangers' kit, which is the Baron's livery on five more bodies.

| file | decides |
|---|---|
| `BotRangerRegalia.cs` | The King's Rangers' kit: the Baron's livery on five more bodies. |

### `BotDashboard/` — watching it happen

`[bots` — an administrator command opening five tabs: the population, their market, what they are short of, what the city wants, and what the population is doing.

**Module** `BotDashboardModule`

| file | decides |
|---|---|
| `BotDashboardGump.cs` | One window onto the whole population, onto the market it trades in, and onto what it cannot get hold of. |
| `BotDashboardModule.cs` | The dashboard as a module: one command, registered once. |

### `mindedBots/` — bots that think

Four of the population choose what to do next through a local language model over Ollama rather than through the auction: a warrior, an architect, a sage and the Baron. The model is given what the bot can see and returns a choice; everything else about them is an ordinary bot.

**Module** `BotMindModule` · **Config** `bot-mind.json` · **Writes** `Minds:`

| offers work | as | on the rung | handing out |
|---|---|---|---|
| `BotMindProposer` | Mind | Free | — |

- **Trap.** The model is asked on a wall clock and the answer costs real seconds. Anything that waits on it must not be holding the game loop.

| file | decides |
|---|---|
| `BotMind.cs` | One thing a mind chose, and what it turned out to be worth. |
| `BotMindChoice.cs` | What a mind came back with: one trade, one number it is prepared to be judged on, and its reason. |
| `BotMindConfig.cs` | What Configuration/bot-mind.json is allowed to say. |
| `BotMindCore.cs` | The way in, and the whole of this assembly's contact with the rest of the shard. |
| `BotMindDeed.cs` | A real piece of the shard's work, taken up because a mind asked for it, and measured because a mind predicted something about it. |
| `BotMindLog.cs` | A log of its own, beside the shard's, holding what a mind decided and why. |
| `BotMindModule.cs` | Two thinking bots, as a module of the shard's own bot system — registered from outside it. |
| `BotMindProposer.cs` | The one place a thought becomes an offer. |
| `BotMindSight.cs` | The world as one bot can see it, written out for the model. |
| `BotMindTalk.cs` | The one place the thinking bots can hear each other. |
| `BotMinds.cs` | What is kept between sessions: one bot's name and the rules it has written for itself. |
| `BotOllama.cs` | The only thing in this assembly that talks to the model, and the only thing that leaves the game thread. |

### `mindedBots/debugger/` — Argus, the observer

A thinking thing that is not one of the population: an invisible figure that nobody in the world can see, that cannot be hurt and cannot hurt anything, whose whole job is to watch the bots and write what it believes into `logs/bot-debugger.log`. It has a door for a person at the keyboard (`argus-in.txt`) and a small set of administrator gestures.

**Module** `BotDebugModule` · **Config** `bot-debugger.json`

- **Trap.** Five false alarms in one day, all of them artefacts of the instrument rather than faults in the shard. Check the watcher before believing it about the population.

| file | decides |
|---|---|
| `BotAudit.cs` | The roll-call: every two minutes, three questions asked of every bot, and a hand laid on the ones that answer no to all of them. |
| `BotConsole.cs` | A door into the running shard for whoever is holding the keyboard rather than a character: write a line into argus-in.txt and the answer appears in argus-out.txt within a couple of seconds. |
| `BotDebugConfig.cs` | What Configuration/bot-debugger.json is allowed to say. |
| `BotDebugCore.cs` | The debugger's way in, and the whole of its contact with the rest of the shard. |
| `BotDebugLog.cs` | The debugger's own file: logs/bot-debugger.log, and nothing else is written into it. |
| `BotDebugMemory.cs` | One thing the debugger has come to believe, and how many times it has come to believe it. |
| `BotDebugModule.cs` | The debugger as a module of the shard's own bot system, registered from outside it. |
| `BotDebugNote.cs` | What the debugger came back with after one look at the population: one claim, the numbers it was made from, a guess at the cause and one change worth making. |
| `BotDebugSight.cs` | What the debugger is told: who it is, what it is looking at, and what it has already learned about the shapes defects take here. |
| `BotDebugger.cs` | The body the debugger looks out of: one figure in a white robe that nobody in the world can see, cannot be hurt, cannot hurt anything, and gets about by appearing somewhere else. |
| `BotHail.cs` | The debugger's ear, and the one door in the world through which a person can reach it. |
| `BotHand.cs` | The debugger's hands: the handful of things a person with an administrator's account would type at a stuck shard, made available to Argus by name, bounded, and written down every single time. |
| `BotVigil.cs` | The debugger itself: the body, the watch it keeps, and the two questions it asks. |
| `BotWatch.cs` | One bot as the debugger has actually seen it: everything here was measured by this file, on this file's own clock, since the moment the debugger first laid eyes on the bot. |
<!--SECTION2:END-->

---

## 3. Recipes

| task | open, in order |
|---|---|
| A trade reports work but produces nothing | the trade's deed (`BotForge`, `BotBake`, `BotSew`, `BotBrew`, `BotFletch`, `BotInscribe`), then `BotCraft/BotCraftwork.cs`, then the engine's `Def<Craft>.cs` for the recipe's own gates |
| A summary bucket is stuck at zero | the class that owns that counter (§1), then the gate immediately above the counter |
| A bot is standing still | `BotWill/BotWill.cs` for what it chose, then that deed's `Advance`, then `BotMovement/BotJourney.cs` |
| Bots will not take a kind of work | the proposer's `Describe()` denominators first; the auction price second (`BotWill`, the deed's `Prior`, `BotLedger`) |
| Something is priced wrong on the market | `BotAuction/BotAuction.cs` for the ask, `BotHaggle` for the movement, `BotListing` for what one stall remembers |
| Add a new craft | a proposer (`IBotProposer`), a deed (`BotDeed`), a `Describe()`/`Forget()` pair, registration in that folder's `*Module.cs` |
| Add a new dial | the field, then the subsystem's `*Config.cs`, then `Distribution/Configuration/bot-*.json` (PascalCase) |
| Change what a class of bot is | `BotClasses/BotClasses.cs` and the class file beside it |

---

## 4. Defect shapes that keep recurring

Recognising the shape is most of the hunt. Each of these has cost this project a session at least once.

**Two thresholds on one shelf.** One number decides whether to set out, a different number decides whether
the work can be done, and the two were written in different files. The symptom is a hard zero in the summary
next to a healthy denominator. Look for a pair of numbers that never meet, not for broken code.

**A new gate needs a new bucket.** A check added without a counter either breaks a denominator or tips its
answers into somebody else's bucket, and the summary then lies about a mechanism nobody can see. Every gate
gets its own name and its own number; there is no bucket called "other".

**The engine refuses in silence.** `CraftItem.Craft` and its family answer a refusal by sending a message to
the player's screen. A bot has no screen. Anything that reads as "the action happened and nothing changed" —
swings with no output, steps with no movement, a spell that never lands — is this. Find the gate in the
engine, not in the bot.

**A multiplier without a floor is a veto.** Any factor that can reach zero silently forbids whatever it
multiplies. Every multiplier needs a floor.

**Work judged before it can pay.** A round that takes six minutes cannot be measured against a thirty-second
window; the symptom is work abandoned exactly on a boundary.

**A number that is both a promise and a wager.** When one field is read as both "what this is worth" and
"what I am betting", the model drifts apart within a day. Split them.

**Seeing is not reaching.** A place on a list is not a place a bot can stand. Ask the engine whether the work
would be accepted *here*, never the distance to a remembered point.

**An invented height is an unreachable place.** A `Z` produced by arithmetic works on flat ground and fails on
a hill. Anything that builds a `Point3D` must settle it against the map.

**The producer sells its own material.** A bot that lists everything it lifts has sold the input of the trade
it carries the tool for. Check what a hunter does with what comes off a corpse before concluding the trade
has no supply.

**A one-way ratchet.** A meter the engine only ever increases — hunger is the known one — makes a mechanism
work a few times and then stop for good, with nothing in the log saying so.

**Two errands wearing one name.** `BotSeeker` buys a scroll to write into a book; `BotArmoury` buys one to
throw. Both built the same undertaking, which then tried to put every scroll into a book — so 233 of 430
rounds in a session reported "the book would not take it" about warriors who had no book, had never wanted
one, and had got exactly what they set out for. A round that succeeded, filed as a failure, 233 times, with
the ledger pricing the trade off it. Underneath the mislabelling was a real refusal in the other direction: a
caster that knew a spell could never buy a scroll of it to throw. When one undertaking serves two callers,
ask what each of them wanted, and make it say which.

**The first one of that type is not necessarily yours.** `FindItemByType<T>` returns whichever it meets
first, and this population goes through every corpse it makes — so one looted necromancer's spellbook sitting
in a caster's pack would answer "where is your spellbook" for ever after. Only a lookup whose *subtype*
matters is exposed: the other twelve in this assembly ask for a concrete tool, where any skillet really is a
skillet. Audited on 05.09.2026; only the spellbook was exposed, and it was **not** the cause of the scroll
losses above — that theory was wrong, and the line above it is what actually did it.

**Instrument before you fix.** Both of the entries above were first diagnosed wrong, on theories that were
plausible and had the shape of defects this project has really had. What settled them was making the sentence
in the log say which of its four ways it had failed. A guess costs a rebuild and a restart; a named counter
costs the same once and never lies again.

**The exit taken between the swing and the timer.** Crafting is asynchronous: the last swing takes the
material before it gives back the thing. Counting before the next swing is only half the rule — a round that
*ends* the moment its material runs out ends inside exactly that gap, and reports, truthfully as far as it can
see, that nothing came of it. Any exit condition on "the material is gone" needs one swing's worth of
patience.

**A trade that can only use what it happens to be holding.** Every other craft here puts a funded order on
the board for what it is short of; the cook could not, so 94% of every look at the skillet answered "no meat"
while the hunters carrying it walked past. The counterweight is the glass rule: **a material with no producer
must not be ordered by the armful**, because an order nobody can fill freezes the buyer's money in escrow
until the market gives up on it.

---

## 5. The engine, where the bots touch it

Everything above is this assembly. These are the files *outside* it that the population depends on, and
every one of them has cost a session at least once because its rule is not where it looks like it should be.

| what | where | the rule |
|---|---|---|
| Craft gates and refusals | `Projects/UOContent/Engines/Craft/Core/CraftItem.cs` | `NeedHeat` / `NeedOven` / `NeedMill` are checked **per recipe**, not by the system's `CanCraft`; the heat table lists forges, ovens, fireplaces, campfires, firepits, heating stands and braziers; a refusal is a message to a screen the bot has not got |
| What an attempt costs | same file, `ConsumeRes` | a **failed** attempt still consumes half the material (`ConsumeOnFailure` is true for smithing), which is why a round set up for one item ends in "out of" after two misses |
| Craft timing | same file, `Craft` | ends by starting a timer: the item appears a second later, so anything counting must count before the *next* swing |
| Which recipe wants what | `Projects/UOContent/Engines/Craft/Def*.cs` | one file per trade; this is where the per-recipe requirements are declared |
| Eating and hunger | `Projects/UOContent/Items/Food/Food.cs` | `FillHunger` adds and refuses at twenty, and **nothing in this fork ever subtracts**; `BotMeal` treats its own ten minutes as the clock instead |
| Recovery rates | `Projects/UOContent/Misc/RegenRates.cs` | fills the mana handler always and the other two **only under AOS**; this shard is Renaissance, so health and stamina ran at flat defaults until `BotMeal.Configure` wrapped them |
| Wearing a tool | `Projects/Server/Mobiles/Mobile.cs`, `EquipItem` | an occupied layer is **refused**, not replaced |
| Harvesting | `Projects/UOContent/Engines/Harvest/Lumberjacking.cs`, `Mining.cs` | the axe must be worn; the pick need not |
| Carving a kill | `Projects/UOContent/Items/Misc/Corpses/Corpse.cs`, `Carve` | costs nothing, no skill roll, doubles on Felucca; yields hides, and tailoring wants leather |
| Repo-wide conventions | `CLAUDE.md` at the repo root | the audit rules every `.cs` file here is held to |
