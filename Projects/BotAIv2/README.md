# UOBot — autonomous bots for ModernUO

## Work, and how much of it gets finished

Every piece of work the bots took on, on the live shard on 26 September 2026 between 13:16 and 14:49 (builds 259–263:
the world awake round the bots as round a player, and from 14:32 the two-level pathfinding chart). "Finished" is
finished ÷ (finished + failed + dropped); dropped work is mostly work put down to run from a fight. Deaths are
counted apart. The shard's target is 95 % finished.

| Work | What it is | Taken on | Finished | Finished share | Died doing it |
|---|---|---:|---:|---:|---:|
| restock | buying what its class must carry — bandages, bottles, arrows, reagents, tools | 758 | 639 | 87 % | 9 |
| flee | running from a fight it is losing | 443 | 223 | 62 % | 80 |
| prowl | walking out to ground where there is something to fight | 424 | 355 | 86 % | 0 |
| rescue | going to the help of a bot under attack | 310 | 194 | 64 % | 3 |
| hunt | killing a creature for its loot and hide | 306 | 192 | 67 % | 6 |
| mine | digging ore | 299 | 239 | 90 % | 8 |
| ward | telling a stranger hunting the guild's land about the toll | 279 | 278 | 100 % | 0 |
| rally | joining its guild's war company | 230 | 72 | 73 % | 52 |
| peddle | selling goods to an NPC shopkeeper | 157 | 128 | 86 % | 1 |
| mend | healing itself or another bot | 129 | 40 | 34 % | 8 |
| unload | taking a full pack to a stall or a counter | 123 | 104 | 89 % | 0 |
| cook | cooking meat at a fire | 109 | 87 | 88 % | 1 |
| stake | standing on a square its guild is claiming | 99 | 32 | 67 % | 33 |
| order | putting money down on the market for what it cannot make | 97 | 97 | 100 % | 0 |
| herbs | picking reagents | 93 | 61 | 69 % | 3 |
| acquire | buying off the market | 92 | 82 | 89 % | 0 |
| reclaim | going back to its own corpse for its things | 85 | 56 | 68 % | 1 |
| forage | picking up reagents lying about | 74 | 64 | 86 % | 0 |
| glean | picking up its spent arrows | 72 | 62 | 89 % | 1 |
| supply | stocking its guild's counter from the shops | 70 | 36 | 56 % | 2 |
| forge | smithing weapons and armour | 56 | 53 | 95 % | 0 |
| pickings | going through a corpse | 44 | 44 | 100 % | 0 |
| chop | felling trees | 37 | 34 | 100 % | 1 |
| harrow | a guild's or the Baron's company clearing a deadly square | 32 | 6 | 26 % | 0 |
| brew | brewing potions | 29 | 20 | 83 % | 3 |
| homeward | walking home | 29 | 22 | 79 % | 0 |
| escort | seeing another bot safely somewhere | 23 | 15 | 94 % | 1 |
| quarrel | fighting a member of a guild it is at war with | 16 | 1 | 7 % | 2 |
| enlist | joining a Captain's company | 15 | 2 | 18 % | 0 |
| sew | tailoring leather and cloth | 15 | 15 | 100 % | 0 |
| fletch | making arrows and bows | 12 | 11 | 100 % | 1 |
| evict | moving a stranger out of its guild's hall | 10 | 9 | 100 % | 0 |
| other | ten rarer kinds: sweeps, house calls, bands, errands, scrolls, rescues from a cell, delves, scouting, plunder, skulking | 30 | 23 | 70 % | 3 |
| **all work** | | **4,597** | **3,296** | **79 %** | **219** |

## Working on now — 26 September 2026

- **Pathfinding in tiers.** A bot's walk was one search from where it stood, cut off after 60 to 150 ms. It could not
  see a way round longer than that, such as a river whose bridge is a thousand steps off, and 38 % of searches ended
  short. Since build 263 a coarse chart does the far part. The chart is a grid of 16-tile squares with the gates
  between them, drawn in 0.3 s of the game loop at boot. It routes every walk longer than 24 tiles, and the search
  only walks the next short leg. In the first ten minutes path search cost nine times less (1.35 s a minute against
  12.5), and 7 % of searches ended short. Now being built into the engine: the full three-tier navigation, so that
  creatures can use it too.
  - **Short:** the precise tile search.
  - **Medium:** hierarchical A* over the chart, with floors and bridges kept apart.
  - **Long:** 128-tile regions, dungeon teleporters and a route cache.
- **The world awake.** Creatures act only near a player's client. Bots have none, so the island slept round them, and
  the shard's balance had been tuned against sleeping creatures. Each bot now wakes the ground round it as a client
  would.
- **Supplies before a fight.** Nobody goes into a dungeon, or joins a war company on somebody else's ground, without
  its class's bandages, bottles, arrows and reagents. The guild's chest pays for what a bot cannot afford.
- **A guild's land.** The yard is the square the hall stands in. Ground belongs to a guild only while it reaches the
  hall through the guild's own squares, and losing the hall's square razes the hall. A guild clears the creatures
  round its own hall with its own company, as a duty.
- **Danger.** Bots stay out only of ground where the hostile strength is three times theirs or more. Before, they
  stayed out of anywhere bots had died lately.
- **Tournament** every hour, with the prizes on a Fibonacci ladder: one prize in 142 is the top one, and most are a
  purse.
- **Proving ground.** Argus measures a bot's real strength by fighting a copy of it in Green Acres.

## What it is

Bots living on a Renaissance-era [ModernUO](https://github.com/modernuo/ModernUO) shard with nobody driving them:
fifty at first, and two newcomers every two hours up to 120. On every turn each bot prices every piece of work the
shard can offer it, takes the best and keeps at it until it is done or something actually happens. The prices are
learned from what the work really paid, so a trade that stops paying stops being chosen without anybody editing a
number.

The bots are a separate assembly that ModernUO loads beside its own content. The engine itself changes in four
files, through the small patches in `Projects/BotAIv2/engine-patches/`.

### What the bots do

- **Earn a living.** They mine and smelt ore, fell trees and pick reagents. They forge weapons and armour, sew
  leather, brew potions, fletch arrows, cook and write spell scrolls. They sell to NPC shopkeepers and run a market
  of their own: stalls, and money put down for what they cannot make.
- **Fight and look after each other.** They hunt the island's creatures and carve them, bind their own wounds and
  their friends', and hire a healer to stand by them. They answer a call for help, band into companies for what one
  bot cannot take, and go down into dungeons as a party.
- **Get better.** Skills rise with use and survive a restart, and a Captain and a Sage teach classes for a fee.
- **Form guilds and go to war.** Bots found their own guilds. A guild raises a hall, claims land a square at a time
  and keeps a chest. Guilds go to war under rules that end the war: 25 dead or 5,000 gold of plunder wins.
- **Commit crimes, and pay for them.** A bot that kills another bot goes red. Murderers found The Shadow, a band of
  thieves with a hideout and a fence, who rob from ambush. The city puts prices on heads, law-abiding bots hunt
  murderers down, the Baron raids the hideout, and whoever is caught sits in a cell.
- **Take errands.** A board offers paid errands (kill, deliver, scout), and every hour there is a championship in
  a ring.

The log records every choice, with the price it won at and the offer it beat. Every five minutes a summary accounts
for each mechanism. Argus is an optional invisible observer. It uses a local model through
[Ollama](https://ollama.com) to watch the population and report what it thinks is wrong. Nothing else needs a model.

## Setting it up

You need the .NET 10 SDK, git, the Ultima Online Classic client files and a client such as ClassicUO.

1. **Clone this repository and run the setup script.** The script clones ModernUO into `../ModernUO`, at commit
   `be3a085`, which the bots are built against. Then it copies the bots in, applies the engine patches, registers the
   two bot assemblies and builds everything.
   ```bash
   git clone https://github.com/DmitriyBol/UOBot.git
   cd UOBot
   ./setup.sh
   ```
   On Windows, the same from PowerShell:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\setup.ps1
   ```
   To put ModernUO somewhere else, give the folder: `./setup.sh ~/shards/ModernUO` or
   `.\setup.ps1 -Target D:\ModernUO`. After a `git pull`, run the script again: it copies the new files over, skips
   what is already done and rebuilds.
2. **Start it once and answer its questions**: the path to the client files, the expansion (type **`UOR`**) and the
   owner account. Enter accepts the default for everything else. On Windows, run this from PowerShell or cmd: the
   Git Bash window is not a real console, so the server cannot ask its questions there.
   ```bash
   cd ../ModernUO/Distribution
   dotnet ModernUO.dll
   ```
   The bots are alive once the log says `Population raised: 50 bots`.
3. **Give them a world.** Log in as the owner at `127.0.0.1:2593` and type `[Decorate`, then
   `[ImportSpawners Data/Spawns/shared/felucca/*.json`, then `[Save` and `[Restart`. These put in the shops, forges
   and creatures, and the bots survey them after the restart. `[bots` opens the dashboard.

<details>
<summary>The same by hand, without the script</summary>

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
   `Distribution/Data/assemblies.json`, which the copy brought in, already lists them after `UOContent.dll`.
4. **Build.**
   ```bash
   dotnet build ModernUO.slnx -c Release
   ```

</details>

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
