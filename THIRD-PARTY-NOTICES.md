# Componentes de terceiros

O código do MediaForge é distribuído sob a licença MIT (ficheiro `LICENSE`).

A aplicação e o instalador incluem ferramentas de linha de comandos de terceiros. Estas
ferramentas são **executáveis independentes**, lançados como processos separados pelo
MediaForge. Não são ligadas (linked) ao código da aplicação e mantêm as suas próprias
licenças, indicadas abaixo.

| Componente | Versão | Licença | Código-fonte e licença |
|---|---|---|---|
| FFmpeg (build *essentials* de gyan.dev) | 9.0.1 | GPL v3 ou posterior (compilado com `--enable-gpl --enable-version3`, inclui x264 e x265) | https://ffmpeg.org/download.html · https://github.com/GyanD/codexffmpeg/releases/tag/9.0.1 |
| Pandoc | 3.11 | GPL v2 ou posterior | https://github.com/jgm/pandoc/releases/tag/3.11 |
| Poppler (pdftoppm, pdftotext, ...) | 26.09.0 | GPL v2 ou v3 | https://poppler.freedesktop.org/ |
| ImageMagick | 7.1.2-29 | Licença ImageMagick | https://imagemagick.org/license/ (texto completo em `Tools/imagemagick/LICENSE.txt`) |

## Código-fonte das ferramentas GPL

O código-fonte correspondente às versões acima está disponível nas ligações da tabela, nos
repositórios e páginas de lançamento dos respetivos projetos. Quem o preferir pode pedi-lo
abrindo uma issue em https://github.com/josepires-dev/MediaForge/issues, e será indicado
o endereço exato da versão distribuída.

## Verificação das ferramentas

FFmpeg e Pandoc não fazem parte do repositório. São descarregados pelo
`scripts/fetch-tools.ps1` a partir dos endereços oficiais acima, com verificação de SHA-256
(valores em `scripts/tools.lock.json`, decisão descrita em `docs/ADR-003-Ferramentas-fora-do-repositorio.md`).

## Dependências .NET

As bibliotecas NuGet usadas (Windows App SDK e outras) têm as suas licenças próprias,
disponíveis nos respetivos pacotes em https://www.nuget.org/.
