# 🐛 PostgreSQL Authentication Fix

## ❌ Problem
```
MessageText: password authentication failed for user "starterkit_user"
```

## 🔍 Root Cause

PostgreSQL initializes the database with a password **only on first run**. If you:
1. Create a container with password `X`
2. Change `.env` to password `Y`
3. Restart containers

The password **does NOT change** because the database volume already exists!

## ✅ Solution

Remove the PostgreSQL volume to force re-initialization with the new password:

```bash
# Stop all services
docker-compose down

# Remove PostgreSQL volume
docker volume rm starterkit_postgres_data

# Start services (PostgreSQL will initialize with new password from .env)
docker-compose up -d
```

## 📝 Quick Fix Commands

```bash
# One-line fix
docker-compose down && docker volume rm starterkit_postgres_data && docker-compose up -d

# Or using Make
make down && docker volume rm starterkit_postgres_data && make up
```

## 🔐 Password Configuration

### In `.env` file:
```bash
POSTGRES_USER=starterkit_user
POSTGRES_PASSWORD=CHANGE_ME_STRONG_PASSWORD
```

### In `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "starterkit": "Host=localhost;Port=5432;Database=starterkit;Username=starterkit_user;Password=CHANGE_ME_STRONG_PASSWORD;Include Error Detail=true"
  }
}
```

### In `appsettings.Docker.json`:
```json
{
  "ConnectionStrings": {
    "starterkit": "Host=postgres;Port=5432;Database=starterkit;Username=starterkit_user;Password=CHANGE_ME_STRONG_PASSWORD;Include Error Detail=true"
  }
}
```

**Important**: All three must match!

## 🧪 Test Connection

```bash
# Test from host
docker exec starterkit-postgres psql -U starterkit_user -d starterkit -c "SELECT 1;"

# If successful, you'll see:
# ?column?
# ----------
#         1
# (1 row)
```

## 🚫 What NOT to Do

❌ **Don't** just restart containers - the old password persists in the volume

❌ **Don't** change only `.env` - you must remove the volume

❌ **Don't** change passwords in different places - keep them synced

## ✅ What TO Do

✅ **Always** remove volumes when changing passwords

✅ **Keep** all password references in sync (`.env`, appsettings)

✅ **Test** connection after changes

## 📊 Volume Management

### List volumes:
```bash
docker volume ls | grep starterkit
```

### Remove specific volume:
```bash
docker volume rm starterkit_postgres_data
```

### Remove all StarterKit volumes (DANGER - deletes all data!):
```bash
docker volume ls -q | grep starterkit | ForEach-Object { docker volume rm $_ }
```

## 🔄 Fresh Start Script

Create `reset-db.ps1`:
```powershell
Write-Host "Stopping containers..." -ForegroundColor Yellow
docker-compose down

Write-Host "Removing PostgreSQL volume..." -ForegroundColor Yellow  
docker volume rm starterkit_postgres_data

Write-Host "Starting containers with fresh database..." -ForegroundColor Green
docker-compose up -d

Write-Host "Waiting for services to be healthy..." -ForegroundColor Green
Start-Sleep -Seconds 15

Write-Host "Testing connection..." -ForegroundColor Green
docker exec starterkit-postgres psql -U starterkit_user -d starterkit -c "SELECT 1;"

Write-Host "`n✅ Database reset complete!" -ForegroundColor Green
```

## 🎯 Prevention

To avoid this issue in the future:

1. **Document passwords** in `.env.example` (without real values)
2. **Don't commit** `.env` to git
3. **Use consistent** password policies
4. **Test after** any configuration change
5. **Backup regularly** before making changes

## 📖 Related Issues

### HTTPS Certificate Error
If you see:
```
Could not find file '/https/aspnetapp.pfx'
```

**Fix**: Disable HTTPS in `docker-compose.override.yml`:
```yaml
environment:
  ASPNETCORE_URLS: "http://+:80"  # HTTP only, no HTTPS
```

### Port Already Allocated
If you see:
```
Bind for 0.0.0.0:5432 failed: port is already allocated
```

**Fix**: Stop all containers:
```bash
docker stop $(docker ps -aq)
docker-compose up -d
```

## ✅ Verification Checklist

After fixing, verify:

- [ ] PostgreSQL container is running: `docker ps | grep postgres`
- [ ] Connection works: `docker exec starterkit-postgres psql -U starterkit_user -d starterkit -c "SELECT 1;"`
- [ ] API can connect: Check `docker logs starterkit-api` for database errors
- [ ] Database has tables: `docker exec starterkit-postgres psql -U starterkit_user -d starterkit -c "\dt"`

## 🎉 Success!

If you can run this without errors:
```bash
docker exec starterkit-postgres psql -U starterkit_user -d starterkit -c "SELECT NOW();"
```

**Your PostgreSQL is working perfectly!** 🎊
