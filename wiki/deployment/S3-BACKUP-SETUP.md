# pgBackRest - PostgreSQL Backup & Restore

Enterprise-grade PostgreSQL backup solution using the official pgBackRest Docker image.

## ✅ What You're Getting

- ✅ **Full, Incremental & Differential backups** (77% less storage)
- ✅ **Point-in-Time Recovery (PITR)** - Restore to any moment
- ✅ **Official Docker image** - No custom Dockerfile needed
- ✅ **Battle-tested** - Industry standard, used by thousands
- ✅ **S3 integration** - Native S3 support
- ✅ **Parallel operations** - Multi-threaded backup/restore
- ✅ **Simple configuration** - Environment variables

## 📋 Prerequisites

- AWS S3 bucket (`starterkit-demo` already configured)
- AWS credentials configured in `.env.prod`
- Docker Compose
- 10 minutes

## 🚀 Quick Start

### Step 1: Start pgBackRest Service

```bash
cd /opt/starterkit

# Start the service
docker compose --env-file .env.prod up -d pgbackrest

# Check status
docker compose --env-file .env.prod ps pgbackrest
```

### Step 2: Initialize pgBackRest

```bash
# Run initialization script
./deployment/postgres/scripts/init-pgbackrest.sh
```

Expected output:
```
==================================================
Initializing pgBackRest
==================================================
Creating stanza...
Verifying configuration...
Performing initial full backup...
Backup information:
stanza: starterkit
    status: ok
    ...
==================================================
pgBackRest initialized successfully!
==================================================
```

### Step 3: Verify Backup in S3

```bash
aws s3 ls s3://starterkit-demo/pgbackrest/ --recursive --human-readable
```

Should show:
```
2026-02-06 02:05:30  487.3 MiB pgbackrest/backup/starterkit/...
2026-02-06 02:00:01    5.2 KiB pgbackrest/archive/starterkit/...
```

## ✅ Done!

Your database now has:
- Automated backups (configured via cron or GitHub Actions)
- Point-in-Time Recovery capability
- Incremental backups (faster, cheaper)
- S3 off-site storage

## 📊 Backup Strategy

### Recommended Schedule

**Full Backup:** Weekly (Sunday 2 AM)
- Complete database backup
- ~5-8 minutes for 5GB database
- Base for incremental/differential

**Differential Backup:** Daily (Monday-Saturday 2 AM)
- Changes since last full backup
- ~1-2 minutes
- Faster restore than incremental chain

**Incremental Backup:** Every 4 hours (optional)
- Changes since last backup
- ~30-60 seconds
- Most storage efficient

**Retention:**
- 4 full backups (4 weeks)
- 8 differential backups
- ≈1 month coverage with PITR

### Setup Automated Backups

**Option A: Cron (Traditional)**

```bash
# Edit crontab
crontab -e

# Add these lines:
# Full backup - Sunday 2 AM
0 2 * * 0 cd /opt/starterkit && ./deployment/postgres/scripts/backup-full.sh

# Differential - Daily (Mon-Sat) 2 AM
0 2 * * 1-6 cd /opt/starterkit && ./deployment/postgres/scripts/backup-diff.sh

# Incremental - Every 4 hours
0 */4 * * * cd /opt/starterkit && ./deployment/postgres/scripts/backup-incr.sh

# Health check - Every 30 minutes
*/30 * * * * cd /opt/starterkit && ./deployment/postgres/scripts/backup-health.sh
```

**Option B: GitHub Actions (Recommended)**

Already configured in `.github/workflows/database-backup.yml`:
- Automatic schedule
- Manual trigger via UI
- Built-in notifications
- No server cron needed

## 🔧 Manual Operations

### Backup Commands

```bash
cd /opt/starterkit

# Full backup
./deployment/postgres/scripts/backup-full.sh

# Differential backup
./deployment/postgres/scripts/backup-diff.sh

# Incremental backup
./deployment/postgres/scripts/backup-incr.sh

# View backup information
./deployment/postgres/scripts/backup-info.sh

# Health check
./deployment/postgres/scripts/backup-health.sh
```

### Direct Commands

```bash
# Full backup
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --type=full backup

# View info
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit info

# Verify backups
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit check
```

## 🔄 Restore Operations

### Restore Latest Backup

```bash
cd /opt/starterkit

# Run restore script
./deployment/postgres/scripts/restore.sh latest
```

⚠️ **WARNING:** This will OVERWRITE your database! The script will ask for confirmation.

### Point-in-Time Recovery (PITR)

Restore to a specific point in time:

```bash
# Example: Restore to before accidental DELETE at 2:30 PM
./deployment/postgres/scripts/restore.sh time "2026-02-06 14:30:00"
```

### Restore to Last Consistent State

```bash
./deployment/postgres/scripts/restore.sh immediate
```

## 🔍 Monitoring

### Check Backup Status

```bash
# Using script
./deployment/postgres/scripts/backup-info.sh

# Direct command
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit info

# JSON output
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --output=json info
```

### Health Check

```bash
# Run health check
./deployment/postgres/scripts/backup-health.sh

# Expected output:
# Last backup: 2 hours ago
# ✓ Backup health check passed
# ✓ Last backup: 2 hours ago
```

### View Logs

```bash
# Docker logs
docker compose --env-file .env.prod logs pgbackrest

# pgBackRest logs (inside container)
docker compose --env-file .env.prod exec pgbackrest \
  cat /var/log/pgbackrest/starterkit-backup.log
```

### S3 Storage Usage

```bash
# Total storage
aws s3 ls s3://starterkit-demo/pgbackrest/ --recursive --summarize --human-readable

# Backup directory
aws s3 ls s3://starterkit-demo/pgbackrest/backup/ --recursive --summarize --human-readable

# WAL archive
aws s3 ls s3://starterkit-demo/pgbackrest/archive/ --recursive --summarize --human-readable
```

## 📊 Performance & Storage

### Expected Performance (5GB Database)

| Operation | Time | Details |
|-----------|------|---------|
| Full Backup | 3-5 min | Complete database |
| Differential Backup | 1-2 min | Changes since last full |
| Incremental Backup | 30-60 sec | Changes since last backup |
| Full Restore | 3-5 min | Delta restore |
| PITR Restore | 4-6 min | Replay WAL logs |

### Storage Efficiency

**Monthly Storage for 5GB Database:**

**Old Approach (Daily Full Backups):**
```
30 days × 500MB = 15GB/month
Cost: $0.19/month
```

**pgBackRest (Full + Differential):**
```
4 full backups:    4 × 500MB = 2.0GB
24 differential:  24 × 50MB  = 1.2GB
WAL archives:                 = 0.3GB
Total:                        = 3.5GB/month
Cost: $0.05/month
Savings: 77% less storage, 74% cheaper!
```

## 💰 Cost Estimate

**S3 Standard-IA Storage:**
- Storage: 3.5GB × $0.0125/GB = $0.044/month
- PUT requests: ~900/month × $0.01/1000 = $0.009/month
- GET requests: ~10/month × $0.001/1000 = $0.00001/month
- **Total: ~$0.05/month or $0.60/year**

## 🚨 Troubleshooting

### Service Won't Start

```bash
# Check logs
docker compose --env-file .env.prod logs pgbackrest

# Check PostgreSQL connection
docker compose --env-file .env.prod exec pgbackrest \
  pg_isready -h postgres -U postgres

# Check S3 access
docker compose --env-file .env.prod exec pgbackrest \
  aws s3 ls s3://starterkit-demo/

# Verify environment variables
docker compose --env-file .env.prod exec pgbackrest env | grep PGBACKREST
```

### Stanza Creation Failed

```bash
# Delete and recreate stanza
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit stanza-delete --force

docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit stanza-create

# Verify
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit check
```

### Backup Failed

```bash
# View error logs
docker compose --env-file .env.prod logs pgbackrest | grep ERROR

# Check stanza status
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit check

# Test S3 connectivity
docker compose --env-file .env.prod exec pgbackrest \
  aws s3 ls s3://starterkit-demo/pgbackrest/

# Manually run backup
./deployment/postgres/scripts/backup-full.sh
```

### WAL Archiving Issues

```bash
# Check PostgreSQL archive logs
docker compose --env-file .env.prod logs postgres | grep archive

# Check archive directory in S3
aws s3 ls s3://starterkit-demo/pgbackrest/archive/starterkit/ --recursive

# Force WAL switch
docker compose --env-file .env.prod exec postgres \
  psql -U postgres -c "SELECT pg_switch_wal();"

# Wait 30 seconds and check S3 for new WAL file
```

## 🔐 Security

### IAM Policy (Least Privilege)

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:ListBucket",
        "s3:GetBucketLocation"
      ],
      "Resource": "arn:aws:s3:::starterkit-demo"
    },
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:DeleteObject"
      ],
      "Resource": "arn:aws:s3:::starterkit-demo/pgbackrest/*"
    }
  ]
}
```

### Enable Encryption (Optional)

1. Uncomment in `docker-compose.yml`:
```yaml
- PGBACKREST_REPO1_CIPHER_TYPE=aes-256-cbc
- PGBACKREST_REPO1_CIPHER_PASS=${BACKUP_ENCRYPTION_KEY}
```

2. Add to `.env.prod`:
```bash
BACKUP_ENCRYPTION_KEY=your-secure-passphrase-here
```

3. Restart service:
```bash
docker compose --env-file .env.prod restart pgbackrest
```

### Use IAM Instance Role (Recommended)

Instead of access keys:

1. Create IAM role with S3 policy (above)
2. Attach role to EC2 instance
3. Remove from `.env.prod`:
```bash
# AWS_ACCESS_KEY_ID=
# AWS_SECRET_ACCESS_KEY=
```

## 📚 Advanced Configuration

### Custom Retention Policy

Edit `deployment/postgres/config/pgbackrest.conf`:

```ini
# Keep 6 full backups (6 weeks)
repo1-retention-full=6

# Keep 12 differential backups
repo1-retention-diff=12
```

Restart service:
```bash
docker compose --env-file .env.prod restart pgbackrest
```

### Change Compression

Edit `deployment/postgres/config/pgbackrest.conf`:

```ini
# Options: none, gz, bz2, lz4, zstd
compress-type=zstd  # Better compression than lz4

# Compression level (higher = better compression, slower)
compress-level=6    # Default is 3
```

### Parallel Processing

Edit `deployment/postgres/config/pgbackrest.conf`:

```ini
# Increase for faster backups (if you have CPU/bandwidth)
process-max=8  # Default is 4
```

## ✅ Success Checklist

- [ ] pgBackRest service running
- [ ] Stanza created successfully
- [ ] Initial full backup completed
- [ ] Backup visible in S3 (`pgbackrest/backup/`)
- [ ] WAL archives in S3 (`pgbackrest/archive/`)
- [ ] Health check passing
- [ ] Automated backups scheduled (cron or GitHub Actions)
- [ ] Restore tested in staging (recommended)

## 🎉 Success!

You now have enterprise-grade PostgreSQL backups with:

- ✅ **Incremental backups** (77% less storage)
- ✅ **Point-in-Time Recovery** (restore to any moment)
- ✅ **Official Docker image** (well-maintained)
- ✅ **Simple configuration** (environment variables)
- ✅ **Automated backups** (cron or GitHub Actions)
- ✅ **S3 storage** (99.999999999% durability)
- ✅ **Less than $1/year** cost

Sleep better knowing your data is protected! 😴

---

**Documentation:**
- Official: https://pgbackrest.org/
- User Guide: https://pgbackrest.org/user-guide.html
- Configuration: https://pgbackrest.org/configuration.html
- Setup Guide: `PGBACKREST-SETUP.md`
