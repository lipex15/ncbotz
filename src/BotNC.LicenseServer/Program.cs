using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using BotNC.LicenseServer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Environment.GetEnvironmentVariable("PEXBOT_LICENSE_DATA") ??
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PEXBOT-LicenseServer");
var store = new LicenseStore(Path.Combine(dataDirectory, "licenses.db"));
await store.InitializeAsync();

var adminHash = await store.GetSettingAsync("admin-password-hash");
if (adminHash is null)
{
    var bootstrapPassword = Environment.GetEnvironmentVariable("PEXBOT_ADMIN_INITIAL_PASSWORD");
    if (string.IsNullOrWhiteSpace(bootstrapPassword) || bootstrapPassword.Length < 16)
        throw new InvalidOperationException("Na primeira execução, defina PEXBOT_ADMIN_INITIAL_PASSWORD com pelo menos 16 caracteres.");
    adminHash = LicenseStore.HashPassword(bootstrapPassword);
    await store.SetSettingAsync("admin-password-hash", adminHash);
    Environment.SetEnvironmentVariable("PEXBOT_ADMIN_INITIAL_PASSWORD", null);
}

var privateKey = await store.GetSettingAsync("signing-private-key");
using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
if (privateKey is null)
{
    privateKey = Convert.ToBase64String(signer.ExportPkcs8PrivateKey());
    await store.SetSettingAsync("signing-private-key", privateKey);
}
else signer.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
var publicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
var dummyPasswordHash = LicenseStore.HashPassword("not-a-valid-account");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/login";
        options.Cookie.Name = "PEXBOT.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(4);
        options.Events.OnSigningIn = context =>
        {
            // O painel local funciona em HTTP somente no loopback. Fora dele o cookie exige HTTPS.
            if (context.Request.Host.Host is "localhost" or "127.0.0.1")
                context.CookieOptions.Secure = false;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();
var adminPort = int.TryParse(Environment.GetEnvironmentVariable("PEXBOT_ADMIN_PORT"), out var configuredAdminPort)
    ? configuredAdminPort : 5928;
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin") &&
        context.Connection.LocalPort != adminPort)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/public-key", () => Results.Ok(new { publicKey }));

app.MapPost("/api/activate", async (ActivationRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password) ||
        request.Username.Length > 80 || request.DeviceId?.Length is not (>= 32 and <= 100) ||
        request.Fingerprint?.Length is not (>= 32 and <= 128) || request.MachineName?.Length is not (>= 1 and <= 100))
        return Results.BadRequest(new { error = "Dados de ativação inválidos." });

    var user = await store.GetUserAsync(request.Username.Trim());
    // O mesmo trabalho de hash para usuário inexistente reduz diferenças observáveis.
    var hash = user?.PasswordHash ?? dummyPasswordHash;
    if (!LicenseStore.VerifyPassword(request.Password, hash) || user is null || user.Disabled)
        return Results.Json(new { error = "Usuário ou senha inválidos, ou conta desativada." }, statusCode: 401);

    var machineName = request.MachineName.Trim();
    var binding = await store.BindOrRejectAsync(user, request.DeviceId, request.Fingerprint, machineName);
    if (binding != DeviceBindingResult.Allowed)
    {
        if (binding == DeviceBindingResult.DeniedWithNewAlert)
            await SendTelegramAlertAsync(user.Username, user.MachineName ?? "(desconhecido)", machineName);
        return Results.Json(new { error = "Esta conta já está vinculada a outro computador. Solicite liberação ao administrador." }, statusCode: 403);
    }

    var license = new OfflineLicense(1, user.Username, request.DeviceId, request.Fingerprint, DateTimeOffset.UtcNow);
    var payload = JsonSerializer.SerializeToUtf8Bytes(license);
    byte[] signature;
    lock (signer) signature = signer.SignData(payload, HashAlgorithmName.SHA256);
    return Results.Ok(new ActivationResponse(
        Convert.ToBase64String(payload), Convert.ToBase64String(signature), publicKey));
}).RequireRateLimiting("login");

app.MapGet("/admin/login", (HttpContext context, IAntiforgery antiforgery) =>
    Results.Content(Page("Entrar", $"""
        <main class="login-layout">
          <div class="login-intro"><span class="eyebrow">ACESSO RESTRITO</span><div class="brand-mark">P</div>
            <h1>Seu PEXBOT,<br><span>sob controle.</span></h1>
            <p>Crie acessos, acompanhe os computadores autorizados e veja tentativas de uso em outro PC.</p>
          </div>
          <section class="surface login-card" aria-labelledby="login-title">
            <span class="section-kicker">PAINEL DO ADMINISTRADOR</span><h2 id="login-title">Bem-vindo de volta</h2>
            <p class="muted">Digite a senha do painel para continuar.</p>
            <form method="post" action="/admin/login" class="stack-form">
              {TokenField(context, antiforgery)}
              <label for="admin-password">Senha de administrador</label>
              <input id="admin-password" type="password" name="password" autocomplete="current-password" required autofocus>
              <button class="button button-primary button-wide">Entrar no painel <span aria-hidden="true">→</span></button>
            </form>
            <p class="form-note">Este painel só abre neste computador.</p>
          </section>
        </main>
        """), "text/html; charset=utf-8"));

app.MapPost("/admin/login", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    var form = await context.Request.ReadFormAsync();
    if (!LicenseStore.VerifyPassword(form["password"].ToString(), adminHash!))
        return Results.Content(Page("Acesso negado", "<main class='message-page surface'><span class='section-kicker'>ACESSO RESTRITO</span><h1>Senha incorreta</h1><p class='muted'>Confira a senha copiada pelo atalho Painel PEXBOT e tente novamente.</p><a class='button button-primary' href='/admin/login'>Voltar ao login</a></main>"), "text/html; charset=utf-8", statusCode: 401);
    var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme));
    await context.SignInAsync(principal);
    return Results.Redirect("/admin");
}).RequireRateLimiting("login");

app.MapGet("/admin", async (HttpContext context, IAntiforgery antiforgery) =>
{
    var users = await store.ListUsersAsync();
    var alerts = await store.ListAlertsAsync();
    var token = TokenField(context, antiforgery);
    var activeUsers = users.Count(user => !user.Disabled);
    var awaitingActivation = users.Count(user => !user.Disabled && string.IsNullOrWhiteSpace(user.MachineName));
    var html = new StringBuilder($"""
        <div class="dashboard-shell">
          <header class="topbar"><a class="brand" href="/admin"><span class="brand-mark brand-mark-small">P</span><span>PEX<span class="brand-accent">BOT</span><small>PAINEL DE ACESSOS</small></span></a><span class="local-pill"><i></i> Painel local protegido</span></header>
          <main>
            <section class="hero"><div><span class="eyebrow">CONTROLE DE ACESSO</span><h1>Gerencie seus <span>usuários.</span></h1><p>Crie logins, acompanhe ativações e controle quais computadores podem usar o bot.</p></div><a class="button button-primary hero-action" href="#criar-acesso">+ Criar acesso</a></section>
            <section class="stats" aria-label="Resumo"><div class="stat surface"><span class="stat-icon blue">◎</span><div><strong>{users.Count}</strong><span>Usuários cadastrados</span></div></div><div class="stat surface"><span class="stat-icon green">✓</span><div><strong>{activeUsers}</strong><span>Contas ativas</span></div></div><div class="stat surface"><span class="stat-icon amber">◷</span><div><strong>{awaitingActivation}</strong><span>Aguardando primeiro login</span></div></div><div class="stat surface"><span class="stat-icon rose">!</span><div><strong>{alerts.Count}</strong><span>Tentativas em outro PC</span></div></div></section>
            <div class="content-grid">
              <div class="main-column">
                <section class="surface section-card" id="usuarios"><div class="section-heading"><div><span class="section-kicker">ACESSOS CADASTRADOS</span><h2>Usuários <span class="count-chip">{users.Count}</span></h2><p>Uma conta para cada pessoa e computador.</p></div><a class="text-link" href="#criar-acesso">Novo usuário <span aria-hidden="true">↗</span></a></div>
                  <label class="search-box"><span aria-hidden="true">⌕</span><input id="user-search" type="search" placeholder="Buscar por login, nome ou computador" aria-label="Buscar usuários"></label>
                  <div class="user-list" id="user-list">
        """);
    if (users.Count == 0)
        html.Append("<div class='empty-state'><span class='empty-icon'>+</span><h3>Nenhum usuário ainda</h3><p>Crie o primeiro acesso no formulário ao lado.</p></div>");
    foreach (var user in users)
    {
        var username = H(user.Username);
        var displayName = H(user.DisplayName);
        var machine = H(user.MachineName ?? "Aguardando ativação");
        var statusClass = user.Disabled ? "blocked" : string.IsNullOrWhiteSpace(user.MachineName) ? "pending" : "active";
        var statusText = user.Disabled ? "Bloqueado" : string.IsNullOrWhiteSpace(user.MachineName) ? "Primeiro login pendente" : "Ativo";
        var initial = H(user.DisplayName[..1].ToUpperInvariant());
        html.Append($"""
            <article class="user-card" data-search="{H((user.Username + " " + user.DisplayName + " " + (user.MachineName ?? "")).ToLowerInvariant())}">
              <div class="user-main"><div class="avatar">{initial}</div><div class="user-identity"><h3>{displayName}</h3><span>@{username}</span></div><span class="status {statusClass}"><i></i>{statusText}</span></div>
              <div class="user-meta"><span class="meta-label">COMPUTADOR VINCULADO</span><strong>{machine}</strong></div>
              <div class="user-actions"><details class="action-details"><summary>Gerenciar acesso <span aria-hidden="true">⌄</span></summary><div class="action-panel">
                <p>Use estas ações quando a pessoa trocar de computador, perder a senha ou precisar ter o acesso suspenso.</p>
                <div class="action-row"><form method="post" action="/admin/users/{user.Id}/device/reset" onsubmit="return confirm('Liberar a troca de computador para {username}? A próxima ativação usará o novo PC.')">{token}<button class="button button-secondary">Liberar troca de PC</button></form>
                <form method="post" action="/admin/users/{user.Id}/state" onsubmit="return confirm('{(user.Disabled ? "Reativar" : "Bloquear")} o acesso de {username}?')">{token}<input type="hidden" name="disabled" value="{(!user.Disabled).ToString().ToLowerInvariant()}"><button class="button {(user.Disabled ? "button-success" : "button-danger")}">{(user.Disabled ? "Reativar acesso" : "Bloquear acesso")}</button></form></div>
                <form class="password-form" method="post" action="/admin/users/{user.Id}/password">{token}<label for="password-{user.Id}">Definir uma nova senha</label><div><input id="password-{user.Id}" type="password" name="password" minlength="12" maxlength="256" placeholder="Pelo menos 12 caracteres" autocomplete="new-password" required><button class="button button-secondary">Salvar nova senha</button></div></form>
              </div></details></div>
            </article>
            """);
    }
    html.Append($"""
                  </div><p class="search-empty" id="search-empty" hidden>Nenhum usuário corresponde à busca.</p></section>
                <section class="surface section-card alerts-card" id="tentativas"><div class="section-heading"><div><span class="section-kicker">SEGURANÇA</span><h2>Tentativas em outro PC</h2><p>Quando um login já vinculado é usado em outra máquina, a tentativa aparece aqui.</p></div><span class="count-chip">{alerts.Count}</span></div>
                  <div class="alerts-list">
        """);
    if (alerts.Count == 0)
        html.Append("<div class='empty-state compact'><span class='empty-icon'>✓</span><h3>Nenhuma tentativa registrada</h3><p>Se alguém tentar compartilhar uma conta, você verá os dois computadores aqui.</p></div>");
    foreach (var alert in alerts)
        html.Append($"<div class='alert-item'><span class='alert-symbol'>!</span><div><strong>@{H(alert.Username)}</strong><p>PC autorizado: <b>{H(alert.ExistingMachine)}</b><br>PC da tentativa: <b>{H(alert.AttemptedMachine)}</b></p></div><time>{H(alert.OccurredUtc)} UTC</time></div>");
    html.Append($"""
                  </div></section>
              </div>
              <aside class="side-column"><section class="surface section-card create-card" id="criar-acesso"><span class="section-kicker">NOVO ACESSO</span><h2>Adicionar usuário</h2><p>Crie um login individual para liberar o primeiro uso do bot.</p>
                <form method="post" action="/admin/users/quick" class="stack-form">{token}
                  <label for="nickname">Apelido da pessoa</label><input id="nickname" name="nickname" minlength="2" maxlength="100" placeholder="ex.: João" autocomplete="off" required><small>O login e uma senha forte serão gerados automaticamente.</small>
                  <button class="button button-primary button-wide">Gerar acesso <span aria-hidden="true">→</span></button>
                </form></section>
                <section class="tip-card"><span class="tip-icon">i</span><h3>Como funciona</h3><ol><li>Você cria o acesso aqui.</li><li>A pessoa entra uma vez no app.</li><li>O login fica vinculado ao computador dela.</li></ol><p>Para trocar de máquina, use <strong>Liberar troca de PC</strong> no cartão do usuário.</p></section>
              </aside>
            </div>
          </main><footer class="footer">PEXBOT <span>•</span> Painel de administração local</footer>
        </div>
        """);
    html.Append("""<script>const search=document.getElementById('user-search');search?.addEventListener('input',()=>{let shown=0;const query=search.value.trim().toLocaleLowerCase('pt-BR');for(const card of document.querySelectorAll('.user-card')){const match=card.dataset.search.includes(query);card.hidden=!match;if(match)shown++;}document.getElementById('search-empty').hidden=shown!==0;});</script>""");
    return Results.Content(Page("Administração", html.ToString()), "text/html; charset=utf-8");
}).RequireAuthorization();

app.MapPost("/admin/users", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var name = form["displayName"].ToString().Trim();
    var password = form["password"].ToString();
    if (username.Length is < 3 or > 80 || !username.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-') ||
        name.Length is < 1 or > 100 || password.Length is < 12 or > 256)
        return Results.BadRequest("Dados inválidos.");
    if (!await store.CreateUserAsync(username, name, password)) return Results.Conflict("Esse login já existe.");
    return Results.Redirect("/admin");
}).RequireAuthorization();

app.MapPost("/admin/users/quick", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    var form = await context.Request.ReadFormAsync();
    var nickname = form["nickname"].ToString().Trim();
    if (nickname.Length is < 2 or > 100) return Results.BadRequest("Apelido inválido.");
    var slug = NicknameSlug(nickname);
    if (slug.Length < 2) return Results.BadRequest("Use pelo menos duas letras ou números no apelido.");
    var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
        .Replace('+', 'A').Replace('/', 'B');
    for (var attempt = 0; attempt < 12; attempt++)
    {
        var username = $"{slug}{RandomNumberGenerator.GetInt32(100, 10000):D4}";
        if (!await store.CreateUserAsync(username, nickname, password)) continue;
        var content = $"<main class='message-page surface'><span class='section-kicker'>ACESSO CRIADO</span><h1>Pronto, {H(nickname)}!</h1><p>Copie estes dados agora e envie à pessoa. A senha não será exibida novamente.</p><div class='credentials'><p>Login: <strong>{H(username)}</strong></p><p>Senha: <strong>{H(password)}</strong></p></div><a class='button button-primary' href='/admin'>Voltar ao painel</a></main>";
        context.Response.Headers.CacheControl = "no-store";
        return Results.Content(Page("Acesso criado", content), "text/html; charset=utf-8");
    }
    return Results.Problem("Não foi possível gerar um login exclusivo. Tente novamente.", statusCode: 503);
}).RequireAuthorization();

app.MapPost("/admin/users/{id:long}/device/reset", async (long id, HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    await store.SetUserStateAsync(id, null, resetDevice: true);
    return Results.Redirect("/admin");
}).RequireAuthorization();

app.MapPost("/admin/users/{id:long}/state", async (long id, HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    var form = await context.Request.ReadFormAsync();
    if (!bool.TryParse(form["disabled"], out var disabled)) return Results.BadRequest();
    await store.SetUserStateAsync(id, disabled, resetDevice: false);
    return Results.Redirect("/admin");
}).RequireAuthorization();

app.MapPost("/admin/users/{id:long}/password", async (long id, HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    var form = await context.Request.ReadFormAsync();
    var password = form["password"].ToString();
    if (password.Length is < 12 or > 256) return Results.BadRequest("Senha inválida.");
    await store.SetUserPasswordAsync(id, password);
    return Results.Redirect("/admin");
}).RequireAuthorization();

await app.RunAsync();

static string TokenField(HttpContext context, IAntiforgery antiforgery) =>
    $"<input type='hidden' name='__RequestVerificationToken' value='{H(antiforgery.GetAndStoreTokens(context).RequestToken ?? "")}' />";

static string H(string text) => HtmlEncoder.Default.Encode(text);

static string Page(string title, string body) => """
    <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <meta name="color-scheme" content="dark"><title>__TITLE__ · PEXBOT</title>
    <style>
    :root{font-family:Inter,"Segoe UI",system-ui,sans-serif;color:#eef5ff;background:#080e19;font-synthesis:none}
    *{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;min-height:100vh;background:radial-gradient(ellipse 54% 36% at 82% -8%,#1b3d6a55,transparent),radial-gradient(ellipse 48% 42% at -13% 31%,#163c6455,transparent),#080e19;color:#eef5ff}
    button,input{font:inherit}button{cursor:pointer}a{color:inherit;text-decoration:none}button:focus-visible,input:focus-visible,summary:focus-visible,a:focus-visible{outline:2px solid #75baff;outline-offset:3px}
    h1,h2,h3,p{margin-top:0}h1{font-size:clamp(2.4rem,4vw,4.2rem);line-height:1.06;letter-spacing:-.055em;margin-bottom:20px}h2{font-size:1.35rem;letter-spacing:-.025em;margin-bottom:8px}h3{font-size:1rem;margin-bottom:3px}p{line-height:1.55}
    .muted,.hero p,.section-heading p,.create-card>p,.tip-card p{color:#91a2bd}.eyebrow,.section-kicker{color:#75baff;font-size:.72rem;font-weight:800;letter-spacing:.16em}.section-kicker{display:block;margin-bottom:10px}
    .dashboard-shell{max-width:1512px;margin:0 auto;padding:0 40px}.topbar{height:88px;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #ffffff12}.brand{display:inline-flex;gap:12px;align-items:center;font-size:1.32rem;font-weight:900;letter-spacing:.04em;line-height:1}.brand small{display:block;margin-top:7px;font-size:.59rem;font-weight:700;color:#8090aa;letter-spacing:.2em}.brand-accent{color:#79b6ff}.brand-mark{width:68px;height:68px;display:grid;place-items:center;border-radius:23px;background:linear-gradient(150deg,#2b91f6,#1250a9 72%);box-shadow:0 16px 42px #136be444,inset 0 1px #ffffff4b;color:#fff;font-weight:900;font-size:2rem;transform:rotate(-6deg)}.brand-mark-small{width:43px;height:43px;border-radius:14px;font-size:1.4rem}
    .local-pill{display:inline-flex;align-items:center;gap:9px;border:1px solid #315067;background:#112437a8;border-radius:999px;padding:9px 15px;color:#c0d1e4;font-size:.79rem;font-weight:700}.local-pill i,.status i{display:inline-block;width:8px;height:8px;border-radius:50%;background:#4ed7a2;box-shadow:0 0 12px #4ed7a266}
    .hero{padding:48px 0 31px;display:flex;justify-content:space-between;align-items:end;gap:24px}.hero h1{margin-top:12px}.hero h1 span,.login-intro h1 span{background:linear-gradient(90deg,#f8fbff,#83baff);background-clip:text;color:transparent}.hero p{margin-bottom:0;max-width:590px}.hero-action{white-space:nowrap;margin-bottom:8px}
    .surface{background:linear-gradient(150deg,#162235ed,#101a2bec);border:1px solid #34476399;box-shadow:0 20px 55px #00000022,inset 0 1px #ffffff0c;border-radius:23px}.stats{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:15px;margin-bottom:24px}.stat{padding:18px;display:flex;align-items:center;gap:15px;min-height:96px}.stat-icon{width:47px;height:47px;border-radius:15px;display:grid;place-items:center;font-size:1.35rem;font-weight:800;flex:none}.stat-icon.blue{background:#1b559d66;color:#8bc4ff}.stat-icon.green{background:#1c806355;color:#69e4b5}.stat-icon.amber{background:#89682a55;color:#ffca72}.stat-icon.rose{background:#8b455655;color:#ff9eaa}.stat strong{display:block;font-size:1.75rem;letter-spacing:-.04em;line-height:1}.stat span:last-child{display:block;color:#98a9c2;font-size:.76rem;margin-top:8px}
    .content-grid{display:grid;grid-template-columns:minmax(0,1fr) 350px;gap:22px;align-items:start}.main-column,.side-column{display:grid;gap:22px;min-width:0}.section-card{padding:27px}.section-heading{display:flex;justify-content:space-between;align-items:start;gap:15px;margin-bottom:20px}.section-heading h2{display:flex;align-items:center;gap:10px}.section-heading p{font-size:.86rem;margin:0}.count-chip{display:inline-grid;place-items:center;min-width:26px;height:26px;padding:0 7px;border-radius:9px;background:#27436b;color:#acd2ff;font-size:.76rem;font-weight:800}.text-link{color:#8cc5ff;font-size:.83rem;font-weight:700;white-space:nowrap;padding-top:23px}.text-link:hover{text-decoration:underline}
    .search-box{height:47px;border:1px solid #3a506c;border-radius:13px;display:flex;align-items:center;gap:11px;padding:0 14px;background:#0d1929;margin-bottom:18px;color:#83baff}.search-box span{font-size:1.5rem;line-height:1}.search-box input{border:0!important;background:none!important;padding:0!important;flex:1;width:100%;outline:0;color:#eef5ff}.search-box:focus-within{border-color:#69adff;box-shadow:0 0 0 3px #388aff22}
    .user-list{display:grid;gap:12px}.user-card{border:1px solid #354b67;border-radius:17px;background:linear-gradient(115deg,#17283f,#132137);padding:20px;transition:border-color .2s,transform .2s,box-shadow .2s}.user-card:hover{border-color:#5484be;transform:translateY(-2px);box-shadow:0 13px 28px #0003}.user-card[hidden]{display:none}.user-main{display:flex;align-items:center;gap:12px;min-width:0}.avatar{width:43px;height:43px;flex:none;display:grid;place-items:center;border-radius:14px;background:linear-gradient(145deg,#2d67a9,#27405e);color:#d2ebff;font-weight:800}.user-identity{min-width:0;flex:1}.user-identity h3,.user-identity span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.user-identity span{display:block;font-size:.76rem;color:#8fa5c1}.status{display:inline-flex;align-items:center;gap:7px;border-radius:999px;padding:6px 10px;font-size:.72rem;font-weight:800;white-space:nowrap}.status.active{background:#174f425e;color:#6be3ae}.status.pending{background:#7059235c;color:#f8c97d}.status.blocked{background:#71354268;color:#f99ca9}.status.pending i{background:#f3bb60;box-shadow:none}.status.blocked i{background:#e68395;box-shadow:none}.user-meta{display:flex;gap:12px;align-items:baseline;padding:17px 0 15px;margin:15px 0 0;border-top:1px solid #ffffff12;min-width:0}.meta-label{font-size:.66rem;letter-spacing:.12em;color:#728cab;font-weight:800;white-space:nowrap}.user-meta strong{font-size:.82rem;color:#d7e4f5;overflow-wrap:anywhere}.user-actions{border-top:1px solid #ffffff12;padding-top:12px}.action-details summary{list-style:none;cursor:pointer;color:#90c5ff;font-size:.8rem;font-weight:800}.action-details summary::-webkit-details-marker{display:none}.action-details summary span{display:inline-block;margin-left:3px;transition:transform .2s}.action-details[open] summary span{transform:rotate(180deg)}.action-panel{padding-top:18px}.action-panel>p{font-size:.78rem;color:#96aac4;margin:0 0 13px}.action-row{display:flex;flex-wrap:wrap;gap:9px}.action-row form{margin:0}.password-form{display:grid;gap:8px;margin-top:21px;padding-top:18px;border-top:1px solid #ffffff17}.password-form label{font-size:.79rem;color:#d7e4f5;font-weight:700}.password-form>div{display:flex;gap:8px}.password-form input{min-width:0;flex:1}
    .button{appearance:none;border:1px solid transparent;border-radius:11px;display:inline-flex;justify-content:center;align-items:center;gap:9px;padding:11px 15px;color:#eef5ff;font-size:.82rem;font-weight:800;line-height:1.15;transition:filter .2s,transform .2s,background .2s;text-align:center}.button:hover{filter:brightness(1.15);transform:translateY(-1px)}.button-primary{background:linear-gradient(105deg,#227ceb,#2e9af7);box-shadow:0 8px 22px #247bf244}.button-secondary{background:#233851;border-color:#426180;color:#cde5ff}.button-danger{background:#5b2d3c;border-color:#9b5060;color:#ffc1ca}.button-success{background:#1f634c;border-color:#419c75;color:#d1ffe8}.button-wide{width:100%;min-height:46px;margin-top:8px}.button-wide span{font-size:1.15rem}.password-form .button{white-space:nowrap}
    .stack-form{display:grid;gap:9px}.stack-form label{font-size:.78rem;font-weight:800;color:#d7e4f5;margin-top:9px}.stack-form input,.password-form input{width:100%;height:44px;border:1px solid #3a506c;border-radius:11px;background:#0b1625;color:#edf5ff;padding:0 13px;outline:0}.stack-form input:focus,.password-form input:focus{border-color:#68aaff;box-shadow:0 0 0 3px #388aff26}.stack-form input::placeholder,.password-form input::placeholder,.search-box input::placeholder{color:#70839e}.stack-form small{font-size:.7rem;line-height:1.4;color:#8195b0}.create-card h2{font-size:1.5rem}.create-card>p{font-size:.83rem;margin-bottom:18px}.tip-card{padding:23px 25px;background:linear-gradient(135deg,#122a45,#111e33);border:1px solid #315276;border-radius:21px}.tip-icon{display:grid;place-items:center;width:31px;height:31px;border-radius:10px;color:#91c5ff;background:#26588b;font-weight:800;margin-bottom:12px}.tip-card h3{font-size:1rem}.tip-card ol{margin:12px 0 14px;padding-left:19px;font-size:.8rem;line-height:1.9;color:#c2d4ea}.tip-card p{font-size:.75rem;margin:0}.tip-card strong{color:#b7dcff}
    .alerts-card{margin-bottom:32px}.alerts-list{display:grid;gap:10px}.alert-item{display:flex;align-items:start;gap:13px;padding:15px;border-radius:13px;background:#281f31;border:1px solid #674053}.alert-symbol{display:grid;place-items:center;flex:none;width:29px;height:29px;border-radius:9px;background:#7c4352;color:#ffe0e4;font-weight:900}.alert-item>div{flex:1;min-width:0}.alert-item strong{font-size:.83rem}.alert-item p{margin:5px 0 0;color:#b9a9b7;font-size:.75rem;overflow-wrap:anywhere}.alert-item b{color:#f4d9df;font-weight:700}.alert-item time{font-size:.7rem;color:#c2aab7;white-space:nowrap}.empty-state{text-align:center;padding:35px 15px;color:#92a5bf}.empty-state.compact{padding:23px 14px}.empty-state h3{color:#e4eefc;margin:10px 0 5px}.empty-state p{font-size:.78rem;margin-bottom:0}.empty-icon{display:grid;place-items:center;width:42px;height:42px;border-radius:13px;background:#274569;color:#b7d9ff;font-size:1.4rem;margin:auto}.search-empty{color:#9bacc3;text-align:center;font-size:.84rem;padding:20px}.footer{padding:18px 0 30px;color:#687c98;font-size:.7rem}.footer span{margin:0 6px}
    .login-layout{min-height:100vh;display:grid;grid-template-columns:minmax(0,1fr) minmax(340px,410px);align-items:center;gap:8vw;max-width:1120px;margin:auto;padding:40px}.login-intro h1{margin-top:30px}.login-intro p{max-width:400px;color:#9cb1cc}.login-card{padding:35px}.login-card h2{font-size:1.7rem}.login-card .muted{font-size:.84rem;margin-bottom:20px}.login-card .stack-form label{margin-top:0}.form-note{text-align:center;color:#778aa4;font-size:.7rem;margin:20px 0 0}.message-page{max-width:480px;margin:15vh auto;padding:35px}.message-page h1{font-size:2rem;margin:12px 0}.message-page .button{margin-top:12px}
    @media(max-width:1100px){.content-grid{grid-template-columns:minmax(0,1fr) 310px}.stats{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:760px){.dashboard-shell{padding:0 18px}.topbar{height:75px}.local-pill{font-size:0;padding:10px}.local-pill i{width:9px;height:9px}.hero{padding:38px 0 24px;display:block}.hero-action{margin-top:20px}.content-grid{grid-template-columns:1fr}.side-column{grid-row:1}.stats{gap:10px}.stat{padding:13px;min-height:82px}.stat-icon{width:38px;height:38px;font-size:1.1rem}.stat strong{font-size:1.45rem}.stat span:last-child{font-size:.65rem}.section-card{padding:19px}.login-layout{grid-template-columns:1fr;gap:25px}.login-intro h1{font-size:2.6rem}.login-intro p{font-size:.87rem}}@media(max-width:480px){.status{font-size:.64rem;max-width:110px;white-space:normal}.user-main{flex-wrap:wrap}.user-identity{flex:1 1 calc(100% - 60px)}.user-meta{display:grid;gap:5px}.password-form>div{display:grid}.section-heading{display:block}.text-link{display:inline-block;padding-top:9px}.alert-item{flex-wrap:wrap}.alert-item time{margin-left:42px}.login-layout{padding:22px}.login-card{padding:25px}}
    @media(prefers-reduced-motion:reduce){*,*::before,*::after{animation-duration:.01ms!important;transition-duration:.01ms!important;scroll-behavior:auto!important}}
    </style></head><body>__BODY__</body></html>
    """.Replace("__TITLE__", H(title)).Replace("__BODY__", body);

static async Task SendTelegramAlertAsync(string username, string existingMachine, string attemptedMachine)
{
    TryShowLocalAlert(username, existingMachine, attemptedMachine);
    var token = Environment.GetEnvironmentVariable("PEXBOT_TELEGRAM_BOT_TOKEN");
    var chat = Environment.GetEnvironmentVariable("PEXBOT_TELEGRAM_CHAT_ID");
    if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chat)) return;
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chat,
            ["text"] = $"PEXBOT: tentativa de acesso em outro computador. Usuário: {username}. PC autorizado: {existingMachine}. PC da tentativa: {attemptedMachine}."
        });
        await client.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", body);
    }
    catch { /* O alerta permanece registrado no banco e no painel. */ }
}

static void TryShowLocalAlert(string username, string existingMachine, string attemptedMachine)
{
    if (!OperatingSystem.IsWindows()) return;
    try
    {
        var start = new ProcessStartInfo
        {
            FileName = "msg.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Environment.UserName);
        start.ArgumentList.Add($"PEXBOT: tentativa de compartilhar login. Usuário: {username}. PC autorizado: {existingMachine}. Novo PC: {attemptedMachine}.");
        Process.Start(start)?.Dispose();
    }
    catch { /* O alerta continua registrado no painel mesmo sem aviso local. */ }
}

static string NicknameSlug(string nickname)
{
    var normalized = nickname.Normalize(NormalizationForm.FormD);
    var slug = new StringBuilder();
    foreach (var character in normalized)
    {
        if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            continue;
        if (char.IsAsciiLetterOrDigit(character))
            slug.Append(char.ToLowerInvariant(character));
    }
    return slug.Length > 24 ? slug.ToString(0, 24) : slug.ToString();
}

internal sealed record ActivationRequest(string Username, string Password, string DeviceId, string Fingerprint, string MachineName);
internal sealed record OfflineLicense(int Version, string Username, string DeviceId, string Fingerprint, DateTimeOffset IssuedAtUtc);
internal sealed record ActivationResponse(string Payload, string Signature, string PublicKey);
