# 🔑 License API

API web para geração e validação de licenças com sistema de segurança completo.

## 📁 Estrutura

```
API/
├── Program.cs                 # Ponto de entrada da API
├── Controllers/              # Controllers da API
│   ├── AuthController.cs     # Autenticação
│   └── LicenseController.cs  # Gerenciamento de licenças
├── Services/                 # Serviços
│   └── WebhookService.cs     # Serviço de webhook
├── Config/                   # Configurações
│   ├── appsettings.json      # Configurações da API
│   ├── admin_credentials.txt # Credenciais do admin
│   └── webhook_config.txt    # Configuração do webhook
└── LicenseAPI.csproj         # Projeto da API
```

## 🚀 Como usar

### 1. Configurar Webhook
```bash
# Execute o script de configuração
setup_webhook.bat
```

### 2. Iniciar API
```bash
cd API
dotnet run
```

### 3. Acessar
- **URL**: http://localhost:5000
- **Login**: DexzD7 / admin123

## 🔐 Segurança

- ✅ **Autenticação obrigatória**
- ✅ **Sessão segura** (2 horas)
- ✅ **Webhook de monitoramento**
- ✅ **Logs completos** de acesso
- ✅ **Captura de IP real**

## 📊 Endpoints

### Autenticação
- `POST /api/auth/login` - Login
- `POST /api/auth/logout` - Logout
- `GET /api/auth/status` - Status da sessão

### Licenças
- `POST /api/license/generate` - Gerar licença
- `GET /api/license/list` - Listar licenças
- `POST /api/license/validate` - Validar licença

## 🔧 Configuração

### Credenciais do Admin
Edite `API/Controllers/AuthController.cs`:
```csharp
private const string ADMIN_USERNAME = "seuusuario";
private const string ADMIN_PASSWORD = "suasenha123";
```

### Webhook
Configure em `API/Config/appsettings.json`:
```json
{
  "Webhook": {
    "Url": "https://discord.com/api/webhooks/SUA_URL",
    "Enabled": true
  }
}
```

## 📦 Para mover para projeto separado

1. **Copie a pasta `API/`** para o novo projeto
2. **Ajuste os namespaces** se necessário
3. **Configure as dependências** no novo projeto
4. **Atualize as URLs** no app WinForms

## 🛡️ Monitoramento

A API envia alertas para:
- ✅ Logins (sucesso/falha)
- ✅ Geração de licenças
- ✅ Validação de licenças
- ✅ Tentativas de acesso não autorizado
- ✅ IP, User-Agent, Headers completos 