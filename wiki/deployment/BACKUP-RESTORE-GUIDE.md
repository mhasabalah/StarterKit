# pgBackRest Backup & Restore Guide

Quick reference for backup and restore operations.

## ✅ Tested Backup Types

All three backup types are working and tested:

| Type | Command | Size | Time | Use Case |
|------|---------|------|------|----------|
| **Full** | `--type=full` | 4.9MB | 62s | Weekly, baseline |
| **Differential** | `--type=diff` | 443B | 8s | Daily, vs last full |
| **Incremental** | `--type=incr` | 444B | 9s | Hourly, vs last backup |

---

## 📦 Backup Commands

**IMPORTANT:** Always use `-f docker-compose.yml` to exclude dev overrides!

### Manual Backups

```bash
cd /opt/starterkit

# Full backup (weekly)
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --type=full --no-archive-check backup

# Differential backup (daily)
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --type=diff --no-archive-check backup

# Incremental backup (hourly)
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --type=incr --no-archive-check backup
```

### View Backup Information

```bash
# List all backups
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit info

# Detailed JSON output
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --output=json info
```

### Using Helper Scripts

```bash
# Full backup
./deployment/postgres/scripts/backup-full.sh

# Differential backup
./deployment/postgres/scripts/backup-diff.sh

# Incremental backup
./deployment/postgres/scripts/backup-incr.sh

# View backup info
./deployment/postgres/scripts/backup-info.sh

# Health check
./deployment/postgres/scripts/backup-health.sh
```

---

## 🔄 Restore Commands

### Option 1: Using Restore Script (Recommended)

```bash
cd /opt/starterkit

# Restore latest backup
./deployment/postgres/scripts/restore.sh latest

# Restore specific backup set
./deployment/postgres/scripts/restore.sh time "2026-02-07 14:30:00"

# Restore to last consistent state
./deployment/postgres/scripts/restore.sh immediate
```

### Option 2: Manual Restore

**1. Stop services:**
```bash
docker compose -f docker-compose.yml --env-file .env.prod stop api postgres
```

**2. Clear data directory:**
```bash
docker compose -f docker-compose.yml --env-file .env.prod run --rm postgres \
  sh -c "rm -rf /var/lib/postgresql/data/pgdata/*"
```

**3. Restore:**
```bash
# Restore latest
docker compose -f docker-compose.yml --env-file .env.prod run --rm pgbackrest \
  pgbackrest --stanza=starterkit --delta restore

# Or restore specific backup
docker compose -f docker-compose.yml --env-file .env.prod run --rm pgbackrest \
  pgbackrest --stanza=starterkit --set=20260207-213946F --delta restore
```

**4. Start PostgreSQL:**
```bash
docker compose -f docker-compose.yml --env-file .env.prod start postgres
```

**5. Verify:**
```bash
docker compose -f docker-compose.yml --env-file .env.prod exec postgres \
  psql -U starterkit_user -d starterkit -c "\dt"
```

**6. Start application:**
```bash
docker compose -f docker-compose.yml --env-file .env.prod start api
```

---

## 📅 Automated Backup Schedule

### Cron Setup (Option A)

```bash
# Edit crontab
crontab -e

# Add these lines:
# Full backup - Sunday 2 AM
0 2 * * 0 cd /opt/starterkit && ./deployment/postgres/scripts/backup-full.sh >> /var/log/pgbackrest/cron.log 2>&1

# Differential - Monday-Saturday 2 AM
0 2 * * 1-6 cd /opt/starterkit && ./deployment/postgres/scripts/backup-diff.sh >> /var/log/pgbackrest/cron.log 2>&1

# Health check - Every 30 minutes
*/30 * * * * cd /opt/starterkit && ./deployment/postgres/scripts/backup-health.sh >> /var/log/pgbackrest/health.log 2>&1
```

### GitHub Actions (Option B)

Already configured in `.github/workflows/database-backup.yml`:
- **Full backup:** Sunday 2 AM
- **Differential:** Monday-Saturday 2 AM
- **Manual trigger:** Available in GitHub Actions UI

---

## 🔍 Monitoring & Verification

### Check Last Backup

```bash
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit info | head -20
```

### Verify S3 Storage

```bash
# List backups in S3
aws s3 ls s3://starterkit-demo/pgbackrest/ --recursive --human-readable

# Total storage used
aws s3 ls s3://starterkit-demo/pgbackrest/ --recursive --summarize --human-readable
```

### Check Logs

```bash
# Docker logs
docker compose -f docker-compose.yml --env-file .env.prod logs pgbackrest

# pgBackRest logs (inside container)
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  cat /var/log/pgbackrest/starterkit-backup.log
```

---

## 🚨 Troubleshooting

### Backup Fails

```bash
# Check configuration
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  cat /etc/pgbackrest/pgbackrest.conf

# Check PostgreSQL connection
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  psql -U starterkit_user -d starterkit -h /var/run/postgresql -c "SELECT version();"

# Check S3 access
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  sh -c "aws s3 ls s3://starterkit-demo/ --region us-east-1"
```

### Restore Fails

```bash
# Check available backups
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit info

# Verify backup integrity
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit verify
```

---

## 📊 Storage & Retention

**Current Settings:**
- **Full backups:** Keep 4 (4 weeks)
- **Differential:** Keep 8
- **Compression:** LZ4 level 3
- **Encryption:** Disabled (optional)

**Expected Monthly Storage (5GB database):**
- 4 full backups: ~2GB
- 24 differential: ~1.2GB
- **Total: ~3.5GB/month**
- **Cost: ~$0.05/month**

---

## 🔐 Security Notes

1. ✅ Secrets stored in Docker secrets (not in config file)
2. ✅ Config file generated at runtime from template
3. ✅ S3 connection uses TLS verification
4. ⚠️ Encryption at rest (optional) - currently disabled
5. ✅ PostgreSQL password via .pgpass file

---

## 🎯 Quick Reference

**Backup:**
```bash
./deployment/postgres/scripts/backup-full.sh
```

**Restore:**
```bash
./deployment/postgres/scripts/restore.sh latest
```

**Check Status:**
```bash
./deployment/postgres/scripts/backup-info.sh
```

**Health Check:**
```bash
./deployment/postgres/scripts/backup-health.sh
```

---

**Documentation:** https://pgbackrest.org/user-guide.html
