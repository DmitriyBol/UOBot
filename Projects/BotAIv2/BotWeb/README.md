# Web: the shard as a page

The whole population as one JSON document on the loopback address, a stream of events, and the page that
draws them. Built on 28.09.2026 for the order "an online dashboard: how many bots there are, where they are,
what they are doing, what routes they walk, and who cannot find a path — with maximum logging."

| File | What is in it |
|---|---|
| `BotEvents.cs` | the event stream: one object per thing that happened, with a sequence number, kept in a ring, written to `logs/bot-events.ndjson`, and fed to live streams; failed searches and alarm lines kept apart for their pages |
| `BotWebSnapshot.cs` | the whole shard as JSON, rebuilt on the game loop every two seconds: population, work, paths, every bot with its route, guilds, wars, squads, alarms, the thinking layer |
| `BotWebCraft.cs` | the crafting chain as the craft systems define it and the market fills it |
| `BotWebHistory.cs` | a sample a minute for the charts |
| `BotWebServer.cs` | the listener: `HttpListener` on `127.0.0.1:2599`, the routes, server-sent events, the log tail |
| `BotWebMap.cs` | a picture of the map from the client's `radarcol.mul`, sampled in slices of the loop and written once |
| `BotWebHooks.cs` | what the minds assembly reports about itself, since this one may not reference it |
| `BotWebConfig.cs` | `Configuration/bot-web.json` |
| `BotWebModule.cs` | the module: opens the stream, starts the listener and the clock |

## The routes

| | |
|---|---|
| `/` | the page, `Distribution/Data/bot-web/index.html`, re-read when the file changes |
| `/api/state` | everything, rebuilt every `SnapshotMs` |
| `/api/events?since=N&limit=M` | events after N; `/api/stream` is the same as server-sent events |
| `/api/paths` | the last failed searches and who, why and where they fail |
| `/api/history` | a minute at a time since boot |
| `/api/craft` | material → products → makers, wants, and what the craft system refused |
| `/api/log?n=300&grep=text` | the tail of the session log |
| `/api/bot/<name>` | one bot with the events about it |
| `/api/map.png` | the map picture |
| `/api/health` | how long the snapshot takes and how many are listening |

## Two rules, and the reasons

**The listener reads nothing from the world.** Every answer is a string the loop wrote and gave away, or a
file, or the event ring under its lock. The game runs on one thread and every observer this project has built
has had to prove it costs the population nothing; this one proves it by construction. `BotWebSnapshot.Describe`
prints what the building costs, and `/api/health` prints it live: about a millisecond for sixty bots.

**Every number on the page is a number the log already prints.** The page adds no counters. It reads the
same statics the five-minute summaries read — `BotWill.Taken`, `BotPath.Reached`, `NavigationService.Routes` —
so the page and the log cannot disagree. The one thing added for the page is the split of searches by purpose
(`BotWalk.FixedSearches`, `ChaseSearches`, `FlightSearches`), because a share of all searches reads a chase
after a moving creature and a flight with no goal as failures of the roads, and the 99 % is a question about
roads.

## The events

One object per event, and the `kind` says which: `say` (with a `channel`: world, guild, local, mood, cry),
`work` (`data.ending`: took, finished, failed, dropped, died), `path` (a search that did not reach; `data.outcome`,
`data.reason`, `from`, `to`), `death`, `war`, `alarm`. They come from the hooks the subsystems already had or
now have: `BotWill.Started` and `BotWill.Ended`, `BotWalk.PlanLeg`, `BotWill.Note`, `BotMobile.OnDeath`,
`BotAlarm.Write`. The file is appended across restarts, like the alarm channel.

## The map picture

Drawn once from `radarcol.mul` in the client folder the server already knows: the topmost static tile's colour,
else the land tile's, one pixel per `MapScale` tiles. Sampled on the loop `RowsPerSlice` rows at a time because
the tile matrix is not safe to read from another thread; encoded to PNG off the loop. Written to
`Data/bot-web/<map>.png` and not drawn again while the file exists. Without the client files the page draws
the bots on a grid.

## Configuration

`Configuration/bot-web.json`, PascalCase, every key optional: `Enabled`, `Bind`, `Port`, `SnapshotMs`,
`PagesMs`, `HistoryMs`, `KeepHistory`, `KeepEvents`, `KeepPaths`, `MapImage`, `MapScale`, `Folder`. The
listener binds to the machine itself by default; the page names every bot and every guild's chest.
