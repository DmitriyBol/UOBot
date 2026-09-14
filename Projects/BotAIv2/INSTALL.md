# BotAI — installation

Autonomous bots for [ModernUO](https://github.com/modernuo/ModernUO). Two assemblies, and the second one is
optional:

| | |
|---|---|
| **BotAIv2** | the population: trades, combat, market, squads, guilds, halls, land, wars, dungeons, the quadrant map. No external dependencies. |
| **BotMindAI** | `mindedBots/` — thinking crafters with a local LLM instead of pure arithmetic, and Argus's squad of observers. Needs [Ollama](https://ollama.com) to think; builds and runs without it. |

`BotMindAI` references `BotAIv2` and never the other way round. You can ship the first without the second; you
cannot ship the second alone.

> **Two engine patches are required**, both in `engine-patches/`, both applied with `git apply` from the fork root
> before building:
>
> - `CraftItem-heat-source.patch` adds seventeen lines to `Projects/UOContent/Engines/Craft/Core/CraftItem.cs`,
>   exposing two questions the engine already answers privately — is this tile a fire, is this mobile standing near
>   one. Without it `BotAIv2` does not compile.
> - `HarvestDefinition-said.patch` adds one event to `Projects/UOContent/Engines/Harvest/Core/HarvestDefinition.cs`,
>   raised where the harvest system sends a player its message, so a bot can hear why a swing produced nothing.
>   Nothing in the engine subscribes and the message is still sent exactly as before.

---

## Requirements

| | |
|---|---|
| Server | ModernUO, .NET 10 SDK |
| **Expansion** | **Renaissance (UOR)** — `"core.expansion": "UOR"` in `Distribution/Configuration/modernuo.json` |
| Client | any UO client ModernUO supports; ClassicUO was used throughout |
| Ollama | only for the thinking bots and the observers — see below |

The era matters. Every weapon, spell circle, armour rating and crafting recipe the bots reason about is read off the
shard's own tables for **this** expansion. On a later expansion the code still builds and the numbers stop meaning
what they say.

---

## 1. Ordinary bots

**Copy the projects**

```
Projects/BotAIv2/                 → your ModernUO checkout, same place
```

**Add them to the solution** (`ModernUO.slnx`):

```xml
<Project Path="Projects/BotAIv2/BotAIv2.csproj" />
<Project Path="Projects/BotAIv2/mindedBots/BotMindAI.csproj" />
```

**Load them** — `Distribution/Data/assemblies.json`:

```json
[
  "UOContent.dll",
  "BotAIv2.dll",
  "BotMindAI.dll"
]
```

**Switch them on** — `Distribution/Configuration/modernuo.json`. `"bots.enabled": "True"`, then one switch per
module, each `"True"` or `"False"`:

```
bots.alarm.enabled      bots.auction.enabled    bots.baron.enabled      bots.classes.enabled
bots.craft.enabled      bots.dashboard.enabled  bots.debugger.enabled   bots.delve.enabled
bots.drill.enabled      bots.estate.enabled     bots.harvest.enabled    bots.hunt.enabled
bots.mend.enabled       bots.mind.enabled       bots.movement.enabled   bots.population.enabled
bots.shops.enabled      bots.spells.enabled     bots.squads.enabled     bots.will.enabled
bots.mind.thinking
```

**Copy the configuration** — `Distribution/Configuration/bot-*.json`. Every file is optional: leave one out and the
code's own defaults apply, and the file writes itself on first boot.

**Build and run**

```bash
dotnet build ModernUO.slnx -c Release
./start-shard-detached.ps1
```

The shard raises the population on start. Confirm it in the log:

```
Population raised: 80 bots at (1440, 1470, 0) on Felucca
```

**Where to start tuning** — `Distribution/Configuration/bot-population.json`:

```json
{
  "Map": "Felucca",
  "Home": [1440, 1470, 0],
  "Purse": 400,
  "Roam": 1000,
  "Classes": { "Gatherer": 12, "Warrior": 11, "Healer": 11, "Archer": 10, "Mage": 10, "WarriorMage": 8,
               "WarriorArcher": 8, "Crafter": 5, "Brawler": 1, "Captain": 1, "Baron": 1, "Architect": 1, "Sage": 1 }
}
```

`Classes` is the whole population: names come from the classes in `BotClasses/`, counts are yours. `Home` is where a
bot with no guild lives; a bot in a guild is born and raised again at its guild's seat.

> Config keys are **PascalCase**. A lower-case key is silently ignored and the default applies, with nothing in the
> log to say so.

**In game** — `[bots` opens the dashboard (administrator only), ten tabs.

**Stopping** — write `do save` into `Distribution/argus-in.txt`, wait for the answer in `argus-out.txt`, then
`taskkill /F /IM ModernUO.exe`. A kill without the save rolls the world back to the last autosave.

---

## 2. Thinking bots and the observers (Ollama)

Optional. The rest of the population is untouched either way, and if Ollama is not running the shard **does not
fail** — the minds log that they could not reach the model and those bots decide by arithmetic like everybody else.

**Install Ollama and pull the models**

```bash
ollama pull qwen3.5:9b
ollama pull deepseek-r1:14b
```

The first is the crafters' model, chosen for latency; the second is the observers'.

**The thinking crafters** — `Distribution/Configuration/bot-mind.json`. Which crafters think is a list of names:

```json
{
  "Model": "qwen3.5:9b",
  "Endpoint": "http://127.0.0.1:11434",
  "CrafterNames": ["Roderic", "Emeric", "Ulric", "Wulfric"]
}
```

The shipped file leaves `CrafterNames` out, so the five named in `BotMinds.CrafterNames` think — Roderic, Emeric,
Ulric, Wulfric and Alaric; `"CrafterNames": []` makes every crafter an ordinary bot. Without Ollama nothing breaks: the
calls fail and those crafters choose by arithmetic. `bot-minds.json` holds the rules each mind has written for itself;
it starts empty and the shard writes it.

**The observers** — `"bots.debugger.enabled": "True"`, and optionally `Distribution/Configuration/bot-debugger.json`
for the helpers' names, their robes and the thresholds. In game, `[argus` brings the lead observer to you; from the
keyboard, write a line into `Distribution/argus-in.txt`.

---

## Notes for a fork

- **`mindedBots/` is nested but is not part of `BotAIv2`.** `BotAIv2.csproj` excludes it explicitly
  (`<Compile Remove="mindedBots\**" />`). Without that exclusion an SDK project would compile those files into
  `BotAIv2`, and since `BotMindAI` references `BotAIv2`, the build would refuse the cycle.
- **Five stores are written under `Distribution/Saves/`**: `BotProgress` (what each bot learned and earned),
  `BotQuads` (what the population found out about the island), `BotClaims` (who owns which square), `BotSeats` (where
  each guild lives) and `BotWars` (wars, truces, clocks and the guilds' opinions of each other). Deleting one resets
  that and nothing else. The world save is not involved — it holds real characters; do not delete it.
- **A running shard holds the assemblies.** Building `BotAIv2` while it runs still reports compile errors; building
  anything under `mindedBots/` does not, because MSBuild skips it once its dependency's copy has failed. Stop the
  shard before trusting that build.
- **Everything is measured in the log.** Each subsystem prints a line naming every refusal separately. If something
  is not happening, the reason is usually already written down.
