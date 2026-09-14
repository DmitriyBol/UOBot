# UOBot — an autonomous bot population for ModernUO

Eighty bots that make their own living on a Renaissance shard, in five guilds. They are born with a purse and
a kit, dress themselves, and from then on nobody tells them anything: each one holds a small auction between
every piece of work the shard can offer it, takes the best, and goes and does it — and, since 14.09.2026, keeps
at it until it is done or something actually happens. They dig ore and smelt it, fell trees, pick reagents out of
the grass, forge weapons, sew leather, brew potions, fletch arrows, cook what they kill, write scrolls, buy and
sell over NPC counters, run a market among themselves with real money on the table, bind their own wounds and
each other's, hunt, form companies for what one bot cannot take, teach each other for a fee, harrow the ground
that has already killed somebody, take parties into dungeons, raise guild halls, claim land, and go to war with
each other under rules that end the war.

A thinking layer is optional. Four crafters can be handed to a local language model instead of the auction, and
three observers — Argus and two helpers — watch the population and write down what they believe is wrong with
it. Both need [Ollama](https://ollama.com); neither is needed for anything else. See **The bots that think** and
**Argus** near the bottom.

**Almost nothing in the engine changes to run this.** It is a separate assembly (`BotAIv2.dll`, namespace
`Server.BotAI.V2`, and the optional `BotMindAI.dll`) that ModernUO loads through `Data/assemblies.json`; not one
line of the core references it. The two exceptions are small engine patches, described under *Installing*: two
accessors on `CraftItem` so the bots can ask the engine's own question about fire, and one event on
`HarvestDefinition` so they can hear why a swing produced nothing.

---

## Quick start

```bash
dotnet build ModernUO.slnx -c Release
```

```bash
./start-shard-detached.ps1
```

Starts the shard so that it outlives the shell that started it and waits until it reports listening on
`127.0.0.1:2593`. Its whole console goes to `logs/session-<yyyy-MM-dd_HH-mm>.log`, one file per start.
`./install-shard-autostart.ps1` registers the same job to come back after a reboot.

Stop it by saving first — write `do save` into `Distribution/argus-in.txt` and wait for the answer in
`Distribution/argus-out.txt` — and then:

```bash
taskkill /F /IM ModernUO.exe
```

A kill without the save rolls the world back to the last autosave. Wars, truces, claims and guild seats are kept
in their own stores and survive a restart either way.

Connect with any Renaissance client at `127.0.0.1:2593`. Then, in game, as an administrator:

| command | what it does |
|---|---|
| `[bots` | the dashboard: ten tabs — the population, their market, what they are short of, the city, what they have learned, the island's squares, revels, halls, claims and guilds |
| `[argus` or `[debugger` | brings the observer to you |

**A running shard holds the assemblies.** Building while it runs still compiles; it fails only at the copy into
`Distribution/Assemblies`. `dotnet build … | grep "error CS"` is therefore a valid syntax check for `BotAIv2`
against a live shard — but not for `BotMindAI`, which MSBuild skips after its dependency's copy fails. Stop the
shard before trusting a build that touched `mindedBots/`.

`bash shard-status.sh` prints one screen about a running shard: wars with their score and clock, blood, halls and
seats, work and commitment, and the newest alarms.

---

## What a bot can do

Forty-six kinds of work, offered by forty-eight proposers and carried out by forty-seven kinds of undertaking.
These are the names that appear in the log.

**Getting raw material**

| work | what it is |
|---|---|
| `mine` | walk to a seam, dig it, smelt the ore into ingots, put the metal away |
| `chop` | fell a tree for logs — and the axe has to be *worn*, which is why nothing was ever cut until that was found |
| `herbs` | pick reagents out of the grass |
| `forage` · `prospect` | anything else worth lifting off the ground; a walk past the last swept ground to find rock |
| `pickings` · `glean` · `plunder` | go through a corpse, pick spent ammunition up, go through a chest standing in the world |

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
| `restock` | buy from a counter, a stall or the guild's own merchant: reagents, bandages, cloth, bottles, blank scrolls |
| `order` | put money down on the bots' own board for something a bot cannot make itself |
| `acquire` | get a spell the book is short of, off a shelf, a stall or the board |
| `unload` | take a full pack to a counter: coin into the account, everything spare onto a stall |

**Fighting and surviving**

| work | what it is |
|---|---|
| `prowl` | look for something worth fighting |
| `hunt` | close with a chosen creature, kill it, carve it, go through it |
| `band` | call a company together for something one bot cannot take |
| `rescue` | go to somebody who cried for help, or hit back at what is hitting you |
| `mend` | bandages and heals: your own wounds first, somebody else's as ordinary work |
| `escort` | a healer standing by one of ours who is fighting, close enough to bind the wound when it comes |
| `flee` | break off and run, which is what the `Failing` rung offers |
| `sweep` · `enlist` | a captain's company patrolling the worst square on the island, and falling in with a company already fighting |
| `liberate` | get a prisoner out of a camp and home |

**The guild**

| work | what it is |
|---|---|
| `hall` · `fit` · `hire` | raise the guild's hall with a levy, buy it a workbench, hire it a merchant |
| `supply` | fetch a batch of what the members keep running out of and leave it on the guild's counter |
| `stake` | stand in a square the guild is claiming, for as long as the claim takes |
| `evict` | walk over to somebody working the guild's land and tell them to move along |
| `rally` · `quarrel` | fall in with the guild's war company; close with a member of the guild it is at war with |
| `remove` | carry a beaten guild's hall out of the winner's yard |

**Leading, teaching, and the island itself**

| work | what it is |
|---|---|
| `drill` · `drill-in` | a captain or a sage holds a class; a bot pays the fee and attends one |
| `scout` | walk into a square nobody has ever stood in and write down what is there |
| `harrow` · `stroll` | the Baron marches a levy at ground that has killed people; walks his own town |
| `delve` | the maker of a guild takes five bots down a dungeon for twenty minutes or twenty corpses |
| `homeward` · `reclaim` | walk back to where the bot lives; go back for what death took |

Some things are not work at all but conditions checked on every beat, because they take no journey and would lose
every auction they entered: eating a meal, banking above a threshold, putting on better armour, taking the bow
back up after a fight.

---

## The population

Thirteen classes. A class here is **a description and a set of limits with no behaviour** — it decides nothing
and commands nobody, and `BotWill` reads it the way it reads the map. The mix shipped in `bot-population.json`:

| class | what it is | of 80 |
|---|---|---|
| `Gatherer` | ore and timber, and the only bot that can find a reagent in the grass | 12 |
| `Warrior` | the plain fighter | 11 |
| `Healer` | the green staff; fights only what has laid hands on it | 11 |
| `Archer` | the bow and nothing else, and the only class that can triple a hit | 10 |
| `Mage` | a spellbook, a blue staff, and no metal | 10 |
| `WarriorMage` | plate, a blade, and spells anyway | 8 |
| `WarriorArcher` | shoots, and has a knife for when that stops working | 8 |
| `Crafter` | metal, cloth and leather | 5 |
| `Brawler` | fights with its hands, and is therefore never holding anything it has to put down | 1 |
| `Captain` | the one bot that exists for the others: holds a training field and leads patrols | 1 |
| `Baron` | the one bot that is not trying to make a living: harrows, walks his rounds, pays a stipend | 1 |
| `Architect` | paid a hundredth of every sale on the market, so it is paid by the health of the market | 1 |
| `Sage` | the captain's opposite number, teaching the half of the population a captain cannot | 1 |

A bot is born — and raised again after it dies — at its guild's seat; the seats stand north, east, south and west
of Britain, and `Home` in `bot-population.json` is where a bot with no guild lives. Every bot starts with 400gp
and roams within a thousand tiles.

**What a bot carries between restarts** is its skills, fame, karma and savings — `Saves/BotProgress`. Everything
else about a bot is rebuilt at boot.

---

## How a bot decides

Three stages, every turn, in `BotWill`:

1. **The ladder.** Which rung the bot is on, from facts alone: `Failing` (hurt, fleeing), `Hunted`, `Bound`
   (charged by a company), `Busy`, `Free`. A rung decides which work is even offered.
2. **Obligations.** Anything already promised is taken before anything is auctioned.
3. **The auction.** Every proposer registered by every subsystem is asked whether it has work for this bot. Each
   offer is priced **per minute** and the best wins. The losing runner-up is printed beside it, so the log always
   says what a choice was made *against*.

The prices are not weights. Every trade opens at a guess and `BotLedger` corrects it by what the work actually
paid — so a trade that stops paying stops being chosen, without anybody editing a number. A representative line:

```
Wulfric took on hunt: after a horse: 287/min = 295 × 0.97, that being the fifth root of
near 0.90 × new 1.00 × room 0.97 × safe 1.00 × purse 1.00; 5 of 5 offers worth anything;
over order: ordering 1 LeatherChest at 8/min
```

295 per minute is what the ledger has learned hunting pays; the five factors are nearness, novelty, room in the
pack, safety of the ground and what is in the purse; 287 is the result; and the thing it beat was worth 8.
**Every factor has a floor**, because a multiplier that can reach zero is a veto — a lesson this project paid for
when an empty purse silently forbade a bot from looking for work at all.

**A choice is kept once it is made.** Work a bot sees through — mining, woodcutting, cooking, carrying goods to a
counter, getting a spell, standing for the guild, a healer standing by a fighter — is held against better offers
for as long as it reckoned it would take. Only events get through: something that will not wait, a call from
outside the bot's own business (a comrade in trouble, the guild's muster, a war company, a paid lesson), or
trouble in the work itself. When an event does take a bot off steadfast work, the work is put down and taken up
again afterwards rather than thrown away. This follows the oldest experiment on the question, Kinny and
Georgeff's: an agent that reconsiders at every new opportunity does worse than one that never reconsiders, and
one that commits but reacts to the right events beats both. Mining went from 44% of trips finished to 74%, and
from 37% abandoned to 5%.

**A class shows in what it picks.** Work that trains one of the bot's own class skills is worth a third more,
work that trains another class's skill a little over half, and the rest — selling, carrying, standing for the
guild — is neutral. A healer does not go looking for fights. The reasoning, the measurements and what is still
missing are in `RESEARCH-decisions.md`.

---

## Guilds, land and war

Five guilds of five to fifteen members, each with one master. The rules are in `GUILDS.md`; the wars and seats are
in `PLAN-wars-and-seats.md`. In short:

- **A hall** is raised by a levy on the members, fitted with the tools of the guild's trade and a merchant, and read
  back out of the world by the name on its sign at the next start.
- **Land** is claimed a thirty-tile square at a time, by members standing in it; working somebody else's land
  sours one guild's opinion of the other, and trading or fighting beside each other improves it.
- **War** starts when one guild's opinion of another falls far enough, and ends by rules rather than by
  exhaustion: twenty-five dead or five thousand gold of plunder wins it, a truce follows, declaring is allowed
  once a day, defence of the guild's own land comes first, and allies join. The loser's hall is carried outside
  the winner's yard.
- **All of it survives a restart**: wars, truces, clocks and opinions in `Saves/BotWars`, claims in
  `Saves/BotClaims`, seats in `Saves/BotSeats`.

**Dungeons.** The dungeon block has no road from the island, so a party is put down and lifted back out; which
dungeon is chosen by measuring what lives in it against what the band is worth, and what the party takes is swept
into one pot and divided at the end, half to the leader.

---

## The economy

There is no shopkeeper handing out an allowance and no spawner dropping gold on the ground. Every coin in the
population's hands came in one of two ways and leaves in one of two others.

**Where gold comes from**

- **Kills.** A creature's purse is new money. This is the only real faucet, and it is why `prowl` and `hunt`
  together are so much of what the fighters do.
- **Selling to an NPC.** A shopkeeper's own money enters the world when a bot sells it something — `peddle`, and it
  is what the raw end of every gathering trade is worth when nobody else wants the stuff.

**Where gold goes**

- **Buying from an NPC.** Reagents, bandages, cloth, bottles, blank scrolls — `restock`; and a horse, which is the
  single largest purchase a bot makes.
- **Guild halls.** The levy for a hall, its workbenches and its merchant's wages.

**What circulates between them** is the bots' own market: stalls and wants, both sides run by bots, with a
hundredth of every settled sale paid to the Architect.

- A **stall** is a standing offer: one kind of thing, a quantity, a price, and what that price has learned.
- A **want** is money already down for something the bot cannot make itself. A want is what turns speculative
  crafting into filled orders.
- Prices move on evidence. A stall that sits unsold is cut; one that empties fast is raised; a want that goes
  unfilled bids up.
- A guild's **counter** in its hall sells to its members what the guild's couriers bought in town, so nine members
  do not each walk to Britain for it.

**The chains that close.** Each is a sequence in which every step is a separate bot acting for its own reasons:

```
kill → carve → hides → scissors → leather → stall → tailor's want → leather armour → worn
seam → dig → smelt → ingots → stall → smith's want → weapon → filled order → carried
kill → carve → raw meat → kept back → fire → cooked meal → eaten → recovers twice as fast
grass → reagent → stall → alchemist's want → potion → drunk in a fight
NPC counter → blank scroll → scribe → spell scroll → market → mage's book
```

**Money has weight, and a bot pays out of its pocket.** The engine takes payment from the pack, not the account,
and a bot only walks to a bank above a threshold. That interaction once produced money that existed and could not
be spent.

---

## Watching it

**The summary.** Every five minutes the population writes a block of lines, each one a complete accounting of one
mechanism with no bucket called "other":

```
Resolve: 1150 of 1492 endings finished (77%), 225 failed, 117 dropped, 0 died; of the drops 15 for
something that would not wait, 13 for a call from outside, 47 for a rung above, 0 for a full pack
and 55 outbid; 35 better offers refused inside a hold and 4 holds lifted for trouble; 13 put down,
13 taken up again and 0 not; …
```

Every number there is a static counter on one class, and `MAP.md` §1 maps each line prefix to the file that
assembles it. A hard zero next to a healthy denominator is the shape most defects here take. **They are running
totals since the shard started, not figures for the last five minutes**: a rate is the difference between two
summaries over the time between them, and two runs compare only at the same age.

**Per-bot lines.** `took on`, `finished`, `failed at`, `dropped` — with the reason in the deed's own words, and on a
drop the numbers that decided it: how far into its own reckoning the work was, what it was taken at and what it was
worth by then. `put down … to take up again` and `took up … again` are an interruption and its end.

**The alarm channel.** `logs/alerts.ndjson` — the shard raises its own alarms there, one JSON line each, raised once,
repeated at most every fifteen minutes, cleared in their own event, with an hourly heartbeat so silence can be
trusted. Wars are announced there too.

**The dials.** Write `dials <word>` into `Distribution/argus-in.txt` to find a number and `dial <Class.Name>
<value>` to change it on the running shard. Every change is journalled to `logs/bot-dials.log` and lost on restart.

**Believe the shard before the watcher.** Five false alarms in one day were all artefacts of the instrument.

---

## Configuration

One file per subsystem in `Distribution/Configuration/`:

```
bot-alarm     bot-auction   bot-baron     bot-classes   bot-craft      bot-debugger   bot-delve
bot-drill     bot-estate    bot-harvest   bot-hunt      bot-mend       bot-mind       bot-movement
bot-population  bot-shops   bot-spells    bot-squad     bot-will
```

and two that the shard writes for itself: `bot-minds.json` (the rules the thinking bots have written) and
`bot-dungeon-halls.json` (which rooms of each dungeon a party has found it can walk between).

Any file that does not exist writes itself on first boot. The whole thing goes off with `"bots.enabled": "False"`
in `modernuo.json`; the per-module switches beside it are there for diagnosis, because halving the number of
running modules is faster than reading a long log.

**Keys are PascalCase, and a wrong key is silent.** A key in lower case is not an error and not a warning — the
value simply stays at its default. The only proof of what a dial is actually set to is the line its module writes
at boot.

`DIALS.md` lists all 842 tunables in the assemblies with their defaults and, where one exists, the configuration
key that overrides it; 303 can be changed without a rebuild, and every one of them through the door above.

---

## Installing it into a fork

1. Copy `Projects/BotAIv2` into the fork beside `UOContent`, `Server` and `Logger`. The `.csproj` paths are
   relative and assume exactly that depth.
2. Add two lines to `ModernUO.slnx`:
   ```xml
   <Project Path="Projects/BotAIv2/BotAIv2.csproj" />
   <Project Path="Projects/BotAIv2/mindedBots/BotMindAI.csproj" />
   ```
3. **Apply the two engine patches** in `Projects/BotAIv2/engine-patches/` from the fork root:
   - `CraftItem-heat-source.patch` — seventeen lines in `Projects/UOContent/Engines/Craft/Core/CraftItem.cs`
     exposing two questions the engine already answers privately: is this tile a fire, and is this mobile standing
     near one. Without it `BotAIv2` does not compile.
   - `HarvestDefinition-said.patch` — one event on `HarvestDefinition`, raised where the harvest system sends its
     message, so a bot can hear "there is no metal here to mine" instead of inferring it from silence. Nothing in
     the engine subscribes; the message is still sent as before.
4. Build. Output lands in `Distribution/Assemblies`:
   ```bash
   dotnet build ModernUO.slnx -c Release
   ```
5. Add `"BotAIv2.dll"` and `"BotMindAI.dll"` after `"UOContent.dll"` in `Distribution/Data/assemblies.json`.
6. Copy `Distribution/Configuration/bot-*.json`.
7. Switch it on in `Distribution/Configuration/modernuo.json`: `"bots.enabled": "True"`, plus one key per module —
   `bots.alarm`, `auction`, `baron`, `classes`, `craft`, `dashboard`, `debugger`, `delve`, `drill`, `estate`,
   `harvest`, `hunt`, `mend`, `mind`, `movement`, `population`, `shops`, `spells`, `squads`, `will`, each
   `.enabled` — and `bots.mind.thinking` for calls to the model.

Set `"core.expansion": "UOR"`. Every weapon, spell circle, armour rating and recipe the bots reason about is read off
the shard's own tables for this expansion.

---

## The bots that think

**Four crafters may think, and in the shipped configuration none do.** On 07.09.2026 the office minds — the
captain's, the architect's, the sage's and the Baron's — were stood down, and thinking was given to the crafter
trade alone, where the work is a chain the auction cannot see: a want on the board, a material that may not exist
yet, a skill that may not be high enough, a price that has to beat buying the thing outright. On 13.09.2026 the
crafters were made ordinary bots again; `CrafterNames` in `bot-mind.json` is empty. The minds, and the state they are
shown, are described in `mindedBots/README.md`.

A thinking bot is otherwise an ordinary bot: the model names a trade from a fixed list, the choice runs as an
ordinary undertaking under the same auction and the same commitment, and its forecast is measured rather than
believed — so it can only win a place by being right.

### Turning them on

1. Install [Ollama](https://ollama.com) and pull the model:
   ```bash
   ollama pull qwen3.5:9b
   ```
2. Leave it serving on `http://127.0.0.1:11434`.
3. Name the crafters that should think in `Distribution/Configuration/bot-mind.json`, for example
   `"CrafterNames": ["Roderic", "Emeric", "Ulric", "Wulfric"]`, and keep at least that many crafters in
   `bot-population.json`.
4. `"bots.mind.enabled": "True"` and `"bots.mind.thinking": "True"` in `modernuo.json`.

Their thinking goes to `logs/bot-minds.log`, and the `Minds:` summary line carries how many decisions each made, how
many were taken up and how long the model took. **If Ollama is not there, nothing breaks**: the calls fail and those
bots choose by arithmetic like everybody else.

---

## Argus, the debugger

A thinking thing that is **not one of the population** — since 13.09.2026 a squad of three: Argus in red, Lynceus in
blue, Heimdall in green. They take no work, join no auction and own nothing. They are invisible figures nobody in the
world can see, that cannot be hurt and cannot hurt anything, and their whole job is to watch the bots and say what
they believe is wrong with them. Each watches a share of the classes and stands beside a different suspect; they see
each other's last conclusions and take turns with the model.

**What they do**

- **Measure.** Every two seconds every bot is read: where it is, whether it moved, what it is doing, and for how
  long. Everything reported was measured on the squad's own clock, never taken from the bots' own counters.
- **Ask two questions** every ten minutes: *is anybody stuck*, and *is anybody doing something that produces
  nothing*.
- **Reflect** every half hour on their own recent findings, and **remember** what they have come to believe in
  `Distribution/Configuration/bot-debugger-memory.json`.
- **Answer a person.** Write a line into `Distribution/argus-in.txt` and the answer appears in
  `Distribution/argus-out.txt` within a couple of seconds — no client, no character, no login.

**The hands** are a bounded set, and `none` heads the list on purpose:

| verb | what it does |
|---|---|
| `none` | do nothing, which is the right answer most of the time |
| `props` | read everything the engine knows about one bot, with its skills and title |
| `sight` | whether that bot can see and lawfully strike what it is fighting, and from how far |
| `where` · `tile` | where it is and who stands on top of it; whether a body fits on a tile and what is on it |
| `pack` | what it carries, what it may keep of each, and what is surplus |
| `tele` · `home` | put it somewhere it can stand; send it back to where the population lives |
| `res` | raise it, if it is dead |
| `free` | let go of whatever work it is holding |
| `shun` | leave whatever it is fighting alone for a while |
| `near` | everything alive around a spot, with health and whether it would fight us |
| `camp` | put an orc camp on the ground, to see whether the population loots it and frees the prisoner |
| `summon` · `call` | bring a bot to the squad to watch it work; bring the administrator to it |
| `resolve` | what a bot holds, how far into its own reckoning, whether a better offer could take it off that now, what it put down, and how its last few pieces of work ended |
| `roles` | how each class spends its working minutes: its own trade, anybody's work, another class's trade |

Through the door only, and never offered to a model: `halls`, `raze`, `revel`, `wars`, `seats`, `seat`, `save`,
`road`, and `resolves` — how much of what the population takes on it sees through, what takes it off the rest, and
the same per trade.

Nothing there deletes anything, sets a property, touches an account or an access level, or acts on a mobile that is
not one of ours. Every use is written to `logs/bot-debugger-commands.log` — a different file from the observations,
so a hand cannot quietly alter what it is watching without the record showing it.

### Turning it on

1. Pull its model — a different one from the population's, deliberately:
   ```bash
   ollama pull deepseek-r1:14b
   ```
2. `"bots.debugger.enabled": "True"` in `modernuo.json`.
3. Optional settings go in `Distribution/Configuration/bot-debugger.json` — the helpers' names and robes, the
   intervals, and the thresholds at which a bot is called frozen, its work silent, or its progress settled.
4. In game, `[argus` or `[debugger` brings it to you.

Observations go to `logs/bot-debugger.log`. **Believe the shard before the watcher**: read a claim, then check the
number it was made from against the shard's own five-minute summary before changing anything.

---

## The documents

| read this | for |
|---|---|
| `MAP.md` | **where anything is.** A block per subsystem, every one of the 268 files with the one thing it decides, and the table that turns a summary line into the file that wrote it |
| `DIALS.md` | what every number is set to, and whether a config file can reach it |
| `ARCHITECTURE.md` | how the whole thing is put together: the rules, the vocabulary, how to add work |
| `RESEARCH-decisions.md` | how game and agent brains decide — commitment, correctness, roles — what this shard was measured doing, what changed because of it, and what should come next |
| `GUILDS.md` | the guilds' manifesto and rules, and the model of a social circle behind them |
| `PLAN-wars-and-seats.md` | wars with an end and guilds with a seat: what was built, what it was measured against, what is open |
| `INSTALL.md` · `BUILD.md` | installing it, building it, and what the boot log should say |
| `HANDOFF.md` | the state of the work, newest first |
| `<Subsystem>/README.md` | why each decision in that subsystem is the way it is |

The subsystem READMEs are written by hand — read them for reasoning, and `MAP.md` §2 and `DIALS.md`, which are
generated from the source by `regen-map.py`, for facts. The generator also reports any README whose file table has
drifted from its folder.

In this repository the code carries only its class summaries; the reasoning that lived in comments beside each
decision is in the fork, and the documents above carry what a reader needs.

---

## Working order

Changes go into the fork — [DmitriyBol/ModernUO-fork](https://github.com/DmitriyBol/ModernUO-fork), in
`Projects/BotAIv2`. Shakedown happens on a live shard. What has actually run comes here. The paths in this
repository mirror the paths in the fork, so moving work either way is a copy of the tree.

**Every change is measured on the shard before it is believed.** The pattern this project keeps returning to is that
a mechanism which looks broken is usually two numbers that never met, and that the engine refuses in silence — it
answers a refusal by sending a message to a screen the bot has not got. `MAP.md` §4 lists the shapes those defects
take.
