# Seq Security Configuration

## Overview

This guide explains how to secure your Seq logging instance with authentication.

## Current Security Issue

**WARNING:** Seq is currently running WITHOUT authentication! Anyone with access to port 5341 can view all logs.

## Quick Fix (Production Server)

### Option 1: Automated Script

Run the security setup script on your production server:

```bash
cd /path/to/StarterKit/deployment
chmod +x secure-seq.sh
./secure-seq.sh
```

The script will:
1. Generate a secure random password
2. Create a password hash compatible with Seq
3. Save the password to `secrets/seq_admin_password.txt`
4. Display the hash to add to `.env.prod`

### Option 2: Manual Setup

1. **Generate a secure password:**

```bash
openssl rand -base64 32
```

Save this password somewhere secure (e.g., password manager).

2. **Generate the password hash:**

```bash
echo 'YOUR_PASSWORD_HERE' | docker run --rm -i datalust/seq:2024.3 config hash
```

This will output a hash like: `PH+Abc123...`

3. **Update `.env.prod`:**

Edit the `.env.prod` file and set:

```bash
SEQ_ADMIN_PASSWORD_HASH=PH+YOUR_GENERATED_HASH_HERE
```

4. **Restart Seq container:**

```bash
docker compose -f docker-compose.yml restart seq
```

Or for full stack:

```bash
docker compose -f docker-compose.yml -f docker-compose.observability.yml restart seq
```

5. **Verify authentication is working:**

Access Seq at `http://your-server:5341` - you should be prompted for login.

Default credentials:
- Username: `admin`
- Password: (the password you generated in step 1)

## Additional Security Layers

### 1. Nginx Basic Authentication

Add an additional layer of HTTP basic auth in nginx (already configured at `deployment/nginx/conf.d/app-production.conf:177-188`):

```nginx
location /seq/ {
    # IP restriction
    allow YOUR_IP_HERE;
    deny all;

    # Basic auth (optional additional layer)
    auth_basic "Seq Logs - Restricted";
    auth_basic_user_file /etc/nginx/.htpasswd;

    proxy_pass http://localhost:5341/;
    # ... rest of config
}
```

Generate htpasswd file:

```bash
sudo apt-get install apache2-utils
sudo htpasswd -c /etc/nginx/.htpasswd seqadmin
sudo systemctl reload nginx
```

### 2. IP Whitelisting

Currently, the nginx config denies all access to `/seq/`. Update it to allow your IP:

```nginx
location /seq/ {
    allow YOUR_IP_ADDRESS;
    deny all;
    # ... rest of config
}
```

### 3. VPN Access Only

Best practice: Access Seq only through a VPN connection to your server.

## Security Checklist

- [ ] Generate secure password for Seq
- [ ] Set `SEQ_ADMIN_PASSWORD_HASH` in `.env.prod`
- [ ] Restart Seq container
- [ ] Verify login works at Seq UI
- [ ] Save password in secure password manager
- [ ] Update nginx IP whitelist for `/seq/` location
- [ ] (Optional) Add nginx basic auth
- [ ] (Optional) Setup VPN for monitoring tools access
- [ ] Add `secrets/` directory to `.gitignore`

## Troubleshooting

### Seq doesn't prompt for password

1. Check that the hash is correctly set:
   ```bash
   grep SEQ_ADMIN_PASSWORD_HASH .env.prod
   ```

2. Verify the container picked up the environment variable:
   ```bash
   docker inspect starterkit-seq | grep SEQ_FIRSTRUN_ADMINPASSWORDHASH
   ```

3. Check Seq logs:
   ```bash
   docker logs starterkit-seq
   ```

### Forgot Seq password

1. Stop the container:
   ```bash
   docker compose stop seq
   ```

2. Remove the Seq data volume (WARNING: This deletes all logs):
   ```bash
   docker volume rm starterkit_seq_data
   ```

3. Generate a new password hash and update `.env.prod`

4. Restart:
   ```bash
   docker compose up -d seq
   ```

### Can't access Seq through nginx

1. Verify Seq is running:
   ```bash
   docker ps | grep seq
   ```

2. Check nginx allows your IP:
   ```nginx
   # In /etc/nginx/sites-available/app-production.conf
   location /seq/ {
       allow YOUR_IP;  # Make sure this matches your IP
       deny all;
   }
   ```

3. Reload nginx:
   ```bash
   sudo nginx -t
   sudo systemctl reload nginx
   ```

## References

- [Seq Authentication Documentation](https://docs.datalust.co/docs/authentication)
- [Seq Docker Configuration](https://hub.docker.com/r/datalust/seq)
- Seq config: `docker-compose.yml:203-230`
- Nginx config: `deployment/nginx/conf.d/app-production.conf:177-188`

## Default Credentials

After setup:
- **Username:** `admin`
- **Password:** (stored in `secrets/seq_admin_password.txt`)
- **URL:** `http://localhost:5341` (internal) or `https://api.example.com/seq/` (via nginx)

## Important Notes

- First-run password hash (`SEQ_FIRSTRUN_ADMINPASSWORDHASH`) only applies on initial startup
- If Seq data already exists, you'll need to reset the password through the UI or delete the volume
- Always use HTTPS when accessing Seq remotely (via nginx reverse proxy)
- Consider rotating the Seq admin password periodically
