# Bots' shops: trade between bots in a place

Patrick's order of 29.09.2026, evening: move away from the internal auction by degrees; bots keep shops by the bank or
their own storage and call out to the bots that need their goods; for now the auction stays, with fewer lots a bot. A
merchant caste that buys what moves and carries it between towns is the next step, not this one.

| File | What is in it |
|---|---|
| `BotShopkeep.cs` | the open shops, the stock count and its prices, the customer's weighing (`Best`, `Beats`), the visit and the till (`Visit`, `Serve`), the cry, the page, the summary |
| `BotStorefront.cs` | one open shop: keeper, pitch, bank, town, the count of its goods and asks, what it has sold this shift |
| `BotShopkeeper.cs` | the proposer: whose goods are worth a shift, and where it would stand |
| `BotKeepShop.cs` | the shift: walk to the pitch, open, call out, serve, shut on the clock or when sold out; put down and reopened for what will not wait |
| `BotPatron.cs` | the proposer for a want on the board: walk to a shop that sells the thing for less than the want offers |
| `BotShopBook.cs` | per town and kind: what bots bought at bots' shops, what they bought from shopkeepers, what they came for and found gone — the merchant's book |
| `BotShopkeepConfig.cs` | `Configuration/bot-botshops.json` |
| `BotShopkeepModule.cs` | module, phase `World`, requires `Will`, `Shops`, `Auction`, `Population`; sets the auction's lot cap; writes `Bot shops:` every five minutes |

Hooks in shared files, each commented where it stands: `BotCore` registers the module; `BotAuction.List` refuses a new kind
past `LotsPerBot` (with `Capped` and a clause on `The market:` line); `BotShopper.Propose` asks `BotShopkeep.Beats` before
the stall and the counter; `BotRestock` has a fourth constructor, a branch in `Advance`, `Bend`, `AtCounter` and `Outlay`
for it, and books each shopkeeper purchase in `BotShopBook`; `BotWebSnapshot` writes `shops`; `BotPhrases` has the four
`shop:` occasions; the page draws a `shops` layer.

---

## Where the goods are

**In the keeper's pack and bank box, all the time.** Nothing is lifted out of the world as a stall lifts it. The shop is a
count (`BotStorefront`), taken when it opens, every `StockMs` after, and at every sale; the till counts again before
anything moves. What is for sale is what the porter would call surplus: the bank box whole (nothing but surplus is ever put
there, `BotUnload.Store`), and the pack over `BotUnload.Keeps`. Never what is bound, coin, a container, a key, a rune or a
runebook, a spellbook, a ship's or a house's deed, anything this assembly made for a bot's own use, or a kind the keeper
has a want out for.

**At the bank it first serves itself.** On opening, a keeper tops its pack up to its own keep list out of its own box
(`SelfSupply`): the porter's `Store` puts the surplus there and nothing else ever takes it out, so without this a shop would
sell its keeper's bandages while the keeper went to a shopkeeper for more.

## Where the shop stands

**Beside the banker nearest home** (`BotPopulation.HomeOf`: the town's bank for a resident of a town, the guild's seat or
the population's home otherwise), within `BankTiles`. The places round the banker from `PitchNear` to `PitchFar` tiles, on
its floor, are read off the map once per `BankMemoryMs`; a pitch is free when no open shop is within `Apart`, the bot has
not lately failed to reach it, and the shard has not proved there is no way to it. At most `PerBank` shops a banker and
`MostOpen` on the island. A guild hall's own counter is the estate's business and is left to it.

## Who comes, and why the shop wins where it does

| buyer | already asks | the shop is weighed against |
|---|---|---|
| a bot short of its kit (`BotShopper`: weapon, ammunition, bandages, draughts, tools, reagents) | the stall if no dearer than the shopkeeper, else the guild's counter, else the shopkeeper | the stall's price and the shopkeeper's price plus the walk to him; the shop's price plus the walk to it |
| a bot with a want on the board (`BotPatron`: materials, replacement gear, scrolls) | waits for a supplier or a stall to cross it | the want's own offer; the shop's price plus the walk |

The walk is priced at `WalkGold` a minute (following `BotRestock.Prior`, what the shard reckons a shopping minute worth),
at the appraisal's own walking pace. Ties go to the shop. A want is taken back off the board only at the till, with the
goods in front of it; what the shop could not supply is asked for again at the same offer.

## Prices

Open at the nearest shopkeeper's ask, or `BotAuction.Worth` when that is less; a kind no shopkeeper sells opens at
`Worth`, falling back on what a shopkeeper pays. Never under `BotAuction.Floor`. Up by `BotAuction.RaiseStep` when a kind
sells again inside `BotAuction.BriskMs`; down by `BotAuction.CutStep` for each kind nobody bought during a whole shift;
always inside `BotAuction.LeastMultiple`…`MostMultiple` of the opening. A buyer pays the price it was quoted when it set
out if the price has risen since. `BotShopkeep.PriceFor` is the one place the price a buyer pays is read.

## Money

The auction's order: the buyer is charged first (`BotAuction.Charge`, pack then account, all or nothing); the goods move;
what did not move is refunded to the buyer's account; the keeper is paid for what did, as coin in its pack (the account if
the pack will not take it, the pack regardless if neither will). No levy.

## The work

`shopkeep` is a steadfast, still work with its own clock: `Still` from the moment it opens (the stall watch and the labour
clock leave it to its shift), held against better offers, put down for a flight or a friend in trouble — the shop shuts
while the keeper is away and opens again when it is back. Finished when the shift has run or the goods are gone (a shift
that sold nothing is finished and says so); failed when the pitch cannot be reached or the bank will not take another
shop. Its takings are measured as coin. The customer's walk is `restock`, the shopper's own errand, unpaid.

## What to check

1. Boot: `Bots' shops are open: … the auction takes at most 3 kinds from one bot`.
2. `X opened a shop by the bank at (x, y) in <town>: …`, and the bot standing there on a client, calling out every
   `CryMs`.
3. `X bought N <kind> at Y's shop in <town> for Ngp … — short of it for its kit` and `— against its own want on the board`.
4. `Bot shops:` every five minutes: shifts, sales by need, the weighing, the till, the book.
5. `The market:` — the new clause `N things stayed in their sellers' hands for want of a lot, at 3 kinds a bot`.
6. The page: the `shops` layer.
