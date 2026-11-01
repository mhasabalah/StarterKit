# ⚙️ GitHub Actions Secrets Setup Guide

Quick guide to configure GitHub secrets for CI/CD.

---

## 🔐 Required Secrets

### 1. EC2_HOST
**Description**: Your EC2 instance public IP address or domain  
**Example**: `54.123.45.67` or `api.yourdomain.com`

**How to get**:
```bash
# From AWS Console
AWS Console → EC2 → Instances → Select your instance → Public IPv4 address

# Or from SSH session
curl -s http://checkip.amazonaws.com
```

**Add to GitHub**:
1. Go to repository **Settings**
2. **Secrets and variables** → **Actions**
3. **New repository secret**
4. Name: `EC2_HOST`
5. Value: Your EC2 IP
6. **Add secret**

---

### 2. EC2_USER
**Description**: SSH username for your EC2 instance  
**Default**: `ubuntu` (for Ubuntu AMI)

**How to determine**:
| AMI | Username |
|-----|----------|
| Ubuntu | `ubuntu` |
| Amazon Linux 2 | `ec2-user` |
| RHEL | `ec2-user` |
| Debian | `admin` |

**Add to GitHub**:
1. Repository **Settings**
2. **Secrets and variables** → **Actions**
3. **New repository secret**
4. Name: `EC2_USER`
5. Value: `ubuntu`
6. **Add secret**

---

### 3. EC2_SSH_PRIVATE_KEY
**Description**: SSH private key to connect to EC2  
**Format**: PEM format (the `.pem` file you downloaded from AWS)

**How to get**:
```bash
# On Windows
notepad your-ec2-key.pem

# On Mac/Linux
cat your-ec2-key.pem
```

**Format should be**:
```
-----BEGIN RSA PRIVATE KEY-----
MIIEowIBAAKCAQEAx...
... (many lines)
...WqB1oQ==
-----END RSA PRIVATE KEY-----
```

**Important**:
- ✅ Include the BEGIN and END lines
- ✅ Copy entire content including all newlines
- ✅ No extra spaces or characters
- ❌ Don't modify the key
- ❌ Don't share this key

**Add to GitHub**:
1. Repository **Settings**
2. **Secrets and variables** → **Actions**
3. **New repository secret**
4. Name: `EC2_SSH_PRIVATE_KEY`
5. Value: Paste entire PEM file content
6. **Add secret**

---

### 4. PRODUCTION_URL
**Description**: Full URL to your production API  
**Example**: `http://54.123.45.67` or `https://api.yourdomain.com`

**Format**:
```
# If using IP (no domain yet)
http://54.123.45.67

# If using domain with SSL
https://api.yourdomain.com

# If using domain without SSL
http://api.yourdomain.com
```

**Add to GitHub**:
1. Repository **Settings**
2. **Secrets and variables** → **Actions**
3. **New repository secret**
4. Name: `PRODUCTION_URL`
5. Value: Your production URL
6. **Add secret**

---

## 🔍 Verification Checklist

After adding all secrets, verify:

```bash
# Test SSH connection locally first
ssh -i your-ec2-key.pem ubuntu@YOUR_EC2_IP

# Test API access
curl http://YOUR_EC2_IP/health

# Should return: {"status": "Healthy", ...}
```

### Verify Secrets in GitHub

1. Go to **Settings** → **Secrets and variables** → **Actions**
2. You should see:
   - ✅ `EC2_HOST`
   - ✅ `EC2_USER`
   - ✅ `EC2_SSH_PRIVATE_KEY`
   - ✅ `PRODUCTION_URL`

---

## 🚀 Optional Secrets (Enhancements)

### Slack Notifications

**SLACK_WEBHOOK_URL**

1. Go to Slack → **Apps** → **Incoming Webhooks**
2. Create new webhook
3. Copy webhook URL
4. Add as GitHub secret

### Discord Notifications

**DISCORD_WEBHOOK_URL**

1. Discord Server → **Server Settings** → **Integrations**
2. **Create Webhook**
3. Copy webhook URL
4. Add as GitHub secret

### Docker Hub (Alternative to GHCR)

**DOCKERHUB_USERNAME** and **DOCKERHUB_TOKEN**

1. Docker Hub → **Account Settings** → **Security**
2. **New Access Token**
3. Copy token
4. Add both username and token as secrets

---

## 🧪 Test Your Setup

### Method 1: Manual Workflow Test

1. Go to **Actions** tab
2. Select **Manual Deploy to Production**
3. Click **Run workflow**
4. Watch the workflow run
5. If it fails on SSH, check your secrets

### Method 2: Push Test Commit

```bash
# Create test branch
git checkout -b test-secrets

# Add a comment to a file
echo "# Test" >> README.md

# Commit and push
git add .
git commit -m "test: verify CI/CD secrets"
git push origin test-secrets

# Create PR to main
# Watch Actions tab
```

### Method 3: Local SSH Test

```bash
# Test the exact same connection GitHub Actions will use
ssh -i your-ec2-key.pem ubuntu@YOUR_EC2_IP "echo 'SSH works!'"

# Should print: SSH works!
```

---

## ❌ Common Issues

### SSH Permission Denied

**Problem**: `Permission denied (publickey)`

**Solutions**:
```bash
# 1. Check key permissions locally
chmod 400 your-ec2-key.pem

# 2. Verify key format
cat your-ec2-key.pem
# Should start with: -----BEGIN RSA PRIVATE KEY-----

# 3. Test SSH locally
ssh -i your-ec2-key.pem ubuntu@YOUR_EC2_IP

# 4. If local SSH works, the problem is in GitHub secret
# Re-copy the entire key including BEGIN/END lines
```

### Wrong EC2_USER

**Problem**: `ubuntu@host: Permission denied`

**Solution**:
```bash
# Try different usernames based on AMI
ssh -i your-ec2-key.pem ec2-user@YOUR_EC2_IP  # Amazon Linux
ssh -i your-ec2-key.pem admin@YOUR_EC2_IP      # Debian
```

### EC2_HOST Not Reachable

**Problem**: `Connection timed out`

**Solutions**:
1. Check EC2 Security Group allows port 22 from GitHub IPs
2. Verify EC2 instance is running
3. Check if public IP changed (use Elastic IP)

---

## 🔒 Security Best Practices

### Secrets Management

- [ ] Never commit secrets to Git
- [ ] Rotate SSH keys every 90 days
- [ ] Use separate keys for CI/CD vs manual access
- [ ] Review GitHub audit log regularly
- [ ] Enable 2FA on GitHub account
- [ ] Limit secret access to necessary workflows only

### EC2 Security

- [ ] Use Elastic IP (prevents IP changes)
- [ ] Security Group: Allow SSH only from GitHub IPs or your IPs
- [ ] Keep SSH keys in secure password manager
- [ ] Enable AWS CloudTrail for audit logging
- [ ] Regular security updates on EC2

---

## 📝 Quick Reference

### Add Secret Command (gh CLI)

If you have GitHub CLI installed:

```bash
# Install gh CLI first: https://cli.github.com/

# Add secrets via command line
gh secret set EC2_HOST
# Paste value when prompted

gh secret set EC2_USER
# Paste value

gh secret set EC2_SSH_PRIVATE_KEY < your-ec2-key.pem

gh secret set PRODUCTION_URL
# Paste value
```

### List Current Secrets

```bash
# Using gh CLI
gh secret list

# Output:
# EC2_HOST                    Updated 2024-01-22
# EC2_USER                    Updated 2024-01-22
# EC2_SSH_PRIVATE_KEY         Updated 2024-01-22
# PRODUCTION_URL              Updated 2024-01-22
```

---

## ✅ Setup Complete Checklist

Before running your first workflow:

- [ ] All 4 required secrets added
- [ ] SSH connection tested locally
- [ ] API health endpoint accessible
- [ ] EC2 instance has latest deployment code
- [ ] Docker and Nginx configured on EC2
- [ ] Firewall allows GitHub Actions IPs (or all for testing)
- [ ] `.github/workflows` files committed to repository
- [ ] Main deployment guide completed (README.Production.md)

---

## 🎉 Ready to Deploy!

Once all secrets are configured:

1. Push to `main` branch
2. Go to **Actions** tab
3. Watch your automated deployment! 🚀

---

## 📞 Need Help?

**SSH Issues**: Test local connection first  
**Secret Format**: Ensure no extra spaces or modifications  
**Workflow Fails**: Check Actions logs for specific error  
**EC2 Access**: Verify Security Group and instance status  

**Documentation**: See README.CICD.md for complete CI/CD guide
