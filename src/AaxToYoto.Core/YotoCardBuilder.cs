namespace AaxToYoto.Core;

/// <summary>
/// Helper for creating Yoto Make Your Own (MYO) card compatible audio files.
/// Yoto cards work best with MP3 files split by chapter, organized in folders.
/// </summary>
public class YotoCardBuilder
{
    /// <summary>
    /// Default minimum chapter duration for Yoto cards.
    /// Very short chapters can confuse the player UI.
    /// </summary>
    public static readonly TimeSpan DefaultMinChapterDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum recommended file size for Yoto cards (in bytes).
    /// While there's no hard limit, files over 100MB may have issues.
    /// </summary>
    public const long MaxRecommendedFileSize = 100 * 1024 * 1024;

    private readonly AaxConverter _converter;
    private readonly YotoOptions _options;

    public YotoCardBuilder(AaxConverter converter, YotoOptions? options = null)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _options = options ?? new YotoOptions();
    }

    /// <summary>
    /// Creates Yoto-compatible chapter MP3 files from the audiobook.
    /// Files are named and organized for easy upload to the Yoto app.
    /// </summary>
    /// <param name="outputDirectory">Base output directory</param>
    /// <param name="progress">Progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing created files and metadata</returns>
    public async Task<YotoBuildResult> BuildAsync(
        string outputDirectory,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Create a subdirectory named after the book
        var bookTitle = SanitizeDirectoryName(
            _converter.Metadata.TitleSansUnabridged ?? 
            _converter.Metadata.Title ?? 
            "Audiobook");
        
        var bookDir = Path.Combine(outputDirectory, bookTitle);
        Directory.CreateDirectory(bookDir);

        // Build file name template based on options
        var template = _options.IncludeBookTitleInFileName
            ? "{book} - {num:D2} - {title}"
            : "{num:D2} - {title}";

        var files = await _converter.ConvertToChapterMp3sAsync(
            bookDir,
            template,
            _options.MinChapterDuration ?? DefaultMinChapterDuration,
            progress,
            cancellationToken);

        // Create playlist file if requested
        string? playlistPath = null;
        if (_options.CreatePlaylist)
        {
            playlistPath = await CreatePlaylistAsync(bookDir, bookTitle, files);
        }

        // Create metadata file for reference
        string? metadataPath = null;
        if (_options.CreateMetadataFile)
        {
            metadataPath = await CreateMetadataFileAsync(bookDir, bookTitle);
        }

        // Extract cover art if available
        string? coverPath = null;
        if (_options.ExtractCoverArt && _converter.Metadata.Cover != null)
        {
            coverPath = Path.Combine(bookDir, "cover.jpg");
            await File.WriteAllBytesAsync(coverPath, _converter.Metadata.Cover, cancellationToken);
        }

        return new YotoBuildResult
        {
            OutputDirectory = bookDir,
            ChapterFiles = files.ToList(),
            PlaylistFile = playlistPath,
            MetadataFile = metadataPath,
            CoverArtFile = coverPath,
            TotalDuration = _converter.Duration,
            ChapterCount = files.Count,
            BookTitle = _converter.Metadata.Title ?? bookTitle,
            Author = _converter.Metadata.FirstAuthor,
            Narrator = _converter.Metadata.Narrator
        };
    }

    private async Task<string> CreatePlaylistAsync(string bookDir, string bookTitle, IReadOnlyList<string> files)
    {
        var playlistPath = Path.Combine(bookDir, $"{bookTitle}.m3u");
        var lines = new List<string> { "#EXTM3U", $"#PLAYLIST:{bookTitle}" };
        
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            lines.Add($"#EXTINF:-1,{Path.GetFileNameWithoutExtension(fileName)}");
            lines.Add(fileName);
        }

        await File.WriteAllLinesAsync(playlistPath, lines);
        return playlistPath;
    }

    private async Task<string> CreateMetadataFileAsync(string bookDir, string bookTitle)
    {
        var metadataPath = Path.Combine(bookDir, "info.txt");
        var metadata = _converter.Metadata;
        
        var lines = new List<string>
        {
            $"Title: {metadata.Title ?? "Unknown"}",
            $"Author: {metadata.FirstAuthor ?? "Unknown"}",
            $"Narrator: {metadata.Narrator ?? "Unknown"}",
            $"Duration: {_converter.Duration:hh\\:mm\\:ss}",
            $"Chapters: {_converter.Chapters.Count}",
            "",
            "Chapter List:",
        };

        int i = 1;
        foreach (var chapter in _converter.Chapters.Chapters)
        {
            lines.Add($"  {i++}. {chapter.Title} ({chapter.Duration:mm\\:ss})");
        }

        await File.WriteAllLinesAsync(metadataPath, lines);
        return metadataPath;
    }

    private static string SanitizeDirectoryName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }
}

/// <summary>
/// Options for building Yoto-compatible audio files.
/// </summary>
public class YotoOptions
{
    /// <summary>
    /// Minimum chapter duration. Chapters shorter than this will be merged with the next chapter.
    /// Default: 10 seconds
    /// </summary>
    public TimeSpan? MinChapterDuration { get; set; }

    /// <summary>
    /// Whether to include the book title in each file name.
    /// Default: false
    /// </summary>
    public bool IncludeBookTitleInFileName { get; set; }

    /// <summary>
    /// Whether to create an M3U playlist file.
    /// Default: true
    /// </summary>
    public bool CreatePlaylist { get; set; } = true;

    /// <summary>
    /// Whether to create a text file with book metadata.
    /// Default: true
    /// </summary>
    public bool CreateMetadataFile { get; set; } = true;

    /// <summary>
    /// Whether to extract and save cover art as cover.jpg.
    /// Default: true
    /// </summary>
    public bool ExtractCoverArt { get; set; } = true;
}

/// <summary>
/// Result of building Yoto card files.
/// </summary>
public class YotoBuildResult
{
    /// <summary>
    /// Directory containing all output files.
    /// </summary>
    public required string OutputDirectory { get; init; }

    /// <summary>
    /// List of chapter MP3 file paths in order.
    /// </summary>
    public required List<string> ChapterFiles { get; init; }

    /// <summary>
    /// Path to the M3U playlist file, if created.
    /// </summary>
    public string? PlaylistFile { get; init; }

    /// <summary>
    /// Path to the metadata text file, if created.
    /// </summary>
    public string? MetadataFile { get; init; }

    /// <summary>
    /// Path to the cover art file, if extracted.
    /// </summary>
    public string? CoverArtFile { get; init; }

    /// <summary>
    /// Total duration of the audiobook.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Number of chapters created.
    /// </summary>
    public int ChapterCount { get; init; }

    /// <summary>
    /// Book title from metadata.
    /// </summary>
    public string? BookTitle { get; init; }

    /// <summary>
    /// Author from metadata.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Narrator from metadata.
    /// </summary>
    public string? Narrator { get; init; }
}


