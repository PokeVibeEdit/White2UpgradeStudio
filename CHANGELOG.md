# Changelog

## Unreleased — GitHub Release updater

- Added a standalone Windows update utility and sidebar **Check for updates** entry.
- Newer GitHub Releases, including prereleases, can be downloaded with GitHub CLI for private repos or public HTTPS releases.
- ZIP updates require matching SHA-256 checksums and validate extraction paths.
- Existing app files are backed up and restored on failure; the running editor must be closed before updating.
- Windows CI packages the updater alongside the editor; tagged preview releases include checksums and stamped versions.
- No unattended updates or ROM file modifications.

## Initial GPLv3 release — licensing and source snapshot

- Added a GNU GPL version 3 (GPL-3.0-only) license.
- GitHub-ready project structure and Windows CI / prerelease workflows.
- Avalonia 11 desktop editor with Pokémon, evolutions, trainer, starter/rival, sprites, ROM files, and experimental map/event tools.
- ROM-derived species/trainer names and Frost-compatible palette PNG support.
- Experimental NSBMD wireframe viewer and 3D event-position preview.

This is a development snapshot, not a tested stable release. See the README for detailed limitations.
