# Deploying postal-edge on Coolify (Linux VPS)

## Prerequisites
- Linux VPS (Ubuntu 22.04+ recommended)
- Coolify installed (self-hosted)
- Docker available on the VPS
- A domain or subdomain pointed at your VPS IP (e.g. `email-api.yourdomain.com`)
- SMTP credentials from your email provider

---

## Step 1 — Push your code to a Git repository

Coolify deploys from Git. Push to GitHub, GitLab, or any Git host.

```bash
git add .
git commit -m "Initial commit"
git remote add origin https://github.com/yourorg/postal-edge.git
git push -u origin main
```

---

## Step 2 — Create a new Coolify resource

1. Open your Coolify dashboard (`https://your-vps-ip:8000`)
2. Click **+ New Project** → name it (e.g. `postal-edge`)
3. Click **+ New Resource** → **Application** → **Dockerfile**
4. Fill in:
   - **Repository**: your Git repo URL
   - **Branch**: `main`
   - **Dockerfile location**: `src/DNA.Email.API/Dockerfile`
   - **Docker build context**: `.` *(repository root — required because the Dockerfile copies from the solution root)*

---

## Step 3 — Configure environment variables in Coolify

Go to your resource → **Environment Variables** tab and add the variables below.

> ASP.NET Core maps double-underscores (`__`) to nested config sections automatically — use the exact variable names shown.

### Core (required)

| Variable | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |

### SMTP — server-level fallback (optional)

Leave blank to require callers to pass `smtpSettings` in every request body.

| Variable | Example value | Notes |
|---|---|---|
| `Smtp__Host` | `smtp.gmail.com` | Your SMTP server hostname |
| `Smtp__Port` | `587` | `587` = STARTTLS · `465` = implicit SSL |
| `Smtp__EnableSsl` | `true` | |
| `Smtp__Username` | `you@gmail.com` | |
| `Smtp__Password` | `abcd efgh ijkl mnop` | Gmail: use an App Password |
| `Smtp__DisplayName` | `DNA Lab` | Sender display name |
| `Smtp__TimeoutMs` | `30000` | Timeout in ms |

### API Keys

Each key uses an **index-based** variable block. Add more with `__1__`, `__2__`, etc.

**Key 0:**

| Variable | Example value |
|---|---|
| `ApiKey__Keys__0__Key` | `sk-prod-abc123...` |
| `ApiKey__Keys__0__Name` | `Production Key` |
| `ApiKey__Keys__0__RateLimit__PermitLimit` | `100` |
| `ApiKey__Keys__0__RateLimit__WindowSeconds` | `60` |
| `ApiKey__Keys__0__RateLimit__QueueLimit` | `0` |

**Key 1 (optional):**

| Variable | Example value |
|---|---|
| `ApiKey__Keys__1__Key` | `sk-prod-xyz789...` |
| `ApiKey__Keys__1__Name` | `High Volume Key` |
| `ApiKey__Keys__1__RateLimit__PermitLimit` | `1000` |
| `ApiKey__Keys__1__RateLimit__WindowSeconds` | `60` |
| `ApiKey__Keys__1__RateLimit__QueueLimit` | `10` |

> Generate a strong key: `openssl rand -hex 32`

### DKIM signing (optional)

| Variable | Example value | Notes |
|---|---|---|
| `Dkim__Enabled` | `true` | |
| `Dkim__Domain` | `yourdomain.com` | |
| `Dkim__Selector` | `mail` | DNS selector prefix |
| `Dkim__PrivateKeyPem` | `-----BEGIN RSA PRIVATE KEY-----\n…` | Full PEM including headers |

### S/MIME signing (optional)

| Variable | Example value |
|---|---|
| `Smime__Enabled` | `true` |
| `Smime__CertificatePath` | `/app/certs/smime.pfx` |
| `Smime__CertificatePassword` | `your-cert-password` |

### CORS

| Variable | Example value | Notes |
|---|---|---|
| `Cors__AllowedOrigins__0` | `https://app.yourdomain.com` | Add `__1__`, `__2__` for more origins |

Use `*` to allow all origins (not recommended in production).

### Limits

| Variable | Default | Notes |
|---|---|---|
| `Limits__MaxRequestBodyBytes` | `52428800` | Max upload size in bytes (50 MB) |

---

## Step 4 — Configure domain and port

In Coolify's **Domains** section:
- **Domain**: `email-api.yourdomain.com`
- **Port**: `8080`
- Enable **HTTPS** — Coolify handles Let's Encrypt automatically

---

## Step 5 — Mount S/MIME certificate (if using S/MIME)

1. SSH into your VPS:
   ```bash
   mkdir -p /srv/postal-edge/certs
   chmod 700 /srv/postal-edge/certs
   ```
2. Upload your certificate:
   ```bash
   scp smime.pfx user@vps:/srv/postal-edge/certs/smime.pfx
   chmod 600 /srv/postal-edge/certs/smime.pfx
   ```
3. In Coolify → **Storages / Volumes** → add bind mount:
   - **Host path**: `/srv/postal-edge/certs`
   - **Container path**: `/app/certs`
   - **Read-only**: yes

---

## Step 6 — Deploy

Click **Deploy** in Coolify. It will:
1. Pull your latest commit
2. Build the Docker image using the multi-stage Dockerfile
3. Start the container with all environment variables applied
4. Set up the reverse proxy + SSL

---

## Step 7 — Verify

```bash
# Liveness check
curl https://email-api.yourdomain.com/health

# SMTP connectivity probe
curl https://email-api.yourdomain.com/health/smtp

# Send a test email
curl -X POST https://email-api.yourdomain.com/api/email/send-text \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: sk-prod-abc123..." \
  -d '{
    "from": { "email": "you@yourdomain.com", "displayName": "DNA Lab" },
    "to": [{ "email": "recipient@example.com" }],
    "subject": "Test from postal-edge",
    "textBody": "Hello! This is a test email."
  }'
```

---

## DKIM DNS Setup

Generate a 2048-bit RSA key pair:

```bash
openssl genrsa -out dkim_private.pem 2048
openssl rsa -in dkim_private.pem -pubout -out dkim_public.pem

# Base64 public key for DNS record
grep -v "PUBLIC KEY" dkim_public.pem | tr -d '\n'
```

Add a TXT record to your domain DNS:

```
Name:   mail._domainkey.yourdomain.com
Type:   TXT
Value:  v=DKIM1; k=rsa; p=<base64-public-key>
```

Paste the full `dkim_private.pem` content (including `-----BEGIN/END-----` lines) into `Dkim__PrivateKeyPem`.

---

## Auto-deploy on Git push

In Coolify → **Webhooks** → copy the deploy webhook URL.
Add it to your Git repo's webhook settings.
Every push to `main` triggers an automatic redeploy.

---

## Coolify tips

| Task | Where |
|---|---|
| View logs | App → **Logs** tab |
| Restart container | App → **Restart** button |
| Roll back | App → **Deployments** tab → click any past deployment |
| Update env vars (no rebuild) | App → **Environment Variables** → save → **Restart** |
| Force rebuild | App → **Deploy** button |
