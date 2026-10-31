# ADR-002: Proteção SSRF na conversão de URLs, diagnóstico de ambiente e limpeza de temporários

## Estado
Aceite (Accepted)

## Data
2026-10-31

## Contexto
A conversão de URLs é a única função do MediaForge que usa a Internet. Um URL fornecido pelo utilizador (ou contido num ficheiro `.url`) pode apontar para `localhost`, redes privadas ou para o serviço de metadados de plataformas cloud (`169.254.169.254`). Validar o URL só antes do pedido não chega: redireccionamentos HTTP e DNS rebinding contornam essa validação.

Esta decisão incorpora ideias do UniversalConverter (comando `doctor`) e reforça o MediaForge sem alterar o seu modelo de execução 100% local.

---

## Decisões

### 1. `UrlSafetyPolicy`: validação em dois níveis
- **Decisão**: (a) validar esquema (apenas HTTP/HTTPS), comprimento (máx. 2048) e anfitrião (`localhost`, `.local`, `.localdomain`, `.internal`, IPs literais); (b) verificar **todos** os endereços IP resolvidos dentro de um `ConnectCallback` do `SocketsHttpHandler`, imediatamente antes de abrir o socket.
- **Justificação**: A verificação na ligação cobre redireccionamentos e DNS rebinding, porque o IP validado é o IP efetivamente usado. O proxy do sistema é desativado (`UseProxy = false`) para que o destino real nunca seja ocultado por um proxy.
- **Intervalos bloqueados (IPv4)**: `0.0.0.0/8`, `10/8`, `100.64/10` (CGNAT), loopback, `169.254/16`, `172.16/12`, `192.168/16`, `192.0.0/24`, `198.18/15`, multicast e reservados (`>= 224`).
- **IPv6**: loopback, `::`, link-local, site-local, ULA e multicast. Endereços IPv4 mapeados em IPv6 são convertidos antes da verificação.

### 2. Rede privada apenas em testes
- **Decisão**: `UrlSafetyPolicy.AllowPrivateNetwork` existe exclusivamente para os smoke tests (servidor em loopback). O construtor de `ConversionService` usa `UrlSafetyPolicy.Default`.
- **Consequência**: não há opção de configuração nem variável de ambiente para desligar a proteção em produção.

### 3. `EnvironmentDoctor`
- **Decisão**: expor um diagnóstico (`--doctor` nos smoke tests e `scripts/doctor.ps1`) que verifica ferramentas portáteis (FFmpeg, Pandoc, ImageMagick, Poppler), a política do ImageMagick (coders `HTTP`, `HTTPS` e `URL` com `rights="none"`) e a pasta temporária (escrita e mínimo de 1 GiB livre).
- **Justificação**: falhas de empacotamento ou um `policy.xml` alterado deixam de aparecer como erros obscuros de conversão e passam a ser detetadas cedo, também no CI. O código de saída é 0 (tudo OK) ou 1 (alguma falha).

### 4. Limpeza de diretórios de trabalho órfãos
- **Decisão**: `ConversionService.CleanupStaleWorkDirectories(TimeSpan)` remove apenas pastas cujo nome é um GUID (formato `N`) e cuja última escrita é anterior ao limite (24 h por predefinição).
- **Justificação**: um crash ou encerramento forçado pode deixar temporários para trás. Pastas recentes (operações em curso) e pastas que não são do MediaForge nunca são tocadas.

### 5. Mensagens de diagnóstico sem fugas
- **Decisão**: `ConversionService.CleanDiagnostic` remove URLs, colapsa quebras de linha e limita o texto a 600 caracteres.
- **Justificação**: a saída de ferramentas externas pode conter URLs com tokens. Esta informação não deve chegar à interface nem a `error.log`.

---

## Consequências
- Conversões de URL para redes internas falham com a mensagem "bloqueado"; é o comportamento pretendido.
- Cada ligação faz uma resolução DNS própria; o custo é desprezável para uso interativo.
- O CI executa o diagnóstico antes dos testes, pelo que um pacote de ferramentas incompleto falha cedo e com mensagem clara.
- Limitação conhecida: a política não impede que um site público devolva conteúdo que referencie recursos externos. Esse risco é tratado pelo bloqueio dos coders de rede do ImageMagick (ver ADR-001) e pelos limites de tamanho e tempo.
