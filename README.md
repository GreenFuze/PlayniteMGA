# MyGamesAnywhere for Playnite

Shows your [MyGamesAnywhere](https://github.com/GreenFuze/MyGamesAnywhere) library in
Playnite.

MGA is a headless server: it owns the connections to Steam, Xbox, Google Drive, network
shares and the rest, works out which source files are the same game, and keeps the
artwork and achievements. This plugin is one of its faces. Everything it shows comes from
MGA's own library, so Playnite and the MGA console cannot drift apart.

## Install

Download `MyGamesAnywhere_0_1_0.pext` from the
[latest release](https://github.com/GreenFuze/PlayniteMGA/releases/latest), open it
with Playnite, and restart Playnite when prompted. Once the add-on database
submission is accepted, the plugin will also appear under **Add-ons → Browse → Libraries**.

Requires Playnite SDK 6.16.0 or later and MGA's scoped frontend API. Profile/password
sign-in requires MGA 0.2.22 or later. The MGA server is installed separately.

## What it does today

- Imports your whole MGA library, including games MGA knows about but cannot deliver
  bytes for, so Playnite can be the single place you look.
- Brings across titles, platforms, descriptions, release dates, developers, publishers,
  genres, ratings, favourites, and cover and background art.
- Tags each game with the MGA connection it came from — `MGA: Xbox`,
  `MGA: GF Google Drive` — so the library stays filterable once MGA is your only
  Playnite library.
- Tells you what your access key can and cannot do before you save it.

## Installing games

Two kinds of game, two different answers.

**Games MGA holds the files for** — a cloud drive, a network share, a local folder — get a
Playnite **Install** action that downloads them from your server. Downloads resume where
they left off, report progress, can be cancelled, and are checked against the file lengths
and revisions the server declared. If the source changes while a download is running, the
install is refused rather than leaving you with a copy made of two versions.

**Games that belong to a store** — Steam, Xbox — get no Install action at all. MGA holds
no bytes for them, only the knowledge that your account has them, so the play action opens
Steam or the Microsoft Store, which own installing and launching them. A button that
cannot do what it says is worse than no button.

**Uninstalling removes only what this plugin downloaded.** It keeps a record of every file
it wrote; anything that has changed since — a save, a patch, a mod — is left alone and
reported. If there is no record, nothing is deleted.

## What it does not do

- **Unpack archives, run installers, or choose emulators.** Those are Playnite's job and
  the device's, and MGA has never seen this machine. Downloaded files are handed over as
  they are, with a play action pointing at the executable when there is an obvious one.
- **Deliver emulators or runtimes.** MGA deliberately does not serve those to a frontend;
  emulator choice and launching stay Playnite's job.
- **Verify content hashes.** It would if it could. MGA emits a checksum only when a source
  recorded one as a sha256 digest, and scanned drives and shares do not, so verification
  rests on length and revision. That catches a truncated transfer and a source that
  changed; it does not catch silent corruption.

## Setting it up

1. In Playnite, open **Add-ons → Extensions settings → MyGamesAnywhere**.
2. Enter your server address (`tv2:8900`, `localhost:8900`, or a full URL) and press
   **Find players**.
3. Choose your player, type its password, and press **Sign in**.
4. Save, then **Update game library**.

Playnite never keeps your password. The server exchanges it once for an access key that
can read your library, fetch its artwork and download your games — and nothing else. The
key is stored encrypted for your Windows account, not in Playnite's settings file, and you
can revoke it from the MGA console at any time without changing your password.

If your server is older than this flow, the plugin says so and you can still issue a key
by hand from the console's **System → Issue client**.

## What happens when a game disappears from MGA

Nothing is deleted quietly.

- A game you have **installed** is tagged `MyGamesAnywhere: no longer in your library`
  and left alone. Your files and play history stay.
- A game you never installed is removed from the Playnite library, because there is
  nothing of yours in it.

A failed or partial sync never counts as "your library shrank". The plugin reconciles
only after a sync that completed.

## Building

```
dotnet build PlayniteMGA.slnx
```

Run the tests:

```
tests\MGA.Playnite.Tests\bin\Debug\net462\MGA.Playnite.Tests.exe
```

Download and remove one real game, without Playnite, to exercise the install path
end to end:

```
tests\MGA.Playnite.Tests\bin\Debug\net462\MGA.Playnite.Tests.exe --install tv2:8900 <key> <copy-id>
```

Check a real server without going through Playnite — useful when something is wrong and
you want to know whether it is the plugin or the server:

```
tests\MGA.Playnite.Tests\bin\Debug\net462\MGA.Playnite.Tests.exe --live tv2:8900 <access-key>
```

Package a `.pext` (needs the Toolbox that ships with an installed Playnite):

```
.\build-package.ps1 -ToolboxPath "C:\Users\<you>\AppData\Local\Playnite\Toolbox.exe"
```

## Licence

Apache 2.0. See [LICENSE](LICENSE).
