        # Docker Volume Mount Explained Simply

## What Are We Talking About?

When we say `/workspace` is "mounted" to `/opt/starterkit`, we're using **Docker volume mounting**.

## The Concept (ELI5 - Explain Like I'm 5)

Imagine:
- Your VPS is a **house** 🏠
- `/opt/starterkit` is a **room** in that house 📁
- The Docker container is a **separate apartment** 🏢
- `/workspace` is a **magic window** 🪟 in the apartment that lets you see and touch everything in the house's room

**When you touch something through the window (`/workspace`), you're actually touching the real thing in the room (`/opt/starterkit`).**

## Real Example

### Step 1: Create a file on the VPS host

```bash
# On VPS, outside any container
echo "Hello from host" > /opt/starterkit/test.txt
```

This creates a REAL file on the VPS hard drive at `/opt/starterkit/test.txt`.

### Step 2: View it from inside the runner container

```bash
# Inside the runner container
docker exec starterkit-github-runner cat /workspace/test.txt
```

**Output:** `Hello from host`

### Step 3: Modify it from inside the container

```bash
# Inside the runner container
docker exec starterkit-github-runner bash -c "echo 'Modified from container' >> /workspace/test.txt"
```

### Step 4: Check it on the host again

```bash
# Back on VPS host
cat /opt/starterkit/test.txt
```

**Output:**
```
Hello from host
Modified from container
```

**They're the SAME file!** Changes in one place appear in the other.

## Visual Diagram

```
VPS HOST MACHINE
════════════════════════════════════════════════════════════════
    /
    ├── home/
    ├── var/
    └── opt/
        └── starterkit/          ← REAL DIRECTORY ON DISK
            ├── .env                   (physically stored here - contains passwords)
            └── docker-compose.yml     (physically stored here)

    ┌─────────────────────────────────────────────────┐
    │  DOCKER CONTAINER: starterkit-github-runner        │
    │  ═════════════════════════════════════════════  │
    │                                                  │
    │  /                                               │
    │  ├── usr/                                        │
    │  ├── tmp/                                        │
    │  └── workspace/  ← MOUNTED DIRECTORY             │
    │      │                                           │
    │      └─── (This is a LINK to /opt/starterkit) ─────┼──┐
    │                                                  │  │
    └──────────────────────────────────────────────────┘  │
                                                           │
            ┌──────────────────────────────────────────────┘
            │  MOUNT BINDING
            │  - /opt/starterkit (host) → /workspace (container)
            │  - Same files, different paths
            └─→ Any changes in one reflect in the other
```

## How the Mount is Created

In `docker-compose.runner.yml`, this line creates the mount:

```yaml
volumes:
  - /opt/starterkit:/workspace:rw
```

**Breaking it down:**
- `/opt/starterkit` = Source (host path - MUST exist before starting container)
- `/workspace` = Destination (container path - created automatically)
- `rw` = Read-Write permissions

**When the container starts:**
1. Docker checks if `/opt/starterkit` exists on the host ✅
2. If yes, it creates `/workspace` inside the container
3. It "binds" them together (like a symbolic link, but stronger)
4. Now both paths point to the SAME physical location

## Testing It Yourself

### Test 1: Verify the mount exists

```bash
# SSH into your VPS
ssh your-user@your-vps-ip

# Check host directory
ls -la /opt/starterkit

# Check container can see it
sudo docker exec starterkit-github-runner ls -la /workspace

# They should show THE SAME FILES
```

### Test 2: Create a file on host, see it in container

```bash
# On VPS host
echo "Test from host" > /opt/starterkit/mount-test.txt

# View from container
sudo docker exec starterkit-github-runner cat /workspace/mount-test.txt

# Should output: Test from host
```

### Test 3: Create a file in container, see it on host

```bash
# In container
sudo docker exec starterkit-github-runner bash -c "echo 'Test from container' > /workspace/container-test.txt"

# View on host
cat /opt/starterkit/container-test.txt

# Should output: Test from container
```

### Test 4: Verify they're the same

```bash
# Get file info from host
ls -li /opt/starterkit/test.txt

# Get file info from container
sudo docker exec starterkit-github-runner ls -li /workspace/test.txt

# They'll have the SAME inode number (proving they're the same file)
```

## Common Confusion

### ❓ "Is it copying files?"
**No!** It's not copying. It's like two doors to the same room.

### ❓ "Are there two versions of the file?"
**No!** There's only ONE file, stored at `/opt/starterkit/` on the host disk.

### ❓ "What if I delete from /workspace?"
**It deletes from /opt/starterkit too!** They're the same location.

### ❓ "What if /opt/starterkit doesn't exist on the host?"
**The container will fail to start** (or create an empty directory, depending on Docker version).

## Why This Matters for Your Workflow

Your GitHub Actions workflow runs INSIDE the runner container, so:

```bash
# When your workflow does this:
cd /workspace
docker compose up -d
```

**What's actually happening:**
1. Workflow changes to `/workspace` (inside container)
2. Runs `docker compose up -d`
3. Docker Compose reads `/workspace/docker-compose.yml`
4. **Which is actually** `/opt/starterkit/docker-compose.yml` on the host!
5. Docker Compose reads `/workspace/.env`
6. **Which is actually** `/opt/starterkit/.env` on the host!
7. All passwords are now in `.env` file (no separate secrets directory)

## Your Current Setup Summary

| Location | Type | Description |
|----------|------|-------------|
| `/opt/starterkit/` | **HOST PATH** | Real directory on VPS disk |
| `/workspace/` | **CONTAINER PATH** | Mounted view of `/opt/starterkit` |
| **SAME FILES** | ✅ | Any changes in one appear in the other |

## What You Need to Do

Since `/workspace` is just a window to `/opt/starterkit`, you need to ensure:

1. **`/opt/starterkit` exists on your VPS host:**
   ```bash
   mkdir -p /opt/starterkit
   ```

2. **Put your files in `/opt/starterkit` on the host:**
   ```bash
   # These files go on the VPS HOST
   cp .env.prod /opt/starterkit/.env
   cp -r secrets/ /opt/starterkit/secrets/
   cp docker-compose.yml /opt/starterkit/
   ```

3. **The runner will automatically see them at `/workspace`:**
   ```bash
   # No action needed - the mount does this automatically!
   ```

## Analogy Time

Think of it like **Google Drive on your computer**:
- The files are stored "in the cloud" (= `/opt/starterkit` on the host)
- You see them in a folder on your desktop (= `/workspace` in container)
- When you edit the file on your desktop, it updates in the cloud
- They're the SAME file, just accessed from two different places

Or like **a shortcut/symlink** (but stronger):
- Windows: Like a Shortcut that actually IS the file
- Linux: Like a symlink, but the file system treats it as the same location

## Quick Reference Commands

```bash
# Create the mount (already done via docker-compose)
# volumes:
#   - /opt/starterkit:/workspace:rw

# Check what's mounted in a container
docker inspect starterkit-github-runner | grep -A 10 Mounts

# Verify they're the same
ls -la /opt/starterkit           # On host
docker exec starterkit-github-runner ls -la /workspace  # In container
# Should be IDENTICAL

# Test the mount is working
echo "test" > /opt/starterkit/test.txt  # Write on host
docker exec starterkit-github-runner cat /workspace/test.txt  # Read from container
# Should output: test
```

---

**Key Takeaway:** `/workspace` and `/opt/starterkit` are **two paths to the same location**. Like two doors to one room. 🚪🚪➡️🏠
