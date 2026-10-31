# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/).

## [Não lançado]

### Segurança
- Conversão de URLs protegida contra SSRF (`UrlSafetyPolicy`): só HTTP/HTTPS, URLs até 2048 caracteres, bloqueio de `localhost`, redes privadas, CGNAT, link-local (incluindo metadados cloud) e endereços reservados IPv4/IPv6.
- Os endereços IP são verificados no momento da ligação, o que cobre redireccionamentos e DNS rebinding; o proxy do sistema é ignorado.
- Diagnósticos de ferramentas externas passam a ocultar URLs e a limitar o tamanho (600 caracteres).
- Adicionados `SECURITY.md` (relatório privado de vulnerabilidades) e Dependabot para NuGet e GitHub Actions.

### Adicionado
- `EnvironmentDoctor` e `scripts/doctor.ps1` (também `--doctor` nos smoke tests): verifica ferramentas portáteis, política do ImageMagick e pasta temporária.
- `scripts/fetch-tools.ps1` e `scripts/tools.lock.json`: descarregam FFmpeg 9.0.1 e Pandoc 3.11 com verificação de SHA-256 (ver ADR-003).
- `ConversionService.CleanupStaleWorkDirectories`: remove diretórios de trabalho órfãos com mais de 24 h, sem tocar em pastas recentes ou alheias.
- `docs/ADR-002-Protecao-SSRF-e-Diagnostico.md` e `docs/ADR-003-Ferramentas-fora-do-repositorio.md`.
- `CONTRIBUTING.md`, modelos de issues (erro e funcionalidade) e de Pull Request.
- Novos smoke tests: SSRF (endereços e URLs), extração de URL, limpeza de diagnósticos, limpeza de temporários, doctor, união JSON/Markdown com tipos incompatíveis.

### Alterado
- Mensagens de erro da união de Markdown/JSON mais precisas (mínimo de dois ficheiros, tipo de ficheiro esperado).
- CI: obtém FFmpeg e Pandoc com cache, executa o diagnóstico do ambiente, cancela execuções redundantes, usa permissões mínimas, cache de NuGet e `timeout-minutes`.
- `build.ps1` obtém automaticamente as ferramentas em falta; os scripts PowerShell passam a UTF-8 com BOM (acentos corretos no PowerShell 5.1).
- Notas internas (`STRUCTURE.md`, `CHECKLIST_LANCAMENTO.md`) movidas para `docs/`; README com o badge de build corrigido.

### Corrigido
- O CI falhava sempre porque `Tools/ffmpeg.exe` e `Tools/pandoc.exe` não estavam no repositório (excedem ou aproximam-se do limite do GitHub).

### Removido
- `HANDOVER.md`, `ja_fiz.txt` e `gh_installer.msi` do repositório.

## [1.0.0] - 2026-10-11

### Alterado
- A aplicação passa a ter apenas as funções do UniversalConverter (XAMPP): conversão de imagens, PDF, vídeo, áudio, texto/Markdown, URLs, notebooks e união de Markdown, com histórico local.

### Removido
- Transferência de media (yt-dlp, gallery-dl, DownloadService, modelos de download, página de download, cookies, playlists).

### Adicionado
- `MediaForge.sln`, scripts de build/publicação, perfil `win-x64`, `launchSettings.json`, CI, instalador Inno Setup, `.gitignore`, `.editorconfig`, `global.json`, `nuget.config`.
