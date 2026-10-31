# ADR-001: Decisões Críticas de Arquitetura e Segurança

## Estado
Aceite (Accepted)

## Data
2026-10-20

## Contexto
O MediaForge é uma aplicação cliente Windows nativa (WinUI 3 / .NET 8) orientada à privacidade e conversão local de ficheiros, sem dependências de servidores ou runtimes dinâmicos externos (como Node.js, Python ou PHP instalados na máquina do utilizador). Como o software processa dados arbitrários de utilizadores não confiáveis e orquestra ferramentas portáteis externas, requer limites rigorosos de segurança, previsibilidade e isolamento.

---

## Decisões

### 1. Fail-Fast explícito no `MergeJsonAsync` (Opção A)
- **Decisão**: Rejeitar a união de ficheiros JSON com raiz incompatível (mistura de Objetos `{}` e Listas `[]`) lançando `ArgumentException` explícito, em vez de recorrer a embrulho silencioso num array genérico.
- **Justificação**: Unir um objeto dicionário a uma lista de registos corrompe o modelo semântico dos dados. Falhar rápido (*Fail-Fast*) impede a corrupção silenciosa de ficheiros downstream e força consistência de tipos.

### 2. Execução de Ferramentas via `ProcessStartInfo.ArgumentList`
- **Decisão**: Abandonar qualquer concatenação de strings para passar parâmetros aos binários externos (`ffmpeg.exe`, `magick.exe`, `pdftoppm.exe`, `pandoc.exe`), utilizando exclusivamente `ProcessStartInfo.ArgumentList.Add(...)`.
- **Justificação**: Previne vulnerabilidades graves de Injeção de Comandos (Command Injection). Caracteres maliciosos em nomes de ficheiros (ex.: espaços, `;`, `&`, aspas e traços) são automaticamente escapados pela API do sistema operativo, sem invocação de interpretadores de comandos (`cmd.exe` ou `powershell`).

### 3. Isolamento e Content Security Policy (CSP) do WebView2
- **Decisão**: Configurar o módulo de conversão Office/OpenDocument/EPUB com mapeamento de anfitrião virtual (`SetVirtualHostNameToFolderMapping("mediaforge.local", ...)`) e cabeçalho CSP estrito no `bridge.html`:
  `default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self'`.
- **Justificação**: O componente WebAssembly executa no motor Chromium do WebView2. O mapeamento virtual impede acesso ao sistema de ficheiros local via protocolo `file://` arbitrário, bloqueia acessos de rede externos e restringe a comunicação ao canal de mensagens bidirecional seguro (`postMessage`).

### 4. Limites de Proteção contra Negação de Serviço (DoS)
- **Decisão**: Restringir ficheiros de entrada a no máximo 100 MB (`MaximumInputBytes = 100L * 1024 * 1024`), dimensões de imagem a 16 000 px (`MaximumDimension = 16_000`) e tempo máximo de processos externos a 5 minutos (`ProcessTimeout = TimeSpan.FromMinutes(5)`).
- **Justificação**: Evita o esgotamento de memória (RAM) e espaço em disco em ambientes de utilizador padrão (non-admin), prevenindo ataques do tipo "zip bomb", "image decompression bombs" ou processos zombis em loop infinito.

---

## Consequências
- A aplicação é determinística, segura contra ficheiros com nomes forjados e imune a fugas de privilégios.
- Casos com JSONs estruturalmente díspares requerem normalização prévia por parte do utilizador.
- Ficheiros fora dos limites de segurança são rejeitados na fronteira da API antes do arranque de qualquer ferramenta pesada.
