#!/bin/bash
# ==============================================
# StarterKit - Production Secrets Setup
# ==============================================
# Generates secure random secrets for production deployment
# Run this script ONCE before deploying to production
# ==============================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SECRETS_DIR="$PROJECT_ROOT/secrets"

echo "════════════════════════════════════════════════════════"
echo "  StarterKit - Production Secrets Setup"
echo "════════════════════════════════════════════════════════"
echo ""
echo "Project root: $PROJECT_ROOT"
echo "Secrets directory: $SECRETS_DIR"
echo ""

# ==============================================
# Create secrets directory
# ==============================================
if [ ! -d "$SECRETS_DIR" ]; then
    echo "📁 Creating secrets directory..."
    mkdir -p "$SECRETS_DIR"
    echo "  ✅ Created: $SECRETS_DIR"
else
    echo "📁 Secrets directory exists: $SECRETS_DIR"
fi

echo ""

# ==============================================
# Check for existing secrets
# ==============================================
EXISTING_SECRETS=0

if [ -f "$SECRETS_DIR/postgres_password.txt" ]; then
    echo "⚠️  WARNING: postgres_password.txt already exists!"
    EXISTING_SECRETS=1
fi

if [ -f "$SECRETS_DIR/redis_password.txt" ]; then
    echo "⚠️  WARNING: redis_password.txt already exists!"
    EXISTING_SECRETS=1
fi

if [ -f "$SECRETS_DIR/rabbitmq_password.txt" ]; then
    echo "⚠️  WARNING: rabbitmq_password.txt already exists!"
    EXISTING_SECRETS=1
fi

if [ -f "$SECRETS_DIR/jwt_secret.txt" ]; then
    echo "⚠️  WARNING: jwt_secret.txt already exists!"
    EXISTING_SECRETS=1
fi

if [ -f "$SECRETS_DIR/whatsapp_access_token.txt" ]; then
    echo "⚠️  WARNING: whatsapp_access_token.txt already exists!"
    EXISTING_SECRETS=1
fi

if [ -f "$SECRETS_DIR/whatsapp_phone_number_id.txt" ]; then
    echo "⚠️  WARNING: whatsapp_phone_number_id.txt already exists!"
    EXISTING_SECRETS=1
fi

if [ $EXISTING_SECRETS -eq 1 ]; then
    echo ""
    read -p "Overwrite existing secrets? [y/N] " -n 1 -r
    echo ""
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "❌ Aborted. Existing secrets preserved."
        exit 0
    fi
    echo ""
fi

# ==============================================
# Generate secrets
# ==============================================
echo "🔐 Generating secure secrets..."
echo ""

# PostgreSQL password (24 bytes = 32 base64 chars)
echo "  Generating postgres_password..."
openssl rand -base64 24 | tr -d '\n' > "$SECRETS_DIR/postgres_password.txt"
chmod 600 "$SECRETS_DIR/postgres_password.txt"
POSTGRES_PASS_LEN=$(wc -c < "$SECRETS_DIR/postgres_password.txt")
echo "  ✅ postgres_password.txt ($POSTGRES_PASS_LEN chars)"

# Redis password (24 bytes = 32 base64 chars)
echo "  Generating redis_password..."
openssl rand -base64 24 | tr -d '\n' > "$SECRETS_DIR/redis_password.txt"
chmod 600 "$SECRETS_DIR/redis_password.txt"
REDIS_PASS_LEN=$(wc -c < "$SECRETS_DIR/redis_password.txt")
echo "  ✅ redis_password.txt ($REDIS_PASS_LEN chars)"

# RabbitMQ password (only used with docker-compose.rabbitmq.yml; harmless if unused)
echo "  Generating rabbitmq_password..."
openssl rand -base64 24 | tr -d '\n' > "$SECRETS_DIR/rabbitmq_password.txt"
chmod 600 "$SECRETS_DIR/rabbitmq_password.txt"
RABBITMQ_PASS_LEN=$(wc -c < "$SECRETS_DIR/rabbitmq_password.txt")
echo "  ✅ rabbitmq_password.txt ($RABBITMQ_PASS_LEN chars)"

#if (search)
# Meilisearch (only used with docker-compose.meilisearch.yml; harmless if unused). The master key is NEVER regenerated
# when it exists: every API key of the server is derived from it. The API key file starts empty: create a scoped key
# once the server runs (wiki/Search/Meilisearch.md) and paste it there.
if [ -f "$SECRETS_DIR/meilisearch_master_key.txt" ]; then
    echo "  ⏭️  meilisearch_master_key.txt exists - kept (API keys are derived from it)"
else
    echo "  Generating meilisearch_master_key..."
    openssl rand -base64 32 | tr -d '\n' > "$SECRETS_DIR/meilisearch_master_key.txt"
    chmod 600 "$SECRETS_DIR/meilisearch_master_key.txt"
    echo "  ✅ meilisearch_master_key.txt"
fi
if [ ! -f "$SECRETS_DIR/meilisearch_api_key.txt" ]; then
    : > "$SECRETS_DIR/meilisearch_api_key.txt"
    chmod 600 "$SECRETS_DIR/meilisearch_api_key.txt"
    echo "  ✅ meilisearch_api_key.txt (empty; fill in a scoped key before switching Search to Meilisearch)"
fi

#endif
# JWT secret (48 bytes = 64 base64 chars)
echo "  Generating jwt_secret..."
openssl rand -base64 48 | tr -d '\n' > "$SECRETS_DIR/jwt_secret.txt"
chmod 600 "$SECRETS_DIR/jwt_secret.txt"
JWT_SECRET_LEN=$(wc -c < "$SECRETS_DIR/jwt_secret.txt")
echo "  ✅ jwt_secret.txt ($JWT_SECRET_LEN chars)"

# HMAC key used to hash every one-time code at rest (Identity's unified OTP service)
for KEY in identity_otp_hmac_key; do
    echo "  Generating $KEY..."
    openssl rand -base64 48 | tr -d '\n' > "$SECRETS_DIR/$KEY.txt"
    chmod 600 "$SECRETS_DIR/$KEY.txt"
    echo "  ✅ $KEY.txt"
done

# Optional privileged (DDL) connection string for the one-shot migrator. Never overwritten; an EMPTY file means
# "migrate with the API connection". Fill it by hand (Host=postgres;Database=...;Username=starterkit_migrator;Password=...).
if [ ! -f "$SECRETS_DIR/migration_connection_string.txt" ]; then
    : > "$SECRETS_DIR/migration_connection_string.txt"
    chmod 600 "$SECRETS_DIR/migration_connection_string.txt"
    echo "  ✅ migration_connection_string.txt (empty; optional, fill in to use a separate migration role)"
fi

# Data Protection key-ring encryption certificate (used by docker-compose.dataprotection.yml).
# NEVER regenerated when it already exists: the key ring in the database is encrypted with it, and replacing it
# would make every authenticator (TOTP) key unreadable. Rotate deliberately (see the wiki), not through this script.
if [ -f "$SECRETS_DIR/dataprotection_cert.pfx" ]; then
    echo "  ⏭️  dataprotection_cert.pfx exists - kept (rotation is a manual, documented step)"
else
    echo "  Generating dataprotection_cert.pfx (self-signed RSA 3072, 10 years)..."
    openssl rand -base64 32 | tr -d '\n' > "$SECRETS_DIR/dataprotection_cert_password.txt"
    chmod 600 "$SECRETS_DIR/dataprotection_cert_password.txt"
    DP_TMP="$(mktemp -d)"
    openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes -subj "/CN=StarterKit Data Protection" -keyout "$DP_TMP/key.pem" -out "$DP_TMP/cert.pem" 2>/dev/null
    openssl pkcs12 -export -out "$SECRETS_DIR/dataprotection_cert.pfx" -inkey "$DP_TMP/key.pem" -in "$DP_TMP/cert.pem" -passout "file:$SECRETS_DIR/dataprotection_cert_password.txt"
    rm -rf "$DP_TMP"
    chmod 600 "$SECRETS_DIR/dataprotection_cert.pfx"
    echo "  ✅ dataprotection_cert.pfx + dataprotection_cert_password.txt (back both up with the database)"
fi

# WhatsApp access token (placeholder to be replaced with real Meta token)
echo "  Creating whatsapp_access_token placeholder..."
echo "REPLACE_WITH_META_WHATSAPP_ACCESS_TOKEN" > "$SECRETS_DIR/whatsapp_access_token.txt"
chmod 600 "$SECRETS_DIR/whatsapp_access_token.txt"
WHATSAPP_TOKEN_LEN=$(wc -c < "$SECRETS_DIR/whatsapp_access_token.txt")
echo "  ✅ whatsapp_access_token.txt ($WHATSAPP_TOKEN_LEN chars)"

# WhatsApp phone number id (placeholder to be replaced with real Meta phone number id)
echo "  Creating whatsapp_phone_number_id placeholder..."
echo "REPLACE_WITH_META_WHATSAPP_PHONE_NUMBER_ID" > "$SECRETS_DIR/whatsapp_phone_number_id.txt"
chmod 600 "$SECRETS_DIR/whatsapp_phone_number_id.txt"
WHATSAPP_PHONE_ID_LEN=$(wc -c < "$SECRETS_DIR/whatsapp_phone_number_id.txt")
echo "  ✅ whatsapp_phone_number_id.txt ($WHATSAPP_PHONE_ID_LEN chars)"

echo ""

# ==============================================
# Display generated secrets
# ==============================================
echo "════════════════════════════════════════════════════════"
echo "  ✅ Secrets Generated Successfully"
echo "════════════════════════════════════════════════════════"
echo ""
echo "📋 Summary:"
echo ""
ls -lh "$SECRETS_DIR"/*.txt 2>/dev/null | grep -v example || echo "  (No secret files found)"
echo ""

# ==============================================
# Display secrets (masked)
# ==============================================
echo "🔍 Secret Preview (first 8 chars only):"
echo ""

if [ -f "$SECRETS_DIR/postgres_password.txt" ]; then
    POSTGRES_PREVIEW=$(head -c 8 "$SECRETS_DIR/postgres_password.txt")
    echo "  postgres_password: ${POSTGRES_PREVIEW}... (${POSTGRES_PASS_LEN} chars total)"
fi

if [ -f "$SECRETS_DIR/redis_password.txt" ]; then
    REDIS_PREVIEW=$(head -c 8 "$SECRETS_DIR/redis_password.txt")
    echo "  redis_password:    ${REDIS_PREVIEW}... (${REDIS_PASS_LEN} chars total)"
fi

if [ -f "$SECRETS_DIR/jwt_secret.txt" ]; then
    JWT_PREVIEW=$(head -c 8 "$SECRETS_DIR/jwt_secret.txt")
    echo "  jwt_secret:        ${JWT_PREVIEW}... (${JWT_SECRET_LEN} chars total)"
fi

if [ -f "$SECRETS_DIR/whatsapp_access_token.txt" ]; then
    WHATSAPP_TOKEN_PREVIEW=$(head -c 8 "$SECRETS_DIR/whatsapp_access_token.txt")
    echo "  whatsapp_access_token: ${WHATSAPP_TOKEN_PREVIEW}... (${WHATSAPP_TOKEN_LEN} chars total)"
fi

if [ -f "$SECRETS_DIR/whatsapp_phone_number_id.txt" ]; then
    WHATSAPP_PHONE_PREVIEW=$(head -c 8 "$SECRETS_DIR/whatsapp_phone_number_id.txt")
    echo "  whatsapp_phone_number_id: ${WHATSAPP_PHONE_PREVIEW}... (${WHATSAPP_PHONE_ID_LEN} chars total)"
fi

echo ""

# ==============================================
# Security warnings
# ==============================================
echo "════════════════════════════════════════════════════════"
echo "  🔐 Security Reminders"
echo "════════════════════════════════════════════════════════"
echo ""
echo "  1. ✅ Secrets are in ./secrets/ (gitignored)"
echo "  2. ✅ Files have 600 permissions (owner read/write only)"
echo "  3. ⚠️  NEVER commit secrets to git"
echo "  4. ⚠️  NEVER share secrets in logs/screenshots"
echo "  5. ✅ Backup secrets securely (encrypted)"
echo ""

# ==============================================
# Next steps
# ==============================================
echo "════════════════════════════════════════════════════════"
echo "  📝 Next Steps"
echo "════════════════════════════════════════════════════════"
echo ""
echo "  1. Verify secrets: ls -lh secrets/*.txt"
echo "  2. Copy .env.prod to .env: cp .env.prod .env"
echo "  3. Build image: docker compose build api"
echo "  4. Deploy: docker compose up -d"
echo "  5. Check logs: docker compose logs -f api"
echo ""
echo "════════════════════════════════════════════════════════"
echo ""
