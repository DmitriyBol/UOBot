# The market: bots trading with each other

Bots put out anything in any quantity, top up what they have out, **ask for what they are short of with the money
on the table**, and move their own prices by what actually sells and what actually gets filled.

| File | What is in it |
|---|---|
| `BotListing.cs` | one stall: what, how much, at what price, what has gone and how the price moved |
| `BotWant.cs` | one want: the same thing with the sign turned round, plus the escrow |
| `BotAuction.cs` | the market itself: list, ask, buy, fill, move a price, forget |
| `BotAuctionConfig.cs` | `Configuration/bot-auction.json` — speeds, not prices |
| `BotAuctionModule.cs` | module, phase `World`, depends on nothing |

---

## A stall, not a ticket

**One stall per bot per kind of thing, and it lives for the life of the shard.** Topping up with more ore adds to
the same stall rather than creating a second one, and that is exactly what makes the price and the sales history
mean something: a bot that has worked out that iron ingots move at nine keeps that number when the next load
arrives. A market of one-shot tickets would relearn its own prices from nothing every trip — which is how v1's
auction managed to be busy and know nothing.

**Nothing expires.** Goods sit here as long as it takes: no listing fee, no duration, no re-listing chore, because
every one of those is a mechanic whose only output is bookkeeping. What replaces them is the price moving: an
unsold stall gets cheaper, which is the same thing an expiry would have said, in the one language a bot can act
on.

**The goods sit here, out of the world.** That is the difference between a market and a promise: a bot cannot sell
the same ore twice, cannot drop it, and cannot lose it to whatever kills it on the way home.

---

## How a bot moves its price

| Event | What the bot does |
|---|---|
| the same thing sold again **soon** (< `BriskMs`, 10 min) | raises the price by `RaiseStep` (15 %) |
| the stall has sat **untouched** for `StaleMs` (30 min) | cuts it by `CutStep` (10 %) |
| in either case | never outside `×0.25…×4` of the first price it asked |

**"Sells often" means "again, soon", not "a lot".** One large purchase says somebody wanted a lot at the price
already asked; two purchases close together are what say the price was too low.

**Nobody but the bot sets a price.** Not one number in the configuration says what anything is worth: they say how
fast a bot changes its mind and how far it may go. A configuration file able to set the price of an ingot is a
configuration file that decides the economy, and then the market is decoration.

The opening ask comes from whoever is listing, and it asks `BotAuction.Worth` first — so the bot asks what the
shard reckons the thing is worth, and falls back to its own stand-in only when the shard has never had an
opinion. From there it is the market's business.

---

## Money is not printed

The order inside `Buy` is the entire guarantee:

1. **Charge the buyer** — coin out of the pack first with `ConsumeTotal` (which really destroys it), the remainder
   from the account with `Banker.Withdraw`. Short? Hand the coin back and do not sell.
2. **Deliver** — as much as could be delivered.
3. **Refund what could not be delivered** — into the account, because a pack can be full.
4. **Pay the seller** — into the account, because this is a market and not a hand-off: the seller may be standing
   in a mine.

The reverse order mints gold: the engine's `Deposit` adds to an account without touching what the depositor is
carrying. v1's economy lost 110,900 in a night with nobody able to say where; this market can account for every
coin.

---

## A want: the same thing with the sign turned round

`BotWant` mirrors `BotListing`, deliberately and all the way down. **One want per buyer per kind, for the life of
the shard**; asking again tops up the same want rather than creating a second; **nothing expires**, and instead of
an expiry the price moves. Only the other way: a stall nobody buys from gets **cheaper**, a want nobody fills gets
**dearer**. It is the same sentence said from two sides, which is why they share one set of numbers in one file.

In v1 "I want" and "I have" lived in a board (`BotBoard`, 746 lines, three kinds of notice in one list),
commissions, supply, postings and separately in the auction. Every one of those had to learn prices for itself,
and none of them did.

**The money is on the table before the want exists.** Gold is taken out of the purse and the account at the moment
a bot asks, and it sits there as a number until somebody earns it or the want is dropped. v1 had none of this, and
somebody offered fifteen hundred for twenty feathers with an empty purse — and the cost of that is not the coin,
it is that afterwards no bot can tell an offer worth crossing a continent for from one that will not be honoured
on arrival. It is also the only defence against a bot bidding absurdly to make its own production look valuable:
an offer costs exactly what it says.

**And a raise has to be paid for too.** A want can buy `escrow / offer` things, so a want for one scroll with
exactly one scroll's money down, whose offer rose fifteen per cent on its own, could suddenly buy nothing. It
would have raised itself out of existence on the first beat and no supplier would ever have seen it. So the
market's beat asks what the offer **would become**, tops up the difference out of the buyer's own purse, and only
then lifts it. A bot with nothing to outbid with cannot claim to have outbid.

**What has been delivered waits here** rather than being pushed at the buyer. The market holds goods out of the
world — that is the whole difference between a market and a promise — and it holds them for the buyer for the same
reasons it holds them for the seller: a pack can be full, a bot can be underground, and a delivery that can fail
on the receiving end is a delivery that loses the goods and the money at once. It is handed over on the market's
own beat, because waiting to be collected is a state nothing was guaranteed to leave: collecting pays nothing per
minute, so a bot with a trade would always rather do its trade.

---

## A bot cannot be on both sides of the same kind of thing

One number with a sign: plus is a stall, minus is a want. Not a check — the shape of the data.

And it is the answer to v1's principal measured defect: bots passed the same fifteen ginseng and the same
seventy-five gold round in a circle, because `BotBoard.Fulfil` handed over the ordered amount whole without asking
whether the filler needed it — and having handed it over, the bot fell below its own threshold and posted an order
of its own.

Here that cannot be expressed. Listing something you have a want for **drops the want and returns the escrow**
(not a rule against the bot — the same fact read from the other side). Filling a want for something you are
yourself short of is refused. Filling your own is refused.

`Made` meanwhile counts **what was produced, never what was acquired**, and whatever went into a want comes back
off `Made`, because it was paid for in coin. Otherwise one ingot would be counted twice — once as goods and once
as the money they fetched.

---

## The work does not all go to one supplier

One rule here is about fairness rather than arithmetic: **one supplier fills at most `Slice` units at a time**, and
the want goes back on the market before it will take from the same one again (`SliceMs`).

Without it a bot that happens to own a large pile closes a want whole the instant it appears — and the price never
gets the chance to fall that would have told a second supplier to look elsewhere. The first bot with a pile owns
every want for that pile.

It is a **window, not a quota**: if nobody else is producing, the same supplier comes back and finishes the job.
And it says nothing about a want for a single indivisible thing, and cannot: one scroll goes to one scribe.

---

## How a shortage reaches a worker

`BotAuction.Worth(type, fallback)` asks three questions in order: what somebody is **offering** for one, with the
money down; what one has **actually changed hands for** on a stall; and only then the caller's stand-in (six for an
ingot, twelve for a tailor's piece).

A producer counts its output at that number, the takings go into the ledger, and **the ledger raises its estimate
of that work next time**. Nobody tells anybody anything: a shortage raises the price, the price raises the
measurement, the measurement raises the estimate. One trip of latency and no new machinery — the same
arithmetic-from-a-shared-fact by which a squad computes its stations and shares.

## Who buys: now somebody does

`BotSpells/` is the first buyer on this market that is itself a bot. The engine's shopkeepers hold three circles of
scrolls and stop, so a caster's book is the first appetite in this population that only another bot can satisfy.
`BotHunt/` adds the other half: whatever comes off a corpse is offered here before it is offered to a counter.

The miners' ingots are still waiting for smithing. Until then metal's buyer is a player, an admin (the `buy` button
on the **Market** tab), or a want if anybody asks for metal. The consequence worth watching stays the same:
**without buyers every price creeps to its floor.** A market that only falls is not a broken market, it is a market
saying "nobody wants this".

## A want nobody can fill

At the ceiling (×4 of the first offer) and after `StaleMs` without movement, a want **gives up and hands the money
back**.

That is information rather than a failure: an offer at four times its opening, unfilled for half an hour, is the
shard saying nobody on it can make this. But saying so while holding the buyer's money for ever would be saying it
at the buyer's expense. The bot gets its escrow back, keeps a ledger row for the attempt, and will ask again later
knowing what the last one cost.

The same line is written by a second cause — a bot with nothing to outbid with. The two are told apart by the
offer against the ceiling, and the log prints both figures.

---

## What to check with a client

1. `The market is open on both sides: ...` at startup — the steps, the price bounds and the slice.
2. Let the population work, then `[bots`, the **Market** tab — stalls with the item's icon, amount, price, how
   much has gone, and a `+raises/-cuts` counter.
3. Press `buy` twice in a row — the price should rise (that is "again, soon"), and the log will say
   `put ... up to Ngp after selling ... again soon`.
4. Buy nothing for half an hour — the log will say `cut ... to Ngp after N sat unsold`.
5. The **Needs** tab — wants: who, what, at what price, how much money is down, how many were filled. The `moves`
   column there is red on raises rather than on cuts: a want getting dearer is something the shard cannot do.
6. After half an hour unfilled — `raised its offer for ... and put another Ngp down`. After several more —
   `gave up wanting ... of a possible N and took back Ngp`.
7. `bot-auction.json` → `"ListGoods": false` — the population carries on working, metal goes to the bank box, and
   the market stays empty. That is the A/B for "is this the market or is this the trade that feeds it".
8. `"Slice": 1` — a want for twenty ingots should be filled by twenty separate hand-overs, and the log will show
   them coming from different bots if there is more than one producer.

---

## Known rough edges

**A bot deleted mid-trade** leaves its stall until the market's next beat (30 s), after which the goods are
destroyed. Handing them to a bank box is pointless: a deleted mobile's box goes with it.

**The market is not saved.** Like the population: on a world reload stalls and wants are cleared, goods are
destroyed and escrow is not returned. Everything here belongs to a world that is being replaced, and gold
deposited to a bot about to be deleted is gold on a corpse nobody will loot.

**Splitting a stack was broken and is fixed.** `Portion` used `Activator.CreateInstance`, which looks for a
genuinely parameterless constructor — and almost everything stackable in the game is declared
`Foo(int amount = 1)`, which has none. So ore, reagents, scrolls and bandages **all** fell through to the
"whole objects only" fallback, and it looked like a working sale. It now uses the engine's
`Type.CreateInstance<T>()`, which fills optional parameters with `Type.Missing`.

**A want knows what something that does not exist yet looks like.** A stall has an object to copy the art and the
name from; a want does not, because it exists before the goods do. One throwaway instance answers both questions,
and the answer is kept for the life of the process because a type's art does not change. Otherwise a row in the
window would read `GreaterHealScroll` with no picture.

**The engine surface was read out of the fork:** `Item.Internalize`, `Item.Stackable/Amount/Hue/ItemID`,
`Container.DropItem/GetAmount/ConsumeTotal`, `Banker.Deposit/Withdraw`, `Gold`,
`Type.CreateInstance<T>()`.
