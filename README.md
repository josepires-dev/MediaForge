# MediaForge para Windows

[![Build](https://github.com/josepires-dev/MediaForge/actions/workflows/build.yml/badge.svg)](https://github.com/josepires-dev/MediaForge/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](https://dotnet.microsoft.com/download/dotnet/8.0)

Aplicação Windows (WinUI 3 / MSIX) equivalente ao UniversalConverter para XAMPP. Converte ficheiros localmente, sem Apache, PHP, Python ou Node.js durante a utilização normal.

## Pré-requisitos de Execução

- **Sistema Operativo**: Windows 10 (versão 1809 / Build 17763 ou superior) ou Windows 11 (x64).
- **Microsoft Edge WebView2 Runtime**: Necessário para a conversão de documentos locais (Office, OpenDocument, EPUB, CSV via WebAssembly). Já vem pré-instalado por predefinição no Windows 10 e 11 atualizados; caso falte, pode ser obtido no site oficial da Microsoft.
- **Permissões**: Corre totalmente em ambiente de utilizador padrão (Standard User), sem necessidade de elevação de Administrador.

## Funcionalidades

| Função | Comportamento |
|---|---|
| **Imagens** | Formatos de escrita realmente disponibilizados pelo ImageMagick portátil; redimensionamento, rotação e qualidade |
| **PDF para imagem** | Poppler renderiza cada página; uma página gera uma imagem, várias páginas geram um ZIP |
| **PDF para texto** | Extração de texto selecionável para Markdown/TXT (sem OCR) |
| **Vídeo** | MP4, WEBM, MKV, AVI e MOV; redimensionamento, rotação, qualidade; extração de fotogramas para ZIP |
| **Áudio** | MP3, WAV, M4A e AAC |
| **Markdown** | `.md`, `.txt`, `.py`, `.html`, `.json`, `.log`, URLs, Office/OpenDocument, EPUB e CSV |
| **Notebooks** | `.py` para `.ipynb`, com marcadores `# %%` |
| **União Markdown & JSON** | Junta dois ou mais `.md` pela ordem escolhida, ou ficheiros `.json` homogéneos (arrays, objetos chave-valor ou conversas exportadas) |
| **Entradas** | Ficheiro (arrastar e largar), texto, URL e seleção múltipla de Markdown/JSON |
| **Histórico** | Local, com opção de limpar |

## Privacidade e limites

- **Processamento 100% local**: Ficheiros temporários gerados em `%LOCALAPPDATA%\MediaForge\Temp`, eliminados imediatamente no fim ou cancelamento de cada operação.
- **Temporários órfãos**: Pastas de trabalho com mais de 24 h (por exemplo, após um crash) são removidas automaticamente; pastas recentes ou que não pertencem ao MediaForge são preservadas.
- **Limites de entrada**: Até 100 MB, dimensões até 16 000 px, processos externos limitados a 5 minutos.
- **Histórico**: Guardado em `%LOCALAPPDATA%\MediaForge\activity-history.json` (mantém até 100 registos).
- **Logs de erro**: Guardados em `%LOCALAPPDATA%\MediaForge\logs\error.log`.
- **Internet**: Apenas a conversão de URLs usa a Internet. Só são aceites URLs HTTP/HTTPS públicos (até 2048 caracteres); `localhost`, redes privadas, endereços link-local e de metadados cloud são bloqueados (proteção SSRF, validada também em redireccionamentos). Ver [ADR-002](docs/ADR-002-Protecao-SSRF-e-Diagnostico.md).
- **Documentos**: Office/OpenDocument/EPUB/CSV usam o módulo WebAssembly incluído e o Microsoft Edge WebView2 Runtime em ambiente isolado.

## Ferramentas portáteis

| Ferramenta | Função | Origem |
|---|---|---|
| **FFmpeg** | Processamento de áudio e vídeo | Descarregada por `scripts\fetch-tools.ps1` (versão e SHA-256 fixados em `scripts\tools.lock.json`) |
| **Pandoc** | Conversão de documentos e HTML | Descarregada por `scripts\fetch-tools.ps1` |
| **ImageMagick** | Manipulação e conversão de imagens | Incluída em `Tools\imagemagick` |
| **Poppler** | Processamento de PDFs | Incluída em `Tools\poppler` |
| **AnyDoc WebAssembly** | Conversão de documentos Office/OpenDocument | Incluída em `Assets\Converter\wasm` |

O FFmpeg e o Pandoc não estão no repositório por excederem ou aproximarem-se do limite de 100 MB do GitHub. O `scripts\build.ps1` obtém-nos automaticamente.

## Arquitetura e Decisões

Consulte a pasta `docs/` para as Architecture Decision Records (ADRs) e para a estrutura do projeto:
- [ADR-001: Decisões Críticas de Arquitetura e Segurança](docs/ADR-001-Decisoes-Criticas.md)
- [ADR-002: Proteção SSRF, diagnóstico de ambiente e limpeza de temporários](docs/ADR-002-Protecao-SSRF-e-Diagnostico.md)
- [ADR-003: Ferramentas grandes fora do repositório](docs/ADR-003-Ferramentas-fora-do-repositorio.md)
- [Estrutura do projeto](docs/STRUCTURE.md) e [checklist de lançamento](docs/CHECKLIST_LANCAMENTO.md)

## Compilar e testar

Requisitos de desenvolvimento: SDK .NET 8, Windows App SDK/NuGet e Node.js (apenas para o teste isolado do WebAssembly).

```powershell
# Limpar artefactos anteriores (opcional)
.\scripts\clean.ps1

# Obter FFmpeg e Pandoc (também é chamado pelo build.ps1; não faz nada se já existirem)
.\scripts\fetch-tools.ps1

# Compilar e executar testes de fumo
.\scripts\build.ps1

# Diagnosticar o ambiente (ferramentas, política do ImageMagick, pasta temporária)
.\scripts\doctor.ps1

# Publicar (pasta + MSIX não assinado)
.\scripts\publish.ps1
```

> [!NOTE]
> Os pacotes MSIX sem assinatura exigem o Modo de Programador ou um certificado confiável (`scripts\new-dev-cert.ps1`). Alternativa: `installer\MediaForge.iss` (Inno Setup).

## Segurança

Para reportar uma vulnerabilidade, siga [SECURITY.md](SECURITY.md). Não use issues públicas.

## Contribuir

Leia [CONTRIBUTING.md](CONTRIBUTING.md). Em resumo: faça fork, crie uma branch, corra `.\scripts\build.ps1`, atualize o `CHANGELOG.md` e abra um Pull Request.

## Licença

[MIT](LICENSE)

As ferramentas incluídas (FFmpeg, Pandoc, Poppler e ImageMagick) são executáveis independentes com licenças próprias, listadas em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
