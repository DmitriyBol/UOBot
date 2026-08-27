# BotAI v2

An autonomous bot population for ModernUO, second version. A **separate assembly**: zero edits to the core, and
nothing in the engine references this project.

## Read in this order

| Document | What it answers |
|---|---|
| **`ARCHITECTURE.md`** | how the whole thing is put together, the vocabulary, how to add work, and the engine facts it is built on. **Start here.** |
| **`BUILD.md`** | how to build it against the fork and what the boot log should say, line by line |
| **`HANDOFF.md`** | the state of the work: what is done, what is known to be missing, what the last review found |
| `<Subsystem>/README.md` | why each individual decision in that subsystem is the way it is |

Every non-obvious decision in this project has its reasoning written next to it, in the file it belongs to. That
is deliberate and it is the main thing that makes the project maintainable: the design can be re-derived from the
documents without asking anybody.

## The folders

```
botAiv2/
  BotCore.cs        — the entry point: Configure() and Initialize(), called by the server through reflection
  BotAIv2.csproj    — builds BotAIv2.dll
  BotModules/       — the loading frame: phases, declared dependencies, a switch on each module
  BotClasses/       — the classes: what a bot is
  BotOutfit/        — the kit and the bind: what a bot holds and what cannot be taken from it
  BotMovement/      — path, road and step: search, pockets of ground, the queue of errands
  BotCombat/        — is it worth stopping: strength, threat, the decision
  BotSquad/         — squads: leader, formation, scouting, dividing spoils (written, unwired)
  BotBaron/         — the Baron: harrows ground that has killed people, and is paid nothing for it
  BotWill/          — what a bot does and why: ladder, obligations, auction, ledger
  BotHarvest/       — dig, smelt, bank it
  BotShops/         — trading with the NPCs, both ways. Where the world's money enters and leaves
  BotCraft/         — buy cloth, sew, sell
  BotAuction/       — the bots' own market, both sides: stalls and funded wants
  BotSpells/        — write scrolls, fill a book
  BotHunt/          — close, fight, loot. The only faucet of gold
  BotMend/          — mending: yourself above all else, others as ordinary work
  BotPopulation/    — the bot itself and the beat: who exists and when they are asked
  BotDashboard/     — [bots: three tabs onto the population, its market and its shortages
```

**Layout rule:** one subsystem, one folder, and its own configuration file lives in it. In v1 every dial on the
shard lived in one `bots.json`, so editing an archer's aim was editing the file that sets the population size, and
a typo in either half killed everything.

**Coupling rule:** subsystems may read each other's **data** and may not call each other's **decisions**.
`BotOutfit` knows what a `BotClass` is because a class is a description; it never asks a class what a bot should
do.

**Wiring rule:** a new subsystem gets its folder, its module descriptor and one line of registration in
`BotCore`. The order of that line means nothing — a module declares what it needs ready and the loader derives the
sequence. See `BotModules/README.md`.

Each module has its own switch: `bots.<name>.enabled` in `modernuo.json`. Not for flexibility but for diagnosis —
halving the number of running modules is faster than reading a 37 MB log.

## Installing it

**In full, with everything verified against the fork: `BUILD.md`.** In brief:

1. Put the folder into the fork as `Projects\BotAIv2` — beside `UOContent`, `Server` and `Logger`. The paths in
   the `.csproj` are relative and assume exactly that depth; one level deeper and every path needs another `..\`.
2. Add `<Project Path="Projects/BotAIv2/BotAIv2.csproj" />` to `ModernUO.slnx` — needed if you build from the
   root, optional if you build the csproj directly.
3. Build: `dotnet build Projects\BotAIv2\BotAIv2.csproj -c Release`. The output lands in
   `Distribution\Assemblies`.
4. Add `"BotAIv2.dll"` to `Distribution\Data\assemblies.json` (which currently lists only `UOContent.dll`).
5. Start the shard. Configuration files that do not exist yet write themselves on first boot — look in
   `Distribution\Configuration`.

The whole thing is switched off by one setting: `bots.enabled` in `modernuo.json`.

## What you get on a first run

Four bots appear in Britain, dress themselves, take on work and go and do it. They dig ore, smelt it and put the
metal on the market; the crafter buys cloth and sews; the mage writes scrolls and fills its own book; the healer
buys the spells it cannot write; anybody who is hurt patches itself up; and whoever is whole enough goes looking
for something to fight, which is where every coin in the world comes from.

`[bots` shows all of it, one row per bot, including the vector each one is developing along.

**What you will not see:** squads (nothing musters one, by decision), armour (there is none in the kit), spells
cast in combat (nothing casts offensively), or anything surviving a restart (the population is rebuilt from
configuration on every world load).

## The state of verification

**It builds clean** against a clone of the fork: `0 Warning(s), 0 Error(s)` with `TreatWarningsAsErrors=true`,
which in this fork means any warning is a shard that does not build. Every engine call was read out of the fork's
own source before it was written.

**It has never been run.** The development machine has no client files (maps, art) and the server will not come up
without them — so everything in these documents about *behaviour* is reasoning rather than observation. What is
known to be true is what the compiler and the fork's source say.

The numbers are the other unverified thing, and they are unverified separately from the code: the rate of 500 gold
a skill point, the ×1.25 margins, 0.8 on crowding, and every trade's per-minute prior. Those are where to start
from, not what is correct. `HANDOFF.md` lists them.
