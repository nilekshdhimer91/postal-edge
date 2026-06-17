# postal-edge

> Lightweight, self-hosted SMTP email API built on .NET 10 — DKIM signing, S/MIME, per-key rate limiting, multipart attachments, and zero server-side persistence.

---

## Features

- **5 dedicated endpoints** — plain text, text + HTML, HTML only, HTML with attachments, and a full generic endpoint
- **DKIM signing** — RSA-SHA256 with relaxed/relaxed canonicalization (RFC 6376)
- **S/MIME signing** — PKCS#7 detached signature via a server-mounted certificate
- **Custom SMTP client** — built on raw TCP/TLS; supports STARTTLS (port 587) and implicit SSL (port 465), no `System.Net.Mail.SmtpClient` quirks
- **Per-request SMTP override** — pass your own SMTP credentials in the request body, or rely on server-level defaults
- **API key authentication** — `X-Api-Key` header; each key has its own rate limit config
- **Per-key rate limiting** — powered by `Microsoft.AspNetCore.RateLimiting` with `PartitionedRateLimiter`
- **Multiple CC & BCC** — on all endpoints with RFC-compliant email validation
- **Attachments** — Base64 JSON body and `multipart/form-data` on the same endpoint
- **Template variables** — `{{FirstName}}` style substitution in subject and body
- **Scalar API docs** — available at `/scalar/v1` in Development only
- **Zero persistence** — no email data is stored anywhere on the server
- **Docker-ready** — multi-stage image, non-root user, Coolify-deployable
- **Reusable Core library** — `DNA.Email.Core` is independent and can be referenced in any .NET project

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| API | ASP.NET Core Web API (`[ApiController]`) |
| SMTP | Custom `RawSmtpClient` (TcpClient + SslStream) |
| MIME | Custom `MimeBuilder` (RFC 2822 / 2045) |
| DKIM | `System.Security.Cryptography` — RSA-SHA256 |
| S/MIME | `System.Security.Cryptography.Pkcs` — SignedCms |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` |
| API docs | `Scalar.AspNetCore` (dev only) |
| Container | Docker (multi-stage, `mcr.microsoft.com/dotnet/aspnet:10.0`) |

> All NuGet packages are Microsoft-published except `Scalar.AspNetCore`. Package versions are centrally managed in `Directory.Packages.props`.

---

## Project Structure

```
postal-edge/
├── src/
│   ├── DNA.Email.Core/          # Reusable class library
│   │   ├── Extensions/          # AddDnaEmailCore DI extension
│   │   ├── Interfaces/          # IEmailService, ISmtpConnectionTester
│   │   ├── Mime/                # Raw RFC 2822 MIME builder
│   │   ├── Models/              # Request & response models
│   │   │   └── Requests/        # Per-endpoint request models
│   │   ├── Options/             # Strongly-typed config (SMTP, DKIM, S/MIME, ApiKey)
│   │   ├── Services/            # SmtpEmailService
│   │   ├── Signing/             # DkimSigner, SmimeSigner
│   │   └── Smtp/                # RawSmtpClient
│   └── DNA.Email.API/           # ASP.NET Core host
│       ├── Controllers/         # EmailController, HealthController
│       ├── Middleware/          # ApiKeyMiddleware
│       ├── Properties/          # launchSettings.json
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Dockerfile
│       └── Program.cs
├── Directory.Packages.props     # Centralized NuGet versions
├── Directory.Build.props        # Shared build properties
├── docker-compose.yml
├── .env.example
└── DEPLOYMENT.md                # Coolify deployment guide
```

---

## API Endpoints

All endpoints require the `X-Api-Key` header.

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/email/send-text` | Plain text email |
| `POST` | `/api/email/send-text-html` | Plain text + HTML (multipart/alternative) |
| `POST` | `/api/email/send-html` | HTML only |
| `POST` | `/api/email/send-html-attachments` | HTML + attachments (JSON Base64 or multipart/form-data) |
| `POST` | `/api/email/send` | Full generic endpoint |
| `GET` | `/health` | Liveness check |
| `GET` | `/health/smtp` | SMTP connectivity probe |

### Example — send a plain text email

```bash
curl -X POST https://your-api.example.com/api/email/send-text \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-api-key" \
  -d '{
    "from": { "email": "hello@yourdomain.com", "displayName": "Your Name" },
    "to": [{ "email": "recipient@example.com" }],
    "cc": [{ "email": "cc@example.com" }],
    "bcc": [{ "email": "hidden@example.com" }],
    "subject": "Hello from postal-edge",
    "textBody": "This email was sent via postal-edge."
  }'
```

### Example — send with per-request SMTP credentials

```json
{
  "from": { "email": "you@gmail.com" },
  "to": [{ "email": "recipient@example.com" }],
  "subject": "Test",
  "textBody": "Hello!",
  "smtpSettings": {
    "host": "smtp.gmail.com",
    "port": 587,
    "username": "you@gmail.com",
    "password": "your-app-password"
  }
}
```

### Example — full generic endpoint with DKIM signing

```json
{
  "from": { "email": "hello@yourdomain.com", "displayName": "DNA Lab" },
  "to": [{ "email": "alice@example.com", "displayName": "Alice" }],
  "cc": [{ "email": "bob@example.com" }],
  "replyTo": { "email": "support@yourdomain.com" },
  "subject": "Welcome, {{FirstName}}!",
  "textBody": "Hi {{FirstName}}, welcome aboard.",
  "htmlBody": "<h1>Hi {{FirstName}}</h1><p>Welcome aboard.</p>",
  "templateVariables": { "FirstName": "Alice" },
  "priority": "High",
  "signing": {
    "type": "Dkim",
    "dkimConfig": {
      "domain": "yourdomain.com",
      "selector": "mail",
      "privateKeyPem": "-----BEGIN RSA PRIVATE KEY-----\n...\n-----END RSA PRIVATE KEY-----"
    }
  }
}
```

### Success response

```json
{
  "success": true,
  "messageId": "3f8a2c1d9b4e7f06@yourdomain.com",
  "message": "Email sent successfully.",
  "sentAtUtc": "2026-06-17T10:30:00Z",
  "summary": {
    "from": "hello@yourdomain.com",
    "to": ["alice@example.com"],
    "cc": ["bob@example.com"],
    "subject": "Welcome, Alice!",
    "hasTextBody": true,
    "hasHtmlBody": true,
    "attachmentCount": 0,
    "dkimSigned": true,
    "smimeSigned": false,
    "smtpHost": "smtp.yourdomain.com"
  }
}
```

### Error response

```json
{
  "success": false,
  "messageId": "",
  "message": "Validation failed.",
  "sentAtUtc": "0001-01-01T00:00:00",
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more validation errors occurred.",
    "validationErrors": {
      "To[0].Email": ["'not-an-email' is not a valid email address."]
    }
  }
}
```

---

## Getting Started (Local)

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (optional, for containerised runs)
- An SMTP server, or [Mailpit](https://mailpit.axllent.org/) for local testing

### 1 — Clone and restore

```bash
git clone https://github.com/yourorg/postal-edge.git
cd postal-edge
dotnet restore
```

### 2 — Configure

Copy `.env.example` to `.env` and fill in your values. For local development you can also edit `src/DNA.Email.API/appsettings.Development.json` directly.

**Minimum config to send an email locally via Mailpit:**

```json
{
  "Smtp": {
    "Host": "localhost",
    "Port": 1025,
    "Username": "",
    "Password": ""
  }
}
```

Spin up Mailpit to capture emails without a real SMTP server:

```bash
docker compose --profile dev up mailpit
```

Mailpit web UI: `http://localhost:8025`

### 3 — Run

```bash
dotnet run --project src/DNA.Email.API
```

Browser opens automatically at `http://localhost:5000/scalar/v1`.

**Pre-configured development API keys:**

| Key | Rate limit |
|---|---|
| `dev-key-001` | 500 / 60 s |
| `dev-key-strict` | 3 / 10 s (for rate limit testing) |

### 4 — Build

```bash
dotnet build -c Release
```

---

## Docker

### Build and run locally

```bash
docker compose up --build
```

API available at `http://localhost:8080`.

### Environment variables

Copy `.env.example` to `.env`:

```bash
cp .env.example .env
# Edit .env with your values
docker compose up
```

---

## Configuration Reference

All settings use ASP.NET Core's double-underscore (`__`) section separator as environment variable names.

### SMTP — server-level fallback

| Key | Default | Description |
|---|---|---|
| `Smtp__Host` | *(empty)* | SMTP server hostname |
| `Smtp__Port` | `587` | `587` = STARTTLS · `465` = implicit SSL |
| `Smtp__EnableSsl` | `true` | Always `true` for modern providers |
| `Smtp__Username` | *(empty)* | SMTP login |
| `Smtp__Password` | *(empty)* | SMTP password / app password |
| `Smtp__DisplayName` | *(empty)* | Default sender display name |
| `Smtp__TimeoutMs` | `30000` | Connection timeout in ms |

Leave `Smtp__Host` and `Smtp__Username` empty to require callers to pass `smtpSettings` per-request.

### API Keys

Each key uses an index block. Add more by incrementing the index (`__0__`, `__1__`, `__2__`, …).

```
ApiKey__Keys__0__Key                      your-secret-key
ApiKey__Keys__0__Name                     Production Key
ApiKey__Keys__0__RateLimit__PermitLimit   100
ApiKey__Keys__0__RateLimit__WindowSeconds 60
ApiKey__Keys__0__RateLimit__QueueLimit    0
```

Generate a strong key:

```bash
openssl rand -hex 32
```

### DKIM

| Key | Default | Description |
|---|---|---|
| `Dkim__Enabled` | `false` | Enable server-level DKIM |
| `Dkim__Domain` | *(empty)* | Signing domain |
| `Dkim__Selector` | `mail` | DNS selector |
| `Dkim__PrivateKeyPem` | *(empty)* | RSA private key in PEM format |

### S/MIME

| Key | Default | Description |
|---|---|---|
| `Smime__Enabled` | `false` | Enable S/MIME signing |
| `Smime__CertificatePath` | *(empty)* | Absolute path to .pfx inside the container |
| `Smime__CertificatePassword` | *(empty)* | Certificate password |

### CORS

```
Cors__AllowedOrigins__0    https://app.yourdomain.com
Cors__AllowedOrigins__1    https://admin.yourdomain.com
```

---

## DKIM DNS Setup

Generate a 2048-bit RSA key pair:

```bash
openssl genrsa -out dkim_private.pem 2048
openssl rsa -in dkim_private.pem -pubout -out dkim_public.pem

# Base64 value for DNS TXT record
grep -v "PUBLIC KEY" dkim_public.pem | tr -d '\n'
```

Add a TXT record to your domain DNS:

```
Name:   mail._domainkey.yourdomain.com
Type:   TXT
Value:  v=DKIM1; k=rsa; p=<base64-public-key>
```

Paste the full contents of `dkim_private.pem` into `Dkim__PrivateKeyPem`.

---

## Deploying to Coolify

See **[DEPLOYMENT.md](DEPLOYMENT.md)** for the full step-by-step guide including:

- Dockerfile-based Coolify resource setup
- Complete environment variable reference for the Coolify UI
- Domain, HTTPS, and port configuration
- S/MIME certificate volume mount
- Auto-deploy webhook setup

Quick summary:

1. Push to Git
2. Create a Coolify resource → **Dockerfile** → build context `.`, Dockerfile path `src/DNA.Email.API/Dockerfile`
3. Add environment variables in the Coolify UI
4. Set domain, port `8080`, enable HTTPS
5. Deploy

---

## Using DNA.Email.Core in Other Projects

`DNA.Email.Core` has no ASP.NET dependency. Reference it in any .NET project:

```xml
<ProjectReference Include="../DNA.Email.Core/DNA.Email.Core.csproj" />
```

Register services:

```csharp
builder.Services.AddDnaEmailCore(builder.Configuration);
```

Inject and use:

```csharp
public class MyService(IEmailService email)
{
    public Task SendWelcomeAsync(string to) =>
        email.SendTextAsync(new SendTextEmailRequest
        {
            From = new EmailAddress { Email = "hello@yourdomain.com" },
            To = [new EmailAddress { Email = to }],
            Subject = "Welcome!",
            TextBody = "Thanks for signing up."
        });
}
```

---

## Security Notes

- **Never commit secrets** — use environment variables or a secrets manager
- **HTTPS only** — DKIM private keys travel in the request body; enforce TLS at the reverse proxy
- **App Passwords** — use Gmail/Outlook App Passwords, not your account password
- **Rotate API keys** — use the index-based config to add a new key before removing the old one
- **S/MIME certificates** — mount via bind volume, never bake into the Docker image
- **BCC is RFC-correct** — BCC addresses are in the SMTP envelope only, never written to email headers

---

## License

MIT
