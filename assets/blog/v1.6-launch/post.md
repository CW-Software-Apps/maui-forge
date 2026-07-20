# MAUI Forge agora é 100% visual: conheça o novo dashboard open source que substitui o script de versão

Se você já perdeu meia hora do dia rodando um script de PowerShell só para descobrir se o `Info.plist` está sincronizado com o `AndroidManifest.xml`, sabe exatamente o problema que o **MAUI Forge** resolve. E a versão mais recente dá um salto: o que começou como um script de terminal virou uma central de comando visual completa e **100% open source** para gerenciar todos os seus apps .NET MAUI — versões, builds, dispositivos, Mac e PC, e Git — em um único lugar, direto do navegador.

Bora conhecer o que mudou e por que isso vale a pena instalar agora.

## De script PowerShell a plataforma open source

O MAUI Forge nasceu como `maui-version.ps1`, um script de ~2700 linhas que cuidava de versões e builds. Funcionava, mas exigia decorar comandos, rodar em um terminal por vez, e não escalava bem quando você tinha uma dezena de apps espalhados em pastas diferentes.

A reescrita em **.NET 10** trouxe a mesma automação, embrulhada em duas interfaces — e com o código-fonte inteiro aberto no GitHub:

- Uma **TUI** (terminal interativo) para quem vive no shell, construída com Spectre.Console.
- Um **dashboard web** completo, com Tailwind CSS e SignalR para logs em tempo real — e é o modo padrão agora.

E como é open source, instalar não exige clonar repositório nem compilar nada. Um único comando via `dotnet tool` resolve:

```bash
dotnet tool install -g CwSoftware.MauiForge
```

Depois disso, é só rodar:

```bash
maui-forge
```

O dashboard sobe em `http://localhost:5123` e pronto — sem configurar nada. E o melhor: o MAUI Forge se **atualiza sozinho**. Ele verifica a versão mais recente no NuGet em segundo plano e, quando encontra uma atualização, avisa e deixa você instalar com um clique — sem travar o que você está fazendo no momento.

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

## Chega de editar Info.plist e AndroidManifest na mão

Esse é, sem exagero, um dos recursos que mais economiza tempo no dia a dia — e talvez o motivo original de o MAUI Forge existir. Bumpar versão de um app MAUI manualmente significa abrir (no mínimo) três arquivos diferentes, editar os números com cuidado para não errar a sintaxe, e torcer para não esquecer nenhum:

- `Info.plist` no iOS, com sua sintaxe XML de `<key>CFBundleShortVersionString</key>`
- `AndroidManifest.xml`, com `android:versionName` e `android:versionCode`
- O `.csproj`, com `<ApplicationDisplayVersion>` e `<ApplicationVersion>`

O MAUI Forge faz isso com **um clique**:

- **`Bump +1`** — incrementa a versão *e* o build number, em todos os arquivos de plataforma ao mesmo tempo, mantendo tudo sincronizado.
- **`Build +1`** — quando você só precisa subir o build number (por exemplo, para reenviar ao TestFlight sem mudar a versão pública), sem tocar no restante.
- **Sincronização iOS ↔ Android** — se as versões saíram de sincronia por qualquer motivo, um botão resolve.
- **Snapshot e undo** — se um bump saiu errado, dá pra desfazer a última alteração de versão sem precisar caçar no histórico do Git.

<div class="blog-callout success"><h4>✅ Sem erro de digitação, sem XML quebrado</h4>
  <p>Cada escrita é feita direto na estrutura do arquivo (Info.plist, AndroidManifest.xml, .csproj, AssemblyInfo.cs ou até o ProjectSettings.asset do Unity), então não existe risco de salvar um XML mal formatado por engano.</p></div>

## Trabalhando ao mesmo tempo no PC e no Mac

Aqui está talvez a maior dor de quem desenvolve MAUI: builds de iOS exigem um Mac, mas seu ambiente de trabalho principal costuma ser Windows. Isso normalmente significa configurar Pair to Mac no Visual Studo, brigar com certificados, perder a paciência com a sincronização de arquivos e, no fim, abandonar o fluxo e fazer tudo manualmente via SSH.

O MAUI Forge resolve isso com o **modo servidor**: você roda o MAUI Forge no Mac (ou em qualquer máquina com o toolchain de build), habilita o modo servidor, e conecta a partir do seu PC:

![Menu de acesso remoto do MAUI Forge com opções para habilitar modo servidor ou conectar a uma máquina remota](/assets/blog/v1.6-launch/04-remote-access-menu.png)

A partir daí, o PC vira um controle remoto completo do Mac:

- Você continua **codando no PC**, no seu editor de sempre.
- Faz commit e push normalmente — o MAUI Forge cuida de manter os dois repositórios sincronizados, então o Mac sempre builda o código mais recente.
- Dispara o **build e o deploy no dispositivo iOS ou simulador** direto do dashboard aberto no PC, sem precisar tocar fisicamente no Mac.
- Escolhe entre **Release** ou **Debug** na hora, sem editar configuração nenhuma.

E o resultado fica ainda mais evidente quando você abre o Build & Run de um app no Mac remoto: a lista de dispositivos compatíveis aparece automaticamente, physical devices e simuladores lado a lado — aqui, por exemplo, rodando o **SilvaData** contra o Mac conectado em `192.168.3.29`:

![Modal de Build & Run do SilvaData mostrando a lista de dispositivos iOS físicos e simuladores compatíveis, com opções de configuração Release ou Debug](/assets/blog/v1.6-launch/05-silvadata-ios-devices.png)

Repare que o iPad físico já aparece identificado (`iPadCezar`) ao lado dos simuladores disponíveis (`iPad`, `iPad Air 11-inch (M4)` e outros) — tudo detectado automaticamente na máquina remota, sem nenhuma configuração manual de SSH ou certificado feita no PC.

<div class="blog-callout info"><h4>ℹ️ Monitoramento de saúde embutido</h4>
  <p>Se o backend remoto ou local parar de responder — travou, foi fechado, o processo caiu — o dashboard percebe sozinho e mostra um aviso claro, com opção de retry, em vez de deixar você comparando páginas em branco.</p></div>

## Build & Run sem sair do navegador

E build no geral — não só no Mac remoto — ficou muito mais direto. Basta escolher plataforma e ação no menu:

![Menu de Build mostrando as opções Build Only e Build & Run para Android e iOS](/assets/blog/v1.6-launch/03-build-run-menu.png)

- **Build Only** — só compila, útil para validar antes de commitar.
- **Build & Run** — build completo seguido de deploy no dispositivo/emulador/simulador escolhido, com um seletor de dispositivo físico, emulador Android, AVD ou simulador iOS.
- **Release ou Debug** — escolhido na hora, sem precisar editar nenhum arquivo de configuração do projeto.
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

## Tudo que o MAUI Forge resolve para você

<ul class="checklist">
  <li>Descoberta automática de apps MAUI, WPF, Blazor, Class Library e até projetos Unity, com profundidade de busca configurável</li>
  <li>Leitura e escrita de versão em 5 formatos diferentes: Info.plist, AndroidManifest.xml, .csproj, AssemblyInfo.cs e ProjectSettings.asset do Unity — sem edição manual</li>
  <li>Sincronização de versão entre iOS e Android com um clique</li>
  <li>Snapshot e undo da última alteração de versão — porque bump errado acontece</li>
  <li>Build e deploy para dispositivo físico, emulador ou simulador, Android e iOS, em Release ou Debug</li>
  <li>Modo servidor para builds de iOS num Mac remoto, controlado 100% pelo PC</li>
  <li>Publicação de release: .apk / .aab no Android, archive + upload para App Store Connect no iOS</li>
  <li>Diagnóstico completo do ambiente: dotnet, git, ssh, xcrun, adb, emulador e workloads instalados</li>
  <li>Instalação e atualização automática via NuGet, sem travar o que você está fazendo</li>
  <li>100% open source, código aberto no GitHub</li>
</ul>

## Por que trocar o script pelo dashboard

Se você gerencia mais de um ou dois apps MAUI, a resposta é simples: **contexto**. Antes, cada decisão (bumpar versão, checar Git, disparar build, escolher dispositivo) exigia lembrar um comando, editar um arquivo XML na mão ou ler um menu de texto. Agora, tudo isso está visível ao mesmo tempo, na mesma tela, com feedback instantâneo.

Some a isso:

- **Zero curva de aprendizado** para quem entra no time — não precisa decorar flags de script nem sintaxe de plist, é clicar.
- **Builds acompanháveis em tempo real**, sem terminal escondido atrás de outras janelas.
- **PC e Mac trabalhando juntos de verdade**, sem Pair to Mac, sem sincronizar pasta manualmente, sem perder tempo configurando o ambiente — codar no PC e buildar/rodar no Mac fica tão simples quanto clicar em um botão.
- **Open source e gratuito**, instalado em segundos com um único comando, e sempre atualizado sozinho depois disso.

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

O MAUI Forge deixou de ser "aquele script que eu uso de vez em quando" para virar o painel de controle que fica aberto o dia inteiro — aberto, gratuito, e conectando seu PC ao seu Mac sem fricção nenhuma. Se seu fluxo de trabalho com MAUI ainda passa por decorar comandos de terminal ou editar Info.plist na mão, é hora de testar a versão visual — e nunca mais olhar pra trás.
