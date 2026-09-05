"""Rebuilds MAP.md's subsystem blocks and the whole of DIALS.md from this folder's own source.

Run from this directory:  python regen-map.py

Everything between the SECTION2 markers in MAP.md is replaced; nothing else is touched.

Each block is half generated and half kept here:

* generated from the code — the module class, the configuration file, the work the subsystem offers (every
  `IBotProposer` with the rung it offers on and the deeds it hands out), and the file table, whose row text is
  the first sentence of that file's own class summary;
* kept in BLOCKS below — the paragraph saying what the subsystem is for, and the traps worth knowing before
  touching it.

DIALS.md is wholly generated: every `public static X Y { get; set; } = default;` in the assembly, with the
default it was written with and the configuration key that overrides it where one exists. It is kept out of
MAP.md so that the map stays cheap to read and a number stays one file away.

Written this way because the two halves rot differently. Anything derived from the code cannot drift, and the
prose is small enough to keep true by hand — which the subsystem READMEs, written once and left, are not: as
of 05.09.2026 several of them describe a shard that no longer exists.
"""

import json
import os
import re

BEGIN = "<!--SECTION2:BEGIN-->"
END = "<!--SECTION2:END-->"

ROOT = "(root)"

# Reading order: the way in, the clock, the decision, then everything a decision reaches for, then the
# subsystems that watch rather than act.
ORDER = [
    ROOT, "BotModules", "BotPopulation", "BotWill", "BotMovement", "BotClasses", "BotOutfit",
    "BotHarvest", "BotCraft", "BotHunt", "BotShops", "BotAuction", "BotSpells",
    "BotCombat", "BotMend", "BotSquad", "BotDrill", "BotBaron", "BotQuad", "BotRanger",
    "BotDashboard", "mindedBots", "mindedBots/debugger",
]

# folder -> (title, what it is, [traps])
BLOCKS = {
    ROOT: (
        "the way in",
        "One file. `BotCore.Configure` and `BotCore.Initialize` are found by the engine's reflection the same "
        "way it finds content, and everything this assembly does hangs off them: the stores that must exist "
        "before the world is read, the regeneration hooks, and the registration of every module in the order "
        "they are listed there.",
        ["`BotCore.Configure` runs after `RegenRates.Configure` by loader ordering rather than by contract. "
         "If that ever inverts, meals stop quickening recovery — they do not break anything."],
    ),
    "BotModules": (
        "the loading frame",
        "Holds the subsystems, works out what order to start them in, starts them, and says what happened. "
        "Two phases: `Settings` runs before the world exists and may only read numbers and files; `World` "
        "runs after it is in memory and may ask where things are. A module can be switched off from its own "
        "configuration file without touching anything else.",
        ["A subsystem that asks the map a question in the `Settings` phase gets a world that is not there "
         "yet. Phase is the first thing to check when something is null only at boot."],
    ),
    "BotPopulation": (
        "what a bot is, and the clock",
        "`BotMobile : PlayerMobile` is the bot. One timer serves the whole population and gives each bot a "
        "turn on its own schedule — `BotBeat`, which is also where four of the summary lines are written. "
        "This folder also owns what survives a restart (`BotProgress`: skills, fame, karma, savings) and the "
        "three pieces of work that belong to no trade: walking home, going back for what death took, and "
        "taking a full pack to a counter.",
        ["A bot must be Player-flagged or death deletes it outright, and a dead bot counts as alive and "
         "silently drops out of the beat.",
         "`BotProgress` is the only place skills persist. Zeroing it is not the same as touching the world "
         "save, and the world save holds Patrick's own character — never delete it."],
    ),
    "BotWill": (
        "the decision",
        "Every turn a bot asks one question and this answers it. Three stages: the ladder says which rung the "
        "bot is on from facts alone, obligations are taken before anything is auctioned, and then every "
        "proposer registered by every other subsystem offers work which is priced per minute and the best "
        "offer wins. Prices are *measured* — `BotLedger` corrects a trade's opening guess by what the work "
        "really paid — rather than assigned by weights. Every `took on` / `finished` / `failed at` line in "
        "the log is written here, and the factors printed beside the price are the whole reckoning.",
        ["A multiplier that can reach zero is a veto: every factor needs a floor.",
         "A deed that answers `Work` for ever, or answers with the same walk for ever, is immortal and "
         "invisible. Both have happened; both cost a session.",
         "The walk order a deed returns must be stable between ticks, because arrival is compared against "
         "it. A destination recomputed every tick resets the route every tick."],
    ),
    "BotMovement": (
        "path, road and step",
        "Getting a bot from where it is to where the work is. The most expensive part of the assembly and the "
        "one with the longest history of measured defects — `RESEARCH.md` beside it is the analysis the "
        "current design came out of. A journey is a destination, a queue of things put aside to do first, and "
        "the plan currently being walked.",
        ["A `Point3D` whose Z came out of arithmetic is a place nothing can stand on: it works on flat ground "
         "and fails on a hill. Settle it against the map.",
         "Arriving is judged by asking the engine whether the work would be accepted here, never by the "
         "distance to a remembered point."],
    ),
    "BotClasses": (
        "what a kind of bot is",
        "Thirteen classes, one file each, plus a registry. A class is **a description and a set of limits with "
        "no behaviour** — it decides nothing and commands nobody. `BotWill` reads a class the way it reads "
        "the map: as a fact about the world. Four of the thirteen cast; three produce; the rest fight, shoot "
        "or heal.",
        ["Adding a class means a file here and a line in the registry. Nothing else in the assembly holds a "
         "list of permitted archetypes, and nothing should — the tool a bot carries is what decides which "
         "trades it is offered."],
    ),
    "BotOutfit": (
        "what a bot owns",
        "Turns a class's kit into items actually in a pack, decides what death has no right to take, and owns "
        "the parts of a bot that are property rather than behaviour: its bond with a weapon, its harness, its "
        "armour, and its horse.",
        ["`Mobile.EquipItem` does not replace an occupied layer, it refuses. Whatever wants a hand has to "
         "free it first, and has to put back what it took."],
    ),
    "BotHarvest": (
        "ore, wood, herbs, and the survey",
        "Where raw material comes from, and — more used than the digging — the survey of the ground that the "
        "whole population navigates by. `BotGround` sweeps the tiles around wherever bots go and remembers "
        "four things: seams of ore, forges with an anvil beside them (`Fires`), every kind of fire the "
        "engine will cook over (`Hearths`), and counters. Everything that needs a *place* asks this.",
        ["The survey is built by walking and is not saved. A restart empties it — which silently cures "
         "\"the island has run out of ore\" and makes any measurement about exhaustion meaningless for the "
         "first two or three hours.",
         "An axe must be worn for the engine to accept a swing; a pick works from the pack. That difference "
         "is why mining worked from the first day and no log was ever cut."],
    ),
    "BotCraft": (
        "turning materials into goods",
        "Five trades that make things: the smith at a forge, the tailor out of leather or cloth, the "
        "alchemist over a bottle, the fletcher out of shafts and feathers, and the cook at any fire. Each is "
        "a proposer that decides whether there is a piece of work, and a deed that walks to the place, swings "
        "until something comes of it, and puts the result either into the order it was made for or onto the "
        "market. `BotCraftwork` is the shared half — choosing a recipe, swinging, counting what appeared.",
        ["Crafting is asynchronous: `CraftItem.Craft` starts a timer. Count what the *last* swing produced at "
         "the top of the next tick, never after the swing you just made.",
         "The engine refuses in silence, because it answers a refusal by sending a message to a screen the "
         "bot does not have. Requirements live on the *recipe* (`SetNeedHeat`, `SetNeedOven`, `SetNeedMill`), "
         "not on the system's `CanCraft`.",
         "A failed attempt eats half the material. A round set up for exactly one item is a round that ends "
         "in `out of metal` after two misses — see `BotAnvil.Tries`."],
    ),
    "BotHunt": (
        "the only new gold in the world",
        "Everything else on this shard moves money about; this brings it in. Choosing what is worth fighting, "
        "prowling for it, killing it, carving it and going through what it left. Carving is folded into "
        "looting rather than made a choice, because the engine charges nothing for it and there is no "
        "decision in it worth a bot's turn.",
        ["Everything lifted off a corpse is listed for sale except the bot's own ammunition and a cook's "
         "raw meat. Anything else a trade needs as *input* will be sold before that trade ever sees it."],
    ),
    "BotShops": (
        "buying and selling over a counter",
        "A capability for every bot rather than a trade of its own: reagents for a caster, bandages for "
        "anybody, metal for a smith, and whatever the population would not buy goes back over the same "
        "counter for coin. Also the board of standing orders — what a bot has put money down for and has not "
        "received yet.",
        ["The engine pays out of the pack, and a bot only walks to a bank above a threshold. Three files "
         "once closed on each other so that money existed and could not be spent."],
    ),
    "BotAuction": (
        "the bots' own market",
        "Both sides of trade between bots. A stall is a standing offer of one kind of thing at one price; a "
        "want is money already down for something a bot needs. Prices move on what actually sold and what "
        "actually got filled, not on a table. This is where the crafts sell to and where the orders that make "
        "crafting worth doing come from.",
        ["A stall and a want can both be healthy and never meet. When trade is low the question is not "
         "whether either side works but whether there is an edge between them."],
    ),
    "BotSpells": (
        "a book that grows",
        "Scrolls, reagents and spellbooks: the first work in the project whose output no shopkeeper sells, "
        "and the first buyer on the bots' market that is itself a bot. A caster wants spells it does not "
        "have, a scribe makes them, and the armoury decides what a book is short of.",
        [],
    ),
    "BotCombat": (
        "is it worth stopping",
        "The fight itself belongs to the engine — `Warmode` and `Combatant` are enough, and its own skill "
        "checks train skills exactly as they do for a player. What is here is the judgement around it: is "
        "this thing worth fighting, is this fight being lost, is somebody calling for help, and how a shooter "
        "keeps its distance.",
        ["Three separate reasons a bot fails to land a blow on something it is standing next to — no line to "
         "it, a shooter that moved too recently, a broken cast — and none of the three says anything in the "
         "log unless a counter is put there."],
    ),
    "BotMend": (
        "bandages",
        "The smallest subsystem here, and the only thing that puts a bot on the `Failing` rung. A bot binds "
        "its own wounds, and a surgeon binds somebody else's.",
        [],
    ),
    "BotSquad": (
        "standing companies",
        "A leader, a few followers, a formation, and dividing what the fight left. Companies are formed from "
        "three places — a hunt that found something too big for one, a patrol, and the Baron's harrowing — "
        "and dissolve when there is nothing left to fight. This folder also *assembles* five of the summary "
        "lines out of counters that mostly live elsewhere.",
        ["A bot on the `Bound` rung takes no work of its own: the auction is switched off for it. A company "
         "with no charge is therefore a company of idle bots.",
         "The subsystem's own README still says nothing calls `BotSquads.Form`. It has three callers and "
         "companies form on every session."],
    ),
    "BotDrill": (
        "the captain",
        "One class exists for the others rather than for itself. A captain holds a field, teaches whoever "
        "pays the fee, marches a company at ground the danger map says is bad, and puts armour orders on the "
        "board for bodies that have none.",
        ["A lesson is worth about fifteen times what a rescue is in skill gained, so any rule that keeps "
         "captains from teaching is a rule against the population levelling at all."],
    ),
    "BotBaron": (
        "the ground that has already killed people",
        "Every other class answers *how does this bot get by*. The Baron answers the one question nobody "
        "else on the island asks, because there is no profit in it: who goes back to the places that have "
        "proved they kill. He raises a levy for it, walks his rounds, tours the towns, and pays a stipend out "
        "of his own account.",
        [],
    ),
    "BotQuad": (
        "the island as squares",
        "The map cut into squares thirty tiles across, each carrying one number: how safe the population has "
        "found it to be. It is written by everything that dies or kills and read by the captain, the Baron "
        "and anything choosing where to go. Also the frontier — the nearest square nobody has ever stood in "
        "— and the scouting that fills it in.",
        ["This one *is* saved between restarts, unlike the ground survey."],
    ),
    "BotRanger": (
        "livery",
        "One file: the King's Rangers' kit, which is the Baron's livery on five more bodies.",
        [],
    ),
    "BotDashboard": (
        "watching it happen",
        "`[bots` — an administrator command opening five tabs: the population, their market, what they are "
        "short of, what the city wants, and what the population is doing.",
        [],
    ),
    "mindedBots": (
        "bots that think",
        "Four of the population choose what to do next through a local language model over Ollama rather "
        "than through the auction: a warrior, an architect, a sage and the Baron. The model is given what "
        "the bot can see and returns a choice; everything else about them is an ordinary bot.",
        ["The model is asked on a wall clock and the answer costs real seconds. Anything that waits on it "
         "must not be holding the game loop."],
    ),
    "mindedBots/debugger": (
        "Argus, the observer",
        "A thinking thing that is not one of the population: an invisible figure that nobody in the world can "
        "see, that cannot be hurt and cannot hurt anything, whose whole job is to watch the bots and write "
        "what it believes into `logs/bot-debugger.log`. It has a door for a person at the keyboard "
        "(`argus-in.txt`) and a small set of administrator gestures.",
        ["Five false alarms in one day, all of them artefacts of the instrument rather than faults in the "
         "shard. Check the watcher before believing it about the population."],
    ),
}

# folder -> the summary lines it writes into. Verified against a live log rather than derived, because a
# clause can reach a line through two levels of Describe().
LINES = {
    "BotPopulation": ["Getting about:", "The market:", "What we know:", "Money:"],
    "BotWill": ["What we know:"],
    "BotMovement": ["Getting about:"],
    "BotOutfit": ["The ground:"],
    "BotHarvest": ["The ground:"],
    "BotCraft": ["Needs:"],
    "BotShops": ["Needs:"],
    "BotAuction": ["The market:"],
    "BotSpells": ["Arms:"],
    "BotCombat": ["Arms:", "Bows:"],
    "BotMend": ["Arms:"],
    "BotSquad": ["Companies:", "Arms:", "Bows:", "Needs:", "The ground:"],
    "BotHunt": ["Companies:"],
    "BotDrill": ["The captain:"],
    "BotBaron": ["The Baron:"],
    "BotQuad": ["The captain:"],
    "mindedBots": ["Minds:"],
}

TYPE_SUMMARY = re.compile(
    r"///\s*<summary>\s*(.*?)///\s*</summary>\s*(?:///.*?\n\s*)*?(?:\[[^\]]*\]\s*)*"
    r"(?:public|internal|sealed|static|abstract|partial)[^\n]*\b(?:class|struct|enum|interface|record)\b",
    re.S,
)


def read(path):
    with open(path, "rb") as handle:
        return handle.read().decode("utf-8-sig", errors="replace")


def purpose(text):
    found = TYPE_SUMMARY.search(text)

    if not found:
        return ""

    body = re.sub(r"^\s*///\s?", "", found.group(1), flags=re.M)
    body = re.sub(r"<[^>]+>", "", body)
    body = " ".join(body.split())
    sentence = re.match(r"(.{5,200}?[.])(\s|$)", body)

    return (sentence.group(1) if sentence else body[:160]).strip()


DIAL = re.compile(r"public static ([\w<>?\[\]]+) (\w+) \{ get; set; \} = ([^;]{1,60});")

# A configuration file can only reach a dial through a line like `BotAuction.StaleMs = settings.StaleMs ??`
# in that subsystem's *Config.cs. Anything not on the left of one of those needs a rebuild to change.
SETTABLE = re.compile(r"(\w+)\.(\w+)\s*=\s*(?:settings|cfg|c)\.(\w+)\s*\?\?")


def collect():
    """Everything the blocks are built out of, read once per file."""
    folders = {}

    for root, dirs, files in os.walk("."):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin")]

        folder = os.path.relpath(root, ".").replace(os.sep, "/")
        folder = ROOT if folder == "." else folder

        for name in sorted(f for f in files if f.endswith(".cs")):
            text = read(os.path.join(root, name))
            stem = name[:-3]
            here = folders.setdefault(
                folder,
                {"files": [], "proposers": [], "module": None, "config": None, "deeds": set(),
                 "dials": [], "settable": {}},
            )

            here["files"].append((name, purpose(text)))

            for dial in DIAL.finditer(text):
                here["dials"].append((stem, dial.group(2), " ".join(dial.group(3).split())))

            for reach in SETTABLE.finditer(text):
                here["settable"][(reach.group(1), reach.group(2))] = reach.group(3)

            if re.search(r"class\s+%s\s*:\s*[^{\n]*BotModule" % re.escape(stem), text):
                here["module"] = stem

            if re.search(r"class\s+%s\s*:\s*[^{\n]*BotDeed" % re.escape(stem), text):
                here["deeds"].add(stem)

            found = re.search(r"Configuration/(bot-[a-z-]+\.json)", text)

            if found and not here["config"]:
                here["config"] = found.group(1)

            if re.search(r"class\s+%s\s*:\s*[^{\n]*IBotProposer" % re.escape(stem), text):
                offers = re.search(r'public string Name\s*=>\s*"([^"]+)"', text)
                rung = re.search(r"Rung\s*=>\s*BotStanding\.(\w+)", text)
                here["proposers"].append(
                    (stem, offers.group(1) if offers else stem, rung.group(1) if rung else "?", text)
                )

    # A proposer's deeds are whatever it constructs that is a deed anywhere in the assembly.
    every = set().union(*(f["deeds"] for f in folders.values())) if folders else set()

    for here in folders.values():
        here["proposers"] = [
            (stem, offers, rung, sorted(set(re.findall(r"new (Bot[A-Z]\w+)\(", text)) & every))
            for stem, offers, rung, text in here["proposers"]
        ]

    return folders


def block(folder, data):
    title, says, traps = BLOCKS.get(folder, ("", "", []))
    out = []

    out.append("")
    out.append("### `%s/`%s" % (folder, " — " + title if title else "") if folder != ROOT
               else "### root%s" % (" — " + title if title else ""))
    out.append("")

    if says:
        out.append(says)
        out.append("")

    facts = []

    if data["module"]:
        facts.append("**Module** `%s`" % data["module"])

    if data["config"]:
        facts.append("**Config** `%s`" % data["config"])

    for line in LINES.get(folder, []):
        facts.append("**Writes** `%s`" % line)

    if facts:
        out.append(" · ".join(facts))
        out.append("")

    if data["proposers"]:
        out.append("| offers work | as | on the rung | handing out |")
        out.append("|---|---|---|---|")

        for stem, offers, rung, deeds in sorted(data["proposers"]):
            out.append("| `%s` | %s | %s | %s |" % (
                stem, offers, rung, ", ".join("`%s`" % d for d in deeds) or "—"))

        out.append("")

    for trap in traps:
        out.append("- **Trap.** %s" % trap)

    if traps:
        out.append("")

    out.append("| file | decides |")
    out.append("|---|---|")

    for name, says_it in data["files"]:
        out.append("| `%s` | %s |" % (name, says_it.replace("|", "/")))

    return out


def dials(folders, order):
    """DIALS.md: every tunable in the assembly, its default, and whether a config file can reach it."""
    out = [
        "# BotAI v2 — Dials",
        "",
        "Every `public static` number in the assembly, the value it is written with, and the configuration",
        "key that overrides it. Generated by `regen-map.py`; see `MAP.md` for where any of these live.",
        "",
        "**A default here is not what the shard is running.** Whatever `Distribution/Configuration/bot-*.json`",
        "sets wins, and a key spelled in the wrong case is silently ignored rather than refused — so the only",
        "proof of a live value is the line the module writes at boot. A dial marked *code only* cannot be",
        "changed without a rebuild.",
    ]

    for folder in order:
        if folder not in folders or not folders[folder]["dials"]:
            continue

        here = folders[folder]
        out.append("")
        out.append("## `%s/`%s" % (folder, "" if not here["config"] else "  —  `%s`" % here["config"]))
        out.append("")
        out.append("| class | dial | default | config key |")
        out.append("|---|---|---|---|")

        for owner, field, value in sorted(here["dials"]):
            key = here["settable"].get((owner, field))
            out.append("| `%s` | `%s` | `%s` | %s |" % (
                owner, field, value, "`%s`" % key if key else "*code only*"))

    with open("DIALS.md", "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(out) + "\n")

    return sum(len(f["dials"]) for f in folders.values()), sum(len(f["settable"]) for f in folders.values())


def main():
    folders = collect()
    order = ORDER + [f for f in sorted(folders) if f not in ORDER]
    lines = []
    rows = 0

    for folder in order:
        if folder not in folders:
            continue

        rows += len(folders[folder]["files"])
        lines += block(folder, folders[folder])

    text = read("MAP.md")

    if BEGIN not in text or END not in text:
        raise SystemExit("MAP.md has lost its SECTION2 markers")

    head, rest = text.split(BEGIN, 1)
    _, tail = rest.split(END, 1)

    with open("MAP.md", "w", encoding="utf-8", newline="\n") as handle:
        handle.write(head + BEGIN + "\n" + "\n".join(lines) + "\n" + END + tail)

    undescribed = [f for f in folders if f not in BLOCKS]
    blank = [n for f in folders.values() for n, s in f["files"] if not s]

    counted, reachable = dials(folders, order)

    print("%d files in %d subsystems" % (rows, len(folders)))
    print("%d dials in DIALS.md, %d of them reachable from a configuration file" % (counted, reachable))

    if undescribed:
        print("no block written for: " + ", ".join(sorted(undescribed)))

    if blank:
        print("no class summary, so no row text: " + ", ".join(blank))


if __name__ == "__main__":
    main()
