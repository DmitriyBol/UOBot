# Voice: what the bots say

The bots speak: when they take up work and when they finish it, when they are hit, when they die, when a war
is declared, when a hall goes up, when they want to buy something, and now and then about how they feel.
Built on 28.09.2026 for the order "socialisation: phrases when they do things, their mood, guild chat, the
world when they are looking for something, a shout when attacked."

| File | What is in it |
|---|---|
| `BotVoice.cs` | `Say(who, channel, text)`: the throttle, the delivery, the event, the transcript; and the occasions — took, ended, struck, died, mood, a guild's news |
| `BotPhrases.cs` | the phrase bank, by occasion, with placeholders |
| `BotVoiceConfig.cs` | `Configuration/bot-voice.json`, which carries the whole bank so it can be edited |
| `BotVoiceModule.cs` | the module: hooks the will, runs the mood clock |

## Speech is a by-product

Nothing here is offered to the auction and nothing costs a beat. A line rides on an event that was happening
anyway, and the only decisions taken are whether to say it and where. This is the invariant every observer
on the shard keeps: the voices must never change what the population does. The one place speech was already
a mechanism — the thinking crafters' remarks, the warden's toll, the fence's threat — now passes through
`BotVoice.Aloud`, unrationed, so the page and the transcript see it.

## Five channels

| channel | audience | in the world |
|---|---|---|
| `local` | whoever stands near | said overhead |
| `guild` | the guild | guild chat, and overhead with the guild's letters |
| `world` | everybody | yelled, and broadcast to every client if `Broadcast` |
| `mood` | whoever stands near | said overhead in its own hue |
| `cry` | whoever stands near | yelled in red |

Every line, whatever the channel, is an event of kind `say` on the stream and a line in `logs/bot-speech.log`,
because the person watching is more often at the page than in the world.

## Rationed per bot

One line per bot per channel every `SayEveryMs` (15 s); a cry every `CryEveryMs` (12 s); a mood every
`MoodEveryMs` (15 min), staggered from boot so fifty bots do not all speak in the same minute. Taking or
finishing work is said with probability `WorkChance` (0.35), a failure with `FailChance` (0.6), a drop with
`DropChance` (0.15). A flight's end is always said. An order is always shouted to the world, because it is a
question to everybody. The throttle is by bot, not by shard: fifty bots each saying something every fifteen
seconds reads as a crowd; one bot every second is a fault.

## The phrase bank

Keys are occasions. `took:mine` is said on taking up mining and falls back to `took:*`; the same for
`finished`, `failed` and `dropped`. The rest are one occasion each: `seek`, `cry`, `cry:low`, `died`,
`mood:high`, `mood:mid`, `mood:low`, `war:declared`, `war:won`, `war:lost`, `war:drawn`, `hall`, `guild:fell`.
Placeholders: `{name}`, `{class}`, `{work}`, `{stage}`, `{guild}`, `{foe}`, `{item}`, `{amount}`, `{price}`,
`{place}`, `{reason}`, `{minutes}`, `{coin}`, `{enemy}`, `{killer}`. A placeholder nobody filled is cut. A bot
never says the same line twice running.

The whole bank is written to `bot-voice.json` the first time the shard runs. A key given there replaces the
code's list for that occasion; keys left out keep the code's. Every word a bot says can be changed without
touching the code, and the channels can be switched off one by one.
