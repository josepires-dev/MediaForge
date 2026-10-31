using System.Text;
using System.Text.RegularExpressions;

namespace MediaForge.Services;

public sealed record DoctorCheck(string Name, bool Passed, string Detail);

/// <summary>
/// Diagnóstico do ambiente (equivalente ao `php cli.php doctor` do UniversalConverter):
/// ferramentas portáteis, política de segurança do ImageMagick e pasta temporária.
/// </summary>
public static class EnvironmentDoctor
{
    private const long MinimumFreeBytes = 1L * 1024 * 1024 * 1024;
    private static readonly string[] BlockedCoders = ["HTTP", "HTTPS", "URL"];
    private static readonly string[] RequiredTools =
    [
        "ffmpeg.exe",
        "pandoc.exe",
        Path.Combine("imagemagick", "magick.exe"),
        Path.Combine("poppler", "pdftotext.exe"),
        Path.Combine("poppler", "pdftocairo.exe"),
        Path.Combine("poppler", "pdfinfo.exe")
    ];

    public static IReadOnlyList<DoctorCheck> Run(string? toolsDirectory = null, string? temporaryRoot = null)
    {
        var tools = Path.GetFullPath(toolsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Tools"));
        var temp = Path.GetFullPath(temporaryRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaForge",
            "Temp"));

        var checks = new List<DoctorCheck>();
        foreach (var tool in RequiredTools)
        {
            var path = Path.Combine(tools, tool);
            checks.Add(File.Exists(path)
                ? new DoctorCheck($"Ferramenta {tool}", true, "Encontrada.")
                : new DoctorCheck($"Ferramenta {tool}", false, $"Em falta: {path}"));
        }
        checks.Add(CheckImageMagickPolicy(tools));
        checks.Add(CheckTemporaryRoot(temp));
        return checks;
    }

    public static string Format(IEnumerable<DoctorCheck> checks)
    {
        var builder = new StringBuilder();
        foreach (var check in checks)
        {
            builder.Append(check.Passed ? "[OK]   " : "[FALHA] ")
                .Append(check.Name).Append(" - ").AppendLine(check.Detail);
        }
        return builder.ToString();
    }

    private static DoctorCheck CheckImageMagickPolicy(string tools)
    {
        const string name = "Política de segurança do ImageMagick";
        var policy = Path.Combine(tools, "imagemagick", "policy.xml");
        if (!File.Exists(policy))
        {
            return new DoctorCheck(name, false, $"policy.xml em falta: {policy}");
        }

        var text = File.ReadAllText(policy);
        var missing = BlockedCoders
            .Where(coder => !Regex.IsMatch(
                text,
                $"domain=\"coder\"\\s+rights=\"none\"\\s+pattern=\"{coder}\"",
                RegexOptions.IgnoreCase))
            .ToArray();
        return missing.Length == 0
            ? new DoctorCheck(name, true, "Delegados HTTP, HTTPS e URL bloqueados.")
            : new DoctorCheck(name, false, $"Coders sem bloqueio: {string.Join(", ", missing)}. Não altere o policy.xml.");
    }

    private static DoctorCheck CheckTemporaryRoot(string temp)
    {
        const string name = "Pasta temporária";
        try
        {
            Directory.CreateDirectory(temp);
            var probe = Path.Combine(temp, ".doctor-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);

            var root = Path.GetPathRoot(temp);
            if (!string.IsNullOrEmpty(root))
            {
                var free = new DriveInfo(root).AvailableFreeSpace;
                if (free < MinimumFreeBytes)
                {
                    return new DoctorCheck(name, false, $"Menos de 1 GiB livre em {root}.");
                }
            }
            return new DoctorCheck(name, true, $"Escrita permitida em {temp}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DoctorCheck(name, false, $"Sem permissão de escrita em {temp}: {exception.Message}");
        }
    }
}
