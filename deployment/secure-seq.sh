#!/bin/bash
# ==============================================
# Seq Security Setup Script
# ==============================================
# This script generates a secure password for Seq
# and configures authentication
# ==============================================

set -e  # Exit on error

echo "=========================================="
echo "Seq Security Setup"
echo "=========================================="
echo ""

# Check if Docker is running
if ! docker info > /dev/null 2>&1; then
    echo "ERROR: Docker is not running. Please start Docker and try again."
    exit 1
fi

# Generate a secure random password
echo "Generating secure password..."
SEQ_PASSWORD=$(openssl rand -base64 32)
echo "Generated password: $SEQ_PASSWORD"
echo ""

# Generate password hash using Seq container
echo "Generating password hash..."
SEQ_HASH=$(echo "$SEQ_PASSWORD" | docker run --rm -i datalust/seq:2024.3 config hash)
echo "Generated hash: $SEQ_HASH"
echo ""

# Save to a secure location
SECRETS_DIR="./secrets"
mkdir -p "$SECRETS_DIR"
chmod 700 "$SECRETS_DIR"

# Save password to file
echo "$SEQ_PASSWORD" > "$SECRETS_DIR/seq_admin_password.txt"
chmod 600 "$SECRETS_DIR/seq_admin_password.txt"

echo "=========================================="
echo "Setup Complete!"
echo "=========================================="
echo ""
echo "Seq Admin Credentials:"
echo "  Username: admin"
echo "  Password: $SEQ_PASSWORD"
echo "  (saved to: $SECRETS_DIR/seq_admin_password.txt)"
echo ""
echo "Password Hash (for .env.prod):"
echo "  SEQ_ADMIN_PASSWORD_HASH=$SEQ_HASH"
echo ""
echo "Next Steps:"
echo "  1. Update .env.prod with the hash above"
echo "  2. Restart Seq container:"
echo "     docker compose -f docker-compose.yml restart seq"
echo "  3. Access Seq at: http://your-server:5341"
echo "  4. Login with username 'admin' and the password above"
echo ""
echo "SECURITY NOTE:"
echo "  - Keep $SECRETS_DIR/seq_admin_password.txt secure!"
echo "  - Add $SECRETS_DIR/ to .gitignore"
echo "  - Consider using nginx basic auth for additional protection"
echo ""
