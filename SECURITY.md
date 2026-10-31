# Política de Segurança

## Versões suportadas

Apenas a versão mais recente publicada em [Releases](../../releases) recebe correções de segurança.

## Como reportar uma vulnerabilidade

**Não abra uma issue pública** para problemas de segurança.

Use o relatório privado do GitHub: separador **Security** → **Report a vulnerability**
(<https://github.com/josepires-dev/MediaForge/security/advisories/new>).

Inclua, se possível:

- descrição do problema e impacto esperado;
- passos para reproduzir (ficheiro de exemplo, URL ou comando);
- versão do MediaForge e do Windows.

Compromisso de resposta: confirmação em até 7 dias e plano de correção ou mitigação em até 30 dias.

## Âmbito

Dentro do âmbito, entre outros:

- contorno da proteção SSRF na conversão de URLs (ver [ADR-002](docs/ADR-002-Protecao-SSRF-e-Diagnostico.md));
- injeção de argumentos ou de comandos nas ferramentas externas (FFmpeg, ImageMagick, Poppler, Pandoc);
- escrita de ficheiros fora das pastas previstas (path traversal);
- contorno dos limites de tamanho, dimensão ou tempo;
- fuga do isolamento do módulo WebAssembly / WebView2.

Fora do âmbito: vulnerabilidades nas próprias ferramentas de terceiros que não possam ser mitigadas pelo MediaForge (reporte-as aos respetivos projetos) e ataques que exijam já acesso de administrador à máquina.

## Ferramentas de terceiros

O MediaForge empacota FFmpeg, ImageMagick, Poppler e Pandoc em versões fixas. As versões e os hashes das ferramentas descarregadas estão em `scripts/tools.lock.json`. A política do ImageMagick (`Tools/imagemagick/policy.xml`) bloqueia os coders de rede e é verificada por `scripts/doctor.ps1`.
