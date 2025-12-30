# AAX to Yoto Card Converter

Convert Audible `.aax` files to Yoto Make Your Own (MYO) card compatible MP3 files with automatic chapter splitting.

**This tool works entirely offline** — no Audible account connection needed. You just need your `.aax` file and activation bytes.

## Features

- 🔓 Decrypt `.aax` files using activation bytes
- 📑 Automatic chapter splitting perfect for Yoto cards
- 🎵 MP3 and M4B output formats
- 📋 Playlist generation (M3U)
- 🖼️ Cover art extraction
- 📊 Metadata preservation
- 💻 Simple command-line interface

## Installation

### Prerequisites

- [.NET 8.0 SDK or later](https://dotnet.microsoft.com/download)
- Your Audible activation bytes (see below)

### Build from Source

```bash
git clone <repo-url>
cd Libation
dotnet build
```

### Run directly

```bash
dotnet run --project src/AaxToYoto.Cli -- <command> [options]
```

## Getting Your Activation Bytes

Your activation bytes are a 4-byte (8 hex character) key specific to your Audible account. You can obtain them using:

- **[audible-cli](https://github.com/mkb79/audible-cli)** - Cross-platform command line tool
- **[inAudible](https://github.com/rmcrackan/inAudible)** - Windows GUI tool

Example activation bytes format: `ABCD1234`

## Usage

### Convert for Yoto Cards (Recommended)

The `yoto` command creates chapter-split MP3 files optimized for Yoto MYO cards:

```bash
aax2yoto yoto -i "My Audiobook.aax" -a ABCD1234 -o ./output
```

This creates:
- Individual MP3 file per chapter
- M3U playlist
- Cover art (cover.jpg)
- Metadata info file

**Options:**
- `-i, --input` - Path to .aax file (required)
- `-a, --activation` - Activation bytes (required)
- `-o, --output` - Output directory (default: current directory)
- `-m, --min-chapter` - Minimum chapter duration in seconds (default: 10)
- `--no-playlist` - Don't create M3U playlist
- `--no-cover` - Don't extract cover art
- `--no-metadata` - Don't create info.txt file
- `-t, --include-title` - Include book title in each filename

### Convert to Single File

```bash
aax2yoto single -i "My Audiobook.aax" -a ABCD1234 -o ./output -f mp3
```

**Options:**
- `-f, --format` - Output format: `mp3` or `m4b` (default: mp3)

### Convert to Chapter Files

```bash
aax2yoto chapters -i "My Audiobook.aax" -a ABCD1234 -o ./output -f mp3
```

**Options:**
- `-f, --format` - Output format: `mp3` or `m4b` (default: mp3)
- `-m, --min-chapter` - Minimum chapter duration in seconds (default: 10)

### Show Audiobook Info

```bash
aax2yoto info -i "My Audiobook.aax" -a ABCD1234
```

Displays metadata and chapter information without converting.

## Example Output

Running `yoto` command produces a directory structure like:

```
output/
└── My Audiobook/
    ├── 01 - Opening Credits.mp3
    ├── 02 - Chapter 1.mp3
    ├── 03 - Chapter 2.mp3
    ├── ...
    ├── My Audiobook.m3u
    ├── cover.jpg
    └── info.txt
```

## Creating Your Yoto Card

1. Run the `yoto` command to create your chapter files
2. Open the [Yoto app](https://yotoplay.com/app)
3. Go to "Make Your Own"
4. Upload the MP3 files in order
5. Link to a blank MYO card

## Technical Details

- Uses [AAXClean](https://github.com/Mbucari/AAXClean) for decryption
- MP3 encoding via LAME
- M4B output preserves AAC audio with chapters

## License

GNU General Public License v3.0 - See [LICENSE](LICENSE) file

## Disclaimer

This tool is intended for personal backup of audiobooks you legally own. Ensure you comply with all applicable laws and Audible's terms of service.
