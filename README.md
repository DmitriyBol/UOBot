# UOBot — an autonomous bot population for ModernUO

Fifty-three bots that make their own living on a Renaissance shard. They are born with a purse and a kit,
dress themselves, and from then on nobody tells them anything: every few seconds each one holds a small
auction between every piece of work the shard can offer it, takes the best-paying, and goes and does it. They
dig ore and smelt it, fell trees, pick reagents out of the grass, forge weapons, sew leather, brew potions,
fletch arrows, cook what they kill, write scrolls, buy and sell over NPC counters, run a market among
themselves with real money on the table, bind their own wounds and each other's, hunt, form companies for
what one bot cannot take, teach each other for a fee, and harrow the ground that has already killed somebody.

Four of them think: a local language model, through Ollama, chooses what they do next instead of the auction.
A fifth thinking thing watches the other fifty-three and writes down what it believes is wrong with them.

**Nothing in the engine changes to run this.** It is a separate assembly (`BotAIv2.dll`, namespace
`Server.BotAI.V2`) that ModernUO loads through `Data/assemblies.json`; not one line of the core references
it. It installs by copying a folder and removes by deleting a line. The single exception is documented under
*Installing* — seventeen lines added to `CraftItem.cs` so the bots can ask the engine's own question about
fire rather than keep a second copy of the answer.

---

## Quick start

```bash
dotnet build ModernUO.slnx -c Release
```

```bash
./start-shard.ps1
```

Starts the shard hidden and waits until it reports listening on `127.0.0.1:2593`. Its whole console goes to
`logs/session-<yyyy-MM-dd_HH-mm>.log`, one file per start.

```bash
taskkill /F /IM ModernUO.exe
```

Connect with any Renaissance client at `127.0.0.1:2593`. Then, in game, as an administrator:

| command | what it does |
|---|---|
| `[bots` | the dashboard: five tabs — the population, their market, what they are short of, what the city wants, what everyone is doing |
| `[argus` or `[debugger` | brings the observer to you |

**A running shard holds the assemblies.** Building while it runs still compiles; it fails only at the copy
into `Distribution/Assemblies`. `dotnet build … | grep "error CS"` is therefore a valid syntax check against a
live shard.

---

## What a bot can do

Fifty-one kinds of work, offered by twenty-eight proposers and carried out by thirty-four deeds. These are
the names that appear in the log, with how often a population of fifty-three took each one over a
two-and-a-half hour session:

**Getting raw material**

| work | what it is |
|---|---|
| `mine` | walk to a seam, dig it, smelt the ore into ingots |
| `chop` | fell a tree for logs and boards — and the axe has to be *worn*, which is why nothing was ever cut until that was found |
| `herbs` | pick reagents out of the grass; the second commonest thing the population does |
| `forage` | anything else worth lifting off the ground |
| `pickings` · `glean` | go through a corpse somebody left, including one this bot did not kill |

**Making things**

| work | out of | into |
|---|---|---|
| `forge` | ingots of any of eight metals, at a forge with an anvil | weapons and armour |
| `sew` | leather off a carcass, or cloth off a counter | armour and clothing |
| `brew` | a reagent and an empty bottle | potions |
| `fletch` | shafts and feathers | arrows and bolts |
| `cook` | raw meat off a kill, at any fire | meals, which are eaten and quicken recovery for ten minutes |
| `inscribe` | a blank scroll and reagents | spell scrolls, which no shopkeeper on this shard sells |

**Trading**

| work | what it is |
|---|---|
| `peddle` | carry goods to an NPC counter and sell them — one of the two ways gold enters this world |
| `restock` | buy from an NPC counter: reagents, bandages, cloth, bottles, blank scrolls |
| `order` | put money down on the bots' own board for something a bot cannot make itself |
| `acquire` | buy a scroll off the market and put the spell in a book |
| `unload` | take a full pack to a counter: coin into the account, everything spare onto a stall |

**Fighting and surviving**

| work | what it is |
|---|---|
| `prowl` | look for something worth fighting — by far the commonest thing a bot does |
| `hunt` | close with a chosen creature, kill it, carve it, go through it |
| `band` | call a company together for something one bot cannot take |
| `rescue` | go to somebody who cried for help |
| `mend` | bandages: your own wounds first, somebody else's as ordinary paid work |
| `flee` | break off and run, which is the only work offered on the `Failing` rung |
| `sweep` · `enlist` | a standing company patrolling, and joining one |

**Leading, teaching, and the island itself**

| work | what it is |
|---|---|
| `drill` | a captain holds a training field |
| `drill-in` | a bot pays the fee and attends one — worth about fifteen times what a rescue is in skill gained |
| `scout` | walk into a square nobody has ever stood in and write down what is there |
| `harrow` | the Baron raises a levy and marches it at ground that has already killed people |
| `stroll` | the Baron tours a town, and pays a stipend out of his own account |
| `homeward` · `reclaim` | walk back to camp; go back for what death took |

And `mind-<anything>` is the same work chosen by one of the four bots that think rather than by the auction.

Some things are not work at all but conditions checked on every beat, because they take no journey and would
lose every auction they entered: eating a meal, banking above a threshold, putting on better armour, taking
the bow back up after a fight.

---

## The population

Thirteen classes. A class here is **a description and a set of limits with no behaviour** — it decides
nothing and commands nobody, and `BotWill` reads it the way it reads the map.

| class | what it is | of 53 |
|---|---|---|
| `Warrior` | the plain fighter | 14 |
| `WarriorMage` | plate, a blade, and spells anyway | 6 |
| `WarriorArcher` | shoots, and has a knife for when that stops working | 6 |
| `Mage` | a spellbook, a blue staff, and no metal | 6 |
| `Archer` | the bow and nothing else, and the only class that can triple a hit | 5 |
| `Healer` | the green staff | 5 |
| `Brawler` | fights with its hands, and is therefore never holding anything it has to put down | 3 |
| `Gatherer` | ore and timber, and the only bot that can find a reagent in the grass | 2 |
| `Crafter` | metal, cloth and leather | 2 |
| `Captain` | the one bot that exists for the others rather than for itself | 1 |
| `Baron` | the one bot that is not trying to make a living | 1 |
| `Architect` | paid by the health of the market rather than by any errand in it | 1 |
| `Sage` | the captain's opposite number, for the half of the population a captain cannot teach | 1 |

They are raised outside Britain at `(1440, 1470)` on Felucca with 400gp each, and roam within a thousand
tiles of it. The mix, the home and the purse are all `Distribution/Configuration/bot-population.json`.

**What a bot carries between restarts** is its skills, fame, karma and savings — `BotProgress`, a file of its
own. Everything else about a bot is rebuilt at boot.

---

## How a bot decides

Three stages, every turn, in `BotWill`:

1. **The ladder.** Which rung the bot is on, from facts alone: `Failing` (hurt, fleeing), `Free`, `Bound`
   (charged by a company). A rung decides which work is even offered.
2. **Obligations.** Anything already promised is taken before anything is auctioned.
3. **The auction.** Every proposer registered by every subsystem is asked whether it has work for this bot.
   Each offer is priced **per minute** and the best wins. The losing runner-up is printed beside it, so the
   log always says what a choice was made *against*.

The prices are not weights. Every trade opens at a guess and `BotLedger` corrects it by what the work
actually paid — so a trade that stops paying stops being chosen, without anybody editing a number. A
representative line:

```
Wulfric took on hunt: after a horse: 287/min = 295 × 0.97, that being the fifth root of
near 0.90 × new 1.00 × room 0.97 × safe 1.00 × purse 1.00; 5 of 5 offers worth anything;
over order: ordering 1 LeatherChest at 8/min
```

295 per minute is what the ledger has learned hunting pays; the five factors are nearness, novelty, room in
the pack, safety of the ground and what is in the purse; 287 is the result; and the thing it beat was worth
8. **Every factor has a floor**, because a multiplier that can reach zero is a veto — a lesson this project
paid for when an empty purse silently forbade a bot from looking for work at all.

---

## The economy

There is no shopkeeper handing out an allowance and no spawner dropping gold on the ground. Every coin in the
population's hands came in one of two ways and leaves in one of two others.

**Where gold comes from**

- **Kills.** A creature's purse is new money. This is the only real faucet, and it is why `prowl` and `hunt`
  together are half of everything the population does.
- **Selling to an NPC.** A shopkeeper's own money enters the world when a bot sells it something — `peddle`,
  and it is what the raw end of every gathering trade is worth when nobody else wants the stuff.

**Where gold goes**

- **Buying from an NPC.** Reagents, bandages, cloth, bottles, blank scrolls — `restock`; and a horse at
  500gp, which is the single largest purchase a bot makes.
- **The market's levy.** One per cent of every settled sale between bots, minimum one gold, paid to whoever
  holds the Baron's office. Not a sink so much as a redistribution: it is the only income of the one bot that
  does not trade.

**What circulates between them** is the bots' own market: stalls and wants, both sides run by bots.

- A **stall** is a standing offer: one kind of thing, a quantity, a price, and what that price has learned.
- A **want** is money already down for something the bot cannot make itself. A want is what turns speculative
  crafting into filled orders, and it is the mechanism the whole crafting side hangs on — a smith reading the
  board is the difference between "somebody needs a blade" and a blade.
- Prices move on evidence. A stall that sits unsold is cut; one that empties fast is raised; a want that goes
  unfilled bids up.

Sixteen minutes into a fresh shard, from the summary:

```
The market: 169 of 1024 stalls holding 1825 things worth 10367gp and 85 of 512 wants for
113 things with 6012gp down; 88 sales and 31 fills for 1434gp
Money: 49 purses that were earned: poorest 3gp, middling 254gp, fattest 960gp held by Bryn
the Gatherer, 12210gp between them with 9650gp of it in pockets and 2560gp in accounts
```

**The chains that close.** Each of these is a sequence in which every step is a separate bot acting for its
own reasons, and each took work to close because a break anywhere in it looks exactly like the whole thing
being dead:

```
kill → carve → hides → scissors → leather → stall → tailor's want → leather armour → worn
seam → dig → smelt → ingots → stall → smith's want → weapon → filled order → carried
kill → carve → raw meat → kept back → fire → cooked meal → eaten → recovers twice as fast
grass → reagent → stall → alchemist's want → potion → drunk in a fight
NPC counter → blank scroll → scribe → spell scroll → market → mage's book
```

**Money has weight, and a bot pays out of its pocket.** The engine takes payment from the pack, not the
account, and a bot only walks to a bank above a threshold. That interaction once produced money that existed
and could not be spent.

---

## Watching it

**The dashboard.** `[bots` — five tabs, a row per bot, including the direction each one is developing in.

**The summary.** Every five minutes the population writes a block of about eighteen lines, each one a
complete accounting of one mechanism with no bucket called "other". They are the primary instrument, and
they are written to be read as sentences:

```
1259 asked to cook: 64 put something on, 1195 had no meat worth cooking, 0 had meat but no
recipe their skill would carry, 0 had both and no fire they could get to (of 9 known);
27 stacks of raw meat kept back off a corpse for their own pan against 0 sold on past the
cap; 2 meals eaten, 332 looks found a bot still fed from the last one
```

Every number there is a static counter on one class, and `MAP.md` §1 maps each line prefix to the file that
assembles it. A hard zero next to a healthy denominator is the shape most defects here take.

**Per-bot lines.** `took on`, `finished`, `failed at`, `dropped` — with the reason in the deed's own words.
Comparing `finished X` against `failed at X` per trade is the fastest audit there is:

```bash
for t in mine chop forge sew brew fletch cook inscribe hunt; do
  echo "$t: fin $(grep -c "finished $t:" "$L") / fail $(grep -c "failed at $t:" "$L")"
done
```

**Argus, the observer.** An invisible figure nobody in the world can see, that cannot be hurt and cannot hurt
anything. Every two minutes it asks three questions of every bot, lays a hand on the ones that answer no to
all three, and writes what it believes into `logs/bot-debugger.log`. A person at the keyboard reaches it by
writing a line into `Distribution/argus-in.txt`; the answer appears in `argus-out.txt` within a couple of
seconds. Its hands are a bounded set — `props`, `sight`, `where`, `tile`, `tele`, `home`, `res`, `free`,
`shun`, and `none`, which heads the list deliberately — and every use of them is written to
`logs/bot-debugger-commands.log`, a separate file from its observations, so that a hand cannot quietly alter
what it is watching without the record showing it. Nothing it can do deletes anything, sets a property,
touches an account, or reaches a mobile that is not one of ours.

**Believe the shard before the watcher.** Five false alarms in one day were all artefacts of the instrument.

---

## Configuration

One file per subsystem in `Distribution/Configuration/`:

```
bot-auction  bot-baron  bot-classes  bot-craft   bot-debugger  bot-drill
bot-harvest  bot-hunt   bot-mend     bot-mind    bot-minds     bot-movement
bot-population  bot-shops  bot-spells  bot-squad  bot-will
```

Any file that does not exist writes itself on first boot. The whole thing goes off with one
`bots.enabled: False` in `modernuo.json`; the per-module switches beside it are not there for flexibility but
for diagnosis, because halving the number of running modules is faster than reading a 37 MB log.

**Keys are PascalCase, and a wrong key is silent.** A key in lower case is not an error and not a warning —
the value simply stays at its default, and a file that appears to have been read is worse than one that fails
to load. The only proof of what a dial is actually set to is the line its module writes at boot.

`DIALS.md` lists all 552 tunables in the assembly with their defaults and, where one exists, the
configuration key that overrides it. 216 of them can be changed without a rebuild; the rest are marked.

---

## Installing it into a fork

1. Copy `Projects/BotAIv2` into the fork beside `UOContent`, `Server` and `Logger`. The `.csproj` paths are
   relative and assume exactly that depth.
2. Add two lines to `ModernUO.slnx`:
   ```xml
   <Project Path="Projects/BotAIv2/BotAIv2.csproj" />
   <Project Path="Projects/BotAIv2/mindedBots/BotMindAI.csproj" />
   ```
3. **Apply `Projects/BotAIv2/engine-patches/CraftItem-heat-source.patch`** (`git apply` from the fork root). Seventeen lines added to
   `Projects/UOContent/Engines/Craft/Core/CraftItem.cs`, exposing two questions the engine already answers
   privately: is this tile a fire, and is this mobile standing near one. `BotGround` surveys the ground for
   places a craft can be worked and asks the engine's own table rather than keeping a second copy of it —
   which is the whole reason for the patch, and the reason it is two accessors and no logic.
4. Build. Output lands in `Distribution/Assemblies`:
   ```bash
   dotnet build Projects/BotAIv2/mindedBots/BotMindAI.csproj -c Release
   ```
5. Add `"BotAIv2.dll"` and `"BotMindAI.dll"` beside `"UOContent.dll"` in `Distribution/Data/assemblies.json`.
6. Copy `Distribution/Configuration/bot-*.json`.
7. Switch it on in `Distribution/Configuration/modernuo.json`: `bots.enabled`, plus one key per module —
   `bots.auction/baron/classes/craft/dashboard/drill/harvest/hunt/mend/mind/movement/population/shops/spells/squads/will.enabled`
   — and `bots.mind.thinking` for the calls to the model.

**For the thinking bots** you also need [Ollama](https://ollama.com) running locally with the model named in
`bot-minds.json`. Without it the four minded bots simply fall back to the auction like everybody else; nothing
else is affected.

---

## The documents

| read this | for |
|---|---|
| `MAP.md` | **where anything is.** A block per subsystem, every one of the 212 files with the one thing it decides, and the table that turns a summary line into the file that wrote it |
| `DIALS.md` | what every number is set to, and whether a config file can reach it |
| `ARCHITECTURE.md` | how the whole thing is put together: the rules, the vocabulary, how to add work |
| `BUILD.md` | building it, and what the boot log should say line by line |
| `HANDOFF.md` | the state of the work |
| `<Subsystem>/README.md` | why each decision in that subsystem is the way it is |

Those subsystem READMEs were written once and several have not kept up — read them for reasoning, never for
facts. `MAP.md` §2 and `DIALS.md` are generated from the source by `regen-map.py` and cannot drift the same
way.

---

## Working order

Changes go into the fork — [DmitriyBol/ModernUO-fork](https://github.com/DmitriyBol/ModernUO-fork), in
`Projects/BotAIv2`. Shakedown happens on a live shard. What has actually run comes here. The paths in this
repository mirror the paths in the fork, so moving work either way is a copy of the tree.

**Every change is measured on the shard before it is believed.** The pattern this project keeps returning to
is that a mechanism which looks broken is usually two numbers that never met, and that the engine refuses in
silence — it answers a refusal by sending a message to a screen the bot has not got. `MAP.md` §4 lists the
shapes those defects take.
