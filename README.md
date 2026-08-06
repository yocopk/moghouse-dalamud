# MogHouse Companion

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin that connects FINAL FANTASY XIV to your
[MogHouse](https://mog-house.com) account: an all-in-one home for quality-of-life tools, added over
time, each switchable on its own.

**Available now — Timers.** Syncs the timers from the game's own Timers window so the site can
notify you when they come up, **even with the game closed and your PC off**.

Nothing leaves the game unless you switch it on in the plugin. What is uploaded, and what then
sends you a push, are two separate choices — the first is made in-game, the second on the website.

> **Status: pre-alpha, not published.**
> Account linking and timer collection are implemented, but the server endpoint they upload to is
> still being built, so nothing has been tested end to end yet. There is no release and no public
> plugin repository entry — the source is public because Dalamud installs plugins from public
> download URLs, not because the plugin is ready to install.

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

### Timer coverage

| Collector | Timers | Readable when |
|---|---|---|
| Workshop voyages | Submarine and airship returns | inside the FC workshop |
| Retainer ventures | Venture completion, per retainer | after opening the retainer bell |
| Allowances | Treasure map, leves, custom deliveries, allied society dailies | always, once logged in |

Grand Company missions and the fashion report are driven by fixed weekly/daily resets, so the server
derives them on its own and the plugin collects nothing for them. Jumbo Cactpot is excluded
entirely — MogHouse already has its own reminder for it.

The "readable when" column is a limit of the game, not of the plugin: FFXIV only populates those
structures in those situations, which is exactly why the timers are uploaded and scheduled
server-side. Once a deadline has been uploaded, the notification fires whether or not the game is
running.

### When it syncs

The plugin reads the collectors every 15 seconds and uploads only when the values actually changed,
with a minimum of 60 seconds between uploads and a heartbeat every 10 minutes so the website can
tell how fresh the data is. Logging in and changing zone also trigger a check. Nothing is hooked
into individual game windows, so a patch that moves an addon around cannot break syncing.

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

To load it in-game without packaging, add the build output directory
(`MogHouseCompanion/bin/x64/Debug`) under `Dalamud Settings → Experimental → Dev Plugin Locations`.
Both configurations put the DLL and its manifest side by side, which is the layout Dalamud expects.

Point the plugin at a local or staging server from **`/moghouse` → Advanced**. Switching server
unlinks the device, because a bearer token only works on the instance that issued it. It defaults
to the dev server during the beta.

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
