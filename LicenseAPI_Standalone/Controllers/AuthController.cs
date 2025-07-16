using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace WinFormsApp1.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        // Credenciais do administrador (você pode alterar essas)
        private const string ADMIN_USERNAME = "DexzD7";
        private const string ADMIN_PASSWORD = "admin123";
        private const string USERS_FILE = "users.json";
        private static readonly object _userLock = new object();

        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { message = "Usuário e senha são obrigatórios" });

            var users = LoadUsers();
            if (users.Any(u => u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)) ||
                request.Username.Equals(ADMIN_USERNAME, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Usuário já existe" });
            }

            var user = new User
            {
                Username = request.Username,
                PasswordHash = HashPassword(request.Password)
            };
            users.Add(user);
            SaveUsers(users);
            return Ok(new { message = "Usuário cadastrado com sucesso" });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var webhookService = HttpContext.RequestServices.GetService<WinFormsApp1.API.Services.WebhookService>();

                if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
                {
                    if (webhookService != null)
                    {
                        await webhookService.SendAccessLogAsync(HttpContext, "LOGIN_FAILED", new { reason = "Campos vazios" });
                    }
                    return BadRequest(new { message = "Usuário e senha são obrigatórios" });
                }

                // Login admin fixo
                if (request.Username == ADMIN_USERNAME && request.Password == ADMIN_PASSWORD)
                {
                    HttpContext.Session.SetString("IsAuthenticated", "true");
                    HttpContext.Session.SetString("Username", request.Username);
                    if (webhookService != null)
                    {
                        await webhookService.SendAccessLogAsync(HttpContext, "LOGIN_SUCCESS", new { username = request.Username });
                    }
                    return Ok(new { message = "Login realizado com sucesso" });
                }

                // Login usuário do arquivo
                var users = LoadUsers();
                var user = users.FirstOrDefault(u => u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase));
                if (user != null && user.PasswordHash == HashPassword(request.Password))
                {
                    // --- NOVO: Checar licença ---
                    var licenses = LoadLicenses();
                    var license = licenses.FirstOrDefault(l => l.User != null && l.User.Equals(request.Username, StringComparison.OrdinalIgnoreCase));
                    if (license == null)
                    {
                        return BadRequest(new { message = "Usuário não possui licença válida." });
                    }
                    if (license.Blocked || license.IsBanned)
                    {
                        return BadRequest(new { message = "Licença bloqueada ou banida." });
                    }
                    if (license.Expiration != null && license.Expiration < DateTime.UtcNow)
                    {
                        return BadRequest(new { message = "Licença expirada." });
                    }
                    if (!string.IsNullOrEmpty(license.HWID) && !string.IsNullOrEmpty(request.HWID) && license.HWID != request.HWID)
                    {
                        return BadRequest(new { message = "HWID não corresponde à licença." });
                    }
                    // --- FIM NOVO ---
                    HttpContext.Session.SetString("IsAuthenticated", "true");
                    HttpContext.Session.SetString("Username", request.Username);
                    if (webhookService != null)
                    {
                        await webhookService.SendAccessLogAsync(HttpContext, "LOGIN_SUCCESS", new { username = request.Username });
                    }
                    return Ok(new { message = "Login realizado com sucesso" });
                }
                else
                {
                    if (webhookService != null)
                    {
                        await webhookService.SendSecurityAlertAsync(
                            "LOGIN_FAILED",
                            $"Tentativa de login falhada. Usuário: {request.Username}",
                            GetClientIP()
                        );
                    }
                    return BadRequest(new { message = "Usuário ou senha incorretos" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Erro interno do servidor" });
            }
        }

        private List<User> LoadUsers()
        {
            lock (_userLock)
            {
                if (!System.IO.File.Exists(USERS_FILE))
                    return new List<User>();
                var json = System.IO.File.ReadAllText(USERS_FILE);
                return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
            }
        }

        private void SaveUsers(List<User> users)
        {
            lock (_userLock)
            {
                var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(USERS_FILE, json);
            }
        }

        private string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        private string GetClientIP()
        {
            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault() ??
                    HttpContext.Request.Headers["X-Real-IP"].FirstOrDefault() ??
                    HttpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ??
                    HttpContext.Connection.RemoteIpAddress?.ToString() ??
                    "Unknown";

            return ip.Split(',')[0].Trim();
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            try
            {
                // Limpar sessão
                HttpContext.Session.Clear();
                
                return Ok(new { message = "Logout realizado com sucesso" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Erro interno do servidor" });
            }
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            try
            {
                var isAuthenticated = HttpContext.Session.GetString("IsAuthenticated") == "true";
                var username = HttpContext.Session.GetString("Username");
                
                return Ok(new { 
                    isAuthenticated = isAuthenticated,
                    username = username
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Erro interno do servidor" });
            }
        }

        [HttpDelete("users/{username}")]
        public IActionResult RemoveUser(string username)
        {
            if (username.Equals(ADMIN_USERNAME, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Não é permitido remover o admin." });
            var users = LoadUsers();
            var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user == null)
                return NotFound(new { message = "Usuário não encontrado" });
            users.Remove(user);
            SaveUsers(users);
            return Ok(new { message = "Usuário removido com sucesso" });
        }

        private List<License> LoadLicenses()
        {
            lock (_userLock)
            {
                if (!System.IO.File.Exists("licenses.json"))
                    return new List<License>();
                var json = System.IO.File.ReadAllText("licenses.json");
                return System.Text.Json.JsonSerializer.Deserialize<List<License>>(json) ?? new List<License>();
            }
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string? HWID { get; set; } // <-- NOVO
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
    public class User
    {
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }
} 