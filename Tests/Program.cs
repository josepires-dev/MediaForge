using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MediaForge.Services;

var positionalArgs = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToArray();
var projectRoot = positionalArgs.Length > 0
    ? Path.GetFullPath(positionalArgs[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var tools = Path.Combine(projectRoot, "Tools");
var wasm = Path.Combine(projectRoot, "Assets", "Converter", "wasm");

if (args.Contains("--doctor"))
{
    var report = EnvironmentDoctor.Run(tools);
    Console.WriteLine(EnvironmentDoctor.Format(report));
    Environment.Exit(report.All(check => check.Passed) ? 0 : 1);
}

var root = Path.Combine(Path.GetTempPath(), "MediaForgeSmoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;

try
{
    var service = new ConversionService(tools, Path.Combine(root, "temp"));
    var formats = await service.GetAvailableFormatsAsync();
    Check(formats.Contains("png") && formats.Contains("jpg"), "ImageMagick advertises writable common formats");

    var ppm = Path.Combine(root, "input.ppm");
    await File.WriteAllTextAsync(ppm, "P3\n2 2\n255\n255 0 0 0 255 0\n0 0 255 255 255 0\n");
    var image = await service.ConvertAsync(ppm, new ConversionOptions("png", Width: 4, Height: 4, Rotation: 90), Path.Combine(root, "out"));
    Check(File.ReadAllBytes(image.FullPath).AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "ImageMagick converts and resizes image");
    var jpeg = await service.ConvertAsync(ppm, new ConversionOptions("jpg", Quality: 80), Path.Combine(root, "out"));
    Check(File.ReadAllBytes(jpeg.FullPath).AsSpan(0, 3).SequenceEqual(new byte[] { 0xff, 0xd8, 0xff }), "ImageMagick converts image to JPEG with quality option");
    var invalidDimensionsRejected = false;
    try
    {
        await service.ConvertAsync(ppm, new ConversionOptions("png", Width: 16_001), Path.Combine(root, "out"));
    }
    catch (ArgumentOutOfRangeException)
    {
        invalidDimensionsRejected = true;
    }
    Check(invalidDimensionsRejected, "Conversion rejects dimensions above the configured safety limit");
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        var cancellationObserved = false;
        try
        {
            await service.ConvertAsync(ppm, new ConversionOptions("png"), Path.Combine(root, "out"), cancellationToken: cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            cancellationObserved = true;
        }
        Check(cancellationObserved, "Conversion honors cancellation before starting work");
    }

    var pdf = Path.Combine(root, "two-pages.pdf");
    await File.WriteAllBytesAsync(pdf, CreatePdf(["MediaForge page one", "MediaForge page two"]));
    var pdfText = await service.ConvertAsync(pdf, new ConversionOptions("md"), Path.Combine(root, "out"));
    Check((await File.ReadAllTextAsync(pdfText.FullPath)).Contains("MediaForge page one", StringComparison.Ordinal), "Poppler extracts selectable PDF text");
    var pdfImages = await service.ConvertAsync(pdf, new ConversionOptions("png"), Path.Combine(root, "out"));
    Check(pdfImages.Format == "zip" && GetZipEntryCount(pdfImages.FullPath) == 2, "Multipage PDF converts to a ZIP of page images");
    var onePagePdf = Path.Combine(root, "one-page.pdf");
    await File.WriteAllBytesAsync(onePagePdf, CreatePdf(["MediaForge single page"]));
    var onePageImage = await service.ConvertAsync(onePagePdf, new ConversionOptions("png"), Path.Combine(root, "out"));
    Check(onePageImage.Format == "png" && File.Exists(onePageImage.FullPath), "Single-page PDF returns one image directly");

    var html = Path.Combine(root, "source.html");
    await File.WriteAllTextAsync(html, "<html><body><h1>MediaForge HTML</h1><p>Local conversion</p></body></html>");
    var markdown = await service.ConvertAsync(html, new ConversionOptions("md"), Path.Combine(root, "out"));
    Check((await File.ReadAllTextAsync(markdown.FullPath)).Contains("# MediaForge HTML", StringComparison.Ordinal), "Pandoc converts HTML to Markdown");
    var plainHtml = await service.ConvertAsync(html, new ConversionOptions("txt"), Path.Combine(root, "out"));
    Check((await File.ReadAllTextAsync(plainHtml.FullPath)).Contains("Local conversion", StringComparison.Ordinal), "Pandoc converts HTML to plain text");
    var textInput = Path.Combine(root, "notes.txt");
    await File.WriteAllTextAsync(textInput, "MediaForge plain text");
    var textMarkdown = await service.ConvertAsync(textInput, new ConversionOptions("md"), Path.Combine(root, "out"));
    Check((await File.ReadAllTextAsync(textMarkdown.FullPath)).Contains("MediaForge plain text", StringComparison.Ordinal), "Plain text converts to Markdown");

    var python = Path.Combine(root, "cells.py");
    await File.WriteAllTextAsync(python, "# %% [markdown]\n# A notebook title\n# %%\nprint('MediaForge')\n");
    var notebook = await service.ConvertAsync(python, new ConversionOptions("ipynb"), Path.Combine(root, "out"));
    using (var notebookJson = JsonDocument.Parse(await File.ReadAllTextAsync(notebook.FullPath)))
    {
        Check(notebookJson.RootElement.GetProperty("nbformat").GetInt32() == 4
            && notebookJson.RootElement.GetProperty("cells").GetArrayLength() == 2, "Python converts to a notebook with marked cells");
    }

    var markdownA = Path.Combine(root, "one.md");
    var markdownB = Path.Combine(root, "two.md");
    await File.WriteAllTextAsync(markdownA, "# First");
    await File.WriteAllTextAsync(markdownB, "# Second");
    var merged = await service.MergeMarkdownAsync([markdownA, markdownB], Path.Combine(root, "out"));
    var mergedText = await File.ReadAllTextAsync(merged.FullPath);
    Check(mergedText.Contains("# First", StringComparison.Ordinal) && mergedText.Contains("# Second", StringComparison.Ordinal), "Markdown files merge in input order");

    var jsonA = Path.Combine(root, "one.json");
    var jsonB = Path.Combine(root, "two.json");
    var jsonC = Path.Combine(root, "three.json");
    await File.WriteAllTextAsync(jsonA, "{\"a\": 1}");
    await File.WriteAllTextAsync(jsonB, "{\"b\": 2}");
    await File.WriteAllTextAsync(jsonC, "{\"c\": 3}");
    var mergedJson = await service.MergeJsonAsync([jsonA, jsonB, jsonC], Path.Combine(root, "out"));
    using (var parsedMerged = JsonDocument.Parse(await File.ReadAllTextAsync(mergedJson.FullPath)))
    {
        Check(parsedMerged.RootElement.GetProperty("a").GetInt32() == 1
            && parsedMerged.RootElement.GetProperty("b").GetInt32() == 2
            && parsedMerged.RootElement.GetProperty("c").GetInt32() == 3, "JSON object files merge into single JSON object");
    }

    var jsonArrayA = Path.Combine(root, "array1.json");
    var jsonArrayB = Path.Combine(root, "array2.json");
    await File.WriteAllTextAsync(jsonArrayA, "[1, 2]");
    await File.WriteAllTextAsync(jsonArrayB, "[3, 4]");
    var mergedJsonArray = await service.MergeJsonAsync([jsonArrayA, jsonArrayB], Path.Combine(root, "out"));
    using (var parsedArray = JsonDocument.Parse(await File.ReadAllTextAsync(mergedJsonArray.FullPath)))
    {
        Check(parsedArray.RootElement.GetArrayLength() == 4, "JSON array files merge into single combined array");
    }

    var chatA = Path.Combine(root, "chat1.json");
    var chatB = Path.Combine(root, "chat2.json");
    await File.WriteAllTextAsync(chatA, "{\"version\": \"1.0\", \"messages\": [{\"id\": 1}, {\"id\": 2}]}");
    await File.WriteAllTextAsync(chatB, "{\"version\": \"1.0\", \"messages\": [{\"id\": 3}]}");
    var mergedChat = await service.MergeJsonAsync([chatA, chatB], Path.Combine(root, "out"));
    using (var parsedChat = JsonDocument.Parse(await File.ReadAllTextAsync(mergedChat.FullPath)))
    {
        Check(parsedChat.RootElement.GetProperty("messages").GetArrayLength() == 3
            && parsedChat.RootElement.GetProperty("totalSessions").GetInt32() == 2, "JSON conversation exports merge all messages without overwriting");
    }

    var mixedJsonRejected = false;
    try
    {
        await service.MergeJsonAsync([jsonA, jsonArrayA], Path.Combine(root, "out"));
    }
    catch (ArgumentException ex) when (ex.Message.Contains("incompatíveis", StringComparison.OrdinalIgnoreCase))
    {
        mixedJsonRejected = true;
    }
    Check(mixedJsonRejected, "Mixed object and array JSON files are rejected fail-fast");

    var singleMergeRejected = false;
    try
    {
        await service.MergeMarkdownAsync([markdownA], Path.Combine(root, "out"));
    }
    catch (ArgumentException ex) when (ex.Message.Contains("pelo menos dois", StringComparison.OrdinalIgnoreCase))
    {
        singleMergeRejected = true;
    }
    Check(singleMergeRejected, "Markdown merge with a single file reports an accurate message");

    var wrongMergeTypeRejected = false;
    try
    {
        await service.MergeMarkdownAsync([markdownA, textInput], Path.Combine(root, "out"));
    }
    catch (ArgumentException ex) when (ex.Message.Contains(".md", StringComparison.OrdinalIgnoreCase)
        && !ex.Message.Contains("JSON", StringComparison.OrdinalIgnoreCase))
    {
        wrongMergeTypeRejected = true;
    }
    Check(wrongMergeTypeRejected, "Markdown merge rejects non-Markdown files without mentioning JSON");

    var urlFile = Path.Combine(root, "website.url");
    var (localWebsiteUrl, localWebsiteServer) = StartLocalWebsite();
    await File.WriteAllTextAsync(urlFile, localWebsiteUrl);
    var localService = new ConversionService(tools, Path.Combine(root, "temp-local"), UrlSafetyPolicy.AllowPrivateNetwork);
    var website = await localService.ConvertAsync(urlFile, new ConversionOptions("md"), Path.Combine(root, "out"));
    await localWebsiteServer.WaitAsync(TimeSpan.FromSeconds(10));
    Check((await File.ReadAllTextAsync(website.FullPath)).Contains("MediaForge URL", StringComparison.Ordinal), "Local website converts through HTTP and Pandoc");

    var loopbackBlocked = false;
    try
    {
        await service.ConvertAsync(urlFile, new ConversionOptions("md"), Path.Combine(root, "out"));
    }
    catch (ArgumentException ex) when (ex.Message.Contains("bloqueado", StringComparison.OrdinalIgnoreCase))
    {
        loopbackBlocked = true;
    }
    Check(loopbackBlocked, "URL conversion blocks loopback targets by default (SSRF protection)");

    var blockedAddresses = new[]
    {
        "0.0.0.0", "10.0.0.1", "100.64.0.1", "127.0.0.1", "169.254.169.254", "172.16.0.1",
        "172.31.255.255", "192.168.1.5", "224.0.0.1", "::1", "fe80::1", "fc00::1", "::ffff:10.0.0.1"
    };
    Check(blockedAddresses.All(address => UrlSafetyPolicy.IsBlockedAddress(IPAddress.Parse(address))),
        "SSRF policy blocks loopback, private, link-local, CGNAT and reserved addresses");
    var publicAddresses = new[] { "1.1.1.1", "8.8.8.8", "172.32.0.1", "2606:4700:4700::1111" };
    Check(publicAddresses.All(address => !UrlSafetyPolicy.IsBlockedAddress(IPAddress.Parse(address))),
        "SSRF policy keeps public addresses reachable");

    var rejectedUrls = new[]
    {
        "ftp://example.com/file", "file:///C:/Windows/win.ini", "http://localhost/admin", "http://printer.local/",
        "http://[::1]/", "http://169.254.169.254/latest/meta-data/", "http://2130706433/", "not a url",
        "https://example.com/" + new string('a', UrlSafetyPolicy.MaximumUrlLength)
    };
    Check(rejectedUrls.All(RejectsUrl), "SSRF policy rejects non-HTTP, local, metadata and oversized URLs");
    Check(UrlSafetyPolicy.Default.ValidateUrl(" https://example.com/path?q=1 ").Host == "example.com", "SSRF policy accepts public HTTPS URLs");

    Check(ConversionService.ExtractUrl("[InternetShortcut]\r\nURL=https://example.com/a\r\n") == "https://example.com/a"
        && ConversionService.ExtractUrl("  https://example.com/b \n") == "https://example.com/b", "URL extraction supports Windows shortcuts and plain URLs");

    var diagnostic = ConversionService.CleanDiagnostic("Error: cannot open https://secret.example.com/a?token=123 \r\n  now");
    Check(!diagnostic.Contains("secret.example.com", StringComparison.Ordinal)
        && !diagnostic.Contains('\n')
        && ConversionService.CleanDiagnostic(new string('x', 5_000)).Length == 600, "Tool diagnostics hide URLs and are capped in length");

    var staleTemp = Path.Combine(root, "temp-stale");
    var staleService = new ConversionService(tools, staleTemp);
    var staleDirectory = Path.Combine(staleTemp, Guid.NewGuid().ToString("N"));
    var freshDirectory = Path.Combine(staleTemp, Guid.NewGuid().ToString("N"));
    var foreignDirectory = Path.Combine(staleTemp, "not-a-work-directory");
    Directory.CreateDirectory(staleDirectory);
    Directory.CreateDirectory(freshDirectory);
    Directory.CreateDirectory(foreignDirectory);
    Directory.SetLastWriteTimeUtc(staleDirectory, DateTime.UtcNow.AddDays(-3));
    Directory.SetLastWriteTimeUtc(foreignDirectory, DateTime.UtcNow.AddDays(-3));
    var removedDirectories = staleService.CleanupStaleWorkDirectories(TimeSpan.FromHours(24));
    Check(removedDirectories == 1
        && !Directory.Exists(staleDirectory)
        && Directory.Exists(freshDirectory)
        && Directory.Exists(foreignDirectory), "Stale work directories are removed while recent and foreign folders are kept");

    var doctorReport = EnvironmentDoctor.Run(tools, Path.Combine(root, "doctor-temp"));
    Check(doctorReport.All(check => check.Passed), "Environment doctor approves the bundled tools and ImageMagick policy");
    var emptyTools = Path.Combine(root, "no-tools");
    Directory.CreateDirectory(emptyTools);
    var brokenReport = EnvironmentDoctor.Run(emptyTools, Path.Combine(root, "doctor-temp"));
    Check(brokenReport.Count(check => !check.Passed) >= 7, "Environment doctor reports missing tools and policy");

    var wav = Path.Combine(root, "tone.wav");
    await File.WriteAllBytesAsync(wav, CreateWave());
    foreach (var format in new[] { "mp3", "wav", "m4a", "aac" })
    {
        var audio = await service.ConvertAsync(wav, new ConversionOptions(format), Path.Combine(root, "out"));
        Check(new FileInfo(audio.FullPath).Length > 1_024, $"FFmpeg converts audio to {format.ToUpperInvariant()}");
    }

    var video = Path.Combine(root, "sample.mp4");
    await Run(Path.Combine(tools, "ffmpeg.exe"), tools, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=32x32:d=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", video]);
    foreach (var format in new[] { "mp4", "webm", "mkv", "avi", "mov" })
    {
        var convertedVideo = await service.ConvertAsync(video, new ConversionOptions(format), Path.Combine(root, "out"));
        Check(new FileInfo(convertedVideo.FullPath).Length > 1_024, $"FFmpeg converts video to {format.ToUpperInvariant()}");
    }
    var frames = await service.ConvertAsync(video, new ConversionOptions("zip"), Path.Combine(root, "out"));
    Check(frames.Format == "zip" && GetZipEntryCount(frames.FullPath) > 0, "FFmpeg extracts video frames to ZIP");

    var docx = Path.Combine(root, "document.docx");
    CreateDocx(docx);
    var officeDocument = await service.ConvertAsync(
        docx,
        new ConversionOptions("md"),
        Path.Combine(root, "out"),
        documentConverter: (bytes, extension, _) =>
        {
            if (extension != "docx" || bytes.Length == 0)
            {
                throw new InvalidOperationException("Office document dispatch failed.");
            }
            return Task.FromResult("# MediaForge Office document");
        });
    Check((await File.ReadAllTextAsync(officeDocument.FullPath)).Contains("MediaForge Office document", StringComparison.Ordinal), "Office document conversion service stores Markdown result");
    await VerifyAnyDocWithNodeAsync(wasm, docx, root);

    var imageFormats = await service.GetAvailableImageFormatsAsync();
    Check(imageFormats.Contains("png") && !imageFormats.Contains("md") && !imageFormats.Contains("txt"), "Image formats exclude non-image formats like Markdown and text");

    var nonStandardTextFile = Path.Combine(root, "Consultas do Search Console — biblia360.net");
    await File.WriteAllTextAsync(nonStandardTextFile, "# Consultas do Search Console — biblia360.net\n\n| Consulta | Cliques |\n|---|---:|\n| biblia 360 | 4 |\n", new UTF8Encoding(false));
    Check(ConversionService.IsTextFile(nonStandardTextFile), "Content-based detection identifies text file with .net extension");
    var nonStandardResult = await service.ConvertAsync(nonStandardTextFile, new ConversionOptions("md"), Path.Combine(root, "out"));
    Check(nonStandardResult.FileName == "Consultas do Search Console — biblia360.net_convertido.md"
        && (await File.ReadAllTextAsync(nonStandardResult.FullPath)).Contains("biblia 360", StringComparison.Ordinal),
        "Text file with non-standard extension converts to Markdown preserving full basename and unicode chars");

    var extensionlessFile = Path.Combine(root, "DOCUMENTO_SEM_EXTENSAO");
    await File.WriteAllTextAsync(extensionlessFile, "Conteúdo de texto sem extensão", new UTF8Encoding(false));
    Check(ConversionService.IsTextFile(extensionlessFile), "Content-based detection identifies extensionless text file");
    var extensionlessResult = await service.ConvertAsync(extensionlessFile, new ConversionOptions("md"), Path.Combine(root, "out"));
    Check(extensionlessResult.FileName == "DOCUMENTO_SEM_EXTENSAO_convertido.md", "Extensionless text file converts to Markdown");

    var dummyBinary = Path.Combine(root, "app.bin");
    await File.WriteAllBytesAsync(dummyBinary, [0x00, 0x01, 0x02, 0xFF]);
    Check(!ConversionService.IsTextFile(dummyBinary), "Binary file with null bytes is not identified as text");
    var binaryRejected = false;
    try
    {
        await service.ConvertAsync(dummyBinary, new ConversionOptions("md"), Path.Combine(root, "out"));
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("não possui um formato suportado", StringComparison.OrdinalIgnoreCase))
    {
        binaryRejected = true;
    }
    Check(binaryRejected, "Unsupported binary file is rejected cleanly without invoking ImageMagick");

    var historyPath = Path.Combine(root, "history", "activity.json");
    var historyService = new HistoryService(historyPath);
    await historyService.AddAsync(new ActivityHistoryItem("Conversão", "input.docx", "output.md", "08/10/2026 20:00", "output.md"));
    Check((await historyService.ReadAsync()).Count == 1, "Activity history persists conversion records");
    await historyService.ClearAsync();
    Check((await historyService.ReadAsync()).Count == 0, "Activity history can be cleared without touching outputs");

    Console.WriteLine($"PASS: {passed} MediaForge smoke checks");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {description}");
    }
    passed++;
    Console.WriteLine($"PASS: {description}");
}

static bool RejectsUrl(string url)
{
    try
    {
        UrlSafetyPolicy.Default.ValidateUrl(url);
        return false;
    }
    catch (ArgumentException)
    {
        return true;
    }
}

static async Task Run(string executable, string workingDirectory, IReadOnlyList<string> arguments)
{
    var start = new ProcessStartInfo(executable)
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    };
    foreach (var argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }
    using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var output = (await stderr) + Environment.NewLine + (await stdout);
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"{Path.GetFileName(executable)} failed ({process.ExitCode}): {output}");
    }
}

static byte[] CreateWave()
{
    const int sampleRate = 8_000;
    const short channels = 1;
    const short bitsPerSample = 16;
    var sampleCount = sampleRate / 4;
    var dataLength = sampleCount * channels * bitsPerSample / 8;
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
    writer.Write(Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(36 + dataLength);
    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
    writer.Write(16);
    writer.Write((short)1);
    writer.Write(channels);
    writer.Write(sampleRate);
    writer.Write(sampleRate * channels * bitsPerSample / 8);
    writer.Write((short)(channels * bitsPerSample / 8));
    writer.Write(bitsPerSample);
    writer.Write(Encoding.ASCII.GetBytes("data"));
    writer.Write(dataLength);
    for (var index = 0; index < sampleCount; index++)
    {
        var sample = (short)(Math.Sin(2 * Math.PI * 440 * index / sampleRate) * short.MaxValue * 0.25);
        writer.Write(sample);
    }
    return stream.ToArray();
}

static byte[] CreatePdf(IReadOnlyList<string> pageTexts)
{
    var objects = new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pageTexts.Count).Select(index => $"{3 + index * 2} 0 R"))}] /Count {pageTexts.Count} >>"
    };
    for (var index = 0; index < pageTexts.Count; index++)
    {
        var pageObject = 3 + index * 2;
        var streamObject = pageObject + 1;
        objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Resources << /Font << /F1 {3 + pageTexts.Count * 2} 0 R >> >> /Contents {streamObject} 0 R >>");
        var content = $"BT /F1 18 Tf 24 250 Td ({pageTexts[index]}) Tj ET";
        objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
    }
    objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

    var pdf = new StringBuilder("%PDF-1.4\n");
    var offsets = new List<int> { 0 };
    for (var index = 0; index < objects.Count; index++)
    {
        offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
        pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
    }
    var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
    pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
    foreach (var offset in offsets.Skip(1))
    {
        pdf.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
    }
    pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
        .Append(xrefOffset).Append("\n%%EOF\n");
    return Encoding.ASCII.GetBytes(pdf.ToString());
}

static void CreateDocx(string path)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    AddEntry(archive, "[Content_Types].xml",
        """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");
    AddEntry(archive, "_rels/.rels",
        """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
    AddEntry(archive, "word/document.xml",
        """<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>MediaForge Office document</w:t></w:r></w:p><w:sectPr/></w:body></w:document>""");
}

static void AddEntry(ZipArchive archive, string name, string value)
{
    var entry = archive.CreateEntry(name);
    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
    writer.Write(value);
}

static (string Url, Task ServerTask) StartLocalWebsite()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var serve = Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var buffer = new byte[4096];
        var request = new MemoryStream();
        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            request.Write(buffer, 0, read);
            if (request.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) >= 0) break;
        }
        var body = Encoding.UTF8.GetBytes("<html><body><h1>MediaForge URL</h1><p>Local website conversion</p></body></html>");
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(body);
        listener.Stop();
    });
    return ($"http://127.0.0.1:{port}/", serve);
}

static int GetZipEntryCount(string path)
{
    using var archive = ZipFile.OpenRead(path);
    return archive.Entries.Count;
}

async Task VerifyAnyDocWithNodeAsync(string wasmDirectory, string docxPath, string tempRoot)
{
    var runtimeDirectory = Path.Combine(tempRoot, "node-wasm");
    Directory.CreateDirectory(runtimeDirectory);
    var modulePath = Path.Combine(runtimeDirectory, "anydoc_wasm.mjs");
    var wasmPath = Path.Combine(runtimeDirectory, "anydoc_wasm_bg.wasm");
    File.Copy(Path.Combine(wasmDirectory, "anydoc_wasm.js"), modulePath);
    File.Copy(Path.Combine(wasmDirectory, "anydoc_wasm_bg.wasm"), wasmPath);
    var scriptPath = Path.Combine(runtimeDirectory, "verify.mjs");
    await File.WriteAllTextAsync(scriptPath,
        """
        import { readFileSync } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        const [modulePath, wasmPath, documentPath] = process.argv.slice(2);
        const api = await import(pathToFileURL(modulePath));
        api.initSync(new Uint8Array(readFileSync(wasmPath)));
        const bytes = new Uint8Array(readFileSync(documentPath));
        const markdown = api.toMarkdownBytes(bytes, api.formatFromExtension('docx'));
        if (!markdown.includes('MediaForge Office document')) process.exit(4);
        console.log('PASS: WASM converts Office document to Markdown');
        """);

    var start = new ProcessStartInfo("node")
    {
        WorkingDirectory = runtimeDirectory,
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    };
    start.ArgumentList.Add(scriptPath);
    start.ArgumentList.Add(modulePath);
    start.ArgumentList.Add(wasmPath);
    start.ArgumentList.Add(docxPath);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js for the WASM smoke test.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"WASM document conversion failed: {await stderr}{Environment.NewLine}{await stdout}");
    }
    passed++;
    Console.WriteLine((await stdout).Trim());
}
