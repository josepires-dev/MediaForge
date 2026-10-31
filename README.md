# MediaForge para Windows

Aplicação Windows (WinUI 3 / MSIX) equivalente ao UniversalConverter para XAMPP. Converte ficheiros localmente, sem Apache, PHP, Python ou Node.js durante a utilização normal.

## Pré-requisitos de Execução

- **Sistema Operativo**: Windows 10 (versão 1809 / Build 17763 ou superior) ou Windows 11 (x64).
- **Microsoft Edge WebView2 Runtime**: Necessário para a conversão de documentos locais (Office, OpenDocument, EPUB, CSV via WebAssembly). Já vem pré-instalado por predefinição no Windows 10 e 11 atualizados; caso falte, pode ser obtido no site oficial da Microsoft.
- **Permissões**: Corre totalmente em ambiente de utilizador padrão (Standard User), sem necessidade de elevação de Administrador.

## Funcionalidades

| Função | Comportamento |
|---|---|
| Imagens | Formatos de escrita realmente disponibilizados pelo ImageMagick portátil; redimensionamento, rotação e qualidade |
| PDF para imagem | Poppler renderiza cada página; uma página gera uma imagem, várias páginas geram um ZIP |
| PDF para texto | Extração de texto selecionável para Markdown/TXT (sem OCR) |
| Vídeo | MP4, WEBM, MKV, AVI e MOV; redimensionamento, rotação, qualidade; extração de fotogramas para ZIP |
| Áudio | MP3, WAV, M4A e AAC |
| Markdown | `.md`, `.txt`, `.py`, `.html`, `.json`, `.log`, URLs, Office/OpenDocument, EPUB e CSV |
| Notebooks | `.py` para `.ipynb`, com marcadores `# %%` |
| União Markdown & JSON | Junta dois ou mais `.md` pela ordem escolhida, ou ficheiros `.json` homogéneos (arrays, objetos chave-valor ou conversas exportadas) |
| Entradas | Ficheiro (arrastar e largar), texto, URL e seleção múltipla de Markdown/JSON |
| Histórico | Local, com opção de limpar |

## Privacidade e limites

- Processamento 100% local. Ficheiros temporários gerados em `%LOCALAPPDATA%\MediaForge\Temp`, eliminados imediatamente no fim ou cancelamento de cada operação.
- Entradas até 100 MB, dimensões até 16 000 px, processos externos limitados a 5 minutos.
- Histórico em `%LOCALAPPDATA%\MediaForge\activity-history.json` (mantém até 100 registos).
- Logs de erro não tratados em `%LOCALAPPDATA%\MediaForge\logs\error.log`.
- Só a conversão de URLs usa a Internet.
- Documentos Office/OpenDocument/EPUB/CSV usam o módulo WebAssembly incluído e o Microsoft Edge WebView2 Runtime em ambiente isolado.

## Ferramentas portáteis incluídas

FFmpeg, ImageMagick, Poppler, Pandoc e o módulo AnyDoc WebAssembly.

## Arquitetura e Decisões

Consulte a pasta `docs/` para as Architecture Decision Records (ADRs):
- [ADR-001: Decisões Críticas de Arquitetura e Segurança](docs/ADR-001-Decisoes-Criticas.md)

## Compilar e testar

Requisitos de desenvolvimento: SDK .NET 8, Windows App SDK/NuGet e Node.js (apenas para o teste isolado do WebAssembly).

```powershell
.\scripts\build.ps1
.\scripts\publish.ps1
```

Os pacotes MSIX sem assinatura exigem o Modo de Programador ou um certificado confiável (`scripts\new-dev-cert.ps1`). Alternativa: `installer\MediaForge.iss` (Inno Setup).
