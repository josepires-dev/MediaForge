using MediaForge.Services;

// Diagnóstico do ambiente do MediaForge (ferramentas portáteis, política do ImageMagick
// e pasta temporária). Uso: dotnet run --project Doctor -- [<raiz do projeto>]
// Código de saída: 0 = tudo OK, 1 = pelo menos uma verificação falhou.
var positionalArgs = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToArray();
var projectRoot = positionalArgs.Length > 0 ? Path.GetFullPath(positionalArgs[0]) : FindProjectRoot();
var report = EnvironmentDoctor.Run(Path.Combine(projectRoot, "Tools"));
Console.WriteLine(EnvironmentDoctor.Format(report));
return report.All(check => check.Passed) ? 0 : 1;

static string FindProjectRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "MediaForge.sln")))
        {
            return directory.FullName;
        }
        directory = directory.Parent;
    }
    throw new InvalidOperationException("Raiz do projeto não encontrada (MediaForge.sln). Indique-a como argumento.");
}
