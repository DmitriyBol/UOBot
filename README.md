# UOBot — an autonomous bot population for ModernUO

Bots that make their own living on the shard: they dress themselves, take on work, mine, smelt, sew, write
scrolls, trade with each other and with the NPCs, patch themselves up, hunt, and train the newcomers. Four of
them think — a local language model, through Ollama, chooses what they do next.

**This is a separate assembly.** Not one line of the ModernUO core changes, and nothing in the engine references
this project. It is installed by copying a folder into the fork, and removed by deleting one line from
`assemblies.json`.

## How this repository relates to the fork

Development and shakedown happen in the fork — [DmitriyBol/ModernUO-fork](https://github.com/DmitriyBol/ModernUO-fork),
in `Projects/BotAIv2`. What lands here is what has already run on a live shard.

**The paths in this repository mirror the paths in the fork.** Moving work either way is a copy of the tree —
no renaming and no editing the `.csproj`: its paths are relative and assume exactly the depth
`Projects\BotAIv2`.

```
UOBot/
  Projects/BotAIv2/              the sources: 183 C# files, ~52,000 lines, 25 documents
    mindedBots/                  BotMindAI — the thinking bots, a separate assembly
  Distribution/Configuration/    the tuned bot configuration, taken off the running shard
  start-shard.ps1 / .cmd         starts the shard hidden, logging to logs\session-<date>.log
```

## What is in it

| Folder | What it is |
|---|---|
| `BotModules/` | the loading frame: phases, declared dependencies, a switch on each module |
| `BotClasses/` | the classes: what a bot is. Data and limits, no behaviour |
| `BotOutfit/` | the kit and the bind: what a bot holds, and what death has no right to take from it |
| `BotMovement/` | path, road and step: search, pockets of ground, the queue of errands |
| `BotCombat/` | is it worth stopping: strength, threat, the decision |
| `BotWill/` | what a bot does and why: the ladder, obligations, the auction of work, the ledger |
| `BotPopulation/` | the bot itself on `PlayerMobile`, the population's beat, birth and raising the fallen |
| `BotHarvest/` | dig → smelt → bank |
| `BotCraft/` | buy cloth → sew → sell |
| `BotSpells/` | write scrolls, fill a book |
| `BotShops/` | NPC trade both ways — where the world's money enters and leaves |
| `BotAuction/` | the bots' own market: stalls, and wants with money behind them |
| `BotHunt/` | close, fight, loot. The only faucet of gold |
| `BotMend/` | mending: yourself above all else, others as ordinary work |
| `BotSquad/` | squads: leader, formation, scouting, dividing the spoils |
| `BotDrill/` | the Captain: posts, the training field, the map of danger |
| `BotBaron/` | the Baron: harrows ground that has already killed people, and is paid nothing for it |
| `BotRanger/` | the Baron's Rangers: livery, provisioning, the quartermaster |
| `BotQuad/` | the island cut into squares thirty tiles across, each carrying one number: how safe it proved |
| `BotDashboard/` | `[bots` — tabs onto the population, its market and its shortages |
| `mindedBots/` | `BotMindAI`: the mind, through Ollama. It references `BotAIv2`; there is no reference back |

Read in this order: `Projects/BotAIv2/ARCHITECTURE.md` (how the whole thing is put together, the vocabulary, how
to add work) → `BUILD.md` (how to build it, and what the boot log should say line by line) → `HANDOFF.md` (the
state of the work) → `<Subsystem>/README.md` (why each decision in that subsystem is the way it is).

## Installing it into the fork

1. Copy `Projects\BotAIv2` into the fork, beside `UOContent`, `Server` and `Logger`.
2. Add two lines to `ModernUO.slnx`:
   ```xml
   <Project Path="Projects/BotAIv2/BotAIv2.csproj" />
   <Project Path="Projects/BotAIv2/mindedBots/BotMindAI.csproj" />
   ```
3. Build — the output lands in `Distribution\Assemblies`:
   ```
   dotnet build Projects\BotAIv2\mindedBots\BotMindAI.csproj -c Release
   ```
4. Add `"BotAIv2.dll"` and `"BotMindAI.dll"` beside `"UOContent.dll"` in `Distribution\Data\assemblies.json`.
5. Copy `Distribution\Configuration\bot-*.json`. Any configuration file that does not exist yet writes itself on
   first boot.
6. Switch it on in `Distribution\Configuration\modernuo.json`: `bots.enabled`, plus one key per module —
   `bots.auction/baron/classes/craft/dashboard/drill/harvest/hunt/mend/mind/movement/population/shops/spells/squads/will.enabled`
   — and `bots.mind.thinking` for the calls to the model.

The whole thing goes off with one `bots.enabled: False`. The per-module switches are not there for flexibility
but for diagnosis: halving the number of running modules is faster than reading a 37 MB log.

**Configuration keys are PascalCase**, and it is not a style question. A key in lower case is not an error and
not a warning — the value silently stays at its default, and a file that appears to have been read is worse than
one that fails to load.

## What happens on a run

Thirty-four bots appear outside Britain (`bot-population.json`): gatherers, crafters, warriors, archers, mages,
healers, a Captain, a Baron, an Architect and a Sage. They dress, take work off the auction and go and do it.
`[bots` shows the whole population in one table — a row per bot, including the vector each one is developing
along.

A bot's real thresholds are not in the code but in `Configuration\bot-*.json`; what the shard actually read is in
the opening lines of its log.

## Working order

Changes go into the fork. The shakedown happens on a live shard. What has run comes here.
