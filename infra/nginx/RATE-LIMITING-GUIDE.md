# ==============================================
# Nginx Configuration - Rate Limiting Strategy
# ==============================================
# Best practices for development vs production

## Current Configuration (BALANCED)

### Rate Limit Zones (nginx.conf)
```nginx
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=100r/s;
limit_req_zone $binary_remote_addr zone=auth_limit:10m rate=10r/s;
limit_req_zone $binary_remote_addr zone=general_limit:10m rate=50r/s;
limit_conn_zone $binary_remote_addr zone=conn_limit:10m;
```

### What This Means:
- **API Endpoints**: 100 requests/second per IP (burst: 50)
- **Auth Endpoints**: 10 requests/second per IP (burst: 5)
- **General**: 50 requests/second per IP (burst: 20)
- **Concurrent Connections**: 10 per IP

---

## Environment-Specific Recommendations

### 🔧 Development (Current Config is GOOD)
```
API:     100 req/s  ✅ Plenty for testing
Auth:    10 req/s   ✅ Allows rapid testing
General: 50 req/s   ✅ Good for dev tools
```

**Use Case:**
- Load testing: ✅ Can handle 100 requests/second
- Integration tests: ✅ Won't hit limits
- Manual testing: ✅ Very comfortable
- Multiple developers: ✅ No conflicts

---

### 🚀 Production - Light Traffic (Current Config)
```
API:     100 req/s  ✅ Good for small/medium apps
Auth:    10 req/s   ✅ Prevents brute force
General: 50 req/s   ✅ Adequate
```

**Suitable for:**
- Small to medium applications
- Internal APIs
- B2B applications
- Up to ~10,000 users

---

### 🏢 Production - High Traffic (If Needed)
```nginx
# For large-scale production
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=500r/s;
limit_req_zone $binary_remote_addr zone=auth_limit:10m rate=20r/s;
limit_req_zone $binary_remote_addr zone=general_limit:10m rate=200r/s;
```

**Suitable for:**
- High-traffic public APIs
- Consumer applications
- 100,000+ users
- Multiple API instances behind load balancer

---

### 🔒 Production - Strict Security (If Under Attack)
```nginx
# Lockdown mode
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=30r/s;
limit_req_zone $binary_remote_addr zone=auth_limit:10m rate=5r/m;  # 5 per MINUTE
limit_req_zone $binary_remote_addr zone=general_limit:10m rate=10r/s;
```

**Use when:**
- Under DDoS attack
- Detecting abuse patterns
- Temporarily locking down
- Cost reduction (API costs)

---

## How to Change Rate Limits

### Option 1: Edit nginx.conf (Current Method)
```bash
# Edit the file
vim infra/nginx/nginx.conf

# Lines 110-113
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=100r/s;

# Reload Nginx
make nginx-reload
```

### Option 2: Environment Variables (Recommended)
Update `nginx.conf` to use environment variables:

```nginx
# nginx.conf
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=${NGINX_API_RATE_LIMIT:-100r/s};
limit_req_zone $binary_remote_addr zone=auth_limit:10m rate=${NGINX_AUTH_RATE_LIMIT:-10r/s};
limit_req_zone $binary_remote_addr zone=general_limit:10m rate=${NGINX_GENERAL_RATE_LIMIT:-50r/s};
```

Then in `.env`:
```bash
NGINX_API_RATE_LIMIT=100r/s
NGINX_AUTH_RATE_LIMIT=10r/s
NGINX_GENERAL_RATE_LIMIT=50r/s
```

---

## Understanding Burst Values

### Current Configuration:
```nginx
# In app.conf
limit_req zone=api_limit burst=50 nodelay;
limit_req zone=auth_limit burst=5 nodelay;
limit_req zone=general_limit burst=20 nodelay;
```

### What is Burst?
- **Burst** = temporary spike allowance
- **nodelay** = process burst requests immediately (don't queue)

### Example:
```
Rate: 100 req/s, Burst: 50

Second 1: 150 requests arrive
- First 100: Processed (within rate)
- Next 50:  Processed (burst)
- Any more: 429 Too Many Requests

Second 2: 50 requests arrive
- All 50: Processed
- Burst replenished gradually
```

---

## Monitoring Rate Limits

### Check if clients are hitting limits:
```bash
# View 429 responses in logs
docker exec starterkit-nginx grep "429" /var/log/nginx/access.log | tail -20

# Count rate limit hits
docker exec starterkit-nginx grep "429" /var/log/nginx/access.log | wc -l

# See which IPs are hitting limits
docker exec starterkit-nginx grep "429" /var/log/nginx/access.log | \
    jq -r '.remote_addr' | sort | uniq -c | sort -rn
```

### Test your rate limits:
```bash
# Install apache bench
apt-get install apache2-utils

# Test API rate limit (100 req/s)
ab -n 200 -c 10 http://localhost/api/health

# Expected: First 150 succeed, rest get 429

# Test with higher concurrency
ab -n 500 -c 50 http://localhost/api/health
```

---

## Best Practices

### ✅ DO:
1. **Start conservative, increase gradually**
   - Begin with lower limits
   - Monitor actual usage
   - Increase based on patterns

2. **Different limits for different endpoints**
   - Auth: Strict (prevent brute force)
   - API: Moderate (normal operations)
   - Health: No limit (monitoring)

3. **Use burst wisely**
   - Allow temporary spikes
   - But not too high (defeats purpose)

4. **Monitor and alert**
   - Track 429 responses
   - Alert on unusual patterns
   - Identify legitimate high-volume clients

5. **Whitelist known IPs**
   ```nginx
   geo $limit {
       default 1;
       10.0.0.0/8 0;      # Internal network
       192.168.1.100 0;   # Known client
   }
   
   map $limit $limit_key {
       0 "";
       1 $binary_remote_addr;
   }
   
   limit_req_zone $limit_key zone=api_limit:10m rate=100r/s;
   ```

### ❌ DON'T:
1. **Set limits too low**
   - Breaks legitimate usage
   - Frustrates users
   - Makes testing difficult

2. **Use same limit for everything**
   - Auth needs stricter limits
   - Health checks need no limits
   - Static assets need no limits

3. **Forget about burst**
   - Real traffic comes in bursts
   - No burst = false positives

4. **Ignore 429 errors**
   - Monitor your logs
   - May indicate attack
   - May indicate misconfiguration

---

## Current Assessment ✅

Your current configuration is **WELL-BALANCED** for:
- ✅ Development (plenty of headroom)
- ✅ Small-medium production (adequate protection)
- ✅ Testing (won't interfere)
- ✅ Security (prevents basic abuse)

### Recommendation: **KEEP AS IS** unless:
1. You're under attack → Tighten
2. High legitimate traffic → Loosen
3. Specific use case needs → Adjust per-endpoint

---

## Quick Reference

| Scenario | API | Auth | General |
|----------|-----|------|---------|
| **Current (Balanced)** | 100/s | 10/s | 50/s |
| Dev/Testing | 200/s | 20/s | 100/s |
| Small Production | 100/s | 10/s | 50/s |
| High-Traffic Production | 500/s | 20/s | 200/s |
| Under Attack | 30/s | 5/m | 10/s |
| No Limits (Dangerous) | ∞ | ∞ | ∞ |

---

**Bottom Line:** Your current config (100/10/50) is **production-ready** and **development-friendly**. No changes needed! ✅
