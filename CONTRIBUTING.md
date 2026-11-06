# Pull request rules

These rules are mandatory for every pull request to `DmitriyBol/UOBot`.

## Repository scope

UOBot contains bot development only. Do not copy the ModernUO engine or the whole working fork into this repository.

Allowed changes:

- `Projects/BotAIv2/` — bot code, bot-specific engine patches and executable tools.
- `Projects/UOContent/Engines/Pathing/Tiered/` — the bots' tiered navigation.
- `Projects/UOContent.Tests/Tests/Engines/Pathing/Tiered/` — tests for that navigation.
- `Distribution/Configuration/bot-*.json` — bot configuration only, never runtime state, backups or ModernUO configuration.
- `Distribution/Data/bot-web/` — the bot dashboard.
- `START_SHARD.bat` and `shard-status.sh` — the two shard helper scripts.

Forbidden changes:

- `Projects/Server/` and any general engine files.
- Other parts of `Projects/UOContent/` and `Projects/UOContent.Tests/`.
- Full ModernUO solutions, project files outside the allowed bot directories, `modernuo.json`, saves, logs, backups, binaries, `bin/`, `obj/` and `__pycache__/`.

## No agent or AI work documents

Pull requests contain implementation, not the working process.

Do not add or modify:

- `PLAN-*`, `ANALYSIS-*`, `DECISIONS.md`, `DIALS.md`, `MAP.md`, `HANDOFF.md`, `NIGHT-*`.
- Reports, investigations, night logs, queues, handovers, review notes and generated agent/AI documentation.
- Existing `README.md` files, unless the owner explicitly asks for a documentation change.

`CONTRIBUTING.md` is the only mandatory documentation exception and may be changed when the owner changes these rules.

## No comments in code

Submitted source code must not contain comments:

- C#: `//`, `///`, `/* ... */`.
- Python, shell and PowerShell: `#` comments, except a required shebang.
- Batch files: `REM` and `::` comment lines.
- HTML: `<!-- ... -->`.

Keep executable strings, literals and directives intact. Do not remove comment-like text inside strings or break code while cleaning it.

## Branch and pull request

- Create `bots/<short-topic>` from `main`.
- Keep code and commit messages in English.
- Give the pull request a short description: what changed, how it was verified and what remains unmeasured.
- Include only the allowed bot files listed above.
- Run `git diff --check` and inspect `git diff --name-only main...HEAD` before publishing.
- Do not claim a feature is live until its build is deployed and measured on the shard.

The working shard is `O:\ModernUOfork\ModernUO-fork`; the bot-only repository checkout is `O:\ModernUOfork\UOBot`.
