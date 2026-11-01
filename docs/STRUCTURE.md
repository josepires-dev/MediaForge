# Estrutura do projeto MediaForge

```
MediaForge.sln                 Solução (App + Core + Tests + Doctor)
MediaForge.csproj              App WinUI 3 (MSIX, win-x64, self-contained)
App.xaml / App.xaml.cs         Ponto de entrada
MainWindow.xaml(.cs)           Janela principal
MainPage.xaml(.cs)             Navegação: Converter / Histórico
ConverterPage.xaml(.cs)        Conversão (ficheiro, texto, URL, união Markdown)
HistoryPage.xaml(.cs)          Histórico de conversões
app.manifest                   Manifesto Win32
Package.appxmanifest           Manifesto MSIX
Assets/                        Ícones e recursos do conversor (WebAssembly)
Services/                      ConversionService, HistoryService, UrlSafetyPolicy, EnvironmentDoctor
Core/                          MediaForge.Core.csproj (liga os ficheiros de Services/)
Tools/                         imagemagick e poppler (versionados); ffmpeg.exe e pandoc.exe (descarregados)
Tests/                         MediaForge.Tests (xUnit)
Doctor/                        MediaForge.Doctor (diagnóstico do ambiente)
Properties/                    launchSettings.json, PublishProfiles/win-x64.pubxml
scripts/                       build, publish, clean, doctor, fetch-tools, new-dev-cert, install-dev, tools.lock.json
installer/                     MediaForge.iss (Inno Setup)
docs/                          ADRs, estrutura do projeto, checklist de lançamento
.github/workflows/build.yml    CI no Windows
.github/                       Dependabot, templates de issues e de Pull Requests
SECURITY.md / CONTRIBUTING.md  Política de segurança e guia de contribuição
```

## Fluxo

1. `scripts\fetch-tools.ps1` obtém o FFmpeg e o Pandoc com verificação de SHA-256 (o `build.ps1` chama-o).
2. `scripts\build.ps1` compila e corre os testes.
3. `scripts\doctor.ps1` valida ferramentas, política do ImageMagick e pasta temporária.
4. `scripts\publish.ps1` gera a pasta publicada e o MSIX (`-Sign` para assinar com certificado de dev).
5. `installer\MediaForge.iss` gera o instalador a partir da pasta publicada.
6. O CI faz 1 a 5 e carrega os artefactos.
