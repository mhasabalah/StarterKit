# Production Deployment with Docker Secrets

This guide shows how to deploy StarterKit to production using **Docker Secrets** for maximum security.

---

## 🔐 Architecture Overview

### How Secrets Flow

```
┌─────────────────────────────────────────────────────────────┐
│ Host VPS                                                    │
│                                                             │
│  ./secrets/postgres_password.txt ──┐                       │
│  ./secrets/redis_password.txt ─────┼─── Docker mounts as   │
│  ./secrets/jwt_secret.txt ─────────┘    /run/secrets/*     │
│                                                             │
│  ┌────────────────────────────────────────────────────┐    │
│  │ API Container                                      │    │
│  │                                                    │    │
│  │  1. Entrypoint reads /run/secrets/*               │    │
│  │  2. Exports as environment variables              │    │
│  │  3. Builds ConnectionStrings                      │    │
│  │  4. Starts .NET application                       │    │
│  └────────────────────────────────────────────────────┘    │
│                                                             │
│  ┌────────────────────────────────────────────────────┐    │
│  │ PostgreSQL Container                               │    │
│  │                                                    │    │
│  │  POSTGRES_PASSWORD_FILE: /run/secrets/...         │    │
│  │  (Native support - reads file automatically)      │    │
│  └────────────────────────────────────────────────────┘    │
│                                                             │
│  ┌────────────────────────────────────────────────────┐    │
│  │ Redis Container                                    │    │
│  │                                                    │    │
│  │  command: sh -c 'password=$(cat /run/secrets/...)'│    │
│  │  (Workaround - Redis doesn't support *_FILE)      │    │
│  └────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

---

## 🚀 Quick Start (Production)

### 1. Generate Secrets

```bash
# Run automated setup script
./deployment/secrets-setup.sh
```

This creates:
- `secrets/postgres_password.txt` (32 chars)
- `secrets/redis_password.txt` (32 chars)
- `secrets/jwt_secret.txt` (64 chars)

All with secure permissions (`chmod 600`).

### 2. Verify Secrets

```bash
# List secret files
ls -lh secrets/*.txt

# Expected output:
# -rw------- 1 user user  32 Jan 30 12:00 postgres_password.txt
# -rw------- 1 user user  32 Jan 30 12:00 redis_password.txt
# -rw------- 1 user user  64 Jan 30 12:00 jwt_secret.txt

# Check permissions are 600
stat -c '%a' secrets/*.txt
# Should output: 600 600 600
```

### 3. Setup Environment

```bash
# Copy production config
cp .env.prod .env

# Verify it doesn't contain passwords (they're in secrets/)
grep PASSWORD .env
# Should show empty values or comments
```

### 4. Deploy

```bash
# Build the API image (includes entrypoint script)
docker compose build api

# Start all services
docker compose up -d

# Watch startup logs
docker compose logs -f api
```

### 5. Verify Deployment

```bash
# Check API startup logs - should see:
docker compose logs api | grep "🔐"

# Expected output:
# 🔐 Reading Docker secrets...
#   ✅ Loaded postgres_password (32 chars)
#   ✅ Loaded redis_password (32 chars)
#   ✅ Loaded jwt_secret (64 chars)
# 🔗 Building connection strings...
#   ✅ ConnectionStrings__starterkit
#   ✅ ConnectionStrings__Redis
#   ✅ JwtSettings__SecretKey
# 🚀 Starting StarterKit API...

# Verify secrets are NOT visible in docker inspect
docker inspect starterkit-api | grep -i password
# Should return nothing (secrets are hidden!)
```

---

## 🔍 How It Works

### docker-compose.yml

```yaml
secrets:
  postgres_password:
    file: ./secrets/postgres_password.txt  # Host path
  redis_password:
    file: ./secrets/redis_password.txt
  jwt_secret:
    file: ./secrets/jwt_secret.txt

services:
  api:
    secrets:
      - postgres_password  # Mounted at /run/secrets/postgres_password
      - redis_password     # Mounted at /run/secrets/redis_password
      - jwt_secret         # Mounted at /run/secrets/jwt_secret
    environment:
      POSTGRES_HOST: postgres
      POSTGRES_DB: starterkit
      # No POSTGRES_PASSWORD here!
```

### C# Configuration Provider (DockerSecretsConfigurationProvider.cs)

```csharp
// Integrated with IConfiguration pipeline
builder.Configuration.AddDockerSecrets();

// Provider automatically:
// 1. Checks if /run/secrets/ exists
// 2. Reads secret files (postgres_password, redis_password, jwt_secret)
// 3. Builds connection strings
// 4. Adds to IConfiguration
// 5. Validates secret strength
// 6. Logs detailed startup information

// Secrets become available as:
// - ConnectionStrings:starterkit
// - ConnectionStrings:Redis
// - JwtSettings:SecretKey
```

---

## 🆚 Production vs Development

| Aspect | Production | Development |
|--------|-----------|-------------|
| **Secrets Location** | `./secrets/*.txt` files | `.env` file |
| **Injection Method** | Docker secrets → Entrypoint → Env vars | docker-compose.override.yml |
| **Visibility** | Hidden in `docker inspect` | Visible in `docker inspect` |
| **Permissions** | 600 (owner only) | 644 (readable) |
| **Complexity** | Higher (entrypoint script) | Lower (direct env vars) |
| **Security** | ✅ Production-grade | ⚠️ Dev-friendly |

---

## 🛡 Security Benefits

### ✅ What You Get

1. **Secrets Never in Environment Variables**
   ```bash
   # This returns NOTHING:
   docker inspect starterkit-api | grep PASSWORD
   ```

2. **Secrets Never in Logs**
   ```bash
   # Entrypoint doesn't log secret values
   docker compose logs api
   # Shows: "✅ Loaded postgres_password (32 chars)"
   # NOT: "Password is: abc123..."
   ```

3. **Secrets Never in Git**
   ```bash
   # secrets/ is in .gitignore
   git status
   # Won't show secret files
   ```

4. **Secrets Have Restricted Permissions**
   ```bash
   ls -l secrets/*.txt
   # -rw------- (600) = only owner can read/write
   ```

5. **Secrets Separate from Config**
   ```bash
   # .env has DB host, port, database name
   # secrets/ has passwords
   # Follows 12-factor app principles
   ```

---

## 🔄 Secret Rotation

### When to Rotate

- Quarterly (every 3 months)
- After security incident
- When employee leaves
- Best practice: automate rotation

### How to Rotate

```bash
# 1. Generate new secrets
./deployment/secrets-setup.sh
# (Choose 'y' to overwrite)

# 2. Rebuild API image (includes entrypoint)
docker compose build api

# 3. Rolling update (zero downtime)
docker compose up -d --no-deps api

# 4. Verify new secrets loaded
docker compose logs api | grep "🔐"

# 5. Securely delete old backups
shred -u secrets.backup.tar.gz  # If you have backups
```

---

## 🧪 Testing Secrets

### Test Locally (Before Production)

```bash
# 1. Generate test secrets
./deployment/secrets-setup.sh

# 2. Start services
docker compose up -d

# 3. Check API logs
docker compose logs api | head -30

# Should see:
# 🔐 Reading Docker secrets...
#   ✅ Loaded postgres_password (32 chars)
#   ✅ Loaded redis_password (32 chars)
#   ✅ Loaded jwt_secret (64 chars)

# 4. Verify app works
curl http://localhost:5000/health
# Should return: {"status":"Healthy"}
```

### Test Secret Changes

```bash
# 1. Change a secret
echo "NewTestPassword123!" > secrets/postgres_password.txt

# 2. Restart API (forces re-read)
docker compose restart api

# 3. Check logs
docker compose logs api | grep "🔐"
# Should show new password length
```

---

## 🚨 Troubleshooting

### Error: "No /run/secrets/postgres_password found"

**Cause:** Secret file doesn't exist on host.

**Fix:**
```bash
# Generate secrets
./deployment/secrets-setup.sh

# Verify they exist
ls -lh secrets/*.txt
```

### Error: "Permission denied" reading secret

**Cause:** Wrong file permissions or running as different user.

**Fix:**
```bash
# Set correct permissions
chmod 600 secrets/*.txt

# Verify ownership
ls -l secrets/*.txt
# Should be owned by your user
```

### Error: "ConnectionStrings__starterkit not set"

**Cause:** Entrypoint script didn't run or failed.

**Fix:**
```bash
# Check entrypoint is executable
ls -l src/Api/docker-entrypoint.sh
# Should show: -rwxr-xr-x

# Rebuild image
docker compose build api

# Check full startup logs
docker compose logs api
```

### Secrets not loading in production

**Cause:** docker-compose.override.yml is overriding production config.

**Fix:**
```bash
# Rename or remove override in production
mv docker-compose.override.yml docker-compose.override.yml.dev

# Or explicitly use base file only
docker compose -f docker-compose.yml up -d
```

---

## 📦 Backup and Recovery

### Backup Secrets

```bash
# Create encrypted backup
tar czf - secrets/*.txt | \
  openssl enc -aes-256-cbc -salt -pbkdf2 -out secrets.backup.tar.gz.enc

# Store securely (off-server):
# - Encrypted cloud storage
# - Hardware security module (HSM)
# - Password manager (1Password, Bitwarden)
```

### Restore Secrets

```bash
# Decrypt and extract
openssl enc -aes-256-cbc -d -pbkdf2 -in secrets.backup.tar.gz.enc | \
  tar xzf -

# Set permissions
chmod 600 secrets/*.txt

# Restart services
docker compose restart
```

---

## 📚 Additional Resources

- Docker Secrets: https://docs.docker.com/engine/swarm/secrets/
- 12-Factor App: https://12factor.net/config
- OWASP Secrets Management: https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html

---

## ✅ Production Checklist

Before deploying:

- [ ] Secrets generated (`./deployment/secrets-setup.sh`)
- [ ] Secrets verified (`ls -lh secrets/*.txt`)
- [ ] Permissions set (`chmod 600 secrets/*.txt`)
- [ ] `.env.prod` copied to `.env`
- [ ] API image built (`docker compose build api`)
- [ ] Services started (`docker compose up -d`)
- [ ] Logs checked (`docker compose logs api | grep "🔐"`)
- [ ] Health endpoint tested (`curl http://localhost:5000/health`)
- [ ] Secrets backed up (encrypted)
- [ ] `.gitignore` includes `secrets/`
- [ ] Production `.env` has no passwords
- [ ] `docker inspect` doesn't show secrets

---

**Last Updated:** 2026-01-30
