# Dashboard: one window onto the whole population

`[bots` — an administrator-level command. Five tabs: the population, their market, what they are short of,
what the city wants, and what everybody is doing.

| File | What is in it |
|---|---|
| `BotDashboardGump.cs` | the window: three tabs, pages, buttons |
| `BotDashboardModule.cs` | module, phase `Settings`: registers the command |

---

## Why it exists

**Because v1 could not answer the question "why is that bot doing that".** Its decisions were unobservable, so
every question about behaviour cost an evening of watching a live shard — and the answers, when they finally
came, sounded like "it is honestly trading, one tick at a time, and that is why it is walking a graveyard".

Not one number on the first tab was invented for the dashboard: the decision layer already keeps all of it. The
only new thing here is that they sit side by side, one row per bot.

---

## The "Bots" tab

| Column | What it is |
|---|---|
| name | the name. Red means dead |
| class | one of the nine |
| rung | the ladder rung: `Free`, `Busy`, or something above it (red) |
| doing | the obligation and its stage: `mine: digging Iron Ore (14 swings)` |
| mood | `1 − (boredom + need)/2`. Red below half |
| **vector** | **how far the bot is along its own line of development** |
| purse | coin in the pack |
| bank | in the account |
| box | items in the bank box |
| stalls | stalls on the market |
| onsale | what is on them, at its own prices |
| → | button: teleport to this bot |

**About the vector.** A class declares what a bot is working towards (`BotClass.Skills`, plus the target of
whichever weapon the roll handed it). The vector is the share of that already covered, and **each skill counts no
higher than its own target**: you cannot make yourself a flattering vector by becoming monstrously good at one
thing. Money says what a bot has; the vector says whether it is becoming something.

Zero for everybody and staying there is not "bad bots" but "the work gives no growth": look then at `doing` and
at the brain's census in the footer.

---

## The "Market" tab

Stalls: **the real item's icon**, name, amount, price, worth, how much has gone, how much was earned, a
`+raises/-cuts` counter, and the seller. Paged.

The `buy` button buys one **with your own gold**, at the asking price. Not decoration: it is a way to see a bot
move its own price without waiting for another bot to want the thing. Buy twice in a row and the price goes up.

---

## The "Needs" tab

Wants: the item's icon, what is being asked for, how many are still wanted, at what price, how much money is
already down, how many were filled and for how much, the offer's movement counter, how much is sitting
uncollected, and who is asking.

**This is the tab that answers "why is nobody mining".** The first two say what the bots are doing and what they
have made; neither can say what the shard has been unable to get hold of. A want at four times its opening offer
with nothing filled is the clearest sentence this population knows how to speak: somebody has been trying to buy
that for half an hour and nobody here can make it.

Which is why the colours are the reverse of the market's: **raises are red, cuts are green.** On a stall a rising
price is a sign of demand; on a want it is a sign that there is no supply.

The `down` column turns red when the money on the table will not pay for even one at the current offer: such a
want stands there and can buy nothing.

---

## How it is built

`DynamicGump` (every row is different) behind a static `DisplayTo` — the shard's rule: everything is checked
before the window exists, so it can never be sent empty.

**The rows are snapshotted in the constructor rather than read again while drawing or answering.** Not tidiness:
bots take turns and the market moves between the moment a window is sent and the moment a button on it comes
back. A row that means one thing on screen and another in the response handler is an admin tool that teleports
you to the wrong bot.

Access is checked **twice** — in the command and in `OnResponse`. A gump response is a packet, and a packet can
be sent by anybody who has ever seen this window.

---

## What to check with a client

1. `[bots` — the window opens, with the brain's census and the market total in the footer.
2. The `→` button on a bot's row teleports to it; the system message says the same as the `doing` column.
3. The `Market` tab, `buy` twice — the stall's price rises.
4. The `Needs` tab — until somebody's book reaches the fourth circle it is honestly empty: "nobody is short of
   anything they cannot buy off a shelf".
5. `bots.dashboard.enabled = false` — the command is not registered and the population works as it did.

---

## Known rough edges

**Read-only apart from two buttons.** Nothing here issues a bot, changes a configuration, or reassigns work —
that is a later pass, and it will require deciding what "order a bot to do something" even means when the whole
point is that it decides for itself.

**Twelve rows to a page**, fixed window size. For four bots that is generous; for a hundred and fifty it is
thirteen pages.

**`Banker.GetBalance` is called per row** — it walks the bank box. For an admin window that is nothing; for
frequent auto-refresh it would want a cache.

**The gump contract was read out of the fork:** `DynamicGump`, `BuildLayout(ref DynamicGumpBuilder)`,
`OnResponse(NetState, in RelayInfo)`,
`AddBackground/AddAlphaRegion/AddLabel/AddLabelCropped/AddItem/AddButton/AddImageTiled`, `Singleton`,
`SendGump/CloseGump` from `Server.Gumps`, `CommandSystem.Register` with `[Usage]`/`[Description]` from `Server`.
