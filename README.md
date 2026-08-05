# MogHouse Companion

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin that syncs your FFXIV timers to
[MogHouse](https://mog-house.com) so the site can notify you when they come up — **even with the
game closed and your PC off**.

> **Status: pre-alpha, not published.**
> This build only links your account; timer collection is not implemented yet. There is no release
> and no public plugin repository entry — the source is public because Dalamud installs plugins
> from public download URLs, not because the plugin is ready to install.

## How it works

The game already tracks all of this in its **Timers** window (`CTRL+U`). The plugin reads those
values and uploads a snapshot of absolute UTC due-times to your MogHouse profile. MogHouse does the
scheduling and sends the push notification — that is what makes "your submarine came back while you
were asleep" work at all, since the game client obviously cannot notify you when it is not running.

```
FFXIV + Dalamud
  └─ MogHouse Companion ──HTTPS, Bearer mgp_…──▶ mog-house.com /api/plugin/v1/*
                                                        │
                                          cron sweep ───┴──▶ web push / mobile push
```

You pick **which** timers notify you, per character, on
[mog-house.com/settings/ffxiv](https://mog-house.com/settings/ffxiv). The plugin has no alert
configuration of its own on purpose: one source of truth means the website and the mobile app can
never disagree about what should fire.

### Planned timer coverage

| | |
|---|---|
| **Estate** | Submarine and airship voyages |
| **Retainers** | Ventures |
| **Allowances** | Treasure maps, leves (near-cap alert), Grand Company missions, allied society dailies |
| **Weekly** | Custom deliveries, fashion report |

Jumbo Cactpot is intentionally excluded — MogHouse already has its own reminder for it.

## Requirements

- Windows, [XIVLauncher](https://goatcorp.github.io/) with Dalamud enabled (console players cannot
  use this)
- A MogHouse account with an active **Mog+** subscription

## Installing

Not available yet. Once the first release is published, add this URL under
`Dalamud Settings → Experimental → Custom Plugin Repositories`:

```
https://mog-house.com/dalamud/pluginmaster.json
```

Then install **MogHouse Companion** from `/xlplugins`.

## Usage

1. `/moghouse` opens the status window.
2. **Link account…** → generate a pairing code on the website, paste it in, press Link.
3. The code is valid for 5 minutes and works once. The plugin stores the bearer token it receives;
   the raw code is never reusable.

Revoke a device at any time from the website. Unlinking in-game only forgets the token locally.

## Building

Requires the **.NET 10 SDK** and a Dalamud installation (the SDK resolves game assemblies from
`%AppData%\XIVLauncher\addon\Hooks\dev`, or from `$DALAMUD_HOME` if set).

```bash
dotnet build --configuration Release
```

Release builds are packaged by `DalamudPackager` into
`MogHouseCompanion/bin/x64/Release/MogHouseCompanion/latest.zip`.

Point the plugin at a local or staging server by editing `BaseUrl` in
`%AppData%\XIVLauncher\pluginConfigs\MogHouseCompanion.json`. It defaults to the dev server during
the beta.

## Releasing

Push a version tag; [`release.yml`](.github/workflows/release.yml) builds, attaches `latest.zip` to
a GitHub release, and regenerates `repo.json` (the Dalamud plugin manifest list) on `main`.

```bash
git tag v0.1.0 && git push origin v0.1.0
```

`https://mog-house.com/dalamud/pluginmaster.json` serves that `repo.json`.

## What gets sent

Only your own timer telemetry: character name, home world, content ID, and the due-times / counts
listed above. No chat, no inventory, no other players, no Free Company data beyond the workshop
timers your own character can see. Everything is scoped to the account the token belongs to.

Deleting a character in the MogHouse UI, or deleting your account, removes the data.

## Disclaimer

This is an unofficial, fan-made plugin. It is **read-only**: it reads timers your character can
already see and performs no automation and no game actions. It is not affiliated with or endorsed
by Square Enix. Third-party tools are not supported by Square Enix and using them is at your own
risk. FINAL FANTASY XIV © SQUARE ENIX CO., LTD.

## Credits

Timer data sources are informed by two long-running plugins that have solved this before:

- [SubmarineTracker](https://github.com/Infiziert90/SubmarineTracker) by Infiziert90 — MIT
- [Accountant](https://github.com/Ottermandias/Accountant) by Ottermandias — Apache-2.0

Both licenses are permissive; any code derived from them will carry its attribution in-place.

## License

[AGPL-3.0-or-later](LICENSE).
