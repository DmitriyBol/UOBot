# UOBot — an autonomous bot population for ModernUO

Fifty-three bots that make their own living on a Renaissance shard. They are born with a purse and a kit,
dress themselves, and from then on nobody tells them anything: every few seconds each one holds a small
auction between every piece of work the shard can offer it, takes the best-paying, and goes and does it. They
dig ore and smelt it, fell trees, pick reagents out of the grass, forge weapons, sew leather, brew potions,
fletch arrows, cook what they kill, write scrolls, buy and sell over NPC counters, run a market among
themselves with real money on the table, bind their own wounds and each other's, hunt, form companies for
what one bot cannot take, teach each other for a fee, and harrow the ground that has already killed somebody.

Four of them think — a local language model chooses what they do next instead of the auction — and a fifth
thinking thing watches all the others and writes down what it believes is wrong with them. Both are optional,
both are off without Ollama, and both have their own sections near the bottom: **The bots that think** and
**Argus**.

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
| `Captain`&nbsp;† | the one bot that exists for the others rather than for itself | 1 |
| `Baron`&nbsp;† | the one bot that is not trying to make a living | 1 |
| `Architect`&nbsp;† | paid by the health of the market rather than by any errand in it | 1 |
| `Sage`&nbsp;† | the captain's opposite number, for the half of the population a captain cannot teach | 1 |

† **These four are offices, and an office needs somebody in it.** They are the bodies the thinking bots claim,
and without a mind they are an ordinary body wearing a title: what makes a captain a captain is that something
is deciding whose square is killing people and which of the young ones is worth an hour of drill, and none of
those is a question the auction can be asked. See **The bots that think**.

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

**They are running totals since the shard started, not figures for the last five minutes** — the `Forget()`
that would reset them runs only on a world reload. So a rate is the difference between two summaries over the
time between them, the same value twice means nothing happened, and two runs compare only at the same age.

**Per-bot lines.** `took on`, `finished`, `failed at`, `dropped` — with the reason in the deed's own words.
Comparing `finished X` against `failed at X` per trade is the fastest audit there is:

```bash
for t in mine chop forge sew brew fletch cook inscribe hunt; do
  echo "$t: fin $(grep -c "finished $t:" "$L") / fail $(grep -c "failed at $t:" "$L")"
done
```

**Argus, the observer.** An invisible figure that measures every bot on its own clock and writes what it
believes into `logs/bot-debugger.log`, reachable from the keyboard without a client. It has its own section
below — **Argus, the debugger** — because it is a thing you switch on rather than a thing the shard has.

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

## The bots that think

Four of the fifty-three do not use the auction. On their turn a local language model is handed what that bot
can see — its purse, its skills, the work on offer with what each is forecast to pay, and the rules it has
written for itself — and it answers with one choice. Everything else about them is an ordinary bot: they walk,
fight, trade and die like the rest, and their work appears in the log under the same names with a `mind-`
prefix.

**They are offices, not builds.** The first version made them a blade, a bow and a book — three ways of
fighting, which between them can only answer one question and answer it three times. Every bot on this shard
fights; almost none of them decides anything that outlives the fight. So each mind was given a subject that is
about the population rather than about the moment.

| mind | claims the body of | falls back to | its subject |
|---|---|---|---|
| **Aldric** | `Captain` | `Warrior` | Where to take a company, whose ground is killing people, and which of the young ones is worth an hour of drill. Holds the training field and sells lessons. |
| **Godric** | `Architect` | `Crafter` | What gets made and how well the shard is equipped. Paid by the health of the market rather than by any errand in it, so it is the only bot with a reason to make something nobody has asked for yet. |
| **Cedric** | `Sage` | `Mage` | What the casters know: which spells are worth writing, who is short of what, and teaching the half of the population a captain cannot. |
| **Baldric** | `Baron` | *nothing* | The island itself. Raises a levy for ground that has already killed somebody, walks his rounds, tours the towns, and pays a stipend out of his own account. He earns no wage — the market's levy is his whole income. |

**Why those four classes need a mind.** Three of the minds fall back to an ordinary build because a thinking
warrior in a warrior's body is still a thinking warrior. The Baron has no fallback on purpose: the sworn
trades, the stipend and the share he stands out of all live on the class, so a Baron mind in a warrior's body
would sit reading a prompt about harrowings it can never be offered. And a `Captain`, `Architect` or `Sage`
body with nothing thinking inside it is a title with no office behind it — the class is raised and dressed
like any other, but the decisions it exists to make are ones the auction was never asked. Raise those four
classes without the minds and you get four ordinary bots with unusual kit.

**They learn, and the learning is kept.** After a stint a mind may reckon up what it forecast against what the
work actually paid, and write itself a rule — *"On this shard, if Drill forecast is zero, skip immediately
regardless of duration."* Those rules live in `Distribution/Configuration/bot-minds.json` under each mind's
own name and survive restarts. Each keeps a bounded number and drops the worst to make room.

### Turning them on

1. Install [Ollama](https://ollama.com) and pull the model:

```bash
ollama pull qwen3.5:9b
```

2. Leave it serving on `http://127.0.0.1:11434` — Ollama's own default, and this project's.

3. Switch the module on in `Distribution/Configuration/modernuo.json`, and let it call the model:

```json
"bots": { "mind": { "enabled": true, "thinking": true } }
```

`enabled` raises the minds; `thinking` is what allows them to spend a call on the model. With `thinking` off
they exist and choose by arithmetic like everybody else, which is the cheap way to run the shard.

4. Make sure the four classes exist in `bot-population.json`, or a mind has no body to claim:

```json
"Classes": { "Captain": 1, "Baron": 1, "Architect": 1, "Sage": 1 }
```

5. Anything you want changed goes in `Distribution/Configuration/bot-mind.json`. An empty `{}` means every
   default stands:

| key | default | what it is |
|---|---|---|
| `Model` | `qwen3.5:9b` | as Ollama names it |
| `Endpoint` | `http://127.0.0.1:11434` | where the daemon listens |
| `KeepAlive` | `30m` | how long the model stays in video memory between questions |
| `TimeoutMs` | `120000` | how long one question may take before it is abandoned |
| `ThinkEveryMs` | — | how often a free bot may be asked to choose again |
| `ReviewEveryMs` | — | how often a mind may spend a call reckoning up instead of choosing |
| `ChoiceHoldsMs` | — | how long a choice waits for the auction to pick it up before it goes stale |
| `MostLessons` | — | how many rules one mind keeps |
| `Insistence` | — | what a mind's asking for a piece of work is worth on top of the work itself |
| `WarriorName` `ArchitectName` `SageName` `BaronName` | Aldric, Godric, Cedric, Baldric | whose rules are whose |

The boot log says what actually happened, and it is the only proof the model was reached:

```
Three minds are awake on qwen3.5:9b at http://127.0.0.1:11434: Aldric the captain,
Godric the architect and Cedric the sage … 4 of 4 have bodies
```

Their thinking goes to `logs/bot-minds.log`, and the `Minds:` line in the five-minute summary carries how many
decisions each made, how many were taken up, how many were outbid, and how long the model took on the wall
clock.

**If Ollama is not there, nothing breaks.** The calls fail, the minds fall back to the auction, and the shard
runs exactly as it does for the other forty-nine bots.

---

## Argus, the debugger

A fifth thinking thing that is **not one of the population**. It takes no work, joins no auction and owns
nothing. It is an invisible figure that nobody in the world can see, that cannot be hurt and cannot hurt
anything, and its whole job is to watch the bots and say what it believes is wrong with them.

**What it does**

- **Measures.** Every two seconds it reads every bot: where it is, whether it moved, what it is doing, and how
  long it has been doing it. Everything it reports was measured by itself on its own clock, never taken from
  the bots' own counters — which is the entire point of it.
- **Asks two questions** every ten minutes: *is anybody stuck*, and *is anybody doing something that produces
  nothing*. Three tests per bot, and it lays a hand only on the ones that answer no to all three.
- **Reflects** every half hour: reads its own recent findings back and asks what shape the defect is.
- **Remembers.** What it has come to believe, and how many times, lives in
  `Distribution/Configuration/bot-debugger-memory.json` and survives restarts.
- **Answers a person.** Write a line into `Distribution/argus-in.txt` and the answer appears in
  `Distribution/argus-out.txt` within a couple of seconds — no client, no character, no login.

**Its hands** are a bounded set, and `none` heads the list on purpose:

| verb | what it does |
|---|---|
| `none` | do nothing, which is the right answer most of the time |
| `props` | read everything the engine knows about one bot |
| `sight` | what that bot can see from where it stands |
| `where` · `tile` | where it is, and what the ground under it is |
| `tele` · `home` | put it somewhere it can stand; send it back to camp |
| `res` | raise it, if it is dead |
| `free` | let go of whatever work it is holding |
| `shun` | mark a creature or a patch as not worth another try |

Nothing there deletes anything, sets a property, touches an account or an access level, or acts on a mobile
that is not one of ours. The world save holds a real person's character, and a model that can be talked round
by its own previous sentence must not be able to reach it. Every use is written to
`logs/bot-debugger-commands.log` — a different file from its observations, so a hand cannot quietly alter what
it is watching without the record showing it.

### Turning it on

1. Pull its model. It thinks with a different one from the population, deliberately:

```bash
ollama pull deepseek-r1:14b
```

2. Switch the module on in `Distribution/Configuration/modernuo.json`:

```json
"bots": { "debugger": { "enabled": true } }
```

3. Optional settings go in `Distribution/Configuration/bot-debugger.json`; `{}` keeps every default —
   measuring every 2s, reporting every 10 minutes, reflecting every 30, and the thresholds at which it calls a
   bot frozen, its work silent, or its progress settled.

4. In game, `[argus` or `[debugger` brings it to you.

Its observations go to `logs/bot-debugger.log`, and nothing else is written there.

**Believe the shard before the watcher.** Five alarms in a single day were all artefacts of the instrument
rather than faults in the population. Read its claim, then check the number it was made from against the
shard's own five-minute summary before changing anything.

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
