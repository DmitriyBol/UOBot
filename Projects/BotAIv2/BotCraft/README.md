# Craft: a producer that depends on nobody

Buy cloth → sew → put it out. The first work in this project that **creates** rather than extracts.

| File | What is in it |
|---|---|
| `BotThread.cs` | sewing: what can be made from a material, and the attempt itself |
| `BotSew.cs` | an obligation with three legs: shop → work → counter |
| `BotTailor.cs` | the proposer: whoever is carrying a sewing kit |
| `BotCraftConfig.cs` | `Configuration/bot-craft.json` |
| `BotCraftModule.cs` | module, phase `World`, requires `Classes`, `Will`, `Shops` |

---

## Why the tailor and not the smith

**A tailor needs no place.** A smith wants a forge and an anvil, so a smith without a workshop is a bot with an
opinion about metal; a sewing kit works wherever the bot is standing. That is the whole reason the first
crafting chain is cloth and not ore.

And the second reason: **a tailor depends on nobody.** Cloth is on a shelf in town, and there is no waiting for
miners. That is the answer to the question this chain was written for — what does a producer do on a shard where
nothing has been produced yet.

---

## The work is the engine's

Everything goes through the shard's own `CraftSystem` — the same call a player's craft window makes. The skill
check, the failure, the material burnt on a bad attempt and the item that appears are all the engine's. That is
what makes the gain real: a tailor here learns tailoring **by tailoring**, not by being credited.

**Attempts are not output.** Crafting runs on its own timer and fails often at the edge of a skill, so what is
counted is what is in the pack afterwards, not how many times the bot swung. In v1 the counter counted attempts
and reported "44 made" about a smith that had produced nothing in three minutes.

**What to sew** is chosen the way anybody learning a trade chooses: **the hardest thing that still comes out
reliably** — five points below its own skill. Difficulty is where the growth comes from; an easy piece teaches
nothing. Recipes needing a second material are skipped: a bot with cloth is a bot with cloth.

---

## Why this pays better than digging

Because **Tailoring is on the crafter's own vector** and Mining is not. With the vector rule
(`BotYield.StrayFactor`) the same tenth of a point counts at full rate for sewing and at a third for digging.
The bid for a trip to the needle is 55 a minute against 45 at the mine, and that is not a nudge: it is the same
statement said as a number.

Cloth costs money and the finished piece is worth more — in skill certainly, in coin if the market agrees.
`GoldPerPiece` (12) is a stand-in of the same kind as the price of an ingot: it **opens a stall**, and from
there the price is the market's business.

---

## What to check with a client

1. `Craft ready: a tailor buys 20 cloth at a time ...` at startup.
2. `took on sew` from the crafter, then `bought 20 Cloth from ... for Ngp`, then
   `finished sew: N Item in M attempts, N put out to sell`.
3. The `Market` tab in `[bots` — the tailor's pieces should appear beside the miners' ingots.
4. The `vector` column for the crafter should be **rising**.

---

## Rough edges

**One material and one trade.** Leather, smithing and "ingots → object" are the next step, and it is the same
shape: it will have a buyer among the bots, because the input of one trade becomes the output of another.

**Failures burn cloth**, which is right, but the bid (55/min) does not account for them — the ledger will
correct the estimate once it has measured the real output.

**It compiles.** The engine surface was read out of the fork and confirmed by the compiler:
`DefTailoring.CraftSystem`, `CraftSystem.CraftItems` (`List<CraftItem>`),
`CraftItem.Resources/Skills/ItemType/Craft(from, system, typeRes, tool)`, `CraftRes.ItemType/Amount`,
`CraftSkill.SkillToMake/MinSkill`, `SewingKit : BaseTool`, `Cloth`.
