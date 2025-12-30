using System.CommandLine;
using AaxToYoto.Core;

namespace AaxToYoto.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("AAX to Yoto Card Converter - Convert Audible .aax files to Yoto-compatible MP3s with chapter splitting");

        // Global options
        var inputOption = new Option<FileInfo>(
            aliases: ["--input", "-i"],
            description: "Path to the .aax file to convert")
        { IsRequired = true };

        var activationOption = new Option<string>(
            aliases: ["--activation", "-a"],
            description: "Audible activation bytes (8 hex characters, e.g., 'ABCD1234')")
        { IsRequired = true };

        var outputOption = new Option<DirectoryInfo>(
            aliases: ["--output", "-o"],
            description: "Output directory (defaults to current directory)")
        { IsRequired = false };

        // Yoto command
        var yotoCommand = new Command("yoto", "Convert AAX to Yoto card format (chapter-split MP3s)")
        {
            inputOption,
            activationOption,
            outputOption
        };

        var minChapterOption = new Option<int>(
            aliases: ["--min-chapter", "-m"],
            getDefaultValue: () => 10,
            description: "Minimum chapter duration in seconds (shorter chapters merged with next)");

        var noPlaylistOption = new Option<bool>(
            aliases: ["--no-playlist"],
            getDefaultValue: () => false,
            description: "Don't create M3U playlist file");

        var noCoverOption = new Option<bool>(
            aliases: ["--no-cover"],
            getDefaultValue: () => false,
            description: "Don't extract cover art");

        var noMetadataOption = new Option<bool>(
            aliases: ["--no-metadata"],
            getDefaultValue: () => false,
            description: "Don't create metadata info file");

        var includeBookTitleOption = new Option<bool>(
            aliases: ["--include-title", "-t"],
            getDefaultValue: () => false,
            description: "Include book title in each chapter filename");

        yotoCommand.AddOption(minChapterOption);
        yotoCommand.AddOption(noPlaylistOption);
        yotoCommand.AddOption(noCoverOption);
        yotoCommand.AddOption(noMetadataOption);
        yotoCommand.AddOption(includeBookTitleOption);

        yotoCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForOption(inputOption)!;
            var activation = context.ParseResult.GetValueForOption(activationOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption) ?? new DirectoryInfo(Directory.GetCurrentDirectory());
            var minChapter = context.ParseResult.GetValueForOption(minChapterOption);
            var noPlaylist = context.ParseResult.GetValueForOption(noPlaylistOption);
            var noCover = context.ParseResult.GetValueForOption(noCoverOption);
            var noMetadata = context.ParseResult.GetValueForOption(noMetadataOption);
            var includeTitle = context.ParseResult.GetValueForOption(includeBookTitleOption);

            context.ExitCode = await ConvertToYoto(
                input, activation, output,
                minChapter, !noPlaylist, !noCover, !noMetadata, includeTitle,
                context.GetCancellationToken());
        });

        // Single file command
        var singleCommand = new Command("single", "Convert AAX to a single audio file")
        {
            inputOption,
            activationOption,
            outputOption
        };

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "mp3",
            description: "Output format: 'mp3' or 'm4b'");
        singleCommand.AddOption(formatOption);

        singleCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForOption(inputOption)!;
            var activation = context.ParseResult.GetValueForOption(activationOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption) ?? new DirectoryInfo(Directory.GetCurrentDirectory());
            var format = context.ParseResult.GetValueForOption(formatOption)!;

            context.ExitCode = await ConvertToSingle(
                input, activation, output, format,
                context.GetCancellationToken());
        });

        // Chapters command (split by chapter, but not Yoto-specific)
        var chaptersCommand = new Command("chapters", "Convert AAX to multiple files, one per chapter")
        {
            inputOption,
            activationOption,
            outputOption
        };
        
        chaptersCommand.AddOption(formatOption);
        chaptersCommand.AddOption(minChapterOption);

        chaptersCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForOption(inputOption)!;
            var activation = context.ParseResult.GetValueForOption(activationOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption) ?? new DirectoryInfo(Directory.GetCurrentDirectory());
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var minChapter = context.ParseResult.GetValueForOption(minChapterOption);

            context.ExitCode = await ConvertToChapters(
                input, activation, output, format, minChapter,
                context.GetCancellationToken());
        });

        // Info command
        var infoCommand = new Command("info", "Display information about an AAX file without converting")
        {
            inputOption,
            activationOption
        };

        infoCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForOption(inputOption)!;
            var activation = context.ParseResult.GetValueForOption(activationOption)!;

            context.ExitCode = await ShowInfo(input, activation);
        });

        rootCommand.AddCommand(yotoCommand);
        rootCommand.AddCommand(singleCommand);
        rootCommand.AddCommand(chaptersCommand);
        rootCommand.AddCommand(infoCommand);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task<int> ConvertToYoto(
        FileInfo input, string activation, DirectoryInfo output,
        int minChapter, bool createPlaylist, bool extractCover, bool createMetadata, bool includeTitle,
        CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"🎧 Opening: {input.Name}");
            using var converter = AaxConverter.Open(input.FullName, activation);
            
            Console.WriteLine($"📖 Title: {converter.Metadata.Title}");
            Console.WriteLine($"✍️  Author: {converter.Metadata.FirstAuthor}");
            Console.WriteLine($"🎙️  Narrator: {converter.Metadata.Narrator}");
            Console.WriteLine($"⏱️  Duration: {converter.Duration:hh\\:mm\\:ss}");
            Console.WriteLine($"📑 Chapters: {converter.Chapters.Count}");
            Console.WriteLine();

            var options = new YotoOptions
            {
                MinChapterDuration = TimeSpan.FromSeconds(minChapter),
                CreatePlaylist = createPlaylist,
                ExtractCoverArt = extractCover,
                CreateMetadataFile = createMetadata,
                IncludeBookTitleInFileName = includeTitle
            };

            var builder = new YotoCardBuilder(converter, options);
            
            Console.Write("🔄 Converting: ");
            var lastPercent = -1;

            var result = await builder.BuildAsync(
                output.FullName,
                progress =>
                {
                    var percent = (int)(progress * 100);
                    if (percent != lastPercent)
                    {
                        Console.Write($"\r🔄 Converting: {percent}% ");
                        lastPercent = percent;
                    }
                },
                ct);

            Console.WriteLine("\r✅ Conversion complete!     ");
            Console.WriteLine();
            Console.WriteLine($"📁 Output: {result.OutputDirectory}");
            Console.WriteLine($"📄 Files created: {result.ChapterFiles.Count} chapter MP3s");
            
            if (result.PlaylistFile != null)
                Console.WriteLine($"🎵 Playlist: {Path.GetFileName(result.PlaylistFile)}");
            if (result.CoverArtFile != null)
                Console.WriteLine($"🖼️  Cover: {Path.GetFileName(result.CoverArtFile)}");
            if (result.MetadataFile != null)
                Console.WriteLine($"ℹ️  Info: {Path.GetFileName(result.MetadataFile)}");

            Console.WriteLine();
            Console.WriteLine("🎯 Ready for Yoto! Upload these files to the Yoto app to create your card.");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n❌ Conversion cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
            return 1;
        }
    }

    static async Task<int> ConvertToSingle(
        FileInfo input, string activation, DirectoryInfo output, string format,
        CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"🎧 Opening: {input.Name}");
            using var converter = AaxConverter.Open(input.FullName, activation);
            
            PrintMetadata(converter);

            var ext = format.ToLowerInvariant() == "m4b" ? ".m4b" : ".mp3";
            var outputName = SanitizeFileName(converter.Metadata.TitleSansUnabridged ?? converter.Metadata.Title ?? "audiobook");
            var outputPath = Path.Combine(output.FullName, outputName + ext);

            Console.Write("🔄 Converting: ");
            var lastPercent = -1;

            if (ext == ".m4b")
            {
                await converter.ConvertToM4bAsync(outputPath, progress =>
                {
                    var percent = (int)(progress * 100);
                    if (percent != lastPercent)
                    {
                        Console.Write($"\r🔄 Converting: {percent}% ");
                        lastPercent = percent;
                    }
                }, ct);
            }
            else
            {
                await converter.ConvertToMp3Async(outputPath, progress =>
                {
                    var percent = (int)(progress * 100);
                    if (percent != lastPercent)
                    {
                        Console.Write($"\r🔄 Converting: {percent}% ");
                        lastPercent = percent;
                    }
                }, ct);
            }

            Console.WriteLine("\r✅ Conversion complete!     ");
            Console.WriteLine($"📄 Output: {outputPath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n❌ Conversion cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
            return 1;
        }
    }

    static async Task<int> ConvertToChapters(
        FileInfo input, string activation, DirectoryInfo output, string format, int minChapter,
        CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"🎧 Opening: {input.Name}");
            using var converter = AaxConverter.Open(input.FullName, activation);
            
            PrintMetadata(converter);

            var bookTitle = SanitizeFileName(converter.Metadata.TitleSansUnabridged ?? converter.Metadata.Title ?? "audiobook");
            var bookDir = Path.Combine(output.FullName, bookTitle);

            Console.Write("🔄 Converting: ");
            var lastPercent = -1;
            var minDuration = TimeSpan.FromSeconds(minChapter);

            IReadOnlyList<string> files;
            if (format.ToLowerInvariant() == "m4b")
            {
                files = await converter.ConvertToChapterM4bsAsync(bookDir, "{num:D2} - {title}", minDuration, progress =>
                {
                    var percent = (int)(progress * 100);
                    if (percent != lastPercent)
                    {
                        Console.Write($"\r🔄 Converting: {percent}% ");
                        lastPercent = percent;
                    }
                }, ct);
            }
            else
            {
                files = await converter.ConvertToChapterMp3sAsync(bookDir, "{num:D2} - {title}", minDuration, progress =>
                {
                    var percent = (int)(progress * 100);
                    if (percent != lastPercent)
                    {
                        Console.Write($"\r🔄 Converting: {percent}% ");
                        lastPercent = percent;
                    }
                }, ct);
            }

            Console.WriteLine("\r✅ Conversion complete!     ");
            Console.WriteLine($"📁 Output: {bookDir}");
            Console.WriteLine($"📄 Files created: {files.Count}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n❌ Conversion cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
            return 1;
        }
    }

    static Task<int> ShowInfo(FileInfo input, string activation)
    {
        try
        {
            Console.WriteLine($"🎧 Opening: {input.Name}");
            using var converter = AaxConverter.Open(input.FullName, activation);
            
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("                      AUDIOBOOK INFO                        ");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine($"  Title:     {converter.Metadata.Title}");
            Console.WriteLine($"  Author:    {converter.Metadata.FirstAuthor}");
            Console.WriteLine($"  Narrator:  {converter.Metadata.Narrator}");
            Console.WriteLine($"  Album:     {converter.Metadata.Album}");
            Console.WriteLine($"  Duration:  {converter.Duration:hh\\:mm\\:ss}");
            Console.WriteLine($"  Copyright: {converter.Metadata.Copyright}");
            Console.WriteLine($"  Chapters:  {converter.Chapters.Count}");
            Console.WriteLine($"  Has Cover: {(converter.Metadata.Cover != null ? "Yes" : "No")}");
            Console.WriteLine();
            Console.WriteLine("───────────────────────────────────────────────────────────");
            Console.WriteLine("                       CHAPTERS                            ");
            Console.WriteLine("───────────────────────────────────────────────────────────");
            Console.WriteLine();

            int i = 1;
            foreach (var chapter in converter.Chapters.Chapters)
            {
                var num = i.ToString().PadLeft(3);
                Console.WriteLine($"  {num}. {chapter.Title,-40} [{chapter.Duration:mm\\:ss}]");
                i++;
            }

            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════");

            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    static void PrintMetadata(AaxConverter converter)
    {
        Console.WriteLine($"📖 Title: {converter.Metadata.Title}");
        Console.WriteLine($"✍️  Author: {converter.Metadata.FirstAuthor}");
        Console.WriteLine($"🎙️  Narrator: {converter.Metadata.Narrator}");
        Console.WriteLine($"⏱️  Duration: {converter.Duration:hh\\:mm\\:ss}");
        Console.WriteLine($"📑 Chapters: {converter.Chapters.Count}");
        Console.WriteLine();
    }

    static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }
}


