# Relatório de Entrega e Transição (Handover) - MediaForge

## 1. Sumário Executivo
A aplicação MediaForge (WinUI 3 / C# .NET 8) foi submetida a um ciclo completo de auditoria arquitetural, testes de caracterização (TDD),加固 de segurança (OWASP), otimização de execução e observabilidade estruturada. O projeto encontra-se estabilizado e pronto para lançamento.

---

## 2. Alterações Realizadas vs. Estado Original

1. **União de Ficheiros JSON (`MergeJsonAsync`)**:
   - **Original**: Quando ficheiros JSON com estruturas misturadas (ex.: um Objeto `{}` e um Array `[]`) eram fornecidos, a aplicação agrupava-os silenciosamente num array genérico.
   - **Atualizado**: Implementada política estrita *Fail-Fast* lançando `ArgumentException` explícito com mensagem explicativa em português, prevenindo corrupção estrutural downstream.
2. **Observabilidade e Registo Estruturado (`ErrorLogger`)**:
   - **Original**: Sem persistência estruturada de exceções não tratadas.
   - **Atualizado**: Criado `MediaForge.Services.ErrorLogger`, gravando registos JSON atómicos com contexto, timestamp UTC, tipo de exceção, mensagem e stack trace em `%LOCALAPPDATA%\MediaForge\logs\error.log`. Conectado ao `UnhandledException` no `App.xaml.cs`.
3. **Segurança e Isolamento**:
   - Sanitização estrita de nomes de ficheiros de saída (`SafeOutputBaseName`) impedindo Path Traversal.
   - Chamadas a ferramentas externas (FFmpeg, ImageMagick, Poppler, Pandoc) protegidas por `ProcessStartInfo.ArgumentList` contra injeção de comandos.
   - Conversor WebAssembly Office/EPUB protegido via anfitrião virtual isolado (`mediaforge.local`) e Content Security Policy (CSP) estrito.
   - Limites rígidos de DoS (100 MB max input, 16 000 px max dimensão, 5 min de timeout de subprocessos).
4. **Documentação e Governação Técnica**:
   - Criado `docs/ADR-001-Decisoes-Criticas.md` documentando as decisões e justificações técnicas.
   - Criado `CHECKLIST_LANCAMENTO.md` com guia de empacotamento MSIX/Inno Setup e estratégia de reversão (Rollback).
   - Atualizado `README.md` com pré-requisitos essenciais (WebView2 Runtime, permissões standard).

---

## 3. Localização dos Artefactos Gerados (Release)

Os pacotes oficiais de produção foram compilados em modo `Release` (x64) com sucesso:

- **Pacote MSIX Bundle**:
  `C:\Users\josepires\Documents\Projetos\MediaForge\AppPackages\MediaForge_1.0.0.0_Test\MediaForge_1.0.0.0_x64.msixbundle`
- **Pacote MSIX x64**:
  `C:\Users\josepires\Documents\Projetos\MediaForge\AppPackages\MediaForge_1.0.0.0_Test\MediaForge_1.0.0.0_x64.msix`
- **Scripts de Instalação e Certificação Local**:
  `C:\Users\josepires\Documents\Projetos\MediaForge\AppPackages\MediaForge_1.0.0.0_Test\Install.ps1`
  `C:\Users\josepires\Documents\Projetos\MediaForge\scripts\install-dev.ps1`

---

## 4. Limitações Conhecidas e Recomendações de Monitorização

1. **Dependência do Microsoft Edge WebView2 Runtime**:
   - A conversão de ficheiros Office (`.docx`, `.xlsx`, `.pptx`), OpenDocument (`.odt`, `.ods`), EPUB e CSV depende do WebView2 Runtime para executar o módulo WebAssembly AnyDoc localmente.
   - Em máquinas Windows 10 LTSC desatualizadas onde o runtime possa ter sido removido, a conversão desses formatos específicos falhará informando o utilizador na interface.
2. **Monitorização de Ficheiros Temporários**:
   - Embora `try/finally` garanta a limpeza de subpastas de trabalho após cada conversão, recomenda-se monitorizar a pasta `%LOCALAPPDATA%\MediaForge\Temp` no caso de encerramentos abruptos da máquina (ex.: corte de energia durante transcodificação FFmpeg).
3. **Análise de Erros**:
   - Caso os utilizadores reportem falhas em formatos raros, consultar diretamente `%LOCALAPPDATA%\MediaForge\logs\error.log`.
