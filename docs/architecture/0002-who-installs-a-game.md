# ADR-0002: Who installs a game

Status: Accepted
Date: 2026-09-06
Jira: MGA-106

## Context

MGA's library mixes two kinds of thing that look identical in a list and are nothing alike
underneath.

For a game on a cloud drive, a network share or a local folder, MGA holds the actual
files. It can enumerate them, and it can serve their bytes. On the owner's library that is
**141 of 254** games.

For a Steam or Xbox game, MGA holds no bytes at all. It knows the account has the game,
and it knows the store's id for it, and that is the whole of it. There is no file to
download and no legitimate way to obtain one — redistributing a storefront's package is
exactly what a plugin like this must never do.

An Install button that behaves the same for both would be lying about one of them.

## Decision

**Files route.** A game backed by a filesystem connection gets a Playnite `InstallController`
that downloads the copy's manifest from MGA and writes the files into a folder on this
machine. Progress, cancellation and resume are real: transfers use Range requests from
whatever is already on disk, and a cancelled download keeps its partial files, because
they are what makes resuming possible.

**Store route.** A Steam or Xbox game gets **no install action** and a play action that
opens `steam://rungameid/<appid>` or `ms-windows-store://pdp/?ProductId=<id>`. The store
installs and launches it, because the store owns it.

**Files win when both exist.** A game present on a drive *and* on Steam is downloaded from
the drive: if MGA can hand over the bytes, that is what the user came to this plugin for.

**A source the last scan could not find is offered by neither.** Its files may be gone.

## What verification actually means here

The manifest carries a length, a revision and an optional checksum per file. The checksum
is almost always absent: MGA emits one only when a source recorded its revision or object
id as a literal `sha256:` digest, and no file in a scanned drive or share does. Measured
on the owner's library, **0 of 25,402** media rows and 0 of the content files carry one.

So verification is:

- **length** — every file must arrive at exactly the declared size, or the install fails;
- **manifest revision** — re-read after the download, and a change means the source moved
  while we were reading it, so the copy may be a mixture of two versions and is refused;
- **checksum** — used when present, which today is never.

This is written down because "verifies checksums" is what the ticket asks for and what a
release note would like to claim, and claiming a check that never runs would be worse than
the gap itself.

## Uninstall removes only what we wrote

Every install records the exact files it created. Uninstall deletes those, skipping any
whose length no longer matches — patched, modded or replaced — and removes directories
only once they are empty. With no record, nothing is deleted at all.

The alternative, deleting the install directory, would take saves, mods, configuration and
anything a game's own installer put there. An install directory is not ours.

## Consequences

- Two routes to keep in step as MGA gains connectors. `ContentRoute` holds the list of
  filesystem-backed plugin ids in one place, and a connector missing from it degrades to
  "no install action" rather than to a broken one.
- Downloaded archives and installers are handed over as files. Unpacking, running an
  installer, and choosing an emulator stay with Playnite and the user, per MGA-106.
- Exercised against the live server by `--install`, which downloads a real game, runs a
  second pass to prove nothing is re-fetched, plants a save file, uninstalls, and checks
  the save survived.
