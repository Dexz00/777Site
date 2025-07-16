using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Configuração do webhook (você pode alterar essa URL)
var webhookUrl = Environment.GetEnvironmentVariable("WEBHOOK_URL") ?? 
                 "https://discord.com/api/webhooks/YOUR_WEBHOOK_URL_HERE";

// Adicionar serviços
builder.Services.AddControllers();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Adicionar serviço de webhook
builder.Services.AddSingleton(new WinFormsApp1.API.Services.WebhookService(webhookUrl));

var app = builder.Build();

// Configurar pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseCors("AllowAll");
app.UseSession();
app.UseRouting();
app.MapControllers();

// Middleware para verificar autenticação e logging
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    var webhookService = context.RequestServices.GetService<WinFormsApp1.API.Services.WebhookService>();
    
    // Rotas que não precisam de autenticação
    if (path == "/login" || path == "/api/auth/login" || path.StartsWith("/api/license/validate"))
    {
        await next();
        return;
    }

    // Verificar se está autenticado
    var isAuthenticated = context.Session.GetString("IsAuthenticated") == "true";
    
    if (!isAuthenticated)
    {
        // Log de acesso não autorizado
        if (webhookService != null)
        {
            await webhookService.SendSecurityAlertAsync(
                "UNAUTHORIZED_ACCESS", 
                $"Tentativa de acesso não autorizado ao caminho: {path}",
                GetClientIP(context)
            );
        }

        if (path.StartsWith("/api/"))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { message = "Não autorizado" });
            return;
        }
        else
        {
            context.Response.Redirect("/login");
            return;
        }
    }

    await next();
});

// Função auxiliar para obter IP do cliente
string GetClientIP(HttpContext context)
{
    var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ??
            context.Request.Headers["X-Real-IP"].FirstOrDefault() ??
            context.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "Unknown";

    return ip.Split(',')[0].Trim();
}

// Rota para página de login
app.MapGet("/login", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Login - Gerador de Licenças</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 0; padding: 0; background: #1a1a1a; color: white; height: 100vh; display: flex; align-items: center; justify-content: center; }
        .login-container { background: #2d2d2d; padding: 40px; border-radius: 15px; width: 350px; box-shadow: 0 10px 30px rgba(0,0,0,0.5); }
        h1 { text-align: center; margin-bottom: 30px; color: #007acc; }
        input { width: 100%; padding: 15px; margin: 10px 0; border-radius: 8px; border: none; background: #3d3d3d; color: white; box-sizing: border-box; }
        input:focus { outline: none; border: 2px solid #007acc; }
        button { width: 100%; padding: 15px; margin: 20px 0; border-radius: 8px; border: none; background: #007acc; color: white; cursor: pointer; font-size: 16px; font-weight: bold; }
        button:hover { background: #005a9e; }
        .error { color: #f44336; text-align: center; margin: 10px 0; }
        .success { color: #4caf50; text-align: center; margin: 10px 0; }
    </style>
</head>
<body>
    <div class='login-container'>
        <h1>🔐 Login</h1>
        <form id='loginForm'>
            <input type='text' id='username' placeholder='Usuário' required />
            <input type='password' id='password' placeholder='Senha' required />
            <button type='submit'>Entrar</button>
        </form>
        <div id='message'></div>
    </div>

    <script>
        document.getElementById('loginForm').addEventListener('submit', async (e) => {
            e.preventDefault();
            
            const username = document.getElementById('username').value;
            const password = document.getElementById('password').value;
            
            try {
                const response = await fetch('/api/auth/login', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ username, password })
                });
                
                const result = await response.json();
                
                if (response.ok) {
                    showMessage('Login realizado com sucesso! Redirecionando...', 'success');
                    setTimeout(() => window.location.href = '/', 1000);
                } else {
                    showMessage(result.message || 'Erro no login', 'error');
                }
            } catch (error) {
                showMessage('Erro de conexão', 'error');
            }
        });

        function showMessage(message, type) {
            const msgDiv = document.getElementById('message');
            msgDiv.innerHTML = '<div class=' + type + '>' + message + '</div>';
        }
    </script>
</body>
</html>
", "text/html"));

// Rota para página principal (protegida)
app.MapGet("/", () => Results.Content(@"<!DOCTYPE html>
<html lang='pt-BR'>
<head>
    <meta charset='UTF-8'>
    <title>Painel de Licenças</title>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        body { margin: 0; font-family: 'Segoe UI', Arial, sans-serif; background: #181c24; color: #fff; }
        .sidebar { position: fixed; left: 0; top: 0; width: 220px; height: 100vh; background: #23283a; display: flex; flex-direction: column; box-shadow: 2px 0 10px #0002; }
        .sidebar h2 { margin: 30px 0 20px 0; text-align: center; font-size: 1.5em; letter-spacing: 2px; color: #4f8cff; }
        .sidebar nav { display: flex; flex-direction: column; gap: 10px; margin-top: 30px; }
        .sidebar nav button { background: none; border: none; color: #fff; font-size: 1.1em; padding: 15px 30px; text-align: left; cursor: pointer; border-radius: 8px; transition: background 0.2s; }
        .sidebar nav button.active, .sidebar nav button:hover { background: #4f8cff22; color: #4f8cff; }
        .main { margin-left: 220px; padding: 40px 30px; min-height: 100vh; background: #181c24; }
        .section-title { font-size: 2em; margin-bottom: 25px; color: #4f8cff; }
        .actions { margin-bottom: 20px; }
        .actions button { background: #4f8cff; color: #fff; border: none; padding: 10px 22px; border-radius: 6px; font-size: 1em; margin-right: 10px; cursor: pointer; transition: background 0.2s; }
        .actions button.danger { background: #f44336; }
        .actions button:hover { filter: brightness(1.1); }
        table { width: 100%; border-collapse: collapse; background: #23283a; border-radius: 10px; overflow: hidden; }
        th, td { padding: 14px 10px; text-align: left; }
        th { background: #23283a; color: #4f8cff; font-weight: 600; }
        tr { border-bottom: 1px solid #222; }
        tr:last-child { border-bottom: none; }
        td .btn { padding: 6px 14px; border-radius: 5px; border: none; font-size: 0.95em; cursor: pointer; margin-right: 5px; }
        td .btn.danger { background: #f44336; color: #fff; }
        td .btn.warning { background: #ff9800; color: #fff; }
        td .btn.success { background: #4caf50; color: #fff; }
        td .btn.info { background: #4f8cff; color: #fff; }
        .status { font-weight: bold; }
        .status.valid { color: #4caf50; }
        .status.expired, .status.blocked { color: #f44336; }
        .status.used { color: #ff9800; }
        .toast { position: fixed; top: 30px; right: 30px; background: #23283a; color: #fff; padding: 18px 30px; border-radius: 8px; box-shadow: 0 2px 12px #0006; font-size: 1.1em; z-index: 9999; opacity: 0; pointer-events: none; transition: opacity 0.3s; }
        .toast.show { opacity: 1; pointer-events: auto; }
        @media (max-width: 700px) {
            .sidebar { width: 100vw; height: auto; flex-direction: row; position: static; box-shadow: none; }
            .sidebar nav { flex-direction: row; justify-content: center; }
            .main { margin-left: 0; padding: 20px 5vw; }
        }
    </style>
</head>
<body>
    <div class='sidebar'>
        <h2>777</h2>
        <nav>
            <button id='nav-licenses' class='active'>Licenças</button>
            <button id='nav-users'>Usuários</button>
        </nav>
    </div>
    <div class='main'>
        <div id='section-licenses'>
            <div class='section-title'>Licenças</div>
            <div class='actions'>
                <button onclick='showCreateLicense()'>Criar Licença</button>
            </div>
            <div id='licenses-table'></div>
        </div>
        <div id='section-users' style='display:none;'>
            <div class='section-title'>Usuários</div>
            <div id='users-table'></div>
        </div>
    </div>
    <div id='toast' class='toast'></div>
    <div id='modal' style='display:none; position:fixed; top:0; left:0; width:100vw; height:100vh; background:#000a; z-index:10000; align-items:center; justify-content:center;'>
        <div style='background:#23283a; padding:30px 40px; border-radius:12px; min-width:320px; max-width:90vw;'>
            <div id='modal-content'></div>
        </div>
    </div>
    <script>
        // Navegação
        document.getElementById('nav-licenses').onclick = function() {
            this.classList.add('active');
            document.getElementById('nav-users').classList.remove('active');
            document.getElementById('section-licenses').style.display = '';
            document.getElementById('section-users').style.display = 'none';
        };
        document.getElementById('nav-users').onclick = function() {
            this.classList.add('active');
            document.getElementById('nav-licenses').classList.remove('active');
            document.getElementById('section-licenses').style.display = 'none';
            document.getElementById('section-users').style.display = '';
            loadUsers();
        };
        // Toast
        function showToast(msg, type='info') {
            var toast = document.getElementById('toast');
            toast.textContent = msg;
            toast.className = 'toast show ' + type;
            setTimeout(() => { toast.className = 'toast'; }, 2500);
        }
        // Modal
        function showModal(html) {
            document.getElementById('modal-content').innerHTML = html;
            document.getElementById('modal').style.display = 'flex';
        }
        document.getElementById('modal').onclick = function(e) {
            if (e.target === this) this.style.display = 'none';
        };
        // Licenças
        async function loadLicenses() {
            var res = await fetch('/api/license/list-advanced');
            var licenses = await res.json();
            var html = '<table><tr><th>Chave</th><th>Usuário</th><th>Expiração</th><th>Status</th><th>Ações</th></tr>';
            for (var i = 0; i < licenses.length; i++) {
                var l = licenses[i];
                var status = l.blocked || l.isBanned ? '<span class=""status blocked"">Bloqueada</span>' : (l.used ? '<span class=""status used"">Usada</span>' : (l.expiration && new Date(l.expiration) < new Date() ? '<span class=""status expired"">Expirada</span>' : '<span class=""status valid"">Válida</span>'));
                html += '<tr><td><b>' + l.key + '</b></td><td>' + (l.user || '-') + '</td><td>' + (l.expiration ? new Date(l.expiration).toLocaleDateString() : 'Ilimitada') + '</td><td>' + status + '</td><td>' +
                    '<button class=""btn danger"" onclick=""removeLicense(\'' + l.key + '\')"">Remover</button>' +
                    '<button class=""btn warning"" onclick=""renewLicensePrompt(\'' + l.key + '\')"">Renovar</button>' +
                    '<button class=""btn warning"" onclick=""resetHWID(\'' + l.key + '\')"">Resetar HWID</button>' +
                    '<button class=""btn danger"" onclick=""blockLicense(\'' + l.key + '\')"">Bloquear</button>' +
                    '<button class=""btn success"" onclick=""unblockLicense(\'' + l.key + '\')"">Desbloquear</button>' +
                    '<button class=""btn info"" onclick=""showLicenseDetails(\'' + l.key + '\')"">Detalhes</button>' +
                    '</td></tr>';
            }
            html += '</table>';
            document.getElementById('licenses-table').innerHTML = html;
        }
        async function removeLicense(key) {
            if (!confirm('Remover licença?')) return;
            await fetch('/api/license/remove/' + key, { method: 'DELETE' });
            showToast('Licença removida', 'danger');
            loadLicenses();
        }
        async function renewLicensePrompt(key) {
            var newDate = prompt('Nova data de expiração (YYYY-MM-DD):');
            if (!newDate) return;
            await fetch('/api/license/renew/' + key, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ newExpiration: newDate + 'T23:59:59Z' })
            });
            showToast('Licença renovada', 'success');
            loadLicenses();
        }
        async function blockLicense(key) {
            await fetch('/api/license/block/' + key, { method: 'POST' });
            showToast('Licença bloqueada', 'danger');
            loadLicenses();
        }
        async function unblockLicense(key) {
            await fetch('/api/license/unblock/' + key, { method: 'POST' });
            showToast('Licença desbloqueada', 'success');
            loadLicenses();
        }
        async function resetHWID(key) {
            await fetch('/api/license/reset-hwid/' + key, { method: 'POST' });
            showToast('HWID resetado', 'success');
            loadLicenses();
        }
        async function showLicenseDetails(key) {
            var res = await fetch('/api/license/details/' + key);
            var l = await res.json();
            var html = '<div style=""font-size:1.1em;"">' +
                '<b>Chave:</b> ' + l.key + '<br>' +
                '<b>Usuário:</b> ' + (l.user || '-') + '<br>' +
                '<b>Expiração:</b> ' + (l.expiration ? new Date(l.expiration).toLocaleDateString() : 'Ilimitada') + '<br>' +
                '<b>Status:</b> ' + (l.blocked || l.isBanned ? 'Bloqueada' : (l.used ? 'Usada' : (l.expiration && new Date(l.expiration) < new Date() ? 'Expirada' : 'Válida'))) + '<br>' +
                '<b>HWID:</b> ' + (l.hwid || '-') + '<br>' +
                '<b>Criada em:</b> ' + new Date(l.createdAt).toLocaleString() + '<br>' +
                '<b>Usada em:</b> ' + (l.usedAt ? new Date(l.usedAt).toLocaleString() : '-') + '<br>' +
                '<b>Histórico:</b><div style=""background:#222;padding:10px;border-radius:6px;margin-top:5px;"">' +
                ((l.history && l.history.length) ? l.history.map(function(ev) {
                    return '<div style=""border-bottom:1px solid #333;padding:3px 0;""><b>' + ev.eventType + '</b> - ' + (ev.details || '') + ' <span style=""color:#aaa"">(' + new Date(ev.timestamp).toLocaleString() + ')</span></div>';
                }).join('') : '<i>Sem histórico</i>') +
                '</div>' +
                '<button class=""btn"" onclick=""closeModal()"" style=""margin-top:15px;"">Fechar</button>' +
            '</div>';
            showModal(html);
        }
        function closeModal() { document.getElementById('modal').style.display = 'none'; }
        function showCreateLicense() {
            var html = '<div style=""font-size:1.1em;"">' +
                '<h3>Criar Nova Licença</h3>' +
                '<input id=""newLicenseUser"" placeholder=""Usuário (opcional)"" style=""width:100%;padding:10px;margin-bottom:10px;border-radius:5px;border:none;background:#222;color:#fff;"">' +
                '<input id=""newLicenseExpiration"" type=""date"" style=""width:100%;padding:10px;margin-bottom:10px;border-radius:5px;border:none;background:#222;color:#fff;"">' +
                '<input id=""newLicenseHWID"" placeholder=""HWID (opcional)"" style=""width:100%;padding:10px;margin-bottom:10px;border-radius:5px;border:none;background:#222;color:#fff;"">' +
                '<button class=""btn info"" onclick=""createLicense()"">Criar</button>' +
                '<button class=""btn"" onclick=""closeModal()"">Cancelar</button>' +
            '</div>';
            showModal(html);
        }
        async function createLicense() {
            var user = document.getElementById('newLicenseUser').value;
            var expiration = document.getElementById('newLicenseExpiration').value;
            var hwid = document.getElementById('newLicenseHWID').value;
            var payload = {};
            if (user) payload.user = user;
            if (expiration) payload.expiration = expiration + 'T23:59:59Z';
            if (hwid) payload.hwid = hwid;
            var res = await fetch('/api/license/generate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            if (res.ok) {
                showToast('Licença criada', 'success');
                closeModal();
                loadLicenses();
            } else {
                var err = await res.json();
                showToast('Erro: ' + (err.message || 'Erro ao criar licença'), 'danger');
            }
        }
        // Usuários
        async function loadUsers() {
            var res = await fetch('/users.json?' + Date.now());
            var users = await res.json();
            var html = '<table><tr><th>Usuário</th><th>Ações</th></tr>';
            for (var i = 0; i < users.length; i++) {
                var u = users[i];
                html += '<tr><td><b>' + u.username + '</b></td><td><button class=""btn danger"" onclick=""removeUser(\'' + u.username + '\')"">Remover</button></td></tr>';
            }
            html += '</table>';
            document.getElementById('users-table').innerHTML = html;
        }
        async function removeUser(username) {
            if (!confirm('Remover usuário?')) return;
            var res = await fetch('/api/auth/users/' + username, { method: 'DELETE' });
            if (res.ok) {
                showToast('Usuário removido', 'danger');
                loadUsers();
            } else {
                var err = await res.json();
                showToast('Erro: ' + (err.message || 'Erro ao remover usuário'), 'danger');
            }
        }
        // Servir users.json para o painel
        window.addEventListener('DOMContentLoaded', () => {
            loadLicenses();
        });
    </script>
</body>
</html>", "text/html"));

app.MapGet("/users.json", () => {
    var path = Path.Combine(Directory.GetCurrentDirectory(), "users.json");
    if (!System.IO.File.Exists(path)) return Results.Json(new object[0]);
    var json = System.IO.File.ReadAllText(path);
    var users = System.Text.Json.JsonSerializer.Deserialize<List<dynamic>>(json);
    // Retornar apenas username
    var result = users?.Select(u => new { username = u.GetProperty("Username").GetString() }).ToArray() ?? Array.Empty<object>();
    return Results.Json(result);
});

app.UseHttpsRedirection();
app.Run(); 