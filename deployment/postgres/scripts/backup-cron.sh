#!/bin/bash
# Crontab configuration for pgBackRest backups
#
# Installation:
# 1. Make this file executable: chmod +x deployment/postgres/scripts/backup-cron.sh
# 2. Install cron jobs: crontab -e
# 3. Add the lines below (adjust paths as needed):

# Full backup - Weekly (Sunday 2 AM)
# 0 2 * * 0 cd /opt/starterkit && /opt/starterkit/deployment/postgres/scripts/backup-full.sh >> /var/log/pgbackrest/cron.log 2>&1

# Differential backup - Daily (2 AM, Monday-Saturday)
# 0 2 * * 1-6 cd /opt/starterkit && /opt/starterkit/deployment/postgres/scripts/backup-diff.sh >> /var/log/pgbackrest/cron.log 2>&1

# Incremental backup - Every 4 hours
# 0 */4 * * * cd /opt/starterkit && /opt/starterkit/deployment/postgres/scripts/backup-incr.sh >> /var/log/pgbackrest/cron.log 2>&1

# Verify backups - Daily (3 AM)
# 0 3 * * * cd /opt/starterkit && docker compose --env-file .env.prod exec -T pgbackrest pgbackrest --stanza=starterkit verify >> /var/log/pgbackrest/cron.log 2>&1

# Expire old backups (retention policy) - Daily (4 AM)
# 0 4 * * * cd /opt/starterkit && docker compose --env-file .env.prod exec -T pgbackrest pgbackrest --stanza=starterkit expire >> /var/log/pgbackrest/cron.log 2>&1

# Health check - Every 30 minutes
# */30 * * * * cd /opt/starterkit && /opt/starterkit/deployment/postgres/scripts/backup-health.sh >> /var/log/pgbackrest/health.log 2>&1

echo "This file contains crontab configuration instructions."
echo "Open it in a text editor to see the cron job examples."
echo ""
echo "To install cron jobs:"
echo "1. crontab -e"
echo "2. Copy the job lines from this file (without the # prefix)"
echo "3. Adjust paths if needed"
echo "4. Save and exit"
