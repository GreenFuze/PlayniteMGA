# MyGamesAnywhere for Playnite

Shows your [MyGamesAnywhere](https://github.com/GreenFuze/MyGamesAnywhere) library in
Playnite.

MGA is a headless server: it owns the connections to Steam, Xbox, Google Drive, network
shares and the rest, works out which source files are the same game, and keeps the
artwork and achievements. This plugin is one of its faces. Everything it shows comes from
MGA's own library, so Playnite and the MGA console cannot drift apart.

## What it does today

- Imports your whole MGA library, including games MGA knows about but cannot deliver
  bytes for, so Playnite can be the single place you look.
- Brings across titles, platforms, descriptions, release dates, developers, publishers,
  genres, ratings, favourites, and cover and background art.
- Tags each game with the MGA connection it came from — `MGA: Xbox`,
  `MGA: GF Google Drive` — so the library stays filterable once MGA is your only
  Playnite library.
- Tells you what your access key can and cannot do before you save it.

## What it does not do yet

- **Install or download games.** That is the next piece of work. Games import as
  not-installed, which is honest: the plugin genuinely cannot put bytes on this machine
  yet.
- **Deliver emulators or runtimes.** MGA deliberately does not serve those to a frontend;
  emulator choice and launching stay Playnite's job.

## Setting it up

1. In the MGA console, open **System** and choose **Issue client**. Give it the
   `catalog.read` and `metadata.read` permissions. MGA shows the key once — copy it then.
2. In Playnite, open **Add-ons → Extensions settings → MyGamesAnywhere**.
3. Enter your server address (`tv2:8900`, `localhost:8900`, or a full URL) and paste the
   key.
4. Press **Check connection**. It will tell you exactly what the key can do.
5. Save, then **Update game library**.

The key is stored encrypted for your Windows account, not in Playnite's settings file,
and is never shown again.

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
