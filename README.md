# Secretária para Autônomo

Agenda online para profissionais autônomos (manicure, cabeleireiro, etc.) com página pública de agendamento e integração com o WhatsApp (Cloud API oficial da Meta).

## Estrutura

```
src/
  SecretariaAutonomo.Domain          Entidades (Tenant, Profissional, Servico, Agendamento, Cliente...)
  SecretariaAutonomo.Contracts       DTOs compartilhados entre API e front
  SecretariaAutonomo.Application     Regras: AgendaService, CalculadoraDisponibilidade, WhatsApp (lembretes e respostas)
  SecretariaAutonomo.Infrastructure  EF Core + PostgreSQL, cliente da WhatsApp Cloud API, job de lembretes
  SecretariaAutonomo.Api             ASP.NET Core Minimal API (painel, página pública, webhook)
  SecretariaAutonomo.Web             Blazor WebAssembly + MudBlazor, instalável como PWA
tests/
  SecretariaAutonomo.Tests           Testes da regra de disponibilidade
```

Toda regra de agenda fica em `AgendaService`. O painel, a página pública e o WhatsApp (futuro chatbot) chamam o mesmo serviço.

## Rodando localmente

Pré-requisitos: .NET 10 SDK e Docker.

```bash
docker compose up -d                                    # PostgreSQL (5433) e Mailpit (http://localhost:8025)
dotnet run --project src/SecretariaAutonomo.Api         # http://localhost:5286
dotnet run --project src/SecretariaAutonomo.Web         # http://localhost:5010
```

Em Development, a API aplica as migrations e cria o tenant de exemplo **Maria Nails** sozinha.

- Painel: http://localhost:5010. Entre com **maria@exemplo.com** / **maria1234** ou crie uma conta em `/cadastrar`.
- Página pública: http://localhost:5010/agendar/maria-nails
- OpenAPI: http://localhost:5286/openapi/v1.json

Testes: `dotnet test`

Nova migration:

```bash
dotnet tool restore
dotnet ef migrations add NomeDaMigration --project src/SecretariaAutonomo.Infrastructure --startup-project src/SecretariaAutonomo.Api --output-dir Persistencia/Migrations
```

## WhatsApp (Cloud API)

1. Crie um app em https://developers.facebook.com (tipo *Business*) e adicione o produto **WhatsApp**.
2. Guarde os segredos com user-secrets, nunca no appsettings:

   ```bash
   cd src/SecretariaAutonomo.Api
   dotnet user-secrets init
   dotnet user-secrets set "WhatsApp:AccessToken"   "<token>"
   dotnet user-secrets set "WhatsApp:PhoneNumberId" "<id do número>"
   dotnet user-secrets set "WhatsApp:VerifyToken"   "<invente um texto>"
   dotnet user-secrets set "WhatsApp:AppSecret"     "<app secret>"
   ```

3. Exponha a API para a internet durante o desenvolvimento (ex.: `ngrok http 5286` ou dev tunnels do VS) e cadastre o webhook `https://<url>/webhooks/whatsapp` com o mesmo VerifyToken. Assine o campo **messages**.
4. No WhatsApp Manager, crie o template **`lembrete_agendamento`** (categoria *Utilidade*, idioma *pt_BR*):
   - Corpo: `Olá {{1}}! Lembrete do seu horário: {{2}} com {{3}}, {{4}}. Você confirma?`
   - Botões de resposta rápida: **Confirmar** e **Cancelar** (nessa ordem).
5. Crie o template **`agendamento_confirmado`** (categoria *Utilidade*, idioma *pt_BR*), enviado assim que o cliente agenda pela página pública:
   - Corpo: `Olá {{1}}! Recebemos seu agendamento de {{2}} com {{3}} para {{4}}. Toque em Confirmar para garantir seu horário ou em Cancelar se não puder ir.`
   - Botões de resposta rápida: **Confirmar** e **Cancelar** (nessa ordem). As respostas são tratadas pelo mesmo webhook do lembrete.
6. Crie também o template **`agendamento_remarcado`** (categoria *Utilidade*, idioma *pt_BR*), usado quando o profissional remarca e marca "avisar o cliente":
   - Corpo: `Olá {{1}}! Seu horário de {{2}} com {{3}} foi remarcado para {{4}}.`

O `LembreteWorker` roda a cada 5 minutos e envia o lembrete para quem tem horário nas próximas 24h. Quando o cliente toca em um botão, o webhook confirma ou cancela o agendamento e responde.

O `ConfirmacaoWorker` envia a confirmação dos agendamentos da página pública em segundo plano, logo após a reserva, para o cliente não esperar pela Meta. A cada 5 minutos, ele também varre o banco atrás de confirmações pendentes, por exemplo se a API reiniciou ou a rede caiu. O agendamento é marcado como confirmado **antes** do envio, então uma falha depois do envio nunca gera mensagem repetida. Se o horário é em menos de 24h, essa mensagem também conta como lembrete.

Para testar sem a Meta, aponte `WhatsApp:BaseUrl` para um servidor falso que responda `{"messages":[{"id":"wamid.X"}]}`.

> Sem as configurações do WhatsApp, o sistema funciona normalmente e só registra no log as mensagens que enviaria.

### Chatbot (menus)

O cliente conversa com o número da plataforma usando botões e listas interativas do WhatsApp. Também pode digitar o número da opção ("2") ou o nome dela ("manicure"). As opções são:

- **Agendar horário:** serviço → profissional (só se houver mais de um) → dia (próximos 14 dias com vaga) → horário (8 por página) → nome (só para cliente novo) → confirmação.
- **Meus horários:** lista os próximos horários do cliente e permite **remarcar** ou **cancelar** cada um.
- **Serviços e preços:** mostra a tabela de serviços.

Comandos e atalhos:
- *menu*, *oi* ou *voltar* recomeçam a conversa, e depois de 30 min parada ela também volta ao menu.
- Frases soltas como "quero agendar", "quero cancelar" ou "quanto custa" levam direto à opção certa.

**Qual negócio?** Como todos os negócios usam o mesmo número, o bot identifica o negócio assim:
1. pelo código `#slug` na mensagem, que vem pronto do botão **"Prefere agendar pelo WhatsApp?"** da página pública;
2. se não houver código, pelo histórico: se o telefone é cliente de um único negócio, entra direto; se for de vários, pergunta qual.

Configure `WhatsApp:NumeroExibicao` com o número da plataforma (E.164 sem "+") para o botão aparecer na página pública.

Os celulares brasileiros são comparados **com e sem o 9 extra**, porque o WhatsApp às vezes entrega o número sem ele.

O fluxo fica em `Application/Chatbot/ChatbotService.cs`. O bot só devolve as respostas (texto, botões ou lista), e quem envia é o `ProcessadorMensagens`. Por isso os testes em `ChatbotTests` simulam conversas inteiras sem WhatsApp.

## Login

ASP.NET Core Identity com bearer tokens: o de acesso dura 1h e o de renovação 14 dias.

- `POST /auth/cadastrar` cria o negócio (tenant), o profissional e o usuário, e já devolve os tokens.
- `POST /auth/entrar` faz o login; `POST /auth/renovar` troca o refresh token por tokens novos.
- `GET /api/conta` devolve os dados do usuário logado.
- O TenantId vai numa claim do token, e o filtro de `/api` usa essa claim para isolar os dados de cada negócio.
- Senha: mínimo de 8 caracteres, com letra minúscula e número. A conta é bloqueada por 10 min após 5 tentativas erradas.

O front guarda os tokens no `localStorage` e renova o token de acesso sozinho antes de expirar.

As chaves que assinam os tokens (ASP.NET Data Protection) ficam na tabela `DataProtectionKeys`. Por isso as sessões sobrevivem a reinícios e deploys.

### Recuperação de senha

- `POST /auth/esqueci-senha` envia por e-mail um link que vale **1 hora** e só pode ser usado uma vez.
  - A resposta é sempre a mesma, exista a conta ou não, e o e-mail sai por uma fila em segundo plano. Assim, nem a resposta nem o tempo revelam quem tem cadastro.
  - No máximo 1 e-mail a cada 2 minutos por conta, para o formulário não virar ferramenta de spam.
  - O endereço do link vem de `App:UrlFrontend`, nunca do Host da requisição.
- `POST /auth/redefinir-senha` troca a senha e já devolve os tokens (entra logado). Também:
  - desbloqueia a conta, se tiver sido bloqueada por tentativas erradas;
  - confirma o e-mail;
  - encerra as sessões nos outros aparelhos, que deixam de conseguir renovar o token.

**E-mail em desenvolvimento:** o `docker compose up -d` também sobe o **Mailpit**. Os e-mails enviados pelo sistema aparecem em http://localhost:8025, e nada sai de verdade.

**E-mail em produção:** configure `Email:SmtpHost`, `SmtpPorta`, `SmtpUsuario`, `SmtpSenha` e `Remetente` com os dados do seu provedor (Brevo, Amazon SES etc.). Use user-secrets ou variáveis de ambiente, nunca o appsettings. Sem `SmtpHost`, os e-mails são descartados e um aviso vai para o log.

## Avisos de novos agendamentos

Quando um cliente agenda pela página pública ou pelo WhatsApp, o profissional é avisado de duas formas:

- **Pop-up no painel:** com o painel aberto, ele consulta `GET /api/agendamentos/novos` a cada 30 s e mostra o pop-up. A Agenda do dia e a Semana se atualizam sozinhas.
- **Notificação no aparelho (Web Push):** chega mesmo com o painel fechado. Cada aparelho liga os avisos pelo sino da barra do topo. No primeiro acesso, o painel também oferece ligar. Tocar na notificação abre a agenda no dia do horário.

Como funciona: o `AgendaService` põe o agendamento na fila, o `PushWorker` envia em segundo plano (o cliente não espera) e as inscrições que o navegador cancelou são apagadas sozinhas. Ao sair do painel, o aparelho deixa de receber avisos.

**Chaves VAPID.** Sem elas, o sino não aparece e nada é enviado. Gere um par uma vez e guarde em user-secrets (produção: variáveis de ambiente):

```bash
npx web-push generate-vapid-keys
cd src/SecretariaAutonomo.Api
dotnet user-secrets set "WebPush:ChavePublica" "<Public Key>"
dotnet user-secrets set "WebPush:ChavePrivada" "<Private Key>"
dotnet user-secrets set "WebPush:Assunto"      "mailto:voce@seudominio.com.br"
```

Não troque as chaves depois de publicar: as inscrições existentes deixam de funcionar e cada aparelho precisa ligar o sino de novo.

Limitações:
- **iPhone/iPad:** só funciona com o painel instalado na tela de início (Compartilhar → Adicionar à Tela de Início), iOS 16.4 ou mais novo. No Safari comum, o sino não aparece.
- Exige HTTPS (em `localhost` funciona sem).
- Por segurança, a API só aceita inscrições dos serviços de push conhecidos (Google, Mozilla, Microsoft e Apple). Veja `ServicosDePush` em `PainelEndpoints`.

## Antes de ir para produção

- ~~Recuperação de senha~~ ✅. Falta configurar o SMTP de produção e `App:UrlFrontend`.
- Configurar `Cors:Origens` e a connection string de produção.
- Gerar as chaves VAPID de produção (`WebPush:ChavePublica`, `WebPush:ChavePrivada` e `WebPush:Assunto`).
- Servir API e front em HTTPS.
- Mensagens de template são cobradas pela Meta; inclua isso no preço dos planos.

## Próximos passos sugeridos

1. ~~Login e cadastro de novos profissionais~~ ✅
2. ~~Telas de horários de trabalho e de folgas/férias~~ ✅
3. ~~Visão semanal da agenda e remarcação~~ ✅
4. ~~Confirmação imediata por WhatsApp ao agendar pela página pública~~ ✅
5. ~~Chatbot por menus~~ ✅ Próximos passos: um número de WhatsApp por negócio (mapear o `phone_number_id` do webhook para o tenant) e IA para entender texto livre.
6. Pagamento de sinal via Pix (Asaas / Mercado Pago).
7. App nas lojas com .NET MAUI Blazor Hybrid, reaproveitando os componentes.
