# Contribuir para o MediaForge

Obrigado pelo interesse! Este guia resume o que precisa para propor uma alteração.

## Antes de começar

- Procure nas [issues](../../issues) se o assunto já existe.
- Para alterações grandes, abra primeiro uma issue a descrever a proposta.
- Vulnerabilidades: siga [SECURITY.md](SECURITY.md), não use issues públicas.

## Ambiente de desenvolvimento

Requisitos: Windows 10/11 x64, SDK .NET 8 (versão fixada em `global.json`), Windows App SDK/NuGet e Node.js (apenas para o teste do WebAssembly).

```powershell
.\scripts\build.ps1      # descarrega as ferramentas em falta, compila e corre os smoke tests
.\scripts\doctor.ps1     # verifica ferramentas, política do ImageMagick e pasta temporária
```

O FFmpeg e o Pandoc não estão no repositório (são grandes demais para o GitHub). O `build.ps1` chama `scripts\fetch-tools.ps1`, que os descarrega com verificação de SHA-256 a partir de `scripts\tools.lock.json`.

## Fluxo de trabalho

1. Faça fork e crie uma branch a partir de `main` (`feature/...` ou `fix/...`).
2. Faça alterações pequenas e focadas, uma ideia por Pull Request.
3. Adicione ou atualize os smoke tests em `Tests/Program.cs` para o comportamento novo.
4. Corra `.\scripts\build.ps1` e confirme que termina sem erros e com todos os testes a passar.
5. Atualize `CHANGELOG.md` (secção *Não lançado*) e, se a decisão for relevante, adicione um ADR em `docs/`.
6. Abra o Pull Request e preencha o modelo.

## Regras de código

- Siga o `.editorconfig`.
- Ferramentas externas só com `ProcessStartInfo.ArgumentList`, nunca com concatenação de strings (ver ADR-001).
- Qualquer acesso à rede passa por `UrlSafetyPolicy` (ver ADR-002).
- Mensagens ao utilizador em português europeu.
- Não adicione binários ao repositório; use `scripts\tools.lock.json`.

## Commits

Mensagens curtas no imperativo, preferencialmente no formato `tipo: descrição` (`feat`, `fix`, `docs`, `chore`, `ci`, `test`, `refactor`).

## Licença

Ao contribuir, aceita que o seu trabalho é licenciado sob a licença MIT do projeto.
