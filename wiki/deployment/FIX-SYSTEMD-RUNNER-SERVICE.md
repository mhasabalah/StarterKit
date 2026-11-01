# Fix Your Systemd Runner Service

> **🗄️ ARCHIVED**: This guide is for the old **Docker-based runner** architecture.
> **✅ MIGRATED**: System migrated to **host-based runner** on 2026-02-02.
> **See instead**: `VPS-RUNNER-SETUP.md` for current host-based setup.

---

**Historical Reference Only - Docker-Based Runner (DEPRECATED)**

## Problem

Your current systemd service doesn't mount `/opt/starterkit` into the runner container, so the workflow can't access deployment files at `/workspace`.

## Solution

Update your systemd service to include the volume mount.

---

## Step 1: Stop the Current Runner

```bash
sudo systemctl stop ephemeral-github-actions-runner
```

## Step 2: Create `/opt/starterkit` Directory

```bash
# Create the directory if it doesn't exist
sudo mkdir -p /opt/starterkit

# Set proper permissions
sudo chown -R $USER:$USER /opt/starterkit
sudo chmod 755 /opt/starterkit
```

## Step 3: Update the Systemd Service

```bash
sudo nano /etc/systemd/system/ephemeral-github-actions-runner.service
```

**Replace the `ExecStart` line with this updated version:**

```ini
[Unit]
Description=Ephemeral GitHub Actions Runner Container
After=docker.service
Requires=docker.service

[Service]
TimeoutStartSec=0
Restart=always
ExecStartPre=-/usr/bin/docker stop %N
ExecStartPre=-/usr/bin/docker rm %N
ExecStartPre=-/usr/bin/docker pull myoung34/github-runner:latest
ExecStart=/usr/bin/docker run --rm \
                              --env-file /etc/ephemeral-github-actions-runner.env \
                              -e RUNNER_NAME=%H \
                              -v /var/run/docker.sock:/var/run/docker.sock \
                              -v /opt/starterkit:/workspace:rw \
                              -v /tmp/runner:/tmp/runner \
                              --name %N \
                              myoung34/github-runner:latest

[Install]
WantedBy=multi-user.target
```

**What changed:**
- Added: `-v /opt/starterkit:/workspace:rw \` (mounts deployment directory)
- Added: `-v /tmp/runner:/tmp/runner \` (for GitHub Actions workspace consistency)

## Step 4: Update Environment File

```bash
sudo nano /etc/ephemeral-github-actions-runner.env
```

**Update to include the correct labels:**

```bash
# Runner configuration
RUNNER_SCOPE=repo
REPO_URL=https://github.com/your-org/StarterKit
LABELS=self-hosted,deploy-only,vps,production
ACCESS_TOKEN=YOUR_PERSONAL_ACCESS_TOKEN
RUNNER_WORKDIR=/tmp/runner/work
DISABLE_AUTO_UPDATE=1
EPHEMERAL=1
```

**Important:** Make sure `LABELS` includes `deploy-only` to match your workflow's `runs-on: [self-hosted, deploy-only]`.

## Step 5: Reload and Restart

```bash
# Reload systemd configuration
sudo systemctl daemon-reload

# Start the runner with new configuration
sudo systemctl start ephemeral-github-actions-runner

# Check status
sudo systemctl status ephemeral-github-actions-runner
```

## Step 6: Verify the Mount Works

```bash
# Create a test file on the host
echo "Test from host" > /opt/starterkit/test.txt

# Check if runner can see it
sudo docker exec ephemeral-github-actions-runner.service ls -la /workspace

# Read the file from inside the runner
sudo docker exec ephemeral-github-actions-runner.service cat /workspace/test.txt
# Should output: Test from host
```

## Step 7: Add Your Deployment Files

Now put your deployment files in `/opt/starterkit`:

```bash
# Copy your environment file
cp .env.prod /opt/starterkit/.env

# Copy your secrets folder
cp -r secrets/ /opt/starterkit/secrets/

# Copy docker-compose.yml
cp docker-compose.yml /opt/starterkit/

# Set secure permissions
chmod 600 /opt/starterkit/.env
chmod -R 600 /opt/starterkit/secrets/*

# Verify
ls -la /opt/starterkit/
```

You should see:
```
drwxr-xr-x  3 user user 4096 ... .
drwxr-xr-x 23 user user 4096 ... ..
-rw-------  1 user user 1234 ... .env
-rw-r--r--  1 user user 5678 ... docker-compose.yml
drwx------  2 user user 4096 ... secrets
```

## Step 8: Test the Workflow

Trigger your GitHub Actions workflow and it should now work!

The workflow will:
1. Checkout to `/tmp/runner/work/StarterKit/StarterKit`
2. Copy files to `/workspace` (which is `/opt/starterkit` on the host)
3. Run deployment from `/workspace`

---

## Troubleshooting

### Runner won't start

```bash
# Check logs
journalctl -u ephemeral-github-actions-runner.service -n 50

# Check for errors
sudo docker logs ephemeral-github-actions-runner.service
```

### Mount not working

```bash
# Verify container is running
sudo docker ps | grep runner

# Check mounts
sudo docker inspect ephemeral-github-actions-runner.service | grep -A 10 Mounts

# Should show both:
# - /var/run/docker.sock
# - /opt/starterkit:/workspace
```

### Permission issues

```bash
# Fix /opt/starterkit permissions
sudo chown -R $(whoami):$(whoami) /opt/starterkit
sudo chmod 755 /opt/starterkit

# Fix secrets permissions
sudo chmod -R 600 /opt/starterkit/secrets/*
```

---

## Complete Service File Reference

Here's the complete, corrected systemd service file:

```ini
[Unit]
Description=Ephemeral GitHub Actions Runner Container
After=docker.service
Requires=docker.service

[Service]
TimeoutStartSec=0
Restart=always
ExecStartPre=-/usr/bin/docker stop %N
ExecStartPre=-/usr/bin/docker rm %N
ExecStartPre=-/usr/bin/docker pull myoung34/github-runner:latest
ExecStart=/usr/bin/docker run --rm \
                              --env-file /etc/ephemeral-github-actions-runner.env \
                              -e RUNNER_NAME=%H \
                              -v /var/run/docker.sock:/var/run/docker.sock \
                              -v /opt/starterkit:/workspace:rw \
                              -v /tmp/runner:/tmp/runner \
                              --name %N \
                              myoung34/github-runner:latest

[Install]
WantedBy=multi-user.target
```

Save this as `/etc/systemd/system/ephemeral-github-actions-runner.service`

---

## Quick Commands Reference

```bash
# Stop runner
sudo systemctl stop ephemeral-github-actions-runner

# Start runner
sudo systemctl start ephemeral-github-actions-runner

# Restart runner
sudo systemctl restart ephemeral-github-actions-runner

# View status
sudo systemctl status ephemeral-github-actions-runner

# View logs (live)
journalctl -f -u ephemeral-github-actions-runner.service

# View logs (last 100 lines)
journalctl -u ephemeral-github-actions-runner.service -n 100

# Reload systemd after editing service file
sudo systemctl daemon-reload
```

---

**After making these changes, your workflow should work correctly!** ✅
