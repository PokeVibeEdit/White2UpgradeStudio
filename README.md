# White 2 Upgrade Studio

A desktop ROM editor for **Pokémon White 2 Upgrade**, written in **C# / .NET 8** with **Avalonia UI**, for Windows and Linux (x86-64 AppImage).

> **Early development / experimental.** This code is a working-source preview, not a verified stable release. Some features use reverse-engineered data layouts that may differ between ROM builds. Back up your ROM before editing. This repository contains **source code only**—no ROMs, extracted game files, or other Nintendo assets.

## Features

| Area | Current functionality |
| --- | --- |
| **Pokémon** | Base stats, personal data, learnable compatibility flags, abilities, and searchable species names from the ROM |
| **Evolutions** | Guided method selector, parameter explanations, and named target species selection |
| **Battle sprites** | Base-species front/back previews, PNG import/export, shiny/normal palettes, and Frost-compatible 16 × 1 PNG palette import/export |
| **Trainers** | Trainer details, party editing, and ROM-derived English trainer/class names with search |
| **Starters + Hugh** | Searchable Pokémon choices and validated updates to six early Hugh battles; later evolution-stage controls are previews only |
| **Maps + Events** | Edit existing NPC/sign/warp/trigger records; experimental 3D event positioning viewer and read-only NSBMD-style geometry wireframe preview |
| **Misc sprites** | Raw NARC resource browser with size-checked import/export (not a visual overworld sprite editor) |
| **ROM files** | NitroFS browser with a hexadecimal preview |

### Not yet supported

- Full CTRMap-style terrain, material, texture, model-placement or script editing.
- Creation/deletion of map events; only existing records are editable.
- Later Hugh teams via the second/final evolution selectors (the specific trainer mappings haven't been verified).
- Per-starter shiny/gender overrides, which have deliberately been omitted because the relevant ROM data hasn't been validated.
- Alternate battle sprite forms, animation sequences, and a full visual misc-sprite editor.
- Editing trainer-name strings themselves (currently read-only names).

## Requirements

- **Windows 10/11 or Linux x86-64**. The editor targets cross-platform `net8.0`, using SkiaSharp for PNG/sprite processing. Linux requires a graphical desktop (X11 or XWayland) and common desktop libraries.
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) to build from source.

## Build and run

In PowerShell, at the repository root:

```powershell
dotnet restore .\White2UpgradeEditor.sln
dotnet build .\White2UpgradeEditor.sln -c Release
dotnet run --project .\White2UpgradeEditor\White2UpgradeEditor.csproj -c Release
```

For a distributable folder (requires the .NET runtime on the destination PC):

```powershell
dotnet publish .\White2UpgradeEditor\White2UpgradeEditor.csproj -c Release -r win-x64 --self-contained false -o .\publish\White2UpgradeStudio
dotnet publish .\White2UpgradeUpdater\White2UpgradeUpdater.csproj -c Release -r win-x64 --self-contained false -o .\publish\White2UpgradeStudio
```

### GitHub Actions and automatic preview releases

Every push to `main` automatically builds and tests the Windows editor/updater and the Linux AppImage. **Only after both platforms pass**, GitHub Actions creates a numbered GitHub **prerelease** containing `White2UpgradeStudio-win-x64.zip`, its SHA-256 sidecar, and a `White2UpgradeStudio-v*-x86_64.AppImage`. No manual version tags are necessary.

A preview tag looks like `v0.2.7-alpha.1`, with a version counter increasing for each workflow run; both applications are stamped with that version so the updater recognizes new releases. A push containing multiple commits produces one release for the resulting `main` head, rather than a separate release for each individual commit. Failed builds do not create releases. Pull requests receive validation builds but do not publish releases.

See the repository's [Releases](https://github.com/PokeVibeEdit/White2UpgradeStudio/releases) page for Windows downloads and Linux AppImages, and the [Actions](https://github.com/PokeVibeEdit/White2UpgradeStudio/actions) tab for build logs and artifacts. Releases are experimental, not stable.

## Updates

The downloadable Windows release includes **White2UpgradeUpdater.exe**, a companion console updater.
In the editor, click **Check for updates** in the sidebar. The updater checks the
[GitHub Releases](https://github.com/PokeVibeEdit/White2UpgradeStudio/releases)
feed for a **newer SemVer tag**, including preview releases. It does not silently
install updates and does not modify a ROM.

- Confirm the update to download the release ZIP and its `.sha256` sidecar.
- The updater verifies SHA-256 and extracts with path-traversal checks.
- **Save your ROM edits and close the editor**, then confirm to install.
- The updater runs outside the application folder, backs up files it replaces under
  `%LOCALAPPDATA%\White2UpgradeStudio\UpdaterBackups`, and attempts rollback
  if copying fails. ROMs and save files are not included in the update package.
- Relaunches the editor after a successful update. Old app files not present in
  the new release aren't removed automatically.

**Private GitHub repository:** The updater can read private releases only for
users with access to the repository and a working [GitHub CLI](https://cli.github.com/)
login (`gh auth login`). If the repository becomes public, it can use GitHub's
public API without the CLI. No personal access token is stored by the updater.

**Publishing updates:** Simply commit your changes and push to `main` (or merge a pull request into `main`). The automated release workflow builds, tests, versions, and publishes a prerelease, including the updater-compatible checksum. Creating or pushing tags manually is no longer required.

Checksums detect corrupted downloads or changed bytes in transit; they are not a substitute for cryptographic release signing. The updater still requires user confirmation to install an available update.

## Linux AppImage

Download `White2UpgradeStudio-v*-x86_64.AppImage` from [GitHub Releases](https://github.com/PokeVibeEdit/White2UpgradeStudio/releases). On a Linux x86-64 desktop:

```bash
chmod +x White2UpgradeStudio-v*-x86_64.AppImage
./White2UpgradeStudio-v*-x86_64.AppImage
```

If your Linux distribution lacks FUSE, you can try `APPIMAGE_EXTRACT_AND_RUN=1 ./White2UpgradeStudio-v*-x86_64.AppImage`. The AppImage bundles the .NET runtime, but requires common Linux graphical libraries. It has not been validated on every distribution.

**Updating on Linux:** The sidebar button opens GitHub Releases. Download a newer AppImage and replace your old AppImage manually. The Windows updater does **not** modify AppImages.

To build the AppImage from source (requires Linux, .NET 8 SDK, `curl`, and `rsvg-convert` from `librsvg2-bin`):

```bash
bash scripts/build-appimage.sh 0.2.0-alpha.1
```

Linux CI builds and smoke-tests the AppImage under Xvfb. Do not distribute ROMs or ROM assets alongside the editor.

## Open and save safely

1. Open an extracted White 2 Upgrade ROM filesystem (`data/a/...`) **or** a compatible `.nds` file.
2. Edit the selected resources.
3. Save to **a new path**, not the input ROM or dump.

**Extracted dump mode:** writes only modified NARC files to a separate patch directory, retaining their game-relative paths.

**Direct `.nds` mode:** replaces files only when the rebuilt NARC is the **same byte size**. If the change alters archive size, the app refuses direct-ROM saving; use dump/patch mode and an appropriate ROM file reinsertion workflow instead.

Always keep an untouched backup and test modifications on a copy.

## Where names come from

Species and trainer/class labels are decrypted at runtime from the **ROM you load**. No lists of the game's copyrighted string content are included here. If the expected English text banks are unavailable or incompatible, numeric IDs remain the fallback.

## Data formats and limitations

See [ROM format notes](docs/ROM-FORMAT-NOTES.md) for verified file paths and starter/rival offsets taken from a development dump. For current scope and implementation details, browse the source files in `White2UpgradeEditor/`.

## Development and contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build reports, changes, and pull requests. Bug reports with complete **compiler errors or exception stack traces** are particularly helpful. **Do not attach commercial game ROMs, proprietary game assets, or copyrighted dumps to issues or pull requests.**

## Credits

This is an independent implementation. These projects and resources informed its scope or binary-format investigation; **no code from them is included here**:

- [Frost's Gen 5 Editor](https://github.com/FrostFalcon/FrostsGen5Editor)
- [CTRMap / CTRMapV guide](https://ds-pokemon-hacking.github.io/docs/generation-v/guides/bw_b2w2-using_ctrmap/)
- [Universal Pokémon Randomizer ZX](https://github.com/Ajarmar/universal-pokemon-randomizer-zx)
- [TrainerTyrant](https://github.com/ThirdLemon/TrainerTyrant)
- [Pokémon BW2 decompilation project](https://github.com/fuddlesworth/pokebw2)
- [White 2 Upgrade](https://github.com/ds-pokemon-hacking/White2Upgrade)

Pokémon and related trademarks belong to their respective owners. This is an unofficial fan tool and is not affiliated with Nintendo, Game Freak, or The Pokémon Company.

## License

White 2 Upgrade Studio source code is licensed under the **GNU General Public License, version 3 only** (**GPL-3.0-only**). See [LICENSE](LICENSE) for the full license text. You may use, modify, and redistribute the source under its terms; distributions of modified versions must comply with GPLv3, including the applicable source-code obligations.

The license applies to this editor project, **not** to Pokémon, Nintendo ROMs, artwork, sprites, game text, or any other third-party material. Dependencies remain subject to their own licenses.
