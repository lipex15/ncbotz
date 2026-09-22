using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
var adminHost = Environment.GetEnvironmentVariable("PEXBOT_ADMIN_HOST");
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin") &&
        !string.IsNullOrWhiteSpace(adminHost) &&
        !string.Equals(context.Request.Host.Host, adminHost, StringComparison.OrdinalIgnoreCase))
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
        <h1>Painel PEXBOT</h1>
        <form method="post" action="/admin/login">
          {TokenField(context, antiforgery)}
          <label>Senha de administrador <input type="password" name="password" autocomplete="current-password" required></label>
          <button>Entrar</button>
        </form>
        """), "text/html; charset=utf-8"));

app.MapPost("/admin/login", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    var form = await context.Request.ReadFormAsync();
    if (!LicenseStore.VerifyPassword(form["password"].ToString(), adminHash!))
        return Results.Content(Page("Acesso negado", "<p>Senha incorreta.</p><a href='/admin/login'>Tentar novamente</a>"), "text/html; charset=utf-8", statusCode: 401);
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
    var html = new StringBuilder("<h1>Painel PEXBOT</h1><h2>Criar acesso</h2>");
    html.Append($"<form method='post' action='/admin/users'>{token}<label>Usuário <input name='username' required maxlength='80'></label><label>Nome <input name='displayName' required maxlength='100'></label><label>Senha inicial <input name='password' type='password' required minlength='12'></label><button>Criar</button></form>");
    html.Append("<h2>Usuários</h2><table><tr><th>Login</th><th>Nome</th><th>Máquina</th><th>Estado</th><th>Ações</th></tr>");
    foreach (var user in users)
    {
        var name = H(user.Username);
        html.Append($"<tr><td>{name}</td><td>{H(user.DisplayName)}</td><td>{H(user.MachineName ?? "Ainda não ativado")}</td><td>{(user.Disabled ? "Bloqueado" : "Ativo")}</td><td>");
        html.Append($"<form method='post' action='/admin/users/{user.Id}/device/reset'>{token}<button>Liberação de PC</button></form>");
        html.Append($"<form method='post' action='/admin/users/{user.Id}/state'>{token}<input type='hidden' name='disabled' value='{(!user.Disabled).ToString().ToLowerInvariant()}'><button>{(user.Disabled ? "Reativar" : "Bloquear")}</button></form></td></tr>");
        html.Append($"<tr><td colspan='5'><form method='post' action='/admin/users/{user.Id}/password'>{token}<label>Nova senha para {name} <input type='password' name='password' minlength='12' required></label><button>Trocar senha</button></form></td></tr>");
    }
    html.Append("</table><h2>Tentativas de outro computador</h2><table><tr><th>Quando (UTC)</th><th>Usuário</th><th>PC autorizado</th><th>PC da tentativa</th></tr>");
    foreach (var alert in alerts)
        html.Append($"<tr><td>{H(alert.OccurredUtc)}</td><td>{H(alert.Username)}</td><td>{H(alert.ExistingMachine)}</td><td>{H(alert.AttemptedMachine)}</td></tr>");
    html.Append("</table>");
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

static string Page(string title, string body) => $$"""
    <!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>{{H(title)}} · PEXBOT</title>
    <style>body{font:16px system-ui;background:#0b1523;color:#eef4ff;max-width:1100px;margin:40px auto;padding:0 20px}h1{color:#80baff}h2{margin-top:36px}form{display:inline-block;margin:6px;padding:10px;background:#142237;border-radius:9px}label{display:inline-grid;gap:5px;margin-right:10px}input,button{font:inherit;padding:8px;border-radius:6px}button{background:#2677da;color:white;border:0;cursor:pointer}table{border-collapse:collapse;width:100%}td,th{padding:10px;border-bottom:1px solid #29415f;text-align:left}td form{padding:0;background:none}</style>
    <body>{{body}}</body></html>
    """;

static async Task SendTelegramAlertAsync(string username, string existingMachine, string attemptedMachine)
{
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

internal sealed record ActivationRequest(string Username, string Password, string DeviceId, string Fingerprint, string MachineName);
internal sealed record OfflineLicense(int Version, string Username, string DeviceId, string Fingerprint, DateTimeOffset IssuedAtUtc);
internal sealed record ActivationResponse(string Payload, string Signature, string PublicKey);
