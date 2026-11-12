# Supplied White 2 Upgrade dump — verification notes

The supplied `White2Upgrade dump.7z` was extracted locally for inspection. The archive structures below were read from the extracted files and verified by parsing their NARC FAT/GMIF member ranges.

| Path | Member count | Observed structure |
|---|---:|---|
| `data/a/0/1/6` | 827 | First 826 members are 76 bytes; final member is auxiliary data |
| `data/a/0/1/9` | 781 | 48-byte records, seven 6-byte evolution slots in the first 42 bytes |
| `data/a/0/9/1` | 814 | 20-byte trainer metadata records |
| `data/a/0/9/2` | 814 | Variable-size party records; first entries include 8- and 16-byte members |
| `data/a/0/5/6` | 1,289 | Starter-selection script at member 854; member length 7,996 bytes |
| `data/a/0/0/4` | 17,985 | Battle sprite resources; includes empty female sprite slots |

## Starter/rival values observed

Starter member 854 has these values repeated at the three expected operands per selection branch:

- starter slot 1: 495 at `0x58B`, `0x590`, `0x595`
- starter slot 2: 498 at `0x5C0`, `0x5C5`, `0x5CA`
- starter slot 3: 501 at `0x5E2`, `0x5E7`, `0x5EC`

The six early Hugh party records match the starter counter-pick mapping:

| Trainer | Class ID | Party count | Level | Species |
|---:|---:|---:|---:|---:|
| 161 | 145 | 1 | 5 | 498 |
| 162 | 145 | 1 | 5 | 501 |
| 163 | 145 | 1 | 5 | 495 |
| 166 | 145 | 1 | 8 | 498 |
| 167 | 145 | 1 | 8 | 501 |
| 168 | 145 | 1 | 8 | 495 |

The updated app decodes English species names from system-message NARC `a/0/0/2`, member 90 (724 strings, IDs 1–721). Verified examples: #1 Bulbasaur, #25 Pikachu, #495 Snivy, #498 Tepig, #501 Oshawott, #649 Genesect, #721 Volcanion. Other name categories remain numeric.

## Important save behavior

- Extracted-dump mode writes only changed NARC archives to a separate patch directory.
- Direct `.nds` mode prebuilds and size-checks every dirty archive before replacing any in-memory ROM bytes. If any archive changes size, the save is rejected before the ROM image is modified.
- The input ROM/dump is never intentionally overwritten by Save As.
