# MAUI Forge agora é 100% visual: conheça o novo dashboard que substitui o script de versão

Se você já perdeu meia hora do dia rodando um script de PowerShell só para descobrir se o `Info.plist` está sincronizado com o `AndroidManifest.xml`, sabe exatamente o problema que o **MAUI Forge** resolve. E a versão mais recente dá um salto: o que começou como um script de terminal virou uma central de comando visual completa para gerenciar todos os seus apps .NET MAUI — versões, builds, dispositivos e Git — em um único lugar, direto do navegador.

Bora conhecer o que mudou e por que isso vale a pena instalar agora.

## De script PowerShell a plataforma real

O MAUI Forge nasceu como `maui-version.ps1`, um script de ~2700 linhas que cuidava de versões e builds. Funcionava, mas exigia decorar comandos, rodar em um terminal por vez, e não escalava bem quando você tinha uma dezena de apps espalhados em pastas diferentes.

A reescrita em **.NET 10** trouxe a mesma automação, só que embrulhada em duas interfaces:

- Uma **TUI** (terminal interativo) para quem vive no shell, construída com Spectre.Console.
- Um **dashboard web** completo, com Tailwind CSS e SignalR para logs em tempo real — e é o modo padrão agora.

```bash
maui-forge
```

Isso é tudo que você digita. O dashboard sobe em `http://localhost:5123` e pronto — sem configurar nada.

<div class="blog-callout info"><h4>ℹ️ Ainda prefere o terminal?</h4>
  <p>O modo TUI continua disponível com <code>maui-forge --cli</code>. Nada foi removido — só ganhou uma alternativa visual muito mais rica.</p></div>

## O painel principal: todos os seus apps, de relance

Assim que você abre o dashboard, vê um mosaico com todas as aplicações monitoradas — nome, tecnologia, versão de cada plataforma, branch do Git e status, tudo em cards limpos:

![Dashboard principal do MAUI Forge com cards de aplicativos, status Git e ações de build](/assets/blog/v1.6-launch/01-dashboard-cards.png)

Repare no que cada card já entrega sem um clique a mais:

- **Versão e build number** de Projeto, Android e iOS lado a lado — se algo estiver fora de sincronia, você vê na hora.
- **Badge de Git**: branch atual e status `Clean` ou `Dirty`.
- **Botões de ação direto no card**: `Bump +1` (incrementa versão e build), `Build +1` (só o build number) e um menu `Build` com as opções completas.
- **Última atividade** do app ("21d ago", "10d ago"), então os projetos mais recentes sobem naturalmente na sua atenção.

Não curte cards? Um clique troca para visão em lista, mais densa e ideal quando você tem muitos projetos abertos ao mesmo tempo:

![Visão em lista do MAUI Forge mostrando várias aplicações com versões e status Git em formato de tabela](/assets/blog/v1.6-launch/02-dashboard-list.png)

Tem busca por nome/caminho e filtros por tecnologia e status do Git — em segundos você isola só os apps "Dirty" que precisam de commit, por exemplo.

## Build & Run sem sair do navegador

Aqui está a parte que realmente economiza tempo: build e deploy completos, direto do dashboard, com escolha de plataforma e dispositivo.

![Menu de Build mostrando as opções Build Only e Build & Run para Android e iOS](/assets/blog/v1.6-launch/03-build-run-menu.png)

- **Build Only** — só compila, útil para validar antes de commitar.
- **Build & Run** — build completo seguido de deploy no dispositivo/emulador/simulador escolhido, com um seletor de dispositivo físico, emulador Android, AVD ou simulador iOS.
- **Logs ao vivo** via SignalR, com uma barra de progresso que acompanha as etapas (Build → Deploy → Launch) — sem precisar ficar alternando para o terminal para saber se travou ou não.
- **Cancelar build** a qualquer momento, direto da interface.

<div class="blog-callout success"><h4>✅ Clean sem medo</h4>
  <p>O menu de limpeza tem quatro níveis — Quick, Android, iOS, Deep e Nuclear — para quando aquele erro de build teimoso só some depois de zerar tudo.</p></div>

## Git integrado de verdade

Esqueça alternar entre o VS Code, o terminal e o GitHub Desktop só para saber se um app está atrasado em relação ao remoto. O MAUI Forge faz `fetch` automático ao abrir cada app e te avisa:

- Quantos commits você está **à frente/atrás** do remoto.
- Se há alterações **não commitadas**.
- Um resumo do **diff** antes de você decidir commitar.
- **Mensagens de commit geradas por IA** — com suporte a Claude CLI, Gemini CLI, Ollama local ou um modo heurístico ("Smart") que funciona sem depender de nenhuma API externa.

E quando está tudo certo, um botão de **Bump & Push** faz o pacote completo: incrementa a versão, commita com a mensagem formatada e envia pro remoto — de uma vez só.

## Gerenciamento remoto: controle a máquina de build de qualquer lugar

Uma das novidades mais interessantes desta versão é o menu de conexão no topo da tela, que substituiu o antigo painel lateral de "Remote Access":

![Menu de acesso remoto do MAUI Forge com opções para habilitar modo servidor ou conectar a uma máquina remota](/assets/blog/v1.6-launch/04-remote-access-menu.png)

Isso abre um cenário bem prático: sua máquina de build (aquela com o SDK do Android, o Xcode via SSH, os certificados de assinatura) pode rodar o MAUI Forge em **modo servidor**, e você acompanha e dispara builds a partir de qualquer outro computador da rede — sem precisar estar fisicamente sentado ali. Você pode inclusive alternar entre visualizar o **host local** e uma **máquina remota** sem perder a conexão ativa.

<div class="blog-callout info"><h4>ℹ️ Monitoramento de saúde embutido</h4>
  <p>Se o backend local parar de responder — travou, foi fechado, o processo caiu — o dashboard percebe sozinho e mostra um aviso claro, com opção de retry, em vez de deixar você comparando páginas em branco. O mesmo vale para conexões remotas: a saúde da máquina remota agora é monitorada continuamente.</p></div>

## Tudo que o MAUI Forge resolve para você

<ul class="checklist">
  <li>Descoberta automática de apps MAUI, WPF, Blazor, Class Library e até projetos Unity, com profundidade de busca configurável</li>
  <li>Leitura e escrita de versão em 5 formatos diferentes: Info.plist, AndroidManifest.xml, .csproj, AssemblyInfo.cs e ProjectSettings.asset do Unity</li>
  <li>Sincronização de versão entre iOS e Android com um clique</li>
  <li>Snapshot e undo da última alteração de versão — porque bump errado acontece</li>
  <li>Build e deploy para dispositivo físico, emulador ou simulador, Android e iOS</li>
  <li>Publicação de release: .apk / .aab no Android, archive + upload para App Store Connect no iOS</li>
  <li>Diagnóstico completo do ambiente: dotnet, git, ssh, xcrun, adb, emulador e workloads instalados</li>
  <li>Atualização automática via NuGet, com instalação adiada sem travar o que você está fazendo</li>
</ul>

## Por que trocar o script pelo dashboard

Se você gerencia mais de um ou dois apps MAUI, a resposta é simples: **contexto**. Antes, cada decisão (bumpar versão, checar Git, disparar build, escolher dispositivo) exigia lembrar um comando ou ler um menu de texto. Agora, tudo isso está visível ao mesmo tempo, na mesma tela, com feedback instantâneo.

Some a isso:

- **Zero curva de aprendizado** para quem entra no time — não precisa decorar flags de script, é clicar.
- **Builds acompanháveis em tempo real**, sem terminal escondido atrás de outras janelas.
- **Trabalho remoto de verdade**, com o modo servidor rodando na máquina de build e você conectando de onde estiver.
- **Instalação em segundos**, com um único comando, e atualização automática depois disso.

## Como instalar

```bash
dotnet tool install -g CwSoftware.MauiForge
```

Ou, se preferir um executável autocontido (sem depender do SDK do .NET instalado):

```powershell
# Windows
.\install.ps1
```

```bash
# macOS/Linux
./install.sh
```

Depois é só rodar:

```bash
maui-forge --path C:\seus\projetos --depth 2
```

E o dashboard sobe automaticamente em `http://localhost:5123`, já escaneando suas pastas em busca de apps.

---

O MAUI Forge deixou de ser "aquele script que eu uso de vez em quando" para virar o painel de controle que fica aberto o dia inteiro. Se seu fluxo de trabalho com MAUI ainda passa por decorar comandos de terminal, é hora de testar a versão visual — e nunca mais olhar pra trás.
