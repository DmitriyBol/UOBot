# Spells: a scribe's trade and a caster's appetite

A book that grows. The first work in the project whose output no shopkeeper sells — and the first buyer on the
bots' market that is itself a bot.

| File | What is in it |
|---|---|
| `BotGrimoire.cs` | what a book holds, what it lacks, and how a scroll becomes a spell in it |
| `BotQuill.cs` | inscription: what can be written, with what, and onto what |
| `BotInscribe.cs` | the scribe's obligation: buy paper → write → keep it or sell it |
| `BotScribe.cs` | the proposer: whoever is carrying a pen |
| `BotAcquire.cs` | the caster's obligation: get one spell — off a shelf, off a stall, or by asking |
| `BotSeeker.cs` | the proposer: whose book is short |
| `BotSpellsConfig.cs` | `Configuration/bot-spells.json` |
| `BotSpellsModule.cs` | module, phase `World`, requires `Classes`, `Will`, `Shops`, `Auction` |

---

## The wall everything here is built around

It is the engine's, not ours. `SBMage` holds the **first three circles** — twenty-four scrolls at 12, 22 and 32
gold — and stops there (`circles = 3`). The other **forty spells are sold by nobody at any price**. In the engine
they have two sources: a monster's corpse and `Inscribe`.

There is no hunting for scrolls in this version and it is not next. So today there is exactly one source —
**another bot**.

That is the whole answer to "why develop if you cannot cast anything stronger": not because we assigned a spell a
price, but because above the third circle there is nowhere to get one.

Inscription, read out of the fork: one blank scroll (5gp from the same mage) + one of each required reagent + mana
+ `Inscribe`. Mana by circle is **4 / 6 / 9 / 11 / 14 / 20 / 40 / 50**, and the minimum skill is
**−25 / −10.8 / 3.5 / 17.8 / 32.1 / 46.4 / 60.7 / 75**. A failure ruins the material, as a tailor's failure burns
cloth.

Two of those numbers carry meaning:

**Fifty mana for an eighth-circle scroll is a mage's entire pool at Int 50.** Meditation at 65 in its targets
stops being decoration: without it the mage writes one scroll and sits down. Which is why the obligation has a
"rest for mana" stage and not only a "write" one.

**Inscribe 80 for the mage, not seventy-five.** `DefInscription.GetChanceAtMin` returns `0.0` — at the minimum
skill the chance is exactly nil. At a target of 75 the eighth circle is not hard, it is impossible. At 80 it is a
one-in-ten attempt that burns the paper and the herbs nine times out of ten — and that is precisely what makes an
eighth-circle scroll worth what somebody will pay for it.

---

## A spell has no price, and that is a decision

There is one currency: `(Δmoney + goods produced + Δskill × rate) / minutes`. Buying a scroll is Δmoney minus
twenty, `Made` zero, `Trains` nothing. **The appraisal is negative and the brain would never choose it.**

So "collect all the spells" cannot be switched on by writing a proposer. It required deciding what a spell is
worth in the only currency there is — and the decision was **not to give it one at all**.

Instead: **a scribe keeps what it made.** Inscription pays in real skill through `CraftSystem` and in a scroll as
goods. A scroll is worth what the market says, and keeping it or selling it does not change what the bot produced.
So the first scroll of a kind goes into its own book if the book lacks it, and every further one is sold.

Collecting spells turns out to be what happens to a scribe that has got good at writing. No spell is priced
anywhere, nothing competes with work, and there is no number in a configuration file to turn.

**What follows, and is worth knowing in advance:** the book is bounded by the skill. A mage at Inscribe 40 will not
write the fifth circle and cannot obtain it except by buying from somebody who will. That is not a side effect —
that *is* the market.

---

## Who writes, who buys

`Inscribe 80` belongs **to the mage only**. The healer and the warrior-mage deliberately do not have it.

Otherwise every caster writes for itself and scrolls have no buyer. This way the market gains two bots whose books
fill up **only by purchase**, and that is the demand side the bots' market never had.

`BotSeeker` is nevertheless offered to **everybody**, scribes included, and the arithmetic sorts it out: a mage
with a pen reckons writing at 60 a minute against 12 for a trip after a scroll, so it writes — until it wants
something above its own Inscribe, and then it buys like everybody else. No role is assigned to anybody.

And incidentally, though it was the deciding argument for doing this subsystem before leather and smithing: **the
mage had no work at all before it.** Not "little" — none: no pickaxe in its kit, `BotMiner.Propose` returns null
without one, no sewing kit. Of the three proposers only a trip to a shop answered it, and that by design is worth
nothing.

---

## What to write: difficulty first, then the book

The first question is the needle's: **the hardest thing that still comes out reliably**, five points below its own
skill. Difficulty is where the growth comes from.

What is new here is that the engine gives all eight spells of a circle **identical** difficulty. So the first
question picks a circle and leaves a genuine second question — and the scribe's own book answers it: it writes what
it lacks, and once a circle is complete it writes whichever of the eight the market pays most for.

---

## The caster's three routes, and the route is a fact rather than a choice

`BotAcquire` obtains **one** spell, the cheapest of the missing ones (spell ids are laid out by circle, so
"cheapest" comes out on its own without a rule).

| Route | When |
|---|---|
| collect what was delivered | something has already been handed over against a standing want |
| a shopkeeper's shelf | circles 1–3, 12/22/32gp |
| another bot's stall | somebody has written one and put it out |
| a want | nobody sells it: put the money down and wait |

The cheaper of the first two, and **a shopkeeper is the ceiling**: no bot can charge more for something that is on
a shelf than the shelf does. That is what keeps this half of the market honest without a rule about honesty.

One gap at a time, and the world is asked about it exactly once. The first version of this file walked all
sixty-four spells and asked for each one who sells it — and that question walks every remembered shop and reads
its whole stock list. Sixty-four times a beat, per bot. That is v1's cost model wearing a new hat, and the
proposer interface forbids it in as many words: a question of the world may be real, but it may not be expensive.

A want that already stands is **not posted twice** — the proposer simply offers nothing. The want raises its own
offer on the market's beat. In v1 two bots wrote six hundred and eighty-eight identical notices in six minutes for
exactly this reason, and the fix is not a cooldown: the want is already there, already working.

---

## Three engine traps, each of which would have looked like working code

**`Spellbook.OnDragDrop` returns `scroll.Deleted`, not "it worked".** It writes the spell, consumes one scroll and
returns whether the object was deleted — and `Item.Consume` only deletes when the stack runs out. So a scroll from
a stack of two **is written into the book and reports failure**, and a caller that believes the answer will try for
ever. The same shape as the loot flag on a merged stack of arrows: the engine answers about the object and the
question was about the contents. So `BotGrimoire.Write` asks the book afterwards — the only reliable question, and
it costs one bit test.

**`Loot.RegularScrollTypes` is not in spell-id order.** The ids come from `Spells/Initializer.cs`: Clumsy 0, Heal
3, Magic Arrow 4. The loot array is in the client's art order, where Reactive Armor comes first. The two orders
agree about which spell is in which circle and disagree about everything else, so a map built by index would be
wrong for the whole first circle and **would look right**. It is read off the objects themselves: one of each
scroll is made, asked what spell it is, and destroyed. Sixty-four items once in the life of the process — cheaper
than being wrong.

**Writing into a book takes one off the stack.** The first version of this file did `continue` on a successful
write — and the rest of the stack reached neither the market nor a want, but rode around in the pack for ever. A
write no longer ends the step: the remainder of a stack is still a stack of scrolls and still has to be placed.

And one worth remembering although it does not touch us: `CraftItem.Craft` refuses outright if a recipe has a
`RequiredExpansion`, because it checks it through `from.NetState` and a bot has no client. The first eight circles
are pre-era with `RequiredExpansion` of `None`, and `BotQuill.Choose` only takes types present in the regular
scroll map, so necromancy and mysticism filter themselves out. But if a bot ever fails to craft something for no
reason, look here.

---

## What was fixed outside this folder

**Six reagents of eight were being issued.** `BotOutfit` and `BotShopper` knew sulphurous ash, black pearl,
garlic, ginseng, spider's silk and nightshade — and did not know **bloodmoss** or **mandrake root**. The six were
chosen to match exactly the three starting spells, and that was right until the book grew. With a full book,
without those two, clumsy, agility, cunning, strength, bless, teleport, unlock, wall of stone, arch cure, greater
heal, lightning, mana drain and recall cannot be cast — and almost nothing can be inscribed. A caster that has
collected a spell it can never speak has been given a defect, not a spell.

**A tool ran out and nobody bought another.** The engine gives a fresh tool 25 to 75 uses, spends one an attempt
and destroys it at zero, so a crafter swinging every three seconds was out of a sewing kit after two and a half
minutes of work — and out of a trade for good. Nothing in the log looked wrong: the proposer simply stops offering
the work, exactly as it does for a bot that never had a kit.

The first fix was wrong: I made the tool unwearable by adding a fourth promise to the bind. Patrik turned it round
— **only the weapon is bound; a tool wears through and is bought again.** That is better for three reasons: the
shard gains a reason to keep toolmakers, a trade gains a floor under its costs, and death gains one more price.
The defect was never the wearing out — it was that **nobody noticed the loss**. Now `BotOutfit.ToolsFor(klass)` is
one list asked by two callers, and `BotShopper` buys a replacement at the highest priority, above bandages.

**A tie in the opening points was resolved by enumeration order.** The hundred points of character creation go to
the three highest targets, and `List.Sort` is an unstable sort. The mage's Magery and Inscribe are both at eighty,
so without the fix the best fifty points could have gone to writing. That is exactly the defect the comments in
`BotWarrior` and `BotCrafter` have warned about from the beginning (Swords and Tactics both at seventy; half the
warriors on the shard spent their best points on Tactics and could not hit anybody) — but the warnings were the
only protection. A tie now breaks on **declaration order**: a class writes its skills in the order they matter to
it, and that is the only thing a class can say about it.

---

## What to check with a client

1. `Spells ready: 64 scrolls mapped, circles 1-3 on shop shelves and 40 that have to be written` at startup. Fewer
   than 64 means the map was not built, and `[bots` → **Needs** will stay empty.
2. The mage: `took on inscribe`, then `bought 20 BlankScroll from ... for 100gp`, then
   `finished inscribe: N HealScroll in M attempts, 1 into its own book, K sold to order`.
3. The healer: `took on acquire`, then `learned CureScroll for 22gp`. That is the first bot to have **learned**
   anything.
4. Further up the book: when the healer reaches the fourth circle the log will say
   `Elowen wants 1 LightningScroll and has put 42gp down for them`, and a row will appear in `[bots` → **Needs**.
   When the mage fills it — `Alden filled Elowen's want for 1 Lightning Scroll and was paid 42gp`.
5. The `vector` column for the mage should be **rising** — for the first time this class has something to rise on.
6. `bots.spells.enabled = false` in `modernuo.json`: everything else works, books stay at three spells, reagents
   are not consumed. That is the A/B for "is this magic or is this the market that feeds it".

---

## What is not here

**Nothing casts.** The book fills up and reagents are spent on writing — but not on spells, because there is no
combat magic and healing casts through `BotMend/` rather than through here. So the demand for herbs today is
proportional to **climbing the skill** rather than to using magic, and it will end when the climb does.

**Gathering herbs is not bot work.** In this era of the engine reagents are shop goods: no skill picks them. The
gatherer's reagent talent (`ForageIntervalMs`, 3–6 of one kind every 15 minutes) is declared in the class data and
is still unimplemented — deliberately, because making it work would mean inventing a mechanic rather than using
one of the engine's. So herbs are traded by a shopkeeper, which is right; what bots trade among themselves is what
a shopkeeper does not have.

**A scroll cannot be read.** A scroll can be cast from without a book and without reagents, which is the second,
perpetual sink for scrolls — but there is nobody to cast. When there is, `SpellScroll.OnDoubleClick` already knows
how.

**Recall and Gate are fourth circle.** If travel across the world ever goes by scroll, a scribe gains an endless
order from the **whole** population rather than from casters, and the temporary Britain boundary comes off with it.
That is the largest unoccupied niche this folder opens.

**Nothing survives a restart.** The population is rebuilt from configuration on every world load, so the book and
the Inscribe live one session. The whole arc of development is intra-session — as it is for skills, but on a book
it is more visible.
