using AAXClean;

namespace AaxToYoto.Core;

/// <summary>
/// Handles decryption and conversion of AAX files to various output formats.
/// Works entirely offline with local files only.
/// </summary>
public class AaxConverter : IDisposable
{
    private readonly AaxFile _aaxFile;
    private readonly Stream _inputStream;
    private bool _disposed;

    /// <summary>
    /// Chapter information extracted from the AAX file.
    /// </summary>
    public ChapterInfo Chapters => _aaxFile.Chapters;

    /// <summary>
    /// Metadata from the AAX file (title, author, narrator, etc.)
    /// </summary>
    public AppleTags Metadata => _aaxFile.AppleTags;

    /// <summary>
    /// Total duration of the audiobook.
    /// </summary>
    public TimeSpan Duration => _aaxFile.Duration;

    private AaxConverter(AaxFile aaxFile, Stream inputStream)
    {
        _aaxFile = aaxFile;
        _inputStream = inputStream;
    }

    /// <summary>
    /// Opens an AAX file and sets the decryption key using activation bytes.
    /// </summary>
    /// <param name="aaxPath">Path to the .aax file</param>
    /// <param name="activationBytes">4-byte activation bytes as hex string (8 characters)</param>
    /// <returns>An AaxConverter ready for conversion</returns>
    public static AaxConverter Open(string aaxPath, string activationBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aaxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationBytes);

        if (!File.Exists(aaxPath))
            throw new FileNotFoundException("AAX file not found", aaxPath);

        var keyBytes = Convert.FromHexString(activationBytes.Trim());
        if (keyBytes.Length != 4)
            throw new ArgumentException("Activation bytes must be exactly 4 bytes (8 hex characters)", nameof(activationBytes));

        var stream = File.OpenRead(aaxPath);
        try
        {
            var aaxFile = new AaxFile(stream);
            aaxFile.SetDecryptionKey(keyBytes);
            return new AaxConverter(aaxFile, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Converts the entire audiobook to a single M4B file.
    /// </summary>
    /// <param name="outputPath">Path for the output file</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ConvertToM4bAsync(
        string outputPath,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        
        using var outputStream = File.Create(outputPath);
        var operation = _aaxFile.ConvertToMp4aAsync(outputStream);
        
        if (progress != null)
        {
            operation.ConversionProgressUpdate += (_, e) => progress(e.FractionCompleted);
        }

        await operation.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Converts the entire audiobook to a single MP3 file.
    /// </summary>
    /// <param name="outputPath">Path for the output file</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ConvertToMp3Async(
        string outputPath,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        using var outputStream = File.Create(outputPath);
        var lameConfig = CreateDefaultLameConfig();
        lameConfig.ID3 = Metadata.ToIDTags();
        
        var operation = _aaxFile.ConvertToMp3Async(outputStream, lameConfig, Chapters);
        
        if (progress != null)
        {
            operation.ConversionProgressUpdate += (_, e) => progress(e.FractionCompleted);
        }

        await operation.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Converts the audiobook to multiple MP3 files, one per chapter.
    /// Ideal for Yoto card creation.
    /// </summary>
    /// <param name="outputDirectory">Directory to place chapter files</param>
    /// <param name="fileNameTemplate">Template for file names. Placeholders: {num}, {title}, {book}</param>
    /// <param name="minChapterDuration">Minimum chapter duration; shorter chapters are merged with next</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created file paths in order</returns>
    public async Task<IReadOnlyList<string>> ConvertToChapterMp3sAsync(
        string outputDirectory,
        string fileNameTemplate = "{num:D2} - {title}",
        TimeSpan? minChapterDuration = null,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        Directory.CreateDirectory(outputDirectory);

        var minDuration = minChapterDuration ?? TimeSpan.FromSeconds(3);
        var chapters = MergeShortChapters(Chapters, minDuration);
        var createdFiles = new List<string>();
        var bookTitle = SanitizeFileName(Metadata.TitleSansUnabridged ?? Metadata.Title ?? "Audiobook");

        int chapterNum = 0;
        FileStream? currentStream = null;

        var lameConfig = CreateDefaultLameConfig();
        lameConfig.ID3 = Metadata.ToIDTags();

        var operation = _aaxFile.ConvertToMultiMp3Async(
            chapters,
            callback =>
            {
                currentStream?.Dispose();
                chapterNum++;
                
                var chapterTitle = SanitizeFileName(callback.Chapter?.Title ?? $"Chapter {chapterNum}");
                var fileName = fileNameTemplate
                    .Replace("{num:D2}", chapterNum.ToString("D2"))
                    .Replace("{num}", chapterNum.ToString())
                    .Replace("{title}", chapterTitle)
                    .Replace("{book}", bookTitle);
                
                var outputPath = Path.Combine(outputDirectory, fileName + ".mp3");
                createdFiles.Add(outputPath);
                
                currentStream = File.Create(outputPath);
                callback.OutputFile = currentStream;
                callback.TrackTitle = callback.Chapter?.Title ?? $"Chapter {chapterNum}";
                callback.TrackNumber = chapterNum;
                callback.TrackCount = chapters.Count;
            },
            lameConfig);

        if (progress != null)
        {
            operation.ConversionProgressUpdate += (_, e) => progress(e.FractionCompleted);
        }

        try
        {
            await operation.WaitAsync(cancellationToken);
        }
        finally
        {
            currentStream?.Dispose();
        }

        return createdFiles;
    }

    /// <summary>
    /// Converts the audiobook to multiple M4B files, one per chapter.
    /// </summary>
    /// <param name="outputDirectory">Directory to place chapter files</param>
    /// <param name="fileNameTemplate">Template for file names. Placeholders: {num}, {title}, {book}</param>
    /// <param name="minChapterDuration">Minimum chapter duration; shorter chapters are merged with next</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created file paths in order</returns>
    public async Task<IReadOnlyList<string>> ConvertToChapterM4bsAsync(
        string outputDirectory,
        string fileNameTemplate = "{num:D2} - {title}",
        TimeSpan? minChapterDuration = null,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        Directory.CreateDirectory(outputDirectory);

        var minDuration = minChapterDuration ?? TimeSpan.FromSeconds(3);
        var chapters = MergeShortChapters(Chapters, minDuration);
        var createdFiles = new List<string>();
        var bookTitle = SanitizeFileName(Metadata.TitleSansUnabridged ?? Metadata.Title ?? "Audiobook");

        int chapterNum = 0;
        FileStream? currentStream = null;

        var operation = _aaxFile.ConvertToMultiMp4aAsync(
            chapters,
            callback =>
            {
                currentStream?.Dispose();
                chapterNum++;
                
                var chapterTitle = SanitizeFileName(callback.Chapter?.Title ?? $"Chapter {chapterNum}");
                var fileName = fileNameTemplate
                    .Replace("{num:D2}", chapterNum.ToString("D2"))
                    .Replace("{num}", chapterNum.ToString())
                    .Replace("{title}", chapterTitle)
                    .Replace("{book}", bookTitle);
                
                var outputPath = Path.Combine(outputDirectory, fileName + ".m4b");
                createdFiles.Add(outputPath);
                
                currentStream = File.Create(outputPath);
                callback.OutputFile = currentStream;
                callback.TrackTitle = callback.Chapter?.Title ?? $"Chapter {chapterNum}";
                callback.TrackNumber = chapterNum;
                callback.TrackCount = chapters.Count;
            });

        if (progress != null)
        {
            operation.ConversionProgressUpdate += (_, e) => progress(e.FractionCompleted);
        }

        try
        {
            await operation.WaitAsync(cancellationToken);
        }
        finally
        {
            currentStream?.Dispose();
        }

        return createdFiles;
    }

    private static ChapterInfo MergeShortChapters(ChapterInfo original, TimeSpan minDuration)
    {
        var merged = new ChapterInfo(original.StartOffset);
        var runningDuration = TimeSpan.Zero;
        string? pendingTitle = null;

        foreach (var chapter in original.Chapters)
        {
            if (runningDuration == TimeSpan.Zero)
                pendingTitle = chapter.Title;

            runningDuration += chapter.Duration;

            if (runningDuration >= minDuration)
            {
                merged.AddChapter(pendingTitle ?? chapter.Title, runningDuration);
                runningDuration = TimeSpan.Zero;
                pendingTitle = null;
            }
        }

        // Add any remaining content as final chapter
        if (runningDuration > TimeSpan.Zero && pendingTitle != null)
        {
            merged.AddChapter(pendingTitle, runningDuration);
        }

        return merged;
    }

    private static NAudio.Lame.LameConfig CreateDefaultLameConfig()
    {
        return new NAudio.Lame.LameConfig
        {
            Mode = NAudio.Lame.MPEGMode.Mono,
            Quality = NAudio.Lame.EncoderQuality.Standard,
            VBR = NAudio.Lame.VBRMode.Default,
            VBRQuality = 5,
            WriteVBRTag = true,
            OutputSampleRate = 44100
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _aaxFile.Dispose();
            _inputStream.Dispose();
            _disposed = true;
        }
    }
}


