# Mending: the rung that had been waiting from the start

The smallest subsystem in the assembly, and the only one that populates `Failing` — a rung that has existed since
the ladder was written and was empty the whole time.

| File | What is in it |
|---|---|
| `BotMend.cs` | what a bot can mend with, who is worth mending, and the three ways of doing it |
| `BotSalve.cs` | an obligation: get there → cast, bandage or drink → until whole |
| `BotMedic.cs` | the `Failing` proposer: mend yourself, above everything else |
| `BotSurgeon.cs` | the `Free` proposer: mend whoever is worst off — **including yourself** |
| `BotMendConfig.cs` | `Configuration/bot-mend.json` |
| `BotMendModule.cs` | module, phase `World`, requires `Classes` and `Will` |

---

## Why this became urgent when it did

The "health is going" rung was written, stood in the ladder, and **was served by nobody**. While nothing on the
shard fought, that was harmless: the brain's answer to failing health is "hold on to what you are doing", and a
bot digging ore does not die of that.

The day hunting arrived it started meaning "go back to the skeleton". That is v1's worst night in one sentence:
443 deaths, 104 of them one bot named Nell rising in the same tile every twenty to forty seconds, and the "too
dangerous to get up" guard firing not once in all 443.

## The order of means, and it flips on one engine fact

**Out of a fight, spell before cloth**, for three reasons. A spell lands in a couple of seconds where a bandage
on yourself takes nine or ten. Mana comes back on its own; bandages cost money at a counter. And the herbs a heal
spends are the ones a caster is walking to town for anyway.

**Under fire it is the other way round, and that is mechanics rather than tactics.** `Spell.OnCasterHurt`
disturbs a cast whenever the caster is a player — and every bot here is a `PlayerMobile`. So **a healer under
blows burns mana and herbs for nothing.** A bandage is not interrupted: the engine calls
`BandageContext.Slip()`, which costs two per cent of the success chance per blow. Cloth works under fire and a
spell does not.

So "spell first" is not overturned but qualified: **spell first where it can work.** "Am I being hit" is read out
of `BotResolve.HurtTick` — the same field `BotWill.Hurt` sets from `OnDamage`.

**And below 40 %, a bottle, before anything.** A potion is instant and **cannot be interrupted at all**: the only
mending in the game that works while something is hitting you. So it is what a bot reaches for when it really is
about to die, and only then: there are one or two of them and a counter is across town. The engine holds both
guards itself — a heal potion refuses a patient at full health and keeps its own cooldown — so none of that has
to be remembered here.

Potions, incidentally, were **declared and unimplemented** from the beginning: `BotPotionKind` and `PotionLimits`
sat in the class data (two heal potions for the brawler) but the kit issued none and nothing drank them. They are
now issued, drunk and replaced — two of the eight families, the two that mend. The other six are buffs and
weapons with nothing to use them, and an errand for a bottle nobody drinks is an errand that produces nothing.
And NPCs across this whole era sell **only the lesser tier** (15gp): the regular and greater ones are an
alchemist's product, and there is no alchemist yet.

## Two rungs, and the ladder holds the difference rather than a rule in a file

| Rung | Who proposes | What |
|---|---|---|
| `Failing` (below 35 %) | `BotMedic` | mend yourself, above any other work |
| `Free` | `BotSurgeon` | mend whoever is worst off within 20 tiles, including yourself |

**Counting itself among the patients is what closes the gap between 35 % and 70 %.** Below 35 % a bot is on
`Failing` and attends only to itself; above that it is on the ordinary rung, and without this it would work at
half health with nothing in the world offering to patch it up. Now the same question — who here is worst off —
answers both cases, and **the ladder keeps the ordering**: at thirty per cent nobody asks `BotSurgeon` anything,
because the bot is already somewhere more urgent.

That ordering is a measured defect, not taste. In v1 the call for help stood **above** failing health, so a bot on
its last few points announced a company it could not join, found nobody able, and posted it again — dozens of
times over. Looking after somebody else has no right to outrank looking after yourself, and here it cannot.

## Casting distance, and this was a defect of mine

A heal reaches 10–12 tiles (`IRangedSpell.TargetRange`), and the first version of `BotSalve` walked to the patient
at `BotArrival.Beside` — **one tile**. So the healer walked itself into melee range of whatever was hitting the
patient, and thereby into the one condition in which its mending does not work.

Now **the distance is chosen by the means**: eight tiles for a spell, `Bandage.Range` for cloth. And the latter is
read out of the engine rather than chosen: on a renaissance shard it is **one** tile, two under AOS, and I had my
own two — a tile too far for this era, so the healer would have stood there unable to bandage anybody.

## Ability decides, not the name of a class

Spell or cloth, whichever it has. A caster is simply **better** at this, and that is a fact about the two
mechanics rather than a rule about classes: two seconds against ten, mana against money. A warrior with bandages
and Healing 60 patching up a miner is exactly as welcome.

The payment is skill, and it is real: a cast trains Magery and a bandage trains Healing, both by the engine's own
check. At 500 a point that is a living. Coin is zero, and rightly: nobody on this shard is yet paid for looking
after each other.

## Three things the engine does that did not have to be invented

**A bandage refuses a whole patient by itself** — "That being is not damaged!". That is the defence against
"bandage a healthy friend for ever", i.e. against the training dummy with a friend in it — the exact shape the
whole ledger exists to refuse. For a spell it has to be placed by hand, and it lives in `BotSurgeon`: the patient
must be genuinely hurt.

**Cloth is spent only when the engine accepted the patient.** The order is taken from the engine's own item:
`BeginHeal` first, and `Consume` only if a context came back. So the "not damaged" refusal costs nothing.

**A cast is two beats, because that is how the engine casts.** `Cast()` starts a delay, the delay ends in
`OnCast`, and `OnCast` puts a target on the bot which somebody has to fill in. A bot has no client to click with,
so the click is `BotMend.Aim`. Mana and herbs are spent there, in the sequence check behind the target, rather
than at the cast.

## One trap the review caught

**A target on a bot is not necessarily ours.** Harvesting puts a cursor up to point at rock. The first version of
`BotSalve` filled in whatever target it found with the patient — so it could have pointed a mining target at a
wounded friend, reaching into somebody else's work through a field they happen to share. Now only the target whose
cast this same work started gets filled in.

## One place where a number is inferred rather than read

The mana cost by circle (`4 / 6 / 9 / 11 / 14 / 20 / 40 / 50`). The engine keeps it inside the spell where nothing
outside can ask, so this is the era's own ladder — the same numbers `DefInscription` charges to scribe the same
circles. If it is wrong, the price is a fizzle instead of a cast and a bot reaching for cloth, which is exactly
the failure this is already written to cope with.

## The estimate rises with the wound

Without this there was a band where **nobody healed at all**. Below 35 % a bot is on `Failing` and attends to
itself; above 70 % it does not want mending; and in between the estimate was a flat thirty a minute, which loses
to a mining trip at forty-five. So a bot at forty per cent went and dug ore.

Now `Expects` is multiplied by how far the patient is past the threshold, with a steepness `Urgency` of 3: at 70 %
that is 30, at 35 % it is 75, at 10 % about 107. So with a real wound mending beats every trade on the shard, and
with a scratch it loses to all of them. No new mechanism — the same trick used everywhere here: the number is made
to reflect the fact instead of a rung being added to carry it.

The estimate is live rather than fixed at proposal, and that is right: a patient that got worse while the healer
walked is a more urgent job than the one it set out on.

## No state that can wait indefinitely

Out of mana, out of cloth, patient whole, patient dead, patient walked away — every one of them ends the
obligation on the beat it becomes true. A healer standing over a corpse with no bandages is the shape of bug this
project finds in itself most often.

And one subtlety about failure: **a failure marks the place with caution**, and the place a bot mends itself is
where it was hurt, usually its own work. So "mended with something" ends as `Done` rather than `Failed`, because
otherwise the ledger would learn that the mine is dangerous because somebody bandaged themselves at its mouth.

## What to check with a client

1. `Mending ready: a bot looks after itself below 70 % health and stops at 95 %, spell before cloth ...` at
   startup, and `Mend` among the `World`-phase modules.
2. Hit a bot by hand down to half health. It should take on `mend` rather than carry on digging: `took on mend`,
   then `finished mend: mended after 2 casts, 0 bandages and 0 bottles`.
3. Hit a **non**-caster — it should use cloth, and `[bots`' `doing` column will show
   `mend: mending itself (0 casts, 3 bandages, 0 bottles)`.
4. Wound a miner next to a mage. The mage should come over on its own:
   `Perrin has started patching up the rest of them`.
5. Below 35 % the rung in `[bots` must become `Failing` — and that is the first time in the project anything
   happens on it rather than "hold on and wait".
6. Take a bot's bandages, herbs and mana away: `is hurt and has neither the mana, the herbs nor the cloth to do
   anything about it` — once, by name.
7. **Hit a bot while it is mending.** It must switch to bandages: `bandages` rather than `casts` in `doing`. If it
   keeps casting under blows, `BotMend.UnderFire` is broken.
8. **Beat it down to 35 %.** A bottle should go (`bottles` in `doing`), not a bandage: it is the only thing that
   works while something is hitting it. Then `BotShopper` should go and buy another —
   `bought 1 LesserHealPotion ... for 15gp`.
9. Wound a bot to 50 % and check that it **does not go digging**: mending at that wound estimates around 55
   against the mine's 45. Before the wound-scaled estimate it went digging.

## What is not here

**Resurrection.** A dead bot is raised by the population's clock a minute later; a healer standing next to the
corpse can do nothing about it, although the engine's bandage can (`Target cannot be resurrected at that
location`). That is the next step and it is cheap.

**Real retreat.** "Flee" currently means "stop chasing" rather than leave: `BotMobile.OnDamage` sets `Combatant`
even when outmatched, so a bot answers blows where it stands. There is no walk-*away-from* primitive in the
project. This is the largest remaining gap and it is deliberately deferred until a live run.

**Protection before mending.** The engine itself softens cast disturbance when `ProtectionSpell` is registered
(second circle, id 14) — so a healer's book could become the answer to a healer's problem. Deferred: it only makes
sense once healers start surviving fights at all, and a run will show that.

**Curing poison with cloth.** `Cure` is cast if the book holds it; the engine's bandage cures poison too, but the
choice between them is not made here — it follows the same "spell before cloth".

**Nobody pays for mending.** A healer heals for the skill. Paying for a service means a want for a **service**
rather than for goods, and the market cannot express that yet: a want names a kind of item, not "come here".
