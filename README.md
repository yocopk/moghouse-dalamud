# MogHouse Companion

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin that connects FINAL FANTASY XIV to your
[MogHouse](https://mog-house.com) account: an all-in-one home for quality-of-life tools, added over
time, each switchable on its own.

**Timers.** Syncs the timers from the game's own Timers window so the site can notify you when they
come up, **even with the game closed and your PC off**.

**Duty Finder.** When the confirm window pops, MogHouse pushes the duty or roulette name to your
phone while the window is still open.

Nothing leaves the game unless you switch it on in the plugin. What is uploaded, and what then
sends you a push, are two separate choices — the first is made in-game, the second on the website.

> **Status: beta, installable.**
> Both modules work end to end against production, and the plugin installs from the custom
> repository below. It is **not** in the official Dalamud plugin list, so it arrives through
> `Experimental` and updates from here rather than from the installer's own catalogue.

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

The two halves answer different questions, and both are deliberate. **Which timers leave the game
at all** is decided in the plugin, in-game — that is the privacy boundary, and it should not depend
on a website honouring a request. **Which of the ones that arrive send you a push** is decided on
[mog-house.com/companion](https://mog-house.com/companion), per character, so the website and the
mobile app can never disagree about what should fire.

A timer switched off in the plugin is cleared from your account on the next sync, and the site stops
offering a notification switch for it.

### Timer coverage

| Collector | Timers | Readable when |
|---|---|---|
| Workshop voyages | One deadline per fleet, set to the **last** vessel back | inside the FC workshop |
| Retainer ventures | Venture completion, per retainer | after opening the retainer bell |
| Allowances | Treasure map, leves, custom deliveries, allied society dailies | always, once logged in |

A voyage is reported as a single row rather than one per vessel: being told about the first of four
submarines is noise, since you would have to walk back three more times. The individual vessels ride
along so the apps can still list them.

Grand Company missions and the fashion report are driven by fixed weekly/daily resets, so the server
derives them on its own and the plugin collects nothing for them. Jumbo Cactpot is excluded
entirely — MogHouse already has its own reminder for it.

The "readable when" column is a limit of the game, not of the plugin: FFXIV only populates those
structures in those situations, which is exactly why the timers are uploaded and scheduled
server-side. Once a deadline has been uploaded, the notification fires whether or not the game is
running.

### When it syncs

The plugin reads the collectors every 30 seconds and uploads only when the values actually changed,
with a floor of 60 seconds between uploads and an hourly heartbeat so the website can tell how fresh
the data is. Logging in triggers a check.

Opening or closing the voyage panel or the retainer bell opens a 90-second window where it reads
every 2 seconds and uploads within 10. That is a *hint*, not a hook on their contents: the workshop
structures go unreadable the moment you leave, and the game fills in a new return time a beat after
the panel closes, so sending four submarines out and walking straight to the aetheryte could
otherwise slip between two polls. A patch that renames one of those windows costs the fast path and
nothing else — the ordinary cadence still catches everything.

The Duty Finder is the exception to all of it. A pop is an event that expires in 45 seconds, so it
is reported the moment the confirm window appears and delivered on that request, never retried.

## Requirements

- Windows, [XIVLauncher](https://goatcorp.github.io/) with Dalamud enabled (console players cannot
  use this)
- A [MogHouse](https://mog-house.com) account — a free one is enough; see [Plans](#plans)

## Plans

The plugin itself is free, and everything it reads is yours to see either way. What a free account
is capped on is how much MogHouse will *watch for you* while the game is closed.

| | Free | Mog+ |
|---|---|---|
| Characters synced | 1 | every one |
| Timers shown, in game and on the site | all | all |
| Daily roulette checklist | ✓ | ✓ |
| MogHouse notifications shown in game | ✓ | ✓ |
| Duty Finder push | ✓ | ✓ |
| Timer push alerts | 1, chosen by you | as many as you like |
| Warning *before* a timer lands | — | ✓ |

Every one of those is enforced on the server. Nothing in this repository decides what your account
is allowed to do, which is the only arrangement that makes sense for a plugin whose source anyone
can read and rebuild.

A free account syncing a second character is told so rather than quietly ignored, and a lapsed
subscription pauses the extras instead of deleting them — it all comes back on renewal without
re-pairing or reconfiguring anything.

## Installing

Add this under `Dalamud Settings → Experimental → Custom Plugin Repositories`:

```
https://raw.githubusercontent.com/yocopk/moghouse-dalamud/main/repo.json
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

The plugin talks to `https://mog-house.com` and has no in-game server picker: for a real player
that control can only ever do harm, since pointing it anywhere else silently stops their
notifications. To aim a development build somewhere else, close the game and edit `BaseUrl` in

```
%AppData%\XIVLauncher\pluginConfigs\MogHouseCompanion.json
```

Clear `Token` in the same file while you are there — a bearer token is only valid on the instance
that issued it, so one carried across servers will do nothing but return 401.

## Releasing

Push a version tag; [`release.yml`](.github/workflows/release.yml) builds, attaches `latest.zip` to
a GitHub release, and regenerates `repo.json` (the Dalamud plugin manifest list) on `main`.

```bash
git tag v0.1.0 && git push origin v0.1.0
```

`repo.json` is served straight off `main`, so the repository URL above needs nothing hosted on
mog-house.com — the tag is the whole publish step.

The installer icon is [`images/icon.png`](images/icon.png) — the MogHouse app icon, 512×512, which
is also the size and location the official Dalamud repository expects if this is ever submitted
there. The manifest points at it by raw URL, so it travels into `repo.json` on its own.

## What gets sent

Only your own telemetry: character name, home world, content ID, the due-times / counts listed
above, and — when the Duty Finder module is on — the name of the duty or roulette that just popped,
which is the same thing the game is showing you on screen at that moment.

No chat, no inventory, no other players, no Free Company data beyond the workshop timers your own
character can see. Everything is scoped to the account the token belongs to.

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
