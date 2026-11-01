using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MediaForge.Services;

[assembly: CollectionBehavior(MaxParallelThreads = 2)]

namespace MediaForge.Tests.Support;

internal static class TestPaths
{
    public static string ProjectRoot { get; } = FindProjectRoot();

    public static string Tools => Path.Combine(ProjectRoot, "Tools");

    public static string Wasm => Path.Combine(ProjectRoot, "Assets", "Converter", "wasm");

    private static string FindProjectRoot()
    {
        var overridden = Environment.GetEnvironmentVariable("MEDIAFORGE_ROOT");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MediaForge.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Raiz do projeto não encontrada (MediaForge.sln). Defina MEDIAFORGE_ROOT.");
    }
}

/// <summary>Pasta temporária isolada e um <see cref="ConversionService"/> por classe de testes.</summary>
public class Workspace : IDisposable
{
    public Workspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "MediaForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Service = new ConversionService(TestPaths.Tools, Path.Combine(Root, "temp"));
    }

    public string Root { get; }

    public string Output => Path.Combine(Root, "out");

    public ConversionService Service { get; }

    public string PathOf(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(Root))
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Um processo externo ainda pode ter a pasta aberta; é lixo temporário.
            }
        }
    }
}

internal static class TestData
{
    public static async Task WritePpmAsync(string path)
        => await File.WriteAllTextAsync(path, "P3\n2 2\n255\n255 0 0 0 255 0\n0 0 255 255 255 0\n");

    public static int GetZipEntryCount(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries.Count;
    }

    public static async Task RunAsync(string executable, string workingDirectory, IReadOnlyList<string> arguments)
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

    public static byte[] CreateWave()
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

    public static byte[] CreatePdf(IReadOnlyList<string> pageTexts)
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

    public static void CreateDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml",
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");
        AddEntry(archive, "_rels/.rels",
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
        AddEntry(archive, "word/document.xml",
            """<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>MediaForge Office document</w:t></w:r></w:p><w:sectPr/></w:body></w:document>""");
    }

    private static void AddEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(value);
    }

    /// <summary>Servidor HTTP mínimo em loopback que responde a um único pedido.</summary>
    public static (string Url, Task ServerTask) StartLocalWebsite()
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

    /// <summary>Converte um .docx com o módulo WASM (anydoc) executado no Node.js e devolve o stdout.</summary>
    public static async Task<string> ConvertDocxWithNodeAsync(string wasmDirectory, string docxPath, string tempRoot)
    {
        var runtimeDirectory = Path.Combine(tempRoot, "node-wasm");
        Directory.CreateDirectory(runtimeDirectory);
        var modulePath = Path.Combine(runtimeDirectory, "anydoc_wasm.mjs");
        var wasmPath = Path.Combine(runtimeDirectory, "anydoc_wasm_bg.wasm");
        File.Copy(Path.Combine(wasmDirectory, "anydoc_wasm.js"), modulePath, overwrite: true);
        File.Copy(Path.Combine(wasmDirectory, "anydoc_wasm_bg.wasm"), wasmPath, overwrite: true);
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
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js for the WASM test.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"WASM document conversion failed: {await stderr}{Environment.NewLine}{await stdout}");
        }
        return (await stdout).Trim();
    }
}
