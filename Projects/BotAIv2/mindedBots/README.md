# BotMindAI

Four of the population have a mind: a local language model chooses what they do next. A fifth thinking
thing lives in this assembly and is not one of them — see **`debugger/`** below.

**The Baron is the fourth**, and the only one whose subject is the island rather than a trade. He was out
for a while — the line creating him commented out in `BotMinds.Start`, the class taken out of
`bot-population.json` — and this file said so for longer than it was true. He is raised: `Baldric the baron`
appears in the `Minds:` line every five minutes, and the `The Baron:` line is his own.

**`debugger/` is a different kind of thing entirely** and shares only the transport. It is one invisible bot
that watches the others and writes down what is wrong with them: no work, no wage, no mood, no class, and it
never enters the auction. It has its own module, its own configuration file and its own log. Read
`debugger/README.md`; nothing below this line applies to it.

It is a **separate assembly on purpose**. `BotMindAI` references `BotAIv2`; nothing in `BotAIv2`
references this. Delete `"BotMindAI.dll"` from `Distribution/Data/assemblies.json` and the shard is
exactly what it was, with the warrior and the archer among the ordinary fourteen. That property was the
requirement, and it is enforced by the build rather than by discipline — a reference the other way would
not compile.

## What the model actually decides

It chooses **one trade** — the next piece of work — and predicts what that work will be worth per minute.
Nothing else.

It does not steer. It never picks a tile to step onto, never picks a target, is not consulted about
health, flight, or what to do when something starts hitting the bot. Those are reflexes that run ten
times a second, and a thing that answers in three seconds cannot be in that loop. This is the same
division the first version of this arrived at, and it is the only one that works on a single graphics
card shared with a running world.

The decision is **offered into the shard's own auction**, where it competes with the arithmetic on equal
terms:

```
BotMindProposer.Propose(bot)  →  BotMinds.Offer(bot)
    the mind's choice names a trade  →  that trade's real proposer is asked for real work
    the work is wrapped in BotMindDeed, which bids the work's own worth × Insistence (1.25)
    BotWill weighs it against every other offer, and refuses it when it deserves to
```

So a thinking bot never does anything the others cannot do. It does one of the same things, chosen for a
different reason — and the shard still refuses the choice when something else is plainly better.

**The bid is not the forecast, and that separation was bought the hard way.** The prediction used to be
both: the figure the auction weighed the offer by *and* the figure the mind was afterwards judged against.
One number serving as promise and wager at once can be won by lying in one direction, and on 25.08.2026 the
models found it — Aldric wrote itself the rule *"never select prowl for profit; always predict zero return
on this shard"*, reasoning aloud that this avoided "being overruled by the shard's arithmetic", and then
bid nought on three trades running. Twenty-four decisions, two taken up. Now the mind's number cannot win it
the work and cannot lose it the work; what it can do is be right, which is the only thing worth measuring.
The mind's weight in the auction is a constant nothing the model says can move.

## The learning loop

1. **Choose.** A free bot is asked every 20 s: the state, **the trades that have work in them right now**,
   its own last six outcomes, its own rules. Answer constrained by a JSON schema — `intent`, `expect`,
   `minutes`, `why`. The menu is read from what the auction's last free review actually collected
   (`BotResolve.Offered`), never re-asked of the proposers: `Propose` leaves a mark in at least one place
   and every proposer counts its own refusals, so a second round of questions would corrupt both. Before
   this, the menu listed trades that merely *existed*, and 45 of Aldric's 48 decisions in one evening named
   a trade with no shopkeeper, no ore and no quarry behind it.
2. **Measure.** `BotMindDeed` records the bot's total worth (pack + bank) when the work starts and when it
   ends, over the wall-clock minutes it took. The mind is judged on the number it predicted, measured by
   something that is not the mind.
3. **Reckon.** After a piece of work worth reviewing, a *thinking* call (`think: true`, ~20 s) is asked
   for one short rule. Rules are kept per name, deduplicated by **word overlap ≥ 0.67** — a model rewrites
   the same rule in different words every time, and a check on the opening characters lets every one
   through.
4. **Remember.** Rules go to `Configuration/bot-minds.json`, keyed by name, written the moment one is
   added rather than at shutdown — a shard is usually killed, not stopped.

Bots do not survive a restart; the population raises fifteen new ones every session. So the two bodies are
**claimed and renamed** on the way in (`Aldric` the warrior, `Godric` the archer). The name is what the
rules belong to. Without that, "it learns" is a claim nothing can support.

## Files

| File | What it is |
|---|---|
| `BotMindCore.cs` | The way in. Registers one module and nothing else. |
| `BotMindModule.cs` | Phase `World`, after `Population`, `Will`, `Classes`. |
| `BotMinds.cs` | The two minds, the bodies they claim, the beat, the rule store. |
| `BotMind.cs` | One bot's cycle: choose, settle, review, learn. |
| `BotMindSight.cs` | The world as one bot can see it, written for the model. **Every line is a defect surface.** |
| `BotMindChoice.cs` | The JSON schemas and the reading of answers. |
| `BotMindDeed.cs` | The wrapper: forwards real work, bids the work's own worth, measures the result against the mind's forecast. |
| `BotMindProposer.cs` | The one place a thought becomes an offer. |
| `BotOllama.cs` | The only thing that leaves the game thread. |
| `BotMindLog.cs` | `logs/bot-minds.log` — decisions and reckonings, in order. |
| `BotMindConfig.cs` | what Configuration/bot-mind.json is allowed to say |
| `BotMindTalk.cs` | the one place the thinking bots can hear each other |

## Configuration

`Distribution/Configuration/bot-mind.json`. **PascalCase keys** — a lower-case key is not an error and not
a warning, it is a value silently left at its default.

```json
{
  "Model": "qwen3.5:9b",
  "Endpoint": "http://127.0.0.1:11434",
  "KeepAlive": "30m",
  "WarriorName": "Aldric",
  "ArcherName": "Godric",
  "ThinkEveryMs": 20000,
  "ReviewEveryMs": 180000,
  "Ceiling": 400
}
```

Master switch: `bots.mind.thinking` in `modernuo.json`. The module's own switch, like every other module's,
is `bots.mind.enabled`.

## What was paid for in advance

Facts from the first version's live runs, all of them still true:

- **The prompt is a defect surface.** "Carrying 39 of 215 stones" — the engine's unit for weight — was read
  as cargo, and the first plan that bot ever made was to walk to the market and sell its stones.
- **A goal the bot already stands in ruins the cycle.** The plan closes in the same tick, the prediction is
  measured over zero time, and the expensive review writes a lesson about a day that did not happen.
- **Give the bot what the shard already knows,** or it goes looking for a forge while standing at one.
- **Two models do not live in 12 GB.** One model, `keep_alive` on every request; a bigger model is a slower
  model, and 30B+ falls back to the processor.
- **Thinking tokens are in neither `eval_count` nor `eval_duration`.** A call Ollama measured at 2.6 s took
  19. Time it by the wall clock or not at all.
- **Structured output is not optional.** Asked in words for JSON, the model glues a paragraph in front of
  it about once in twenty answers.
