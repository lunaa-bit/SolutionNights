# Developing Saucer Nights

## Requirements

- .NET 10 SDK
- FINAL FANTASY XIV with XIVLauncher and Dalamud installed (the build references Dalamud's files from
  `%AppData%\XIVLauncher\addon\Hooks\dev\`)

## Build and load

```
dotnet build SaucerNights/SaucerNights.csproj -c Release
```

Add `SaucerNights/bin/Release/SaucerNights.dll` under *Dalamud Settings → Experimental → Dev Plugin Locations*, then
enable it in the plugin installer's *Dev Tools* tab. `SaucerNights/bin/Release/SaucerNights/latest.zip` is the packaged
plugin.

## Project layout

| Path | What |
|---|---|
| `SaucerNights.Core/` | Game engines, protocol, crypto, fairness, sessions, bots. No Dalamud dependency. |
| `SaucerNights/App/` | Settings and app state shared by the windows (Dalamud-free). |
| `SaucerNights/Ui/` | All drawing: theme, cards, chips, table scenes, animation, widgets, lobby (ImGui only). |
| `SaucerNights/Game/` | Dalamud glue: chat transport, fonts, windows. `Plugin.cs` is the entry point. |
| `SaucerNights.Tests/` | xUnit tests, including whole games of bots over a simulated linkshell. |
| `SaucerNights.Preview/` | Renders the real UI offline to PNGs (software rasteriser) for design checks. |
| `docs/PROTOCOL.md` | Wire format, encryption and fairness scheme. |
| `repo.json` | The custom plugin repository file Dalamud reads. |

## Testing without the game

```
dotnet test SaucerNights.Tests
dotnet run --project SaucerNights.Preview -- <outDir> [lobby host settings bj-turn pk-showdown pk-show icon ...]
```

The tests cover hand evaluation, every blackjack rule path, side pots and raise rules, the chat codec and
fragmentation, key exchange and sealing, shuffle uniformity, showing cards, the fairness reveal, and multi-minute
simulated games (with 5% of lines lost) that check chip conservation, private cards and fairness verification.
`HubTests` runs several plugin instances against one fake linkshell: discovery, joining, host-plays poker, watchers,
leaving and closing.

The preview tool draws the plugin's real UI code with a software renderer, so layout changes can be checked without
launching the game. The screenshots in `docs/screenshots/` and `images/icon.png` come from it.

## Releasing

1. Bump `<Version>` in `SaucerNights/SaucerNights.csproj` and `SaucerNights.Core/SaucerNights.Core.csproj`.
2. Commit, then push a tag with the same four-part version:

   ```
   git tag 1.0.1.0
   git push origin 1.0.1.0
   ```

3. The *Release* workflow tests and builds the plugin, creates a GitHub release with `SaucerNights.zip`, and updates
   `repo.json` on `main` so everyone's Dalamud offers the update.

To release by hand instead: build, rename `latest.zip` to `SaucerNights.zip`, attach it to a GitHub release tagged with
the version, and edit the version, the three download links and `LastUpdate` in `repo.json`.
