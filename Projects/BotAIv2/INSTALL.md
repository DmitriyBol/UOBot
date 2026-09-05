# BotAI — installation

Autonomous bots for [ModernUO](https://github.com/modernuo/ModernUO). Two assemblies, and the second one is
optional:

| | |
|---|---|
| **BotAIv2** | the population: trades, combat, market, squads, the quadrant map. No external dependencies. |
| **BotMindAI** | `mindedBots/` — a few of those bots think with a local LLM instead of pure arithmetic. Needs [Ollama](https://ollama.com). |

`BotMindAI` references `BotAIv2` and never the other way round. You can ship the first without the second;
you cannot ship the second alone.

> **One engine patch is required.** `engine-patches/CraftItem-heat-source.patch` adds seventeen lines to
> `Projects/UOContent/Engines/Craft/Core/CraftItem.cs`, exposing two questions the engine already answers
> privately — is this tile a fire, is this mobile standing near one. The ground survey asks those rather than
> keeping a second copy of the engine's heat table. Apply it with `git apply` from the fork root before
> building, or `BotAIv2` will not compile.

---

## Requirements

| | |
|---|---|
| Server | ModernUO, .NET 10 SDK |
| **Expansion** | **Renaissance (UOR)** — `"core.expansion": "UOR"` in `Distribution/Configuration/modernuo.json` |
| Client | any UO client ModernUO supports; ClassicUO was used throughout |
| Ollama | only for the thinking bots — see below |

The era matters. Every weapon, spell circle, armour rating and crafting recipe the bots reason about is read
off the shard's own tables for **this** expansion. On a later expansion the code still builds and the numbers
stop meaning what they say.

---

## 1. Ordinary bots

**Copy the projects**

```
Projects/BotAIv2/                 → your ModernUO checkout, same place
```

**Add them to the solution** (`ModernUO.slnx`):

```xml
<Project Path="Projects/BotAIv2/BotAIv2.csproj" />
```

**Copy the configuration** — `Distribution/Configuration/bot-*.json`. Every file is optional: leave one out
and the code's own defaults apply.

**Build and run**

```bash
dotnet build Projects/BotAIv2/BotAIv2.csproj
```

The shard raises the population on start. Confirm it in the log:

```
Population raised: 34 bots at (1440, 1470, 0) on Felucca
```

**Where to start tuning** — `Distribution/Configuration/bot-population.json`:

```json
{
  "Map": "Felucca",
  "Home": [1440, 1470, 0],
  "Purse": 400,
  "Roam": 500,
  "Classes": { "Warrior": 10, "Gatherer": 2, "Mage": 3, "Healer": 3, "Captain": 1, "Baron": 1 }
}
```

`Classes` is the whole population: names come from `bot-classes.json`, counts are yours.

> Config keys are **PascalCase**. A lower-case key is silently ignored and the default applies, with nothing
> in the log to say so.

**In game** — `[bots` opens the dashboard (administrator only): the population, their market, what they are
short of, what the city wants, what they have learned, and the island's quadrant map.

---

## 2. Thinking bots (Ollama)

Optional. Four bots by default get a language model behind their decisions; the rest of the population is
untouched.

**Install Ollama and pull a model**

```bash
ollama pull qwen3.5:9b
```

Any instruct-tuned model works. The 9B was chosen for latency: a bot decides every 20 seconds, and a model
that takes longer than that simply misses its turn.

**Copy the project**

```
Projects/BotAIv2/mindedBots/      → alongside BotAIv2, inside it
```

**Add it to the solution**

```xml
<Project Path="Projects/BotAIv2/mindedBots/BotMindAI.csproj" />
```

**Copy the configuration** — `bot-mind.json` (settings) and `bot-minds.json` (what each mind has learned;
starts empty and is written by the shard).

**Build**

```bash
dotnet build Projects/BotAIv2/mindedBots/BotMindAI.csproj
```

Confirm it in the log:

```
Aldric has a mind of its own now: it was Gerda 2, a Captain, and is thinking with qwen3.5:9b
```

**Settings** — `Distribution/Configuration/bot-mind.json`, all optional:

```json
{
  "Model": "qwen3.5:9b",
  "Endpoint": "http://127.0.0.1:11434",
  "ThinkEveryMs": 20000,
  "WarriorName": "Aldric",
  "ArchitectName": "Godric",
  "SageName": "Cedric",
  "BaronName": "Baldric"
}
```

If Ollama is not running the shard **does not fail** — the minds log that they could not reach the model and
those bots decide by arithmetic like everybody else. That is the intended fallback: a missing LLM should cost
you four opinions, not a shard.

---

## Notes for a fork

- **`mindedBots/` is nested but is not part of `BotAIv2`.** `BotAIv2.csproj` excludes it explicitly
  (`<Compile Remove="mindedBots\**" />`). Without that exclusion an SDK project would compile those files
  into `BotAIv2`, and since `BotMindAI` references `BotAIv2`, the build would refuse the cycle.
- **Two save files** are written under `Distribution/Saves/`: `BotProgress` (what each bot learned and
  earned) and `BotQuads` (what the population found out about the island). Deleting either resets that and
  nothing else. The world save is not involved — do not delete it.
- **Everything is measured in the log.** Each subsystem prints a line naming every refusal separately. If
  something is not happening, the reason is usually already written down.
