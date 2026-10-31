using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaForge.Services;

public sealed record ConversionOptions(
    string Format,
    int? Width = null,
    int? Height = null,
    int? Quality = null,
    int Rotation = 0);

public sealed record ConversionResult(string FileName, string FullPath, string Format);

public sealed record ConversionProgress(string Message, double? Percent = null);

public sealed class ConversionService
{
    public const long MaximumInputBytes = 100L * 1024 * 1024;
    public const int MaximumDimension = 16_000;
    private const int MaximumDiagnosticLength = 600;
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleWorkDirectoryAge = TimeSpan.FromHours(24);
    private static readonly HttpClient DefaultHttpClient = UrlSafetyPolicy.Default.CreateHttpClient();
    private static readonly HashSet<string> VideoInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "m4v", "mov", "mkv", "avi", "webm", "wmv", "flv", "mpeg", "mpg", "3gp"
    };
    private static readonly HashSet<string> AudioInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "wma"
    };
    private static readonly HashSet<string> DocumentInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "doc", "docx", "docm", "ppt", "pptx", "pptm", "pps", "ppsx", "ppsm", "pot",
        "xls", "xlsx", "xlsm", "xlsb", "odt", "ods", "odp", "rtf", "epub", "csv"
    };
    public static readonly HashSet<string> KnownImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "webp", "gif", "svg", "bmp", "ico", "tiff", "tif", "avif",
        "heic", "psd", "eps", "tga", "wbmp", "jp2", "jxl", "pcx", "dds", "ppm",
        "pgm", "pbm", "pam", "dpx", "exr", "hdr", "cur"
    };
    public static readonly HashSet<string> KnownTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "md", "markdown", "txt", "text", "log", "json", "xml", "yaml", "yml",
        "ini", "cfg", "conf", "sql", "sh", "bat", "cmd", "ps1", "cs", "js",
        "ts", "jsx", "tsx", "css", "scss", "less", "toml", "env", "properties",
        "c", "cpp", "h", "hpp", "java", "rs", "go", "rb", "php", "py"
    };
    private static readonly string[] MediaOutputFormats =
    [
        "mp4", "webm", "mkv", "avi", "mov", "mp3", "wav", "m4a", "aac", "md", "txt", "zip"
    ];
    private readonly string _toolsDirectory;
    private readonly string _temporaryRoot;
    private readonly UrlSafetyPolicy _urlPolicy;
    private readonly HttpClient _httpClient;

    public ConversionService(string? toolsDirectory = null, string? temporaryRoot = null, UrlSafetyPolicy? urlPolicy = null)
    {
        _toolsDirectory = Path.GetFullPath(toolsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Tools"));
        _temporaryRoot = Path.GetFullPath(temporaryRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaForge",
            "Temp"));
        _urlPolicy = urlPolicy ?? UrlSafetyPolicy.Default;
        _httpClient = ReferenceEquals(_urlPolicy, UrlSafetyPolicy.Default)
            ? DefaultHttpClient
            : _urlPolicy.CreateHttpClient();
    }

    public async Task<IReadOnlyList<string>> GetAvailableImageFormatsAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunToolAsync(
            ResolveTool(Path.Combine("imagemagick", "magick.exe")),
            ["identify", "-list", "format"],
            _toolsDirectory,
            cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException(LastUsefulLine(result.StandardError + Environment.NewLine + result.StandardOutput)
                ?? "Não foi possível consultar os formatos do ImageMagick.");
        }

        var writable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"^\s*([A-Za-z0-9]+)\*?\s+([r-][w-][+-]?)\s+");
            if (match.Success && match.Groups[2].Value.Contains('w'))
            {
                writable.Add(match.Groups[1].Value);
            }
        }

        var imageFormats = KnownImageExtensions
            .Where(format => writable.Contains(ToMagickFormat(format)));

        return imageFormats.Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetAvailableFormatsAsync(CancellationToken cancellationToken = default)
    {
        var imageFormats = await GetAvailableImageFormatsAsync(cancellationToken);
        return imageFormats.Concat(MediaOutputFormats).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Remove pastas de trabalho abandonadas (por exemplo após um corte de energia durante a transcodificação).
    /// Só toca em pastas com nome GUID ("N") mais antigas do que <paramref name="maxAge"/>.
    /// </summary>
    public int CleanupStaleWorkDirectories(TimeSpan maxAge)
    {
        if (!Directory.Exists(_temporaryRoot))
        {
            return 0;
        }

        var removed = 0;
        var threshold = DateTime.UtcNow - maxAge;
        foreach (var directory in Directory.EnumerateDirectories(_temporaryRoot))
        {
            var name = Path.GetFileName(directory);
            if (name.Length != 32 || !Guid.TryParseExact(name, "N", out _))
            {
                continue;
            }
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < threshold)
                {
                    Directory.Delete(directory, recursive: true);
                    removed++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Em uso ou sem permissão: fica para a próxima limpeza.
            }
        }
        return removed;
    }

    public async Task<ConversionResult> ConvertAsync(
        string sourcePath,
        ConversionOptions options,
        string outputDirectory,
        IProgress<ConversionProgress>? progress = null,
        Func<ReadOnlyMemory<byte>, string, CancellationToken, Task<string>>? documentConverter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        var source = Path.GetFullPath(sourcePath);
        var info = new FileInfo(source);
        if (!info.Exists || info.Length == 0)
        {
            throw new ArgumentException("Selecione um ficheiro existente e não vazio.", nameof(sourcePath));
        }
        if (info.Length > MaximumInputBytes)
        {
            throw new ArgumentException("O ficheiro excede o limite de 100 MB.", nameof(sourcePath));
        }

        var extension = Path.GetExtension(source).TrimStart('.').ToLowerInvariant();
        var format = NormalizeFormat(options.Format);
        var targetDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(targetDirectory);
        Directory.CreateDirectory(_temporaryRoot);
        CleanupStaleWorkDirectories(StaleWorkDirectoryAge);
        var workDirectory = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ConversionProgress("A preparar a conversão…"));
            string resultPath;
            string resultFormat;
            if (extension == "pdf" && format is "md" or "txt")
            {
                resultPath = await ConvertPdfToTextAsync(source, format, workDirectory, progress, cancellationToken);
                resultFormat = format;
            }
            else if (extension == "pdf")
            {
                (resultPath, resultFormat) = await ConvertPdfToImagesAsync(source, format, options, workDirectory, progress, cancellationToken);
            }
            else if (VideoInputs.Contains(extension) || AudioInputs.Contains(extension))
            {
                (resultPath, resultFormat) = await ConvertMediaAsync(source, extension, format, options, workDirectory, progress, cancellationToken);
            }
            else if (DocumentInputs.Contains(extension))
            {
                if (format != "md" || documentConverter is null)
                {
                    throw new InvalidOperationException("Este documento requer o conversor local Office/OpenDocument e uma saída Markdown.");
                }

                progress?.Report(new ConversionProgress("A converter o documento para Markdown…"));
                var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
                var markdown = await documentConverter(bytes, extension, cancellationToken);
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    throw new InvalidOperationException("O conversor não encontrou conteúdo no documento.");
                }
                resultPath = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido.md");
                await File.WriteAllTextAsync(resultPath, markdown, new UTF8Encoding(false), cancellationToken);
                resultFormat = "md";
            }
            else if (extension is "url")
            {
                resultPath = await ConvertUrlFileAsync(source, format, workDirectory, progress, cancellationToken);
                resultFormat = format;
            }
            else if (extension == "py" && format == "ipynb")
            {
                resultPath = await ConvertPythonToNotebookAsync(source, workDirectory, cancellationToken);
                resultFormat = "ipynb";
            }
            else if (extension is "html" && format is "md" or "txt")
            {
                resultPath = await ConvertHtmlAsync(source, format, workDirectory, progress, cancellationToken);
                resultFormat = format;
            }
            else if (IsTextInput(extension, source) && format is "md" or "txt")
            {
                resultPath = await ConvertTextAsync(source, extension, format, workDirectory, cancellationToken);
                resultFormat = format;
            }
            else if (KnownImageExtensions.Contains(extension))
            {
                (resultPath, resultFormat) = await ConvertImageAsync(source, format, options, workDirectory, progress, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException($"O ficheiro '{Path.GetFileName(source)}' não possui um formato suportado para conversão para {format.ToUpperInvariant()}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var finalName = GetUniqueOutputPath(targetDirectory, Path.GetFileName(resultPath));
            File.Move(resultPath, finalName);
            progress?.Report(new ConversionProgress("Conversão concluída.", 100));
            return new ConversionResult(Path.GetFileName(finalName), finalName, resultFormat);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    public async Task<ConversionResult> MergeMarkdownAsync(
        IReadOnlyList<string> inputPaths,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (inputPaths.Count < 2)
        {
            throw new ArgumentException("Selecione pelo menos dois ficheiros Markdown para juntar.", nameof(inputPaths));
        }

        var output = new StringBuilder();
        foreach (var path in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!info.Exists || !info.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{Path.GetFileName(path)}' não existe ou não é um ficheiro .md.", nameof(inputPaths));
            }
            if (info.Length > MaximumInputBytes)
            {
                throw new ArgumentException($"O ficheiro '{info.Name}' excede o limite de 100 MB.", nameof(inputPaths));
            }
            output.Append(await File.ReadAllTextAsync(path, cancellationToken)).AppendLine().AppendLine();
        }

        Directory.CreateDirectory(outputDirectory);
        var destination = GetUniqueOutputPath(Path.GetFullPath(outputDirectory), "ficheiros_juntos.md");
        await File.WriteAllTextAsync(destination, output.ToString(), new UTF8Encoding(false), cancellationToken);
        return new ConversionResult(Path.GetFileName(destination), destination, "md");
    }

    public async Task<ConversionResult> MergeJsonAsync(
        IReadOnlyList<string> inputPaths,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (inputPaths.Count < 2)
        {
            throw new ArgumentException("Selecione pelo menos dois ficheiros JSON para juntar.", nameof(inputPaths));
        }

        var parsedDocuments = new List<System.Text.Json.Nodes.JsonNode?>();
        var allAreArrays = true;
        var allAreObjects = true;

        foreach (var path in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!info.Exists || !info.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{Path.GetFileName(path)}' não existe ou não é um ficheiro .json.", nameof(inputPaths));
            }
            if (info.Length > MaximumInputBytes)
            {
                throw new ArgumentException($"O ficheiro '{info.Name}' excede o limite de 100 MB.", nameof(inputPaths));
            }

            var text = await File.ReadAllTextAsync(path, cancellationToken);
            var node = System.Text.Json.Nodes.JsonNode.Parse(text);
            if (node is not System.Text.Json.Nodes.JsonArray)
            {
                allAreArrays = false;
            }
            if (node is not System.Text.Json.Nodes.JsonObject)
            {
                allAreObjects = false;
            }
            parsedDocuments.Add(node);
        }

        System.Text.Json.Nodes.JsonNode mergedNode;
        if (allAreArrays)
        {
            var combinedArray = new System.Text.Json.Nodes.JsonArray();
            foreach (var doc in parsedDocuments)
            {
                if (doc is System.Text.Json.Nodes.JsonArray arr)
                {
                    while (arr.Count > 0)
                    {
                        var item = arr[0];
                        arr.RemoveAt(0);
                        combinedArray.Add(item);
                    }
                }
            }
            mergedNode = combinedArray;
        }
        else if (allAreObjects)
        {
            var objectDocs = parsedDocuments.OfType<System.Text.Json.Nodes.JsonObject>().ToList();
            var allHaveMessagesArray = objectDocs.All(obj => obj.TryGetPropertyValue("messages", out var mNode) && mNode is System.Text.Json.Nodes.JsonArray);

            if (allHaveMessagesArray)
            {
                // All objects represent chat conversations with a "messages" array (like Claude/ChatGPT exports).
                // Merge all messages into a unified conversation document while recording all conversation sessions.
                var unifiedDoc = new System.Text.Json.Nodes.JsonObject();
                var combinedMessages = new System.Text.Json.Nodes.JsonArray();
                var sessionsArray = new System.Text.Json.Nodes.JsonArray();

                foreach (var obj in objectDocs)
                {
                    // Clone session info for the sessions list
                    if (System.Text.Json.Nodes.JsonNode.Parse(obj.ToJsonString()) is System.Text.Json.Nodes.JsonObject sessionClone)
                    {
                        sessionsArray.Add(sessionClone);
                    }

                    // Extract and append messages to the unified messages array
                    if (obj.TryGetPropertyValue("messages", out var msgNode) && msgNode is System.Text.Json.Nodes.JsonArray arr)
                    {
                        while (arr.Count > 0)
                        {
                            var msg = arr[0];
                            arr.RemoveAt(0);
                            combinedMessages.Add(msg);
                        }
                    }
                }

                // Preserve standard metadata from the first document (or latest)
                unifiedDoc["version"] = "1.0";
                unifiedDoc["exportedAt"] = DateTime.UtcNow.ToString("o");
                unifiedDoc["totalSessions"] = objectDocs.Count;
                unifiedDoc["totalMessages"] = combinedMessages.Count;
                unifiedDoc["messages"] = combinedMessages;
                unifiedDoc["sessions"] = sessionsArray;

                mergedNode = unifiedDoc;
            }
            else
            {
                // Check if keys collide between documents
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hasKeyCollision = false;
                foreach (var obj in objectDocs)
                {
                    foreach (var kvp in obj)
                    {
                        if (!seenKeys.Add(kvp.Key))
                        {
                            hasKeyCollision = true;
                            break;
                        }
                    }
                    if (hasKeyCollision) break;
                }

                if (hasKeyCollision)
                {
                    // When key collisions occur between full documents, combine them in an array to avoid data loss
                    var documentCollection = new System.Text.Json.Nodes.JsonArray();
                    foreach (var obj in objectDocs)
                    {
                        if (System.Text.Json.Nodes.JsonNode.Parse(obj.ToJsonString()) is { } docClone)
                        {
                            documentCollection.Add(docClone);
                        }
                    }
                    mergedNode = documentCollection;
                }
                else
                {
                    // Disjoint property keys: merge cleanly into one combined object
                    var combinedObject = new System.Text.Json.Nodes.JsonObject();
                    foreach (var obj in objectDocs)
                    {
                        var keys = obj.Select(kv => kv.Key).ToList();
                        foreach (var key in keys)
                        {
                            var value = obj[key];
                            obj.Remove(key);
                            combinedObject[key] = value;
                        }
                    }
                    mergedNode = combinedObject;
                }
            }
        }
        else
        {
            throw new ArgumentException("Não é possível juntar ficheiros JSON de tipos incompatíveis (objetos e listas misturados).", nameof(inputPaths));
        }

        Directory.CreateDirectory(outputDirectory);
        var destination = GetUniqueOutputPath(Path.GetFullPath(outputDirectory), "ficheiros_juntos.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(destination, mergedNode.ToJsonString(jsonOptions), new UTF8Encoding(false), cancellationToken);
        return new ConversionResult(Path.GetFileName(destination), destination, "json");
    }

    private async Task<string> ConvertPdfToTextAsync(
        string source,
        string format,
        string workDirectory,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ConversionProgress("A extrair texto do PDF…"));
        var output = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido." + format);
        var result = await RunToolAsync(
            ResolveTool(Path.Combine("poppler", "pdftotext.exe")),
            ["-layout", "-enc", "UTF-8", source, output],
            workDirectory,
            cancellationToken);
        EnsureProcessSucceeded(result, "O Poppler não conseguiu extrair texto do PDF.");
        if (!File.Exists(output))
        {
            throw new InvalidOperationException("O Poppler não gerou um ficheiro de texto.");
        }
        var text = await File.ReadAllTextAsync(output, cancellationToken);
        text = text.Replace("\f", "\n\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Este PDF não contém texto selecionável. PDFs digitalizados requerem OCR, que não está disponível.");
        }
        await File.WriteAllTextAsync(output, text, new UTF8Encoding(false), cancellationToken);
        return output;
    }

    private async Task<(string Path, string Format)> ConvertPdfToImagesAsync(
        string source,
        string format,
        ConversionOptions options,
        string workDirectory,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var available = await GetAvailableImageFormatsAsync(cancellationToken);
        if (!available.Contains(format, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"O formato {format.ToUpperInvariant()} não está disponível para imagens nesta instalação.");
        }

        progress?.Report(new ConversionProgress("A renderizar as páginas do PDF…"));
        var prefix = Path.Combine(workDirectory, "page");
        var renderInfo = await GetPdfRenderInfoAsync(source, cancellationToken);
        var render = await RunToolAsync(
            ResolveTool(Path.Combine("poppler", "pdftocairo.exe")),
            ["-png", "-r", renderInfo.Dpi.ToString(CultureInfo.InvariantCulture), source, prefix],
            workDirectory,
            cancellationToken);
        EnsureProcessSucceeded(render, "O Poppler não conseguiu renderizar as páginas do PDF.");
        var pages = Directory.GetFiles(workDirectory, "page-*.png").OrderBy(PageNumber).ToArray();
        if (pages.Length != renderInfo.PageCount)
        {
            throw new InvalidOperationException("O Poppler não gerou uma imagem para cada página do PDF.");
        }

        var converted = new List<string>(pages.Length);
        for (var index = 0; index < pages.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ConversionProgress($"A converter a página {index + 1} de {pages.Length}…", (index * 100d) / pages.Length));
            var output = Path.Combine(workDirectory, $"{SafeOutputBaseName(source)}_pagina_{index + 1:D4}.{format}");
            await ConvertImageFileAsync(pages[index], output, format, options, workDirectory, cancellationToken);
            converted.Add(output);
        }

        if (converted.Count == 1)
        {
            var singleOutput = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido." + format);
            File.Move(converted[0], singleOutput);
            return (singleOutput, format);
        }

        var archive = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            foreach (var file in converted)
            {
                zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Fastest);
            }
        }
        return (archive, "zip");
    }

    private async Task<PdfRenderInfo> GetPdfRenderInfoAsync(string source, CancellationToken cancellationToken)
    {
        var result = await RunToolAsync(
            ResolveTool(Path.Combine("poppler", "pdfinfo.exe")),
            ["-f", "1", "-l", "999999", "-box", source],
            _toolsDirectory,
            cancellationToken);
        EnsureProcessSucceeded(result, "O Poppler não conseguiu ler as dimensões do PDF.");
        var pageCountMatch = Regex.Match(result.StandardOutput, @"^Pages:\s*(\d+)", RegexOptions.Multiline);
        var pageSizes = Regex.Matches(result.StandardOutput, @"Page\s+\d+\s+size:\s*([\d.]+)\s+x\s*([\d.]+)\s+pts", RegexOptions.Multiline);
        if (!pageCountMatch.Success
            || !int.TryParse(pageCountMatch.Groups[1].Value, out var pageCount)
            || pageCount < 1
            || pageSizes.Count != pageCount)
        {
            throw new InvalidOperationException("O Poppler não conseguiu determinar o número e as dimensões de todas as páginas do PDF.");
        }

        var largestPagePoints = pageSizes.Cast<Match>()
            .SelectMany(match => new[]
            {
                double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
            })
            .Max();
        if (largestPagePoints <= 0)
        {
            throw new InvalidOperationException("O PDF contém dimensões de página inválidas.");
        }
        var dpi = Math.Max(1, Math.Min(150, (int)Math.Floor(MaximumDimension * 72 / largestPagePoints)));
        return new PdfRenderInfo(dpi, pageCount);
    }

    private async Task<(string Path, string Format)> ConvertImageAsync(
        string source,
        string format,
        ConversionOptions options,
        string workDirectory,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var available = await GetAvailableImageFormatsAsync(cancellationToken);
        if (!available.Contains(format, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"O formato {format.ToUpperInvariant()} não está disponível para imagens nesta instalação.");
        }
        progress?.Report(new ConversionProgress("A converter imagem…"));
        var output = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido." + format);
        await ConvertImageFileAsync(source, output, format, options, workDirectory, cancellationToken);
        return (output, format);
    }

    private async Task ConvertImageFileAsync(
        string source,
        string output,
        string format,
        ConversionOptions options,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { source, "-auto-orient" };
        if (options.Width is not null || options.Height is not null)
        {
            arguments.Add("-resize");
            arguments.Add($"{options.Width?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}x{options.Height?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}");
        }
        if (options.Rotation != 0)
        {
            arguments.Add("-rotate");
            arguments.Add(options.Rotation.ToString(CultureInfo.InvariantCulture));
        }
        if (options.Quality is not null
            && new[] { "jpg", "jpeg", "webp", "tiff", "tif", "avif", "heic" }.Contains(format, StringComparer.OrdinalIgnoreCase))
        {
            arguments.Add("-quality");
            arguments.Add(options.Quality.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (format is "jpg" or "jpeg")
        {
            arguments.AddRange(["-background", "white", "-alpha", "remove"]);
        }
        if (format == "ico")
        {
            arguments.AddRange(["-define", "icon:auto-resize=256,128,64,48,32,16"]);
        }
        arguments.Add(output);
        var result = await RunToolAsync(
            ResolveTool(Path.Combine("imagemagick", "magick.exe")),
            arguments,
            workDirectory,
            cancellationToken);
        EnsureProcessSucceeded(result, "O ImageMagick não conseguiu converter a imagem.");
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
        {
            throw new InvalidOperationException("O ImageMagick não gerou uma imagem válida.");
        }
    }

    private async Task<(string Path, string Format)> ConvertMediaAsync(
        string source,
        string extension,
        string format,
        ConversionOptions options,
        string workDirectory,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var isVideo = VideoInputs.Contains(extension);
        var isAudio = AudioInputs.Contains(extension);
        var videoFormats = new[] { "mp4", "webm", "mkv", "avi", "mov", "zip" };
        var audioFormats = new[] { "mp3", "wav", "m4a", "aac" };
        if ((!isVideo && !isAudio) || (!videoFormats.Contains(format) && !audioFormats.Contains(format)))
        {
            throw new InvalidOperationException("Escolha um formato de áudio ou vídeo suportado.");
        }
        if (isAudio && videoFormats.Contains(format))
        {
            throw new InvalidOperationException("Não é possível criar vídeo a partir de um ficheiro que contém apenas áudio.");
        }

        progress?.Report(new ConversionProgress("A converter áudio/vídeo…"));
        if (format == "zip")
        {
            var framesDirectory = Path.Combine(workDirectory, "frames");
            Directory.CreateDirectory(framesDirectory);
            var result = await RunToolAsync(
                ResolveTool("ffmpeg.exe"),
                ["-hide_banner", "-y", "-i", source, "-qscale:v", "2", Path.Combine(framesDirectory, "frame_%05d.jpg")],
                workDirectory,
                cancellationToken);
            EnsureProcessSucceeded(result, "O FFmpeg não conseguiu extrair os fotogramas.");
            var frames = Directory.GetFiles(framesDirectory, "*.jpg");
            if (frames.Length == 0)
            {
                throw new InvalidOperationException("O vídeo não continha fotogramas válidos para extrair.");
            }
            var archive = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_fotogramas.zip");
            ZipFile.CreateFromDirectory(framesDirectory, archive, CompressionLevel.Fastest, false);
            return (archive, "zip");
        }

        var output = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido." + format);
        var arguments = new List<string> { "-hide_banner", "-y", "-i", source };
        if (audioFormats.Contains(format))
        {
            arguments.AddRange(["-vn"]);
            arguments.AddRange(format switch
            {
                "mp3" => ["-c:a", "libmp3lame", "-b:a", "192k"],
                "wav" => ["-c:a", "pcm_s16le"],
                "m4a" or "aac" => ["-c:a", "aac", "-b:a", "192k"],
                _ => throw new InvalidOperationException("Formato de áudio não suportado.")
            });
        }
        else
        {
            var filters = new List<string>();
            if (options.Width is not null || options.Height is not null)
            {
                filters.Add(options.Width is not null && options.Height is not null
                    ? $"scale={options.Width}:{options.Height}"
                    : options.Width is not null ? $"scale={options.Width}:-2" : $"scale=-2:{options.Height}");
            }
            if (options.Rotation == 90) filters.Add("transpose=1");
            if (options.Rotation == 180) filters.AddRange(["hflip", "vflip"]);
            if (options.Rotation == 270) filters.Add("transpose=2");
            if (filters.Count > 0)
            {
                arguments.AddRange(["-vf", string.Join(',', filters)]);
            }
            var quality = options.Quality ?? 90;
            var x264Crf = Math.Clamp((int)Math.Round(40 - quality * 0.22), 18, 40).ToString(CultureInfo.InvariantCulture);
            var vp9Crf = Math.Clamp((int)Math.Round(52 - quality * 0.34), 18, 52).ToString(CultureInfo.InvariantCulture);
            var aviQuality = Math.Clamp((int)Math.Round(31 - quality * 0.28), 2, 31).ToString(CultureInfo.InvariantCulture);
            arguments.AddRange(format switch
            {
                "mp4" or "mov" => ["-map", "0:v:0?", "-map", "0:a:0?", "-c:v", "libx264", "-preset", "medium", "-crf", x264Crf, "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"],
                "webm" => ["-map", "0:v:0?", "-map", "0:a:0?", "-c:v", "libvpx-vp9", "-crf", vp9Crf, "-b:v", "0", "-c:a", "libopus", "-b:a", "128k"],
                "mkv" => ["-map", "0:v:0?", "-map", "0:a:0?", "-c:v", "libx264", "-crf", x264Crf, "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k"],
                "avi" => ["-map", "0:v:0?", "-map", "0:a:0?", "-c:v", "mpeg4", "-q:v", aviQuality, "-c:a", "libmp3lame", "-b:a", "192k"],
                _ => throw new InvalidOperationException("Formato de vídeo não suportado.")
            });
        }
        arguments.Add(output);
        var conversion = await RunToolAsync(ResolveTool("ffmpeg.exe"), arguments, workDirectory, cancellationToken);
        EnsureProcessSucceeded(conversion, "O FFmpeg não conseguiu converter o ficheiro multimédia.");
        if (!File.Exists(output) || new FileInfo(output).Length < 1_024)
        {
            throw new InvalidOperationException("O FFmpeg não gerou um ficheiro multimédia válido.");
        }
        return (output, format);
    }

    private async Task<string> ConvertHtmlAsync(
        string source,
        string format,
        string workDirectory,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ConversionProgress("A converter HTML para texto…"));
        var output = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido." + format);
        var result = await RunToolAsync(
            ResolveTool("pandoc.exe"),
            ["-f", "html", "-t", format == "md" ? "gfm" : "plain", "--wrap=none", "-o", output, source],
            workDirectory,
            cancellationToken);
        EnsureProcessSucceeded(result, "O Pandoc não conseguiu converter o HTML.");
        EnsureNonEmpty(output, "O Pandoc não gerou conteúdo.");
        return output;
    }

    private async Task<string> ConvertUrlFileAsync(
        string source,
        string format,
        string workDirectory,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (format is not ("md" or "txt"))
        {
            throw new InvalidOperationException("A conversão de URLs apenas suporta Markdown ou texto.");
        }

        var urlText = ExtractUrl(await File.ReadAllTextAsync(source, cancellationToken));
        var uri = _urlPolicy.ValidateUrl(urlText);
        progress?.Report(new ConversionProgress("A obter o conteúdo do website…"));
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumInputBytes)
        {
            throw new InvalidOperationException("O website excede o limite de 100 MB.");
        }
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var htmlPath = Path.Combine(workDirectory, "website.html");
        await using (var outputStream = File.Create(htmlPath))
        {
            await CopyBoundedAsync(responseStream, outputStream, MaximumInputBytes, cancellationToken);
        }
        var host = uri.Host.Replace(':', '_');
        var output = Path.Combine(workDirectory, host + "_convertido." + format);
        var result = await RunToolAsync(
            ResolveTool("pandoc.exe"),
            ["-f", "html", "-t", format == "md" ? "gfm" : "plain", "--wrap=none", "-o", output, htmlPath],
            workDirectory,
            cancellationToken);
        EnsureProcessSucceeded(result, "O Pandoc não conseguiu converter o website.");
        EnsureNonEmpty(output, "O website não devolveu conteúdo convertível.");
        return output;
    }

    /// <summary>
    /// Aceita tanto um URL simples como um atalho do Windows (.url, formato INI com a linha "URL=...").
    /// </summary>
    public static string ExtractUrl(string fileContent)
    {
        var text = fileContent.Trim();
        if (!text.StartsWith("[InternetShortcut]", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[4..].Trim();
            }
        }
        return string.Empty;
    }

    private async Task<string> ConvertTextAsync(
        string source,
        string extension,
        string format,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        if (format == "txt")
        {
            var textCopy = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido.txt");
            cancellationToken.ThrowIfCancellationRequested();
            var rawText = await File.ReadAllTextAsync(source, cancellationToken);
            await File.WriteAllTextAsync(textCopy, rawText, new UTF8Encoding(false), cancellationToken);
            return textCopy;
        }
        if (format != "md")
        {
            throw new InvalidOperationException("Este tipo de documento apenas suporta saída Markdown ou TXT.");
        }

        var content = await File.ReadAllTextAsync(source, cancellationToken);
        var language = extension switch { "py" => "python", "json" => "json", _ => "text" };
        var markdown = extension is "py" or "json" or "log"
            ? FenceCode(content, language)
            : content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var output = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido.md");
        await File.WriteAllTextAsync(output, markdown, new UTF8Encoding(false), cancellationToken);
        EnsureNonEmpty(output, "O ficheiro não contém conteúdo convertível.");
        return output;
    }

    private async Task<string> ConvertPythonToNotebookAsync(string source, string workDirectory, CancellationToken cancellationToken)
    {
        var code = await File.ReadAllTextAsync(source, cancellationToken);
        var cells = BuildPythonCells(code);
        var notebook = new
        {
            cells,
            metadata = new { },
            nbformat = 4,
            nbformat_minor = 4
        };
        var output = Path.Combine(workDirectory, SafeOutputBaseName(source) + "_convertido.ipynb");
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(notebook, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return output;
    }

    private static object[] BuildPythonCells(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var cells = new List<object>();
        var current = new List<string>();
        var markdownCell = false;
        void Flush()
        {
            if (current.Count == 0) return;
            if (markdownCell)
            {
                cells.Add(new { cell_type = "markdown", metadata = new { }, source = current.ToArray() });
            }
            else
            {
                cells.Add(new { cell_type = "code", execution_count = (int?)null, metadata = new { }, outputs = Array.Empty<object>(), source = current.ToArray() });
            }
            current.Clear();
        }

        foreach (var line in lines)
        {
            var marker = Regex.Match(line, @"^\s*#\s*%%(?:\s*\[(markdown)\])?.*$", RegexOptions.IgnoreCase);
            if (marker.Success)
            {
                Flush();
                markdownCell = marker.Groups[1].Success;
                continue;
            }
            current.Add(markdownCell && Regex.IsMatch(line, @"^\s*#\s?") ? Regex.Replace(line, @"^\s*#\s?", string.Empty) : line);
        }
        Flush();
        if (cells.Count == 0)
        {
            cells.Add(new { cell_type = "code", execution_count = (int?)null, metadata = new { }, outputs = Array.Empty<object>(), source = Array.Empty<string>() });
        }
        return cells.ToArray();
    }

    private string ResolveTool(string relativePath)
    {
        var path = Path.Combine(_toolsDirectory, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"A ferramenta portátil não foi encontrada: {relativePath}", path);
        }
        return path;
    }

    private static async Task<ProcessResult> RunToolAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Não foi possível iniciar {Path.GetFileName(executable)}.");
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            throw new TimeoutException($"{Path.GetFileName(executable)} excedeu o limite de execução de cinco minutos.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void EnsureProcessSucceeded(ProcessResult result, string fallback)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(LastUsefulLine(result.StandardError + Environment.NewLine + result.StandardOutput) ?? fallback);
        }
    }

    private static string? LastUsefulLine(string output)
    {
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(candidate => candidate.Trim())
            .LastOrDefault(candidate => candidate.Length > 0 && !candidate.StartsWith("frame=", StringComparison.OrdinalIgnoreCase));
        return line is null ? null : CleanDiagnostic(line);
    }

    /// <summary>
    /// Limpa mensagens de ferramentas externas antes de as mostrar ao utilizador:
    /// oculta URLs, normaliza espaços e limita o tamanho.
    /// </summary>
    public static string CleanDiagnostic(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }
        var cleaned = Regex.Replace(message, @"https?://\S+", "[ligação ocultada]", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length <= MaximumDiagnosticLength ? cleaned : cleaned[..MaximumDiagnosticLength];
    }

    private static void ValidateOptions(ConversionOptions options)
    {
        if (options.Width is < 1 or > MaximumDimension || options.Height is < 1 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "As dimensões têm de estar entre 1 e 16000 píxeis.");
        }
        if (options.Quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A qualidade tem de estar entre 1 e 100.");
        }
        if (options.Rotation is not (0 or 90 or 180 or 270))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A rotação suportada é 0, 90, 180 ou 270 graus.");
        }
        if (string.IsNullOrWhiteSpace(options.Format))
        {
            throw new ArgumentException("Selecione um formato de saída.", nameof(options));
        }
    }

    private static string NormalizeFormat(string format)
    {
        var value = format.Trim().TrimStart('.').ToLowerInvariant();
        return value == "jpeg" ? "jpg" : value;
    }

    private static string ToMagickFormat(string format)
    {
        return format switch
        {
            "jpg" or "jpeg" => "JPEG",
            "tif" or "tiff" => "TIFF",
            _ => format.ToUpperInvariant()
        };
    }

    public static bool IsTextInput(string extension, string? path = null)
    {
        if (KnownTextExtensions.Contains(extension)) return true;
        return !string.IsNullOrWhiteSpace(path) && IsTextFile(path);
    }

    public static bool IsTextFile(string path)
    {
        if (!File.Exists(path)) return false;
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (KnownTextExtensions.Contains(ext)) return true;
        if (VideoInputs.Contains(ext) || AudioInputs.Contains(ext) || DocumentInputs.Contains(ext) || KnownImageExtensions.Contains(ext) || ext == "pdf")
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length == 0) return true;
            Span<byte> buffer = stackalloc byte[(int)Math.Min(8192, stream.Length)];
            var read = stream.Read(buffer);
            if (read == 0) return true;
            var slice = buffer[..read];

            if (slice.Length >= 3 && slice[0] == 0xEF && slice[1] == 0xBB && slice[2] == 0xBF) return true;
            if (slice.Length >= 2 && slice[0] == 0xFF && slice[1] == 0xFE) return true;
            if (slice.Length >= 2 && slice[0] == 0xFE && slice[1] == 0xFF) return true;

            if (slice.IndexOf((byte)0) >= 0) return false;

            var decoder = Encoding.UTF8.GetDecoder();
            decoder.Fallback = DecoderFallback.ExceptionFallback;
            var chars = new char[read];
            decoder.GetChars(slice.ToArray(), 0, slice.Length, chars, 0, flush: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRecognizedExtension(string extension)
    {
        return VideoInputs.Contains(extension)
            || AudioInputs.Contains(extension)
            || DocumentInputs.Contains(extension)
            || KnownImageExtensions.Contains(extension)
            || KnownTextExtensions.Contains(extension)
            || extension is "pdf" or "url" or "html" or "htm" or "ipynb";
    }

    private static string FenceCode(string source, string language)
    {
        var maxFence = Regex.Matches(source, "`+").Select(match => match.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, maxFence + 1));
        return $"{fence}{language}\n{source}{(source.EndsWith('\n') ? string.Empty : "\n")}{fence}\n";
    }

    private static string SafeOutputBaseName(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var name = IsRecognizedExtension(extension)
            ? Path.GetFileNameWithoutExtension(path)
            : Path.GetFileName(path);

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        name = name.Trim('.', ' ');
        return string.IsNullOrEmpty(name) ? "ficheiro" : name;
    }

    private static string GetUniqueOutputPath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            path = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(path)) return path;
        }
    }

    private static int PageNumber(string path)
    {
        var match = Regex.Match(Path.GetFileNameWithoutExtension(path), @"-(\d+)$");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : int.MaxValue;
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0) return;
            total += count;
            if (total > maxBytes)
            {
                throw new InvalidOperationException("O conteúdo obtido excede o limite de 100 MB.");
            }
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    private static void EnsureNonEmpty(string path, string message)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Um ficheiro ainda em uso não deve esconder o resultado da conversão;
            // CleanupStaleWorkDirectories remove o que sobrar na próxima execução.
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    private sealed record PdfRenderInfo(int Dpi, int PageCount);
}
