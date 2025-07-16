using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace WinFormsApp1.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LicenseController : ControllerBase
    {
        private const string LICENSES_FILE = "licenses.json";
        private static readonly object _lock = new object();

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateLicense([FromBody] GenerateLicenseRequest request)
        {
            try
            {
                var webhookService = HttpContext.RequestServices.GetService<WinFormsApp1.API.Services.WebhookService>();

                // Não exigir mais o campo Type
                // if (string.IsNullOrEmpty(request.Type))
                // {
                //     if (webhookService != null)
                //     {
                //         await webhookService.SendAccessLogAsync(HttpContext, "LICENSE_GENERATION_FAILED", new { reason = "Tipo vazio" });
                //     }
                //     return BadRequest(new { message = "Tipo de licença é obrigatório" });
                // }

                // Gerar chave sem tipo
                var licenseKey = GenerateUniqueLicenseKey();
                var license = new License
                {
                    Key = licenseKey,
                    Type = "", // Não usar mais tipo
                    CreatedAt = DateTime.UtcNow,
                    Used = false,
                    Expiration = request.Expiration ?? DateTime.UtcNow.AddDays(30), // padrão: 30 dias
                    HWID = request.HWID,
                    Blocked = false,
                    IsBanned = false,
                    User = request.User,
                    History = new List<LicenseEvent> {
                        new LicenseEvent {
                            Timestamp = DateTime.UtcNow,
                            EventType = "CREATED",
                            Details = $"Licença criada (expira: {(request.Expiration ?? DateTime.UtcNow.AddDays(30)).ToString("yyyy-MM-dd")})",
                            PerformedBy = HttpContext.Session.GetString("Username") ?? "API"
                        }
                    }
                };

                SaveLicense(license);

                // Log de geração de licença
                if (webhookService != null)
                {
                    await webhookService.SendAccessLogAsync(HttpContext, "LICENSE_GENERATED", new {
                        licenseKey = licenseKey,
                        generatedBy = HttpContext.Session.GetString("Username") ?? "Unknown"
                    });
                }

                return Ok(new { licenseKey = licenseKey, message = "Licença gerada com sucesso", expiration = license.Expiration });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Erro interno do servidor" });
            }
        }

        [HttpGet("list")]
        public IActionResult ListLicenses()
        {
            try
            {
                var licenses = LoadLicenses();
                return Ok(licenses);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Erro ao carregar licenças" });
            }
        }

        [HttpPost("validate")]
        public async Task<IActionResult> ValidateLicense([FromBody] ValidateLicenseRequest request)
        {
            try
            {
                // Validação de limites de usuário e senha
                if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3 || request.Username.Length > 20)
                {
                    return BadRequest(new { valid = false, message = "O nome de usuário deve ter entre 3 e 20 caracteres." });
                }
                if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6 || request.Password.Length > 32)
                {
                    return BadRequest(new { valid = false, message = "A senha deve ter entre 6 e 32 caracteres." });
                }
                var webhookService = HttpContext.RequestServices.GetService<WinFormsApp1.API.Services.WebhookService>();
                var licenses = LoadLicenses();
                var license = licenses.FirstOrDefault(l => l.Key.Equals(request.LicenseKey, StringComparison.OrdinalIgnoreCase));

                if (license == null)
                {
                    // Log de licença inválida
                    if (webhookService != null)
                    {
                        await webhookService.SendSecurityAlertAsync(
                            "LICENSE_INVALID",
                            $"Tentativa de usar licença inexistente: {request.LicenseKey}",
                            GetClientIP()
                        );
                    }
                    return BadRequest(new { valid = false, message = "Licença não encontrada" });
                }

                // Checar bloqueio/banimento
                if (license.Blocked || license.IsBanned)
                {
                    license.History?.Add(new LicenseEvent {
                        Timestamp = DateTime.UtcNow,
                        EventType = "BLOCKED_ATTEMPT",
                        Details = $"Tentativa de uso de licença bloqueada/banida por {request.Username}",
                        PerformedBy = request.Username
                    });
                    SaveLicenses(licenses);
                    return BadRequest(new { valid = false, message = "Licença bloqueada ou banida" });
                }

                // Checar expiração
                if (license.Expiration != null && license.Expiration < DateTime.UtcNow)
                {
                    license.History?.Add(new LicenseEvent {
                        Timestamp = DateTime.UtcNow,
                        EventType = "EXPIRED_ATTEMPT",
                        Details = $"Tentativa de uso de licença expirada por {request.Username}",
                        PerformedBy = request.Username
                    });
                    SaveLicenses(licenses);
                    return BadRequest(new { valid = false, message = "Licença expirada" });
                }

                // Checar HWID se fornecido
                if (!string.IsNullOrEmpty(license.HWID) && !string.IsNullOrEmpty(request.HWID) && license.HWID != request.HWID)
                {
                    license.History?.Add(new LicenseEvent {
                        Timestamp = DateTime.UtcNow,
                        EventType = "HWID_MISMATCH",
                        Details = $"HWID não bate: esperado {license.HWID}, recebido {request.HWID}",
                        PerformedBy = request.Username
                    });
                    SaveLicenses(licenses);
                    return BadRequest(new { valid = false, message = "HWID não corresponde à licença" });
                }

                if (license.Used)
                {
                    // Log de licença já usada
                    if (webhookService != null)
                    {
                        await webhookService.SendSecurityAlertAsync(
                            "LICENSE_ALREADY_USED",
                            $"Tentativa de reutilizar licença: {request.LicenseKey} (usada por: {license.UsedBy})",
                            GetClientIP()
                        );
                    }
                    license.History?.Add(new LicenseEvent {
                        Timestamp = DateTime.UtcNow,
                        EventType = "REUSED_ATTEMPT",
                        Details = $"Tentativa de reutilizar licença por {request.Username}",
                        PerformedBy = request.Username
                    });
                    SaveLicenses(licenses);
                    return BadRequest(new { valid = false, message = "Licença já foi utilizada" });
                }

                // --- REGISTRO DE USUÁRIO ANTES DE MARCAR LICENÇA COMO USADA ---
                var userRegisterResult = RegisterUser(request.Username, request.Password);
                if (!userRegisterResult.success)
                {
                    return BadRequest(new { valid = false, message = userRegisterResult.message });
                }

                // Marcar como usada
                license.Used = true;
                license.UsedAt = DateTime.UtcNow;
                license.UsedBy = request.Username;
                license.User = request.Username;
                if (!string.IsNullOrEmpty(request.HWID))
                    license.HWID = request.HWID;
                license.History?.Add(new LicenseEvent {
                    Timestamp = DateTime.UtcNow,
                    EventType = "VALIDATED",
                    Details = $"Licença validada e usada por {request.Username} (HWID: {request.HWID})",
                    PerformedBy = request.Username
                });
                SaveLicenses(licenses);

                // Log de validação bem-sucedida
                if (webhookService != null)
                {
                    await webhookService.SendAccessLogAsync(HttpContext, "LICENSE_VALIDATED", new {
                        licenseKey = request.LicenseKey,
                        username = request.Username,
                        licenseType = license.Type
                    });
                }

                return Ok(new { valid = true, message = "Licença válida", license = license });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Erro ao validar licença" });
            }
        }

        // --- Método privado para registrar usuário (igual ao AuthController) ---
        private (bool success, string message) RegisterUser(string username, string password)
        {
            const string USERS_FILE = "users.json";
            const string ADMIN_USERNAME = "DexzD7";
            object _userLock = new object();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, "Usuário e senha são obrigatórios");
            List<User> users;
            lock (_userLock)
            {
                if (!System.IO.File.Exists(USERS_FILE))
                    users = new List<User>();
                else
                {
                    var json = System.IO.File.ReadAllText(USERS_FILE);
                    users = JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
                }
                if (users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)) ||
                    username.Equals(ADMIN_USERNAME, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Usuário já existe");
                }
                var user = new User
                {
                    Username = username,
                    PasswordHash = HashPassword(password)
                };
                users.Add(user);
                var jsonOut = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(USERS_FILE, jsonOut);
            }
            return (true, "Usuário cadastrado com sucesso");
        }
        private string HashPassword(string password)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
        public class User
        {
            public string Username { get; set; } = "";
            public string PasswordHash { get; set; } = "";
        }

        [HttpDelete("remove/{key}")]
        public IActionResult RemoveLicense(string key)
        {
            var licenses = LoadLicenses();
            var license = licenses.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (license == null)
                return NotFound(new { message = "Licença não encontrada" });
            licenses.Remove(license);
            SaveLicenses(licenses);
            return Ok(new { message = "Licença removida com sucesso" });
        }

        [HttpPost("renew/{key}")]
        public IActionResult RenewLicense(string key, [FromBody] RenewLicenseRequest request)
        {
            var licenses = LoadLicenses();
            var license = licenses.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (license == null)
                return NotFound(new { message = "Licença não encontrada" });
            license.Expiration = request.NewExpiration;
            license.History?.Add(new LicenseEvent {
                Timestamp = DateTime.UtcNow,
                EventType = "RENEWED",
                Details = $"Validade renovada para {request.NewExpiration:yyyy-MM-dd}",
                PerformedBy = request.PerformedBy ?? "API"
            });
            SaveLicenses(licenses);
            return Ok(new { message = "Validade renovada", expiration = license.Expiration });
        }

        [HttpPost("block/{key}")]
        public IActionResult BlockLicense(string key)
        {
            var licenses = LoadLicenses();
            var license = licenses.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (license == null)
                return NotFound(new { message = "Licença não encontrada" });
            license.Blocked = true;
            license.IsBanned = true;
            license.History?.Add(new LicenseEvent {
                Timestamp = DateTime.UtcNow,
                EventType = "BLOCKED",
                Details = "Licença bloqueada/banida",
                PerformedBy = "API"
            });
            SaveLicenses(licenses);
            return Ok(new { message = "Licença bloqueada/banida" });
        }

        [HttpPost("unblock/{key}")]
        public IActionResult UnblockLicense(string key)
        {
            var licenses = LoadLicenses();
            var license = licenses.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (license == null)
                return NotFound(new { message = "Licença não encontrada" });
            license.Blocked = false;
            license.IsBanned = false;
            license.History?.Add(new LicenseEvent {
                Timestamp = DateTime.UtcNow,
                EventType = "UNBLOCKED",
                Details = "Licença desbloqueada/desbanida",
                PerformedBy = "API"
            });
            SaveLicenses(licenses);
            return Ok(new { message = "Licença desbloqueada/desbanida" });
        }

        [HttpPost("reset-hwid/{key}")]
        public IActionResult ResetHWID(string key)
        {
            var licenses = LoadLicenses();
            var license = licenses.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (license == null)
                return NotFound(new { message = "Licença não encontrada" });
            license.HWID = null;
            license.History?.Add(new LicenseEvent {
                Timestamp = DateTime.UtcNow,
                EventType = "HWID_RESET",
                Details = "HWID resetado",
                PerformedBy = "API"
            });
            SaveLicenses(licenses);
            return Ok(new { message = "HWID resetado" });
        }

        [HttpGet("details/{key}")]
        public IActionResult LicenseDetails(string key)
        {
            var licenses = LoadLicenses();
            var license = licenses.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (license == null)
                return NotFound(new { message = "Licença não encontrada" });
            return Ok(license);
        }

        [HttpGet("list-advanced")]
        public IActionResult ListLicensesAdvanced([FromQuery] string? filter = null)
        {
            var licenses = LoadLicenses();
            IEnumerable<License> result = licenses;
            if (!string.IsNullOrEmpty(filter))
            {
                switch (filter.ToLower())
                {
                    case "valid":
                        result = licenses.Where(l => !l.Used && !l.Blocked && (l.Expiration == null || l.Expiration > DateTime.UtcNow));
                        break;
                    case "expired":
                        result = licenses.Where(l => l.Expiration != null && l.Expiration < DateTime.UtcNow);
                        break;
                    case "used":
                        result = licenses.Where(l => l.Used);
                        break;
                    case "blocked":
                        result = licenses.Where(l => l.Blocked || l.IsBanned);
                        break;
                }
            }
            return Ok(result);
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

        // Gerar chave sem tipo
        private string GenerateUniqueLicenseKey()
        {
            var random = new Random();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var randomPart = random.Next(1000, 9999);
            return $"LIC-{timestamp}-{randomPart}";
        }

        private List<License> LoadLicenses()
        {
            lock (_lock)
            {
                if (!System.IO.File.Exists(LICENSES_FILE))
                {
                    return new List<License>();
                }

                var json = System.IO.File.ReadAllText(LICENSES_FILE);
                return JsonSerializer.Deserialize<List<License>>(json) ?? new List<License>();
            }
        }

        private void SaveLicense(License license)
        {
            var licenses = LoadLicenses();
            licenses.Add(license);
            SaveLicenses(licenses);
        }

        private void SaveLicenses(List<License> licenses)
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(licenses, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(LICENSES_FILE, json);
            }
        }
    }

    public class GenerateLicenseRequest
    {
        // public string Type { get; set; } = ""; // Remover campo Type
        public DateTime? Expiration { get; set; } // Data de expiração opcional
        public string? HWID { get; set; } // HWID opcional
        public string? User { get; set; } // Usuário opcional
    }

    public class ValidateLicenseRequest
    {
        public string LicenseKey { get; set; } = "";
        public string Username { get; set; } = "";
        public string? HWID { get; set; } // HWID opcional
        public string Password { get; set; } = ""; // <-- NOVO
    }

    public class License
    {
        public string Key { get; set; } = "";
        public string Type { get; set; } = ""; // Pode manter, mas não será mais usado
        public DateTime CreatedAt { get; set; }
        public bool Used { get; set; }
        public DateTime? UsedAt { get; set; }
        public string? UsedBy { get; set; }
        public DateTime? Expiration { get; set; } // Data de expiração
        public string? HWID { get; set; } // HWID associado
        public bool Blocked { get; set; } = false; // Bloqueada/banida
        public bool IsBanned { get; set; } = false; // Banida (sinônimo)
        public string? User { get; set; } // Usuário associado
        public List<LicenseEvent>? History { get; set; } = new List<LicenseEvent>(); // Histórico
    }

    public class LicenseEvent
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = ""; // ex: VALIDATED, USED, BANNED, UNBANNED, HWID_RESET
        public string? Details { get; set; }
        public string? PerformedBy { get; set; } // usuário/admin
    }

    public class RenewLicenseRequest
    {
        public DateTime NewExpiration { get; set; }
        public string? PerformedBy { get; set; }
    }
} 