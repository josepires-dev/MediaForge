# Checklist de Lançamento (Release Gate) - MediaForge

## 1. Verificação de Versão e Metadados
- [x] **Versão do Produto**: `1.0.0` (Conforme `CHANGELOG.md` e manifesto do pacote).
- [x] **Framework Target**: `.NET 8.0` (`net8.0-windows10.0.19041.0` / `net8.0`).
- [x] **Arquitetura Alvo**: `win-x64` (Processamento 64-bit nativo).
- [x] **Segurança de Entrada**: Limites estritos ativos (100 MB max file, 16 000 px max dimensão, 5 min timeout).
- [x] **Permissões**: Corre sob privilégios normais de utilizador standard (non-admin).

---

## 2. Passos para Gerar os Pacotes de Distribuição

### Opção A: Pacote MSIX (Recomendado para Windows 10/11)
1. Abrir a consola PowerShell no diretório raiz do projeto.
2. (Opcional para teste de dev) Gerar ou renovar certificado de teste:
   ```powershell
   .\scripts\new-dev-cert.ps1
   ```
3. Executar o script de publicação com assinatura local:
   ```powershell
   .\scripts\publish.ps1 -Configuration Release -Sign
   ```
4. O pacote MSIX final será gerado no diretório `AppPackages\MediaForge_1.0.0.0_Test\`.

### Opção B: Instalador Standalone Tradicional (Inno Setup)
1. Certificar-se de que a publicação foi realizada:
   ```powershell
   .\scripts\publish.ps1 -Configuration Release
   ```
2. Compilar o ficheiro de instalação através do Inno Setup:
   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\installer\MediaForge.iss
   ```
3. O instalador `MediaForge-Setup.exe` será gerado na pasta `installer\Output\`.

---

## 3. Plano de Rollback (Reversão de Emergência)

Se uma versão lançada apresentar falhas críticas de compatibilidade ou regressão em produção:

1. **Procedimento para Instalações MSIX**:
   - Incrementar a versão de correção no `Package.appxmanifest` (ex: `1.0.0.1`) apontando para os binários estáveis anteriores.
   - Redistribuir o pacote atualizado; o Windows Installer substituirá a versão com defeito sem perdas de ficheiros do utilizador.
2. **Procedimento para Instalador Inno Setup**:
   - Reinstalar a versão estável prévia por cima da versão defeituosa (os binários em `Program Files\MediaForge` são sobrescritos com segurança).
3. **Integridade de Dados e Histórico Local**:
   - O histórico de atividade (`%LOCALAPPDATA%\MediaForge\activity-history.json`) é retrocompatível e mantém-se intacto entre versões.
   - Limpar ficheiros temporários órfãos (se existirem) através da eliminação de `%LOCALAPPDATA%\MediaForge\Temp`.
