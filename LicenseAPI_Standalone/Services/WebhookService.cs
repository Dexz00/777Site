using System.Text;
using System.Text.Json;

namespace WinFormsApp1.API.Services
{
    public class WebhookService
    {
        private readonly string _webhookUrl;
        private readonly HttpClient _httpClient;

        public WebhookService(string webhookUrl)
        {
            _webhookUrl = webhookUrl;
            _httpClient = new HttpClient();
        }

        public async Task SendAccessLogAsync(HttpContext context, string action, object? data = null)
        {
            try
            {
                var accessInfo = new
                {
                    timestamp = DateTime.UtcNow,
                    ip = GetClientIP(context),
                    userAgent = context.Request.Headers["User-Agent"].ToString(),
                    method = context.Request.Method,
                    path = context.Request.Path,
                    query = context.Request.QueryString.ToString(),
                    action = action,
                    data = data,
                    headers = GetRelevantHeaders(context),
                    session = context.Session.GetString("Username") ?? "Anonymous"
                };

                var payload = new
                {
                    content = $"🚨 **API Access Alert**\n" +
                             $"**IP:** {accessInfo.ip}\n" +
                             $"**Action:** {accessInfo.action}\n" +
                             $"**Path:** {accessInfo.path}\n" +
                             $"**Method:** {accessInfo.method}\n" +
                             $"**User:** {accessInfo.session}\n" +
                             $"**Time:** {accessInfo.timestamp:yyyy-MM-dd HH:mm:ss UTC}\n" +
                             $"**User-Agent:** {accessInfo.userAgent}\n" +
                             $"**Query:** {accessInfo.query}",
                    embeds = new object[]
                    {
                        new
                        {
                            title = "🔍 API Access Details",
                            color = GetColorForAction(accessInfo.action),
                            fields = new object[]
                            {
                                new { name = "IP Address", value = accessInfo.ip, inline = true },
                                new { name = "Action", value = accessInfo.action, inline = true },
                                new { name = "User", value = accessInfo.session, inline = true },
                                new { name = "Path", value = accessInfo.path, inline = true },
                                new { name = "Method", value = accessInfo.method, inline = true },
                                new { name = "Timestamp", value = accessInfo.timestamp.ToString("yyyy-MM-dd HH:mm:ss UTC"), inline = true },
                                new { name = "User-Agent", value = accessInfo.userAgent.Length > 100 ? accessInfo.userAgent.Substring(0, 100) + "..." : accessInfo.userAgent, inline = false },
                                new { name = "Query Parameters", value = string.IsNullOrEmpty(accessInfo.query) ? "None" : accessInfo.query, inline = false }
                            },
                            timestamp = accessInfo.timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                await _httpClient.PostAsync(_webhookUrl, content);
            }
            catch (Exception ex)
            {
                // Log error silently to avoid breaking the API
                Console.WriteLine($"Webhook error: {ex.Message}");
            }
        }

        public async Task SendSecurityAlertAsync(string alertType, string details, string ip = "")
        {
            try
            {
                var payload = new
                {
                    content = $"🚨 **SECURITY ALERT**\n" +
                             $"**Type:** {alertType}\n" +
                             $"**Details:** {details}\n" +
                             $"**IP:** {ip}\n" +
                             $"**Time:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}",
                    embeds = new object[]
                    {
                        new
                        {
                            title = "🛡️ Security Alert",
                            color = 0xFF0000, // Red
                            description = details,
                            fields = new object[]
                            {
                                new { name = "Alert Type", value = alertType, inline = true },
                                new { name = "IP Address", value = ip, inline = true },
                                new { name = "Timestamp", value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"), inline = true }
                            },
                            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                await _httpClient.PostAsync(_webhookUrl, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Security webhook error: {ex.Message}");
            }
        }

        private string GetClientIP(HttpContext context)
        {
            // Try to get real IP from various headers
            var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ??
                    context.Request.Headers["X-Real-IP"].FirstOrDefault() ??
                    context.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ??
                    context.Connection.RemoteIpAddress?.ToString() ??
                    "Unknown";

            return ip.Split(',')[0].Trim(); // Get first IP if multiple
        }

        private Dictionary<string, string> GetRelevantHeaders(HttpContext context)
        {
            var relevantHeaders = new[] { "Referer", "Origin", "Host", "Accept", "Accept-Language" };
            var headers = new Dictionary<string, string>();

            foreach (var header in relevantHeaders)
            {
                if (context.Request.Headers.ContainsKey(header))
                {
                    headers[header] = context.Request.Headers[header].ToString();
                }
            }

            return headers;
        }

        private int GetColorForAction(string action)
        {
            return action switch
            {
                "LOGIN_SUCCESS" => 0x00FF00, // Green
                "LOGIN_FAILED" => 0xFF0000,  // Red
                "LICENSE_GENERATED" => 0x0099FF, // Blue
                "LICENSE_VALIDATED" => 0x00FF00, // Green
                "LICENSE_INVALID" => 0xFF6600,   // Orange
                "UNAUTHORIZED_ACCESS" => 0xFF0000, // Red
                _ => 0x808080 // Gray
            };
        }
    }
} 