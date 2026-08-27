# Classes

Nine classes, one file each. A class is **a description and a set of limits, with no behaviour**: it decides
nothing and controls nobody. Deciding is `BotWill`'s, and it reads classes the way it reads the map — as facts
about the world.

## Layout

| File | What is in it |
|---|---|
| `BotClass.cs` | the contract: what any class must be able to say about itself |
| `BotRole.cs` | role in a group — Melee, Ranged, Caster, Medic, Producer |
| `BotPotionKind.cs` | potion families, which is the granularity a carrying limit needs |
| `BotKit.cs` | what is issued at birth, and on what terms of binding |
| `BotWeaponOption.cs` | weapon + skill + target + ammunition, as one fact |
| `BotArsenal.cs` | the weapons of the era, the spell ids and the potion types, named once |
| `BotClasses.cs` | the registry of nine, and applying the configuration overrides |
| `BotClassConfig.cs` | reading `Configuration/bot-classes.json` |
| the other nine | the classes themselves |

## The numbers, as the code has them

Stats total 100 for everybody. The potion limit is one of each family bar two exceptions. Brewing is once every
10 minutes for everybody except the healer.

| Class | Role | Str/Dex/Int | Main skill | Skill targets | Talent |
|---|---|---|---|---|---|
| Warrior | Melee | 50/35/15 | by the weapon roll | weapon 75, Tactics 70, Anatomy 60, Healing 60 | — |
| WarriorMage | Melee, casts | 40/25/35 | by the weapon roll | weapon 70, Magery 65, Tactics 60, Anatomy 50, Healing 50 | 2 mana / 4 s without a staff, plate allowed, 2 mana potions |
| WarriorArcher | Ranged | 40/45/15 | Archery | bow or crossbow 75, Tactics 65, Anatomy 55, Healing 50, dagger 40 | — |
| Archer | Ranged | 35/50/15 | Archery | bow 80, Tactics 70, Anatomy 60, Healing 50 | ×3 crit at Archery/1000 — 3 % at the start, 10 % at grandmaster |
| Brawler | Melee | 40/50/10 | Wrestling | Wrestling 80, Tactics 70, Anatomy 65, Healing 60 | hands always free, 2 heal potions |
| Mage | Caster | 25/25/50 | Magery | Magery 80, **Inscribe 80**, Meditation 65, Alchemy 40, Wrestling 30 | blue staff 2 mana / 4 s, no plate |
| Healer | Medic | 25/30/45 | Healing | Healing 80, Anatomy 70, Magery 60, Meditation 60, Alchemy 50 | green staff 4 mana / 4 s, brews every 7 minutes, no plate |
| Crafter | Producer | 50/30/20 | **Blacksmith** | Mining 75, Blacksmith 70, Tailoring 60, Tinkering 50, Tactics 40, Healing 40, weapon 40 | once an hour, one forging gives two items for one lot of material |
| Gatherer | Producer | 55/30/15 | Mining | Mining 80, Lumberjacking 75, Tactics 40, Healing 40, weapon 40 | every 15 minutes, 3–6 reagents of one kind |

Everybody's weapon is rolled from six families: three swords, a war mace and two fencing weapons. The
warrior-mage is offered a quarterstaff instead of the dagger — an ordinary fighting staff, not the one that pays
mana back.

**The mage's Inscribe 80 is its trade.** Without it a mage has no work at all: no pickaxe in the kit, no sewing
kit, so the only proposer that answers it is a trip to a shop, which by design is worth nothing. Eighty rather
than the seventy-five the eighth circle asks for, because the engine gives a nil chance at exactly the minimum:
at 75 the top circle is not hard, it is impossible. See `BotSpells/README.md`.

## Three places where a number is explained rather than chosen

**A weapon comes with its skill.** `BotWeaponOption` holds the weapon, the skill, the target and the ammunition
as one fact, because it is one fact. In the first version the profile trained `Swords` and handed out war maces
at random, so a third of the melee bots spent their lives swinging something they could not use — and the gear
appraiser, comparing damage numbers, approved. Damage says what a weapon does on a hit; whether it hits is the
skill's business.

**The weapon target sits above `Tactics`.** The opening points go to the three highest targets in order, and in
the first version `Swords` and `Tactics` were both at 70 — a tie resolved by a dictionary's enumeration order.
Half the warriors on the shard spent their best fifty points on Tactics and could not hit anybody.

**And the tie is now resolved rather than worked around.** Until this was fixed, those warnings were the only
protection: `List.Sort` is an unstable sort, so with the mage's Magery and Inscribe both at eighty the best
fifty points could have gone to writing. A tie now breaks on **declaration order**: a class writes its skills in
the order they matter to it, and that is the only thing a class can say about it.

**The crafter's `Mining` is higher, and its main skill is `Blacksmith`.** Smelting ore into ingots is a `Mining`
check, so a smith with low mining burns the ore and never reaches the anvil at all: a measured smith at `Mining`
26 turned two ore in a hundred into ingots. So mining has to come first. But while the trade was inferred from
the largest target, the champion of smiths came out a grandmaster **miner** holding an apprentice's hammer. So
`MainSkill` is declared outright rather than derived.

## What the configuration may change

`Configuration/bot-classes.json` overrides any **number**: stats, skill targets, potion limits, talent
intervals, crit chance, mana regeneration, the plate ban. The file writes itself on first boot — nine names and
not one value, so by default it changes nothing.

What the configuration **cannot** touch: weapon lists, tools, the contents of a spellbook. Those are not
numbers, they are what a class *is* — and a file able to empty a smith's tool list would be a file able to
deliberately reproduce the first version's worst defect.

## Loose ends

- **The mana potion is now brewable** — by the healer and the mage. In the first version it deliberately was not
  brewable and only arrived by ship; the item is out of era, the engine's `DefAlchemy` knows nothing about it,
  so it needs a recipe of its own at the alchemy step.
- **The archer's crit should be visible to the danger arithmetic.** "Fight or not" is computed as toughness ×
  damage, and an archer that quietly hits three times harder than its numbers will refuse fights it would win.
- **Two numbers are mine rather than from the specification:** the 10-minute brewing threshold for everybody
  (the mage's number, since alchemy is open to all) and the weapon target of 40 for the crafter and gatherer.
- **Six of the eight potion families are declared and unused.** `PotionLimits` covers all eight; only Heal and
  Cure are issued and drunk (see `BotMend/`). The other six are buffs and weapons with nothing to use them.
