# deployment/

These are server-side deployment assets. How to use them is documented in
**[wiki/deployment/](../wiki/deployment/README.md)**.

```
deployment/
├── deploy.sh                  # zero-downtime deploy (pull image, replace api, health check)
├── rollback.sh                # roll back to the previous image (CI: rollback.yml)
├── pre-deploy-backup.sh       # backup before deploying
├── secrets-setup.sh           # create missing secrets/*.txt (+ Data Protection certificate)
├── secure-seq.sh              # Seq authentication bootstrap
├── .env.production.example    # template for DEPLOY_DIR/.env.prod (placeholders only)
├── nginx/
│   ├── nginx.conf             # main Nginx config (workers, TLS, rate-limit zones)
│   └── conf.d/app-production.conf   # site config: server_name, upstream, admin IP allow-lists
├── postgres/
│   ├── config/pgbackrest.conf.template   # rendered at container start (no secrets in git)
│   └── scripts/               # pgBackRest backup/restore/health helpers
└── scripts/                   # backup-db.sh, restore-db.sh, health-check.sh
```

| Task | Guide |
|---|---|
| First deployment to a VPS | [Deploy-to-VPS.md](../wiki/deployment/Deploy-to-VPS.md) |
| CI/CD and GitHub setup | [CI-CD.md](../wiki/deployment/CI-CD.md) |
| Secrets | [Secrets.md](../wiki/deployment/Secrets.md) |
| Backups and restore | [Backups.md](../wiki/deployment/Backups.md) |
| Nginx | [Nginx.md](../wiki/deployment/Nginx.md) |

Normal deployments go through GitHub Actions (push to `master`, then approve the `production` environment). Run
these scripts by hand only for emergencies or first-time setup.
