# Shops: trading with the NPCs, both ways

A capability for **every** bot rather than for one trade: a mage buys reagents, anybody buys bandages, a crafter
buys cloth to work with — and whatever the population refused goes back over the same counter for coin.

| File | What is in it |
|---|---|
| `BotShops.cs` | where the shopkeepers are, what they sell, what they buy, and both transactions |
| `BotRestock.cs` | an obligation: go and buy what has run out — off a shelf or off another bot's stall |
| `BotShopper.cs` | the proposer: who is short of what |
| `BotPeddle.cs` | an obligation: take what nobody wanted to somebody who will buy it |
| `BotPeddler.cs` | the proposer: whose stall the population has ignored |
| `BotShopsConfig.cs` | `Configuration/bot-shops.json` |
| `BotShopsModule.cs` | module, phase `World`, requires `Classes` and `Will` |

---

## Both directions of the world's money live here

Coin handed to a shopkeeper **leaves** the world; coin taken from one is the only coin that **enters** it. Bots
are born with none and nothing else mints any, so without `BotPeddler` every piece of work with an `Outlay`
failed on its first beat and the only thing that could happen on the shard was digging, which is free.

Which of the two dominates is the whole health of the economy, and it is one subtraction: see the
`N things bought for Xgp, M sold for Ygp` line this module prints.

---

## A purchase goes through the shopkeeper itself

`BaseVendor.OnBuyItems` is the same call a player's shop window makes. It charges the shard's real prices out of
the pack and the account behind it, and hands over real goods. A bot is a customer on exactly a player's terms,
which matters more than it sounds: every price scalar, every access rule and every stock limit applies without
being reimplemented, and nothing here can invent goods or money.

**Two lines that are not obvious, both paid for by the first version:**

Shelves refill on a timer that is only wound when somebody **opens the shop window**. Bots never open one — so
without an explicit `Restock()` a shop a bot has cleaned out stays empty for good.

Prices carry the shard's own scalars and update **on demand** — `UpdateBuyInfo()` before every transaction.

And a third, ours: **affordability is worked out here** rather than left to the engine. It takes an order whole
or not at all — an order for ten against a purse of eight buys nothing and says nothing.

---

## A sale goes through it too

`Buys` asks the shopkeeper about **this object** rather than about a type, because that is the only question the
engine answers: `IsSellable` looks at the item. `Buyer` finds the nearest one that will take it, and `Sell` goes
through `OnSellItems`.

**The takings are measured, not added up from the price tags.** The engine decides how much of an order it
honours — it refuses anything not in the seller's own pack, anything immovable, anything above its per-visit
limit — so believing the asking prices would be counting an intention. And it requires `IsStandardLoot()`, while
the bind marks issued gear `Newbied`: **bound gear cannot be sold, enforced by the engine**, which is a free
guarantee on the bind's third promise.

---

## Who is short of what

The list of needs comes **from the class's kit** — the same list the world handed the bot at birth. No table of
who buys what: add a class and it arrives with its own kit, and this works by itself.

| What | For whom | When |
|---|---|---|
| **the weapon** | anybody whose blade has worn through | it is gone from hand and pack |
| **ammunition** | archers | below half of what it was born with |
| **a tool** | anybody with one on the class list | worn through, or left in a corpse |
| bandages | everybody | below half of what it was born with |
| **potions** | everybody, per `PotionLimits` | one for one, as soon as one is missing |
| reagents (**eight** kinds) | anybody whose kit has reagents | below half |
| blank scrolls | anybody with a pen | in the course of the work, by the scribe's own obligation |

**The weapon comes before everything**, because a bot with no weapon cannot even defend itself — and because it
is the one thing on this list a crafter can make. A blade wears down on every landed hit and the engine destroys
it at zero.

**Then the tool.** Only the weapon is bound; a tool wears through — the engine gives a fresh one 25 to 75 uses,
spends one an attempt and destroys it at zero. A bot with no tool has no trade at all and cannot even earn the
price of bandages. **And the failure is silent by nature:** the proposer simply stops offering the work, which
looks exactly like a bot that never had a kit. This errand is the whole of what stands between "tools wear out"
and "trades quietly end".

The tool list comes from `BotOutfit.ToolsFor` — the same one birth issued from, including the mortar and the pen
derived from the build. The potion list comes from `BotOutfit.PotionsFor` the same way.

Then bandages, then potions, then reagents: one keeps a bot standing, one is the only mending that works while
something is hitting it, and the last makes it useful.

**The bots' market is asked before a counter, always.** `BotShopper` compares the shelf against
`BotAuction.Cheapest` and takes whichever is cheaper — so a shopkeeper is the **ceiling** on what a bot can
charge and never the preference. That ordering is where a crafter's living comes from: a fighter's gold came off
a monster, and it goes to a smith rather than out of the world whenever the smith asks less than the shelf.

**Who sells what** (read out of `SBInfo`): pickaxe 22 at the blacksmith, miner, tinker and weaponsmith;
**hatchet 25 at the weaponsmith only** — the narrowest supply on the shard; sewing kit 3 at the tailor and
tinker; smith hammer 21; scissors 11; mortar 8 at the alchemist and herbalist; pen 8 at the mage and scribe;
lesser heal and cure potions 15 at the alchemist and mage. The tinker stocks nearly everything at once, so one
tinker's shop in a town covers almost any loss.

Supplies are deliberately **not bound** — they are meant to run out. That is what makes a shop worth walking to,
and what makes another bot's production worth paying for.

---

## How the two errands are priced

**Buying: at nothing, and that is honest.** A purchase creates no wealth — coin becomes goods — so the
obligation declares `Made` equal to what was paid and the trip comes out at **about nothing per minute** rather
than at a loss. It is never punished and never preferred over work that produces something, which is exactly
where an errand to the shops belongs.

The one number that does real work there is `Outlay`: the brain measures need against it. A bot that cannot
afford its own bandages feels short of money, and stops the moment it can.

**Selling: at what the load is actually worth, per minute.** The proposer knows both numbers exactly — how many
there are and what this shopkeeper pays for one — so a guess would be strictly worse than the truth, and twenty
ingots outranking three is the behaviour anybody would want. `Coin` is 1.0, and this is the only obligation in
the project of which that is true.

**And the condition for going there at all is the market's own price.** A stall that has never sold one and has
stood for a full stale period has been in front of every bot on the shard for half an hour with nobody
interested. That is the shard saying "nobody here wants this", in the only language it has, and it costs no new
number to hear. No "is this junk" test, no table of worthless things.

`BotPeddler` can also only ever offer what the bot **itself decided to sell**: tools, herbs, paper and bandages
are never on a stall, so they are never candidates. That is the same sign rule that kills the ginseng carousel —
in v1 two bots sold the same shopkeeper the same reagents four thousand times because nothing distinguished
"goods" from "the things I need to do my job".

---

## What to check with a client

1. `Shops ready: ...` at startup, then `Found N shopkeepers within 160 tiles of ...` from the first bot to ask.
2. `No shopkeeper within reach ... sells Bandage` — the spawn point is too far from the shops; move it or raise
   `Reach`.
3. Once something starts being consumed: `Alden bought 12 Bandage from Ellie for 36gp` and `took on restock`.
4. `Alden sold 1 things to Verity for 80gp` — the first gold to enter the world over a counter.
5. The reload line: `N things bought for Xgp, M sold for Ygp`. **The difference between those two numbers is the
   health of the economy**, in one subtraction. In v1 it was −110,900 over a night and nobody could say where.

---

## Rough edges

**Reagents are now consumed.** Not by casting — nothing casts — but by inscription: a failed scroll ruins the
herbs and the paper, so climbing Inscribe from nothing to eighty burns hundreds of reagents. That is the first
real demand for restocking in the project, and it makes `Short = 0.5` a number that finally means something.
Bandages are consumed as of `BotMend/`: self-mending burns them for anybody without mana or a book.

Eight kinds of reagent rather than six: almost nothing can be written without bloodmoss and mandrake root. See
`BotSpells/README.md`.

**Shopkeepers are held by reference** and live exactly one world: the list is cleared on a reload.

**The Britain boundary** applies here as it does in the ground: a shop outside `BotPopulation.Roam` is never
offered.

**It compiles** — the engine surface was read out of the fork and confirmed by the compiler:
`BaseVendor.OnBuyItems/OnSellItems/GetBuyInfo/GetSellInfo/Restock/UpdateBuyInfo/IsActiveSeller/IsActiveBuyer/
LastRestock/RestockDelay`, `GenericBuyInfo.Type/Price/Amount/GetDisplayEntity`, `BuyItemResponse`,
`SellItemResponse`, `IShopSellInfo.IsSellable/GetSellPriceFor`, `Item.IsStandardLoot`.
