# ADR-003: Ferramentas grandes fora do repositório

## Estado
Aceite (Accepted)

## Data
2026-10-31

## Contexto
O `Tools/ffmpeg.exe` (98 MB) e o `Tools/pandoc.exe` (223 MB) estavam no `.gitignore` por causa do limite de 100 MB por ficheiro do GitHub, mas o `build.ps1` e a compilação dependem deles. Resultado: o CI no GitHub falhava em todas as execuções com `MSB3030` (ficheiro não encontrado), e qualquer pessoa que clonasse o repositório não conseguia compilar sem instruções externas.

## Decisão
- FFmpeg e Pandoc deixam de ser ficheiros do repositório e passam a ser descarregados por `scripts/fetch-tools.ps1`.
- `scripts/tools.lock.json` fixa, para cada ferramenta, a versão, o URL oficial de release, o SHA-256 do arquivo, o caminho do executável dentro do arquivo e o SHA-256 do executável.
- O script verifica os dois hashes antes de instalar e recusa qualquer discrepância. É idempotente e usa uma cache local (no CI, `actions/cache` com a chave baseada no hash do `tools.lock.json`).
- `build.ps1` chama o `fetch-tools.ps1` automaticamente.
- ImageMagick (30 MB) e Poppler continuam versionados por estarem abaixo do limite.

## Consequências
- O repositório fica com clone mais leve e o CI passa a ser reprodutível.
- Atualizar uma ferramenta é uma alteração de uma entrada no `tools.lock.json`, revista num Pull Request.
- O primeiro build precisa de acesso à Internet; depois funciona offline a partir da cache.
- Ficam por tratar as licenças de redistribuição ao empacotar o instalador (FFmpeg *essentials* é GPL); ver `CHECKLIST_LANCAMENTO.md`.
