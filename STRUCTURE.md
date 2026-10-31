# Estrutura do projeto MediaForge

```
MediaForge.sln                 Solução (App + Core + SmokeTests)
MediaForge.csproj              App WinUI 3 (MSIX, win-x64, self-contained)
App.xaml / App.xaml.cs         Ponto de entrada
MainWindow.xaml(.cs)           Janela principal
MainPage.xaml(.cs)             Navegação: Converter / Histórico
ConverterPage.xaml(.cs)        Conversão (ficheiro, texto, URL, união Markdown)
HistoryPage.xaml(.cs)          Histórico de conversões
app.manifest                   Manifesto Win32
Package.appxmanifest           Manifesto MSIX
Assets/                        Ícones e recursos do conversor (WebAssembly)
Services/                      ConversionService, HistoryService
Core/                          MediaForge.Core.csproj (liga os ficheiros de Services/)
Tools/                         ffmpeg, imagemagick, poppler, pandoc
Tests/                         MediaForge.SmokeTests
Properties/                    launchSettings.json, PublishProfiles/win-x64.pubxml
scripts/                       build.ps1, publish.ps1, new-dev-cert.ps1, install-dev.ps1
installer/                     MediaForge.iss (Inno Setup)
.github/workflows/build.yml    CI no Windows
```

## Fluxo

1. `scripts\build.ps1` compila e corre os smoke tests.
2. `scripts\publish.ps1` gera a pasta publicada e o MSIX (`-Sign` para assinar com certificado de dev).
3. `installer\MediaForge.iss` gera o instalador a partir da pasta publicada.
4. O CI faz 1 e 2 e carrega os artefactos.
