# ZK Mod Menu (ZKMM)

A Celeste mod for the [Everest](https://everestapi.github.io/) mod loader, made for players with a lot of mods.

Everest puts the settings of every mod into one long "Mod Options" list. ZK Mod Menu adds a **ZK Mod Menu** entry to the pause menu instead, where every installed mod gets a page of its own.

## What it does

- **Mod list.** Every installed mod, A to Z, with a search box on top. Favourites are pinned above the rest.
- **Mod pages.** One full screen per mod: name, version, author and description, a Favourite button, then that mod's settings (switches, sliders, choices, key bindings, buttons, grouped under the mod's own sub-headers) and a "Reset all to default" button. Mods without settings say so.
- **ZKMM Settings.** Theme (Dark, Light, Colourful), list density, animations, what the list shows, and more. Reached with the gear button next to the search box.
- Works with every mod as it is. The settings on a mod's page are the same ones the mod shows in Everest's Mod Options, so changing one does exactly what it does there.

## Controls

| Action | Keyboard | Controller |
| --- | --- | --- |
| Move | Arrow keys or W A S D | D-pad or left stick |
| Select / press | C (your Confirm key) or Enter | A |
| Change a value | Left / Right | Left / Right |
| Favourite a mod (in the list), reset a setting (on a mod page) | X | X |
| Back | Backspace or Esc | B |
| Search | Select the search box, type, Enter | |
| Leave the menu and resume the game | | Start |

The "X" action can be rebound in ZKMM Settings under Controls. Page Up, Page Down and the search key use Everest's own bindings.

On a key binding row, press Confirm, then the key or controller button you want. Delete removes the binding, Backspace or Esc cancels.

## Install

Copy `ZKModMenu.zip` into the `Mods` folder of your Celeste install and start the game. Needs a recent Everest (the .NET Core builds, 5000 or newer).

## Things worth knowing

- **Author and description.** Everest does not store these (a mod's `everest.yaml` only has its name and version). ZKMM reads them from the public Celeste mod database that Everest's updater and Olympus use (`maddie480.ovh`), keeps them in Everest's cache folder and refreshes them once a week in the background. No connection, or the option turned off: the page simply shows no author. Only a plain download is made; nothing about you or your mods is sent.
- **Default values.** To know what "default" is, ZKMM asks a mod to build its menu once with untouched settings. This works for nearly every mod; if a mod's page ever misbehaves, turn off "Detect default values" in ZKMM Settings. A setting whose default is not known cannot be reset and says so.
- **Mods with their own screens.** If a mod's button opens a screen of its own, ZKMM steps aside until that screen closes.
- Settings a mod only offers from the title screen do not show up, exactly as in Everest's in-game Mod Options.
- Everest's "Mod Options" entry stays in the pause menu unless you hide it in ZKMM Settings. It is always available from the title screen.

## Building from source

Needs the .NET 8 SDK (or newer) and a Celeste install with Everest.

```
# with this folder inside Celeste/Mods/
dotnet build -c Release

# anywhere else
dotnet build -c Release -p:CelestePrefix=/path/to/Celeste
```

The result is `bin/ZKModMenu.dll`. A mod zip is `everest.yaml`, `bin/ZKModMenu.dll` and the `Dialog` folder.

## How the code is laid out

- `Source/Core` is the whole menu: list, pages, settings, themes, drawing. It knows nothing about Celeste; it talks to a small canvas (rectangles, text, one image) and to rows and mods through interfaces.
- `Source/Everest` connects it to the game: the pause menu entry, input, drawing with the game's font, saved options, and the code that turns each mod's Everest menu items into rows.
- `Harness` is a development tool, not part of the mod. It draws the Core to PNG files with made-up mods and runs navigation checks, so the menu can be worked on without starting the game: `cd Harness && dotnet run -c Release -- out-folder`.
