# Deploy pgBackRest to Production

Quick deployment guide for pgBackRest on production server.

## 📋 Prerequisites

- ✅ AWS S3 bucket: `starterkit-demo`
- ✅ AWS credentials in `.env.prod`
- ✅ PostgreSQL running in production
- ✅ SSH access to production server

## 🚀 Deployment Steps

### On Production Server (ubuntu@ip-172-31-12-216:/opt/starterkit)

```bash
# 1. SSH to production server
ssh ubuntu@ip-172-31-12-216

# 2. Navigate to project
cd /opt/starterkit

# 3. Pull latest code
git pull origin master

# 4. Start pgBackRest service
docker compose --env-file .env.prod up -d pgbackrest

# 5. Check service status
docker compose --env-file .env.prod ps pgbackrest

# Expected: STATUS shows "Up"
```

### Initialize pgBackRest

```bash
# 6. Run initialization script
./deployment/postgres/scripts/init-pgbackrest.sh

# This will:
# - Create stanza
# - Verify configuration
# - Perform initial full backup
# - Show backup info

# Expected output:
# ==================================================
# Initializing pgBackRest
# ==================================================
# Creating stanza...
# Verifying configuration...
# Performing initial full backup...
# Backup information:
# stanza: starterkit
#     status: ok
#     ...
# ==================================================
# pgBackRest initialized successfully!
# ==================================================
```

### Verify Backup in S3

```bash
# 7. Check backup in S3
aws s3 ls s3://starterkit-demo/pgbackrest/ --recursive --human-readable

# Expected: See backup files under pgbackrest/backup/starterkit/
# Expected: See WAL archives under pgbackrest/archive/starterkit/
```

### Setup Automated Backups

**Option A: Cron (Traditional)**

```bash
# 8. Edit crontab
crontab -e

# Add these lines:
# Full backup - Sunday 2 AM
0 2 * * 0 cd /opt/starterkit && ./deployment/postgres/scripts/backup-full.sh >> /var/log/pgbackrest/cron.log 2>&1

# Differential - Monday-Saturday 2 AM
0 2 * * 1-6 cd /opt/starterkit && ./deployment/postgres/scripts/backup-diff.sh >> /var/log/pgbackrest/cron.log 2>&1

# Health check - Every 30 minutes
*/30 * * * * cd /opt/starterkit && ./deployment/postgres/scripts/backup-health.sh >> /var/log/pgbackrest/health.log 2>&1

# Save and exit
```

**Option B: GitHub Actions (Recommended)**

Already configured in `.github/workflows/database-backup.yml`!

Just push code to GitHub and backups run automatically:
- **Full:** Sunday 2 AM
- **Differential:** Monday-Saturday 2 AM
- **Manual trigger:** Available in GitHub Actions UI

```bash
# 9. Push to GitHub (if not already done)
git push origin master
```

## ✅ Verification

### Check Backup Info

```bash
# View backup information
./deployment/postgres/scripts/backup-info.sh

# Or directly:
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit info
```

### Check Health

```bash
# Run health check
./deployment/postgres/scripts/backup-health.sh

# Expected: "✓ Backup health check passed"
```

### Check Logs

```bash
# View logs
docker compose --env-file .env.prod logs pgbackrest

# Check for errors
docker compose --env-file .env.prod logs pgbackrest | grep -i error
```

### Test Manual Backup (Optional)

```bash
# Trigger differential backup
./deployment/postgres/scripts/backup-diff.sh

# Should complete in 1-2 minutes
# Check S3 for new backup
```

## 📊 What Happens Now

### Backup Schedule

- **Sunday 2 AM:** Full backup (~500MB, 3-5 minutes)
- **Monday-Saturday 2 AM:** Differential backup (~50MB, 1-2 minutes)
- **Continuous:** WAL archiving (for Point-in-Time Recovery)

### Storage Growth

**Week 1:**
- 1 full backup: 500MB
- 6 differential backups: 300MB
- WAL archives: 50MB
- **Total: ~850MB**

**Month 1:**
- 4 full backups: 2GB
- ~24 differential: 1.2GB
- WAL archives: 300MB
- **Total: ~3.5GB**

### Costs

- **Storage:** ~$0.05/month
- **API Requests:** ~$0.01/month
- **Total:** **$0.06/month** ($0.72/year)

## 🔧 Common Operations

### Manual Backups

```bash
# Full backup
./deployment/postgres/scripts/backup-full.sh

# Differential backup
./deployment/postgres/scripts/backup-diff.sh

# Incremental backup (optional)
./deployment/postgres/scripts/backup-incr.sh
```

### View Backup Info

```bash
# Summary
./deployment/postgres/scripts/backup-info.sh

# Detailed info
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit info

# JSON output
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --output=json info
```

### Check S3 Storage

```bash
# Total storage used
aws s3 ls s3://starterkit-demo/pgbackrest/ --recursive --summarize --human-readable

# Backup directory
aws s3 ls s3://starterkit-demo/pgbackrest/backup/ --recursive --summarize --human-readable

# Archive directory
aws s3 ls s3://starterkit-demo/pgbackrest/archive/ --recursive --summarize --human-readable
```

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
```

### Stanza Creation Fails

```bash
# Check environment variables
docker compose --env-file .env.prod exec pgbackrest \
  env | grep -E "PGBACKREST|AWS"

# Check AWS credentials
docker compose --env-file .env.prod exec pgbackrest \
  aws sts get-caller-identity

# Try manual creation
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --log-level-console=debug stanza-create
```

### Backup Fails

```bash
# Check stanza
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit check

# Check disk space
docker compose --env-file .env.prod exec pgbackrest df -h

# View detailed logs
docker compose --env-file .env.prod logs pgbackrest
```

### No Backups in S3

```bash
# Verify S3 path
aws s3 ls s3://starterkit-demo/pgbackrest/

# Check AWS credentials
cat .env.prod | grep AWS

# Manually trigger backup
./deployment/postgres/scripts/backup-full.sh
```

## 📅 Monitoring Schedule

### Daily (First Week)

```bash
# Check backup completed
./deployment/postgres/scripts/backup-info.sh

# Check logs
docker compose --env-file .env.prod logs pgbackrest | tail -50

# Check S3 storage growth
aws s3 ls s3://starterkit-demo/pgbackrest/ --recursive --summarize --human-readable
```

### Weekly

```bash
# Verify backup integrity
docker compose --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit check

# Check retention (should see 4 full backups max)
./deployment/postgres/scripts/backup-info.sh

# Review logs for any warnings
docker compose --env-file .env.prod logs pgbackrest | grep -i warn
```

## 🔄 Restore Procedure

**⚠️ CRITICAL: Test restore in staging first!**

### Restore Latest Backup

```bash
# Stop application
docker compose --env-file .env.prod stop api

# Run restore script
./deployment/postgres/scripts/restore.sh latest

# Script will:
# 1. Ask for confirmation
# 2. Stop PostgreSQL
# 3. Clear data directory
# 4. Restore from latest backup
# 5. Start PostgreSQL
# 6. Verify data
```

### Point-in-Time Recovery

```bash
# Restore to specific time (e.g., before accidental DELETE)
./deployment/postgres/scripts/restore.sh time "2026-02-06 14:30:00"
```

## ✅ Success Checklist

- [ ] Service started successfully
- [ ] Stanza created
- [ ] Initial full backup completed (check logs)
- [ ] Backup visible in S3 (`s3://starterkit-demo/pgbackrest/backup/`)
- [ ] WAL archives in S3 (`s3://starterkit-demo/pgbackrest/archive/`)
- [ ] Automated backups scheduled (cron or GitHub Actions)
- [ ] Health check passing
- [ ] No errors in logs

## 📚 Documentation

- **Setup Guide:** `PGBACKREST-SETUP.md`
- **User Guide:** `wiki/deployment/S3-BACKUP-SETUP.md`
- **Scripts:** `deployment/postgres/scripts/`
- **Configuration:** Environment variables in `docker-compose.yml` (no config files!)
- **Architecture:** `ARCHITECTURE.md` (explains our approach)

## 🎉 Success!

Once deployed, you have:

- ✅ **Automated backups** (daily differential, weekly full)
- ✅ **Point-in-Time Recovery** (restore to any moment)
- ✅ **77% less storage** than daily full backups
- ✅ **S3 durability** (99.999999999%)
- ✅ **$0.72/year** cost
- ✅ **Industry-standard** solution

Your database is now protected! 🚀

---

**Next:** Monitor for the first week, verify backups complete successfully.
