# Contributing

This project is an early source preview. Please keep contributions focused, reviewable, and safe for ROM data.

## Local development

1. Install the .NET 8 SDK and use Windows.
2. Run `dotnet restore .\White2UpgradeEditor.sln`.
3. Run `dotnet build .\White2UpgradeEditor.sln -c Release`.
4. Test using a personal backup of a compatible ROM or extracted dump.

## Changes and pull requests

- Open an issue for larger feature proposals before writing broad changes.
- Keep binary parsers strict about bounds and preserve unknown bytes when writing records.
- Validate archive member counts and formats before applying edits.
- Favor non-destructive save operations and meaningful error messages.
- Describe what you tested, using which editing mode (extracted dump or `.nds`).
- When modifying UI code, confirm that a single control is never assigned two visual parents.
- Do not add commercial ROMs, asset dumps, proprietary sprites, or actual game text data to the repository.

## Reporting problems

When opening a bug report, include the steps to reproduce, your Windows/.NET version, and a complete error stack trace or build log. Remove personal paths and ROM contents before posting.

## Licensing

The project is licensed under **GNU GPL version 3 only (GPL-3.0-only)**. See [LICENSE](LICENSE). By submitting a contribution, you agree that your contribution may be distributed under that license, and you confirm you have the right to license the contributed code. Do not contribute code or assets under incompatible terms.
