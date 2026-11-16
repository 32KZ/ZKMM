# ZK Mod Menu (ZKMM)

A mod settings menu for Celeste, made for players with a lot of mods.

Celeste's mod loader, [Everest](https://everestapi.github.io/), puts the settings of every installed mod into one long "Mod Options" list. With dozens of mods that list gets hard to find anything in. ZK Mod Menu adds a **ZK Mod Menu** entry to the pause menu instead: a searchable A to Z list of your mods, with favourites pinned to the top, where every mod opens a full page of its own.

![The ZK Mod Menu mod list in the Dark theme](docs/images/preview-list-dark.png)

<sub>Preview renders from the development harness, using made-up mods and a stand-in font. They are not in-game screenshots.</sub>

> [!WARNING]
> **Status: V1, untested in game.** This version compiles against Everest and its menu logic and drawing pass an offline test harness, but it has **not yet been run inside Celeste**. Expect rough edges, most likely text sizes with the game's real font. The online author and description lookup has not been tested either. Bug reports are very welcome; see [Reporting a problem](#reporting-a-problem).

## Contents

- [Features](#features)
- [Install](#install)
- [Using it](#using-it)
- [Controls](#controls)
- [ZKMM Settings](#zkmm-settings)
- [How it works](#how-it-works)
- [Known limitations](#known-limitations)
- [Uninstall](#uninstall)
- [Building from source](#building-from-source)
- [How the code is laid out](#how-the-code-is-laid-out)
- [Reporting a problem](#reporting-a-problem)

## Features

- **One entry in the pause menu.** "ZK Mod Menu" sits right above Everest's own "Mod Options".
- **Mod list.** Every installed mod in A to Z order, with a search box at the top. Search matches a mod's name, its ID and its author.
- **Favourites.** Favourite the mods you change most and they stay pinned above the rest.
- **A page per mod.** Name, version, author and description at the top, then a Favourite button, then that mod's settings grouped under its own sub-headers, then "Reset all to default".
- **Every kind of setting.** Switches, sliders, choices, buttons and key bindings. Key bindings are changed right on the page.
- **Reset to default.** Reset one setting, a whole mod, or every mod at once.
- **Three themes.** Dark, Light and Colourful, switchable at any time.
- **Keyboard and controller.** Everything works with arrow keys or WASD plus two buttons, or a controller.
- **Works with every mod as it is.** Mods do not need to support ZKMM. The settings on a mod's page are the same ones that mod shows in Everest's Mod Options.

| Mod page, Colourful theme | ZKMM Settings, Light theme |
| --- | --- |
| ![A mod's page in the Colourful theme](docs/images/preview-mod-page-colourful.png) | ![The ZKMM Settings page in the Light theme](docs/images/preview-settings-light.png) |

<sub>Also harness renders with made-up mods, not in-game screenshots.</sub>

## Install

You need Celeste with **Everest** installed. Everest is installed with [Olympus](https://everestapi.github.io/), the Celeste mod manager. ZKMM needs one of the current .NET Core builds of Everest (build 5000 or newer); any up to date Everest is fine.

1. Get `ZKModMenu.zip`. There are no published downloads yet, so for now [build it from source](#building-from-source); that section ends with how to make the zip.
2. Find your Celeste folder. On Steam: right click Celeste, then **Manage → Browse local files**. Olympus also shows the folder of the install it manages.
3. Copy `ZKModMenu.zip` into the `Mods` folder inside it. Do not unzip it; Everest loads mod zips directly.
4. Start the game. Pause in any level and choose **ZK Mod Menu**.

To update, replace the old `ZKModMenu.zip` with the new one. Your favourites and ZKMM options are kept.

## Using it

**Opening it.** Pause in a level and choose **ZK Mod Menu**. Leaving the menu with Back brings you back to the pause menu with the entry still selected.

**The list.** The top row holds the search box and the **ZKMM Settings** button; Left and Right switch between them. Below that are your favourites, then every other mod, both in A to Z order. Each row shows the mod's name, author, version and how many settings it has (the author and version line can be turned off).

**Searching.** Move to the search box and press Confirm, type, then press Enter (or Down) to jump into the results. Esc stops typing and keeps what you typed. To clear the search, move to the search box and press X.

**Favourites.** Press X on a mod in the list, or use the Favourite button at the top of its page. Favourites are saved with your other ZKMM options.

**A mod's page.** Move up and down through the settings and change them with Left and Right (or Confirm for buttons and switches). Changes take effect straight away, exactly as in Everest's Mod Options, and the mod's settings are saved when you leave its page. X resets the selected setting to its default. A dot next to a setting's name means it is not at its default. "Reset all to default" at the bottom resets the whole mod. A mod that has no settings says so.

**Key bindings.** Select a binding row and press Confirm, then press the key or controller button you want. Delete removes the binding; Backspace or Esc cancels.

## Controls

| Action | Keyboard | Controller |
| --- | --- | --- |
| Move | Arrow keys or W A S D | D-pad or left stick |
| Select / press | C (your Confirm key) or Enter | A |
| Change a value | Left / Right | Left / Right |
| Favourite a mod (in the list), reset a setting (on a mod page), clear the search (on the search box) | X | X |
| Back | Backspace or Esc | B |
| Page up / page down / jump to search | Everest's own menu bindings | Everest's own menu bindings |
| Leave the menu and resume the game | | Start |

The X action is ZKMM's own binding and can be rebound in ZKMM Settings under **Controls**. Confirm and Back follow the game's own Confirm and Cancel bindings, so if you have rebound those, ZKMM follows.

## ZKMM Settings

Open them with the gear button next to the search box. The same options also appear under "ZK Mod Menu" in Everest's Mod Options.

| Section | Setting | Default | What it does |
| --- | --- | --- | --- |
| Look | Theme | Dark | Dark, Light or Colourful. Changes as soon as you pick it. |
| Look | List density | Comfortable | Compact fits more mods on screen. |
| Look | Animations | On | Smooth scrolling and page transitions. |
| List | Show author and version in list | On | The second line under each mod's name. |
| List | Show mods without settings | On | Off hides mods that have nothing to change (favourites and search results still show). |
| Mod pages | Detect default values | On | Lets ZKMM work out each setting's default so it can be reset. See [How it works](#how-it-works). |
| Mod pages | Look up author and description online | On | See [How it works](#how-it-works). |
| Mod pages | Restyle mods' own screens | On | Menus a mod opens from its page are shown as ZKMM pages. Off shows them the way the mod made them, in Celeste's own menu style. |
| Pause menu | Hide Everest's Mod Options button | Off | Removes the old entry from the pause menu. It stays available from the title screen. |
| Controls | Favourite / reset to default | X key, X button | Rebinds the X action. |
| Reset | Clear all favourites | | Press twice to confirm. |
| Reset | Reset all mod settings | | Puts every setting with a known default back to that default, for every mod. Press twice to confirm. |

## How it works

**The settings are each mod's own.** ZKMM does not keep a copy of anyone's settings and mods do not need to know about it. When you open a mod's page, ZKMM asks the mod to build its normal Everest Mod Options section into a hidden menu, then shows each of those items as a row on the page. Changing a row changes the real item, so the mod saves and reacts exactly as it would in Everest's Mod Options. A mod made of several Everest modules gets one page that holds all of them.

**Defaults.** Everest does not record what a setting's default is. To find out, ZKMM builds the mod's menu once more with a fresh, untouched settings object and reads the values from it. That is what X and "Reset all to default" go back to. This works for nearly every mod; a setting whose default cannot be found simply cannot be reset, and says so. If a mod's page ever misbehaves, turn **Detect default values** off.

**Author and description.** A mod's `everest.yaml` only has its name and version, so Everest has no author or description to show. ZKMM reads them from the public Celeste mod database that Everest's updater and Olympus already use (`everest_update.yaml` and `mod_search_database.yaml` on `maddie480.ovh`). It keeps the result in Everest's cache folder (`Mods/Cache/ZKModMenu.modinfo.txt`) and refreshes it in the background once a week. Only a plain download is made: nothing about you or your installed mods is sent. With no connection, or with **Look up author and description online** turned off, pages simply show no author or description.

**Where things are saved.** ZKMM's own options and your favourites are stored by Everest in `Saves/modsettings-ZKModMenu.celeste`, like any other mod's settings.

**Mods with their own screens.** Some mods have a button that opens a screen of their own. Such a button expects Everest's Mod Options underneath it, and each mod hands back in its own way: some take the options menu off screen and put it back when their screen closes, some switch it off and on again, some go back to the pause menu. ZKMM follows all of these. A plain menu is shown as a ZKMM page of its own, with the mod's name in the trail at the top, and Back does whatever that menu does when it is backed out of. A screen with code of its own (Everest's key config screens, for example) is shown as the mod made it while ZKMM waits behind it. A setting that opens inside the options menu itself is shown in that menu, in Celeste's own style, until it hands the controls back. If a screen goes back to the pause menu, ZKMM closes. And if a screen closes without handing back to anything, ZKMM loads the mod's page again after a moment, so the game is never left paused with nothing on screen.

## Known limitations

- **Not yet run in game.** See the status note at the top. The first in-game runs will very likely need small fixes.
- **Mods' own screens are followed, not understood.** ZKMM works out what a mod's screen is doing by watching the menus (see [How it works](#how-it-works)). The ways mods usually do this are covered, and a screen that leaves nothing behind is caught, but a mod that draws a screen without using a menu at all may look wrong inside ZKMM. If a restyled screen misbehaves, turn **Restyle mods' own screens** off; if it still does, use Everest's Mod Options for that mod and please report it.
- **Online lookup untested.** The author and description download is written from the database's documented format but has not been tested against the live server.
- **Title screen only settings.** Settings a mod only offers from the title screen do not appear, exactly as in Everest's in-game Mod Options. Use Everest's Mod Options on the title screen for those.
- **Pause menu only.** ZK Mod Menu is opened from the pause menu in a level. On the title screen, use Everest's Mod Options.
- **Default detection is a best guess.** A few mods may build their menus in ways that make a default impossible to read, and those settings cannot be reset from ZKMM.
- **Minimum Everest version.** `everest.yaml` asks for Everest 1.5000.0. That is a safe guess for the .NET Core builds, not a tested minimum.
- **English only** for now. All of ZKMM's own text is English.

## Uninstall

Delete `ZKModMenu.zip` from your `Mods` folder. To remove everything ZKMM left behind, also delete `Saves/modsettings-ZKModMenu.celeste` and `Mods/Cache/ZKModMenu.modinfo.txt`. The settings of your other mods are untouched; ZKMM only ever changed them through the mods themselves.

## Building from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) (or newer) and a Celeste install with Everest. The build reads `Celeste.dll`, `FNA.dll` and `YamlDotNet.dll` from the game folder; they are only used to compile and are never copied into the mod.

```sh
# Easiest: put this repository inside Celeste/Mods/ and build there.
# The game's files are found two folders up.
dotnet build -c Release

# Anywhere else: point the build at your Celeste folder.
dotnet build -c Release -p:CelestePrefix=/path/to/Celeste
```

The result is `bin/ZKModMenu.dll`. With the repository inside `Celeste/Mods/`, Everest loads it straight from the folder, which is the quickest way to test changes: build, then restart the game.

To make an installable zip, put these three things at the top level of a zip named `ZKModMenu.zip`:

```
everest.yaml
bin/ZKModMenu.dll
Dialog/English.txt
```

**Building without owning the game.** V1 was compiled without a copy of Celeste, against a reference `Celeste.dll` built from the stripped game assemblies in Everest's own repository (`lib-stripped/`) patched with Everest's `Celeste.Mod.mm`. That route is fiddly and the resulting DLL must never be distributed; if you have the game, use it instead.

## How the code is laid out

```
Source/Core/      The whole menu: list, pages, rows, themes, input handling and drawing.
                  Knows nothing about Celeste. It draws through a small canvas interface
                  (rectangles, text, one image) and talks to mods through interfaces.
Source/Everest/   The connection to the game: the pause menu entry, input, drawing with the
                  game's font, saved options, the online mod database, and the code that
                  turns each mod's Everest menu items into rows.
Dialog/           ZKMM's text for Everest's Mod Options.
Harness/          A development tool, not part of the mod. It draws the Core to PNG files
                  with made-up mods, runs navigation checks, and plays through the ways
                  mods open and close screens of their own.
```

Because the Core has no game dependency, most of the menu can be worked on without starting Celeste:

```sh
cd Harness
dotnet run -c Release -- out-folder
```

The harness uses SkiaSharp and expects the Inter Bold font at `/usr/share/fonts/opentype/inter/Inter-Bold.otf` (the `fonts-inter` package on Debian and Ubuntu) as a stand-in for the game's font. The images in this README come from it.

## Reporting a problem

Please [open an issue](../../issues) with:

- what you did and what happened,
- your Everest version (shown on the title screen),
- the mod involved, if the problem is on one mod's page,
- the relevant part of `log.txt` from your Celeste folder (search it for `ZKModMenu`).

If a single mod's page misbehaves, turning off **Detect default values** in ZKMM Settings is a good first thing to try.
