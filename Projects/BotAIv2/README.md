# UOBot — autonomous bots for ModernUO

Eighty bots that live on a Renaissance-era [ModernUO](https://github.com/modernuo/ModernUO) shard with nobody
driving them. On every turn each bot prices every piece of work the shard can offer it, takes the best and keeps
at it until it is done or something actually happens. The prices are learned from what the work really paid, so a
trade that stops paying stops being chosen without anybody editing a number.

The bots are a separate assembly that ModernUO loads beside its own content. The engine itself changes in three
files, through the small patches in `Projects/BotAIv2/engine-patches/`.

## What the bots do

- **Earn a living.** They mine and smelt ore, fell trees and pick reagents. They forge weapons and armour, sew
  leather, brew potions, fletch arrows, cook and write spell scrolls. They sell to NPC shopkeepers and run a market
  of their own: stalls, and money put down for what they cannot make.
- **Fight and look after each other.** They hunt the island's creatures and carve them, bind their own wounds and
  their friends', and hire a healer to stand by them. They answer a call for help, band into companies for what one
  bot cannot take, and go down into dungeons as a party.
- **Get better.** Skills rise with use and survive a restart, and a Captain and a Sage teach classes for a fee.
- **Form guilds and go to war.** Bots found their own guilds. A guild raises a hall, claims land a square at a time
  and keeps a chest. Guilds go to war under rules that end the war: 25 dead or 5,000 gold of plunder wins, then a
  day's truce.
- **Commit crimes, and pay for them.** A bot that kills another bot goes red. Murderers found The Shadow, a band of
  thieves with a hideout and a fence, who rob from ambush. The city puts prices on heads, law-abiding bots hunt
  murderers down, the Baron raids the hideout, and whoever is caught sits in a cell.
- **Take errands.** A board offers paid errands (kill, deliver, scout), and once a week there is a championship in
  a ring.

The log records every choice, with the price it won at and the offer it beat. Every five minutes a summary accounts
for each mechanism. Argus is an optional invisible observer. It uses a local model through
[Ollama](https://ollama.com) to watch the population and report what it thinks is wrong. Nothing else needs a model.

## Quick start

You need the .NET 10 SDK, git, the Ultima Online Classic client files and a client such as ClassicUO.

1. **Clone ModernUO and this repository side by side.** The bots are built against ModernUO `be3a085`. Newer
   ModernUO has moved its serialization packages to 4.x, and the bots do not build against those yet.
   ```bash
   git clone https://github.com/modernuo/ModernUO.git
   git -C ModernUO checkout be3a085
   git clone https://github.com/DmitriyBol/UOBot.git
   ```
2. **Copy the bots in and patch the engine.**
   ```bash
   cp -r UOBot/Projects UOBot/Distribution ModernUO/
   cd ModernUO
   git apply Projects/BotAIv2/engine-patches/*.patch
   ```
3. **Register the two assemblies.** Add them to `ModernUO.slnx` beside the other projects:
   ```xml
   <Project Path="Projects/BotAIv2/BotAIv2.csproj" />
   <Project Path="Projects/BotAIv2/mindedBots/BotMindAI.csproj" />
   ```
   Then list them after `UOContent.dll` in `Distribution/Data/assemblies.json`:
   ```json
   ["UOContent.dll", "BotAIv2.dll", "BotMindAI.dll"]
   ```
4. **Build.**
   ```bash
   dotnet build ModernUO.slnx -c Release
   ```
5. **Start it once and answer its questions**: the path to the client files, the expansion (type **`UOR`**) and the
   owner account. Enter accepts the default for everything else. On Windows, run this from PowerShell or cmd: the
   Git Bash window is not a real console, so the server cannot ask its questions there.
   ```bash
   cd Distribution
   dotnet ModernUO.dll
   ```
   The bots are alive once the log says `Population raised: 80 bots`.
6. **Give them a world.** Log in as the owner at `127.0.0.1:2593` and type `[Decorate`, then
   `[ImportSpawners Data/Spawns/shared/felucca/*.json`, then `[Save` and `[Restart`. These put in the shops, forges
   and creatures, and the bots survey them after the restart. `[bots` opens the dashboard.

## Read more

The documents live in `Projects/BotAIv2/`:

| | |
|---|---|
| `GUIDE.md` | the long read: every kind of work, the classes, how a bot decides, guilds, war, crime, the economy, and running and watching a shard |
| `INSTALL.md` | installing into an existing fork, the switches, the engine patches, Argus |
| `MAP.md` | where anything is, file by file |
| `DIALS.md` | every tunable number and the config key that reaches it |
| `DECISIONS.md` | what was decided and tried, and the defects that keep coming back |

New work is written in the fork, [DmitriyBol/ModernUO-fork](https://github.com/DmitriyBol/ModernUO-fork), and comes
here after it has run on a live shard. The paths here match the paths there. The code in this repository keeps only
its class summaries, and the reasoning is in the documents.
