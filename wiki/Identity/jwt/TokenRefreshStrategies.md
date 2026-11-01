# JWT Token Refresh Strategies - Frontend Implementation Guide

**Complete Guide to Token Refresh Patterns with Recommendations**

## Table of Contents
1. [Overview](#overview)
2. [Strategy 1: Reactive Refresh (401 Response)](#strategy-1-reactive-refresh-401-response)
3. [Strategy 2: Proactive Refresh (Before Expiration)](#strategy-2-proactive-refresh-before-expiration)
4. [Strategy 3: Silent Refresh with Hidden Iframe](#strategy-3-silent-refresh-with-hidden-iframe)
5. [Strategy 4: Refresh on User Activity](#strategy-4-refresh-on-user-activity)
6. [Strategy 5: Hybrid Approach](#strategy-5-hybrid-approach)
7. [Comparison & Recommendations](#comparison--recommendations)
8. [Production Implementation](#production-implementation)

---

## Overview

### The Problem
Access tokens expire every **15 minutes**. Users shouldn't have to re-login every 15 minutes.

### The Solution
Automatically refresh the access token using a long-lived refresh token (7 days).

### Key Questions
1. **When** to refresh? (before expiration vs after 401)
2. **How** to refresh? (interceptor vs timer vs iframe)
3. **What** to do during refresh? (queue requests vs retry)
4. **How** to handle failures? (logout vs retry)

---

## Strategy 1: Reactive Refresh (401 Response)

### Concept
Wait for server to reject the request with `401 Unauthorized`, then refresh token and retry.

### Flow Diagram

```
User Request
    ↓
API Call with Access Token
    ↓
┌─────────────────────────────────────┐
│ Is Token Valid?                     │
├─────────────────────────────────────┤
│ YES → Return Data                   │
│ NO  → Return 401 Unauthorized       │
└─────────────────────────────────────┘
    ↓ (401)
Axios Interceptor Catches 401
    ↓
Call Refresh Token API
    ↓
┌─────────────────────────────────────┐
│ Refresh Success?                    │
├─────────────────────────────────────┤
│ YES → Store New Tokens              │
│     → Retry Original Request        │
│ NO  → Logout User                   │
└─────────────────────────────────────┘
```

### Implementation (Axios)

```javascript
import axios from 'axios';

const api = axios.create({
  baseURL: 'http://localhost:5000/api'
});

// Request interceptor - Add access token
api.interceptors.request.use(
  (config) => {
    const accessToken = localStorage.getItem('accessToken');
    if (accessToken) {
      config.headers.Authorization = `Bearer ${accessToken}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor - Handle 401
let isRefreshing = false;
let failedQueue = [];

const processQueue = (error, token = null) => {
  failedQueue.forEach((prom) => {
    if (error) {
      prom.reject(error);
    } else {
      prom.resolve(token);
    }
  });
  failedQueue = [];
};

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    // Not a 401 or already retried - reject
    if (error.response?.status !== 401 || originalRequest._retry) {
      return Promise.reject(error);
    }

    // If already refreshing, queue this request
    if (isRefreshing) {
      return new Promise((resolve, reject) => {
        failedQueue.push({ resolve, reject });
      })
        .then((token) => {
          originalRequest.headers.Authorization = `Bearer ${token}`;
          return api(originalRequest);
        })
        .catch((err) => Promise.reject(err));
    }

    // Mark as retried and start refresh
    originalRequest._retry = true;
    isRefreshing = true;

    const refreshToken = localStorage.getItem('refreshToken');

    if (!refreshToken) {
      logout();
      return Promise.reject(error);
    }

    try {
      // Call refresh endpoint
      const { data } = await axios.post(
        'http://localhost:5000/api/auth/refresh',
        { refreshToken }
      );

      // Store new tokens
      localStorage.setItem('accessToken', data.accessToken);
      localStorage.setItem('refreshToken', data.refreshToken);

      // Update default header
      api.defaults.headers.common['Authorization'] = `Bearer ${data.accessToken}`;

      // Process queued requests
      processQueue(null, data.accessToken);

      // Retry original request
      originalRequest.headers.Authorization = `Bearer ${data.accessToken}`;
      return api(originalRequest);
    } catch (refreshError) {
      processQueue(refreshError, null);
      logout();
      return Promise.reject(refreshError);
    } finally {
      isRefreshing = false;
    }
  }
);

function logout() {
  localStorage.clear();
  window.location.href = '/login';
}

export default api;
```

### Pros ✅
- ✅ **Simple** - No timers, no complexity
- ✅ **Lazy** - Only refreshes when needed
- ✅ **Handles multiple requests** - Request queueing prevents race conditions
- ✅ **Works with any backend** - Standard HTTP 401 response

### Cons ❌
- ❌ **Extra request** - Every expired token causes failed request + refresh
- ❌ **Poor UX** - Brief delay when token expires
- ❌ **401 noise** - Server logs filled with 401 errors (normal behavior)

### Best For
- ✅ Simple applications
- ✅ Low-traffic apps (few API calls)
- ✅ When server-side token validation is fast

---

## Strategy 2: Proactive Refresh (Before Expiration)

### Concept
Check token expiration **before** making requests and refresh proactively.

### Flow Diagram

```
User Request
    ↓
Check Token Expiration
    ↓
┌─────────────────────────────────────┐
│ Token Expires Soon? (< 2 min)       │
├─────────────────────────────────────┤
│ YES → Refresh Token First           │
│     → Wait for New Token            │
│     → Make Request with New Token   │
│                                     │
│ NO  → Make Request Immediately      │
└─────────────────────────────────────┘
```

### Implementation (Axios)

```javascript
import axios from 'axios';
import jwtDecode from 'jwt-decode';

const api = axios.create({
  baseURL: 'http://localhost:5000/api'
});

// Check if token will expire soon
function willExpireSoon(token, bufferMinutes = 2) {
  if (!token) return true;

  try {
    const decoded = jwtDecode(token);
    const expiresAt = decoded.exp * 1000; // Convert to milliseconds
    const now = Date.now();
    const bufferMs = bufferMinutes * 60 * 1000;

    return expiresAt - now < bufferMs;
  } catch (error) {
    return true; // If can't decode, assume expired
  }
}

// Refresh token function
let refreshPromise = null;

async function refreshTokenIfNeeded() {
  const accessToken = localStorage.getItem('accessToken');

  // If token is still valid, no refresh needed
  if (!willExpireSoon(accessToken)) {
    return accessToken;
  }

  // If already refreshing, return the same promise
  if (refreshPromise) {
    return refreshPromise;
  }

  // Start refresh
  refreshPromise = (async () => {
    try {
      const refreshToken = localStorage.getItem('refreshToken');

      if (!refreshToken) {
        throw new Error('No refresh token');
      }

      const { data } = await axios.post(
        'http://localhost:5000/api/auth/refresh',
        { refreshToken }
      );

      // Store new tokens
      localStorage.setItem('accessToken', data.accessToken);
      localStorage.setItem('refreshToken', data.refreshToken);

      return data.accessToken;
    } catch (error) {
      localStorage.clear();
      window.location.href = '/login';
      throw error;
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

// Request interceptor - Refresh before request
api.interceptors.request.use(
  async (config) => {
    // Refresh token if needed
    const accessToken = await refreshTokenIfNeeded();

    if (accessToken) {
      config.headers.Authorization = `Bearer ${accessToken}`;
    }

    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor - Fallback for 401
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    if (error.response?.status === 401 && !originalRequest._retry) {
      originalRequest._retry = true;

      try {
        const newToken = await refreshTokenIfNeeded();
        originalRequest.headers.Authorization = `Bearer ${newToken}`;
        return api(originalRequest);
      } catch (refreshError) {
        return Promise.reject(refreshError);
      }
    }

    return Promise.reject(error);
  }
);

export default api;
```

### Pros ✅
- ✅ **No failed requests** - Refreshes before expiration
- ✅ **Better UX** - No delay from 401 errors
- ✅ **Cleaner logs** - No 401 errors in server logs
- ✅ **Predictable** - Refresh happens at known intervals

### Cons ❌
- ❌ **More complex** - JWT decoding, expiration checking
- ❌ **Client-side timing** - Relies on client clock being correct
- ❌ **Extra refreshes** - May refresh even if user is idle

### Best For
- ✅ Production applications
- ✅ High-traffic apps (many API calls)
- ✅ When UX is critical

---

## Strategy 3: Silent Refresh with Hidden Iframe

### Concept
Use a hidden iframe to refresh tokens in the background without disrupting the user.

### Flow Diagram

```
Background Timer (every 10 minutes)
    ↓
Create Hidden Iframe
    ↓
Load /auth/refresh-iframe endpoint
    ↓
Server sets new token in cookie (httpOnly)
    ↓
Iframe sends postMessage with success
    ↓
Parent window receives message
    ↓
Update access token in memory
```

### Implementation

**Backend Endpoint:**
```csharp
// /auth/refresh-iframe
[HttpGet("refresh-iframe")]
public async Task<IActionResult> RefreshIframe()
{
    // Get refresh token from httpOnly cookie
    var refreshToken = Request.Cookies["refresh_token"];

    if (string.IsNullOrEmpty(refreshToken))
    {
        return Content("<script>window.parent.postMessage({ type: 'auth-error' }, '*');</script>", "text/html");
    }

    // Refresh tokens
    var result = await _authService.RefreshTokenAsync(refreshToken);

    // Set new refresh token in httpOnly cookie
    Response.Cookies.Append("refresh_token", result.RefreshToken, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Expires = DateTimeOffset.UtcNow.AddDays(7)
    });

    // Send access token to parent window
    var html = $@"
        <script>
            window.parent.postMessage({{
                type: 'auth-success',
                accessToken: '{result.AccessToken}'
            }}, '*');
        </script>
    ";

    return Content(html, "text/html");
}
```

**Frontend:**
```javascript
class SilentRefreshManager {
  constructor() {
    this.refreshInterval = null;
    this.iframe = null;
  }

  start() {
    // Refresh every 10 minutes (token expires in 15)
    this.refreshInterval = setInterval(() => {
      this.silentRefresh();
    }, 10 * 60 * 1000);

    // Listen for messages from iframe
    window.addEventListener('message', this.handleMessage.bind(this));
  }

  stop() {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }
    window.removeEventListener('message', this.handleMessage.bind(this));
  }

  silentRefresh() {
    // Create hidden iframe
    this.iframe = document.createElement('iframe');
    this.iframe.style.display = 'none';
    this.iframe.src = 'http://localhost:5000/api/auth/refresh-iframe';
    document.body.appendChild(this.iframe);

    // Timeout after 10 seconds
    setTimeout(() => {
      if (this.iframe && this.iframe.parentNode) {
        this.iframe.remove();
      }
    }, 10000);
  }

  handleMessage(event) {
    // Verify origin
    if (event.origin !== 'http://localhost:5000') {
      return;
    }

    if (event.data.type === 'auth-success') {
      // Store new access token
      localStorage.setItem('accessToken', event.data.accessToken);
      console.log('Token refreshed silently');

      // Remove iframe
      if (this.iframe && this.iframe.parentNode) {
        this.iframe.remove();
      }
    } else if (event.data.type === 'auth-error') {
      // Refresh failed - logout
      localStorage.clear();
      window.location.href = '/login';
    }
  }
}

// Usage
const refreshManager = new SilentRefreshManager();
refreshManager.start();
```

### Pros ✅
- ✅ **Silent** - User never notices
- ✅ **Cookie-based refresh token** - More secure (httpOnly)
- ✅ **Works across tabs** - Cookie shared across tabs

### Cons ❌
- ❌ **Complex** - Iframe, postMessage, CORS
- ❌ **Cookie requirements** - Needs server support for cookies
- ❌ **Browser security** - Some browsers block third-party cookies
- ❌ **Not for mobile apps** - Only works in browsers

### Best For
- ✅ Web apps with strict security requirements
- ✅ When refresh tokens must be httpOnly cookies
- ✅ Multi-tab synchronization needed

---

## Strategy 4: Refresh on User Activity

### Concept
Only refresh tokens when user is actually using the app (mouse move, keyboard, clicks).

### Implementation

```javascript
import { debounce } from 'lodash';

class ActivityBasedRefresh {
  constructor(api) {
    this.api = api;
    this.lastActivity = Date.now();
    this.inactivityThreshold = 5 * 60 * 1000; // 5 minutes
    this.refreshThreshold = 2 * 60 * 1000; // 2 minutes before expiration

    this.setupActivityListeners();
    this.startRefreshCheck();
  }

  setupActivityListeners() {
    const updateActivity = debounce(() => {
      this.lastActivity = Date.now();
      this.checkAndRefresh();
    }, 1000);

    // Track user activity
    window.addEventListener('mousemove', updateActivity);
    window.addEventListener('keydown', updateActivity);
    window.addEventListener('click', updateActivity);
    window.addEventListener('scroll', updateActivity);
  }

  startRefreshCheck() {
    // Check every minute
    setInterval(() => {
      this.checkAndRefresh();
    }, 60 * 1000);
  }

  async checkAndRefresh() {
    const accessToken = localStorage.getItem('accessToken');

    if (!accessToken) return;

    // Check if user is active
    const isActive = Date.now() - this.lastActivity < this.inactivityThreshold;

    if (!isActive) {
      console.log('User inactive, skipping refresh');
      return;
    }

    // Check if token will expire soon
    try {
      const decoded = jwtDecode(accessToken);
      const expiresAt = decoded.exp * 1000;
      const now = Date.now();

      if (expiresAt - now < this.refreshThreshold) {
        console.log('User active, refreshing token');
        await this.refreshToken();
      }
    } catch (error) {
      console.error('Token check error:', error);
    }
  }

  async refreshToken() {
    const refreshToken = localStorage.getItem('refreshToken');

    if (!refreshToken) return;

    try {
      const { data } = await axios.post(
        'http://localhost:5000/api/auth/refresh',
        { refreshToken }
      );

      localStorage.setItem('accessToken', data.accessToken);
      localStorage.setItem('refreshToken', data.refreshToken);

      console.log('Token refreshed');
    } catch (error) {
      console.error('Refresh failed:', error);
      localStorage.clear();
      window.location.href = '/login';
    }
  }
}

// Usage
const refreshManager = new ActivityBasedRefresh();
```

### Pros ✅
- ✅ **Battery efficient** - Only refreshes when user is active
- ✅ **Smart** - No unnecessary refreshes for idle users
- ✅ **Better security** - Inactive sessions timeout naturally

### Cons ❌
- ❌ **Complex** - Activity tracking, debouncing
- ❌ **Edge cases** - User active but not making API calls
- ❌ **Timer management** - Multiple timers to manage

### Best For
- ✅ Mobile apps (battery saving)
- ✅ Long-lived sessions
- ✅ Apps with periods of inactivity

---

## Strategy 5: Hybrid Approach (RECOMMENDED)

### Concept
Combine **proactive refresh** (before expiration) with **reactive refresh** (401 fallback).

### Implementation

```javascript
import axios from 'axios';
import jwtDecode from 'jwt-decode';

const API_BASE_URL = 'http://localhost:5000/api';

const api = axios.create({
  baseURL: API_BASE_URL
});

// ========================================
// TOKEN UTILITIES
// ========================================

function getTokenExpiration(token) {
  if (!token) return null;

  try {
    const decoded = jwtDecode(token);
    return decoded.exp * 1000; // Convert to milliseconds
  } catch (error) {
    return null;
  }
}

function willExpireSoon(token, bufferSeconds = 120) {
  const expiresAt = getTokenExpiration(token);

  if (!expiresAt) return true;

  const now = Date.now();
  const bufferMs = bufferSeconds * 1000;

  return expiresAt - now < bufferMs;
}

// ========================================
// REFRESH TOKEN LOGIC
// ========================================

let refreshPromise = null;

async function refreshToken() {
  // If already refreshing, return same promise
  if (refreshPromise) {
    return refreshPromise;
  }

  refreshPromise = (async () => {
    try {
      const refreshToken = localStorage.getItem('refreshToken');

      if (!refreshToken) {
        throw new Error('No refresh token available');
      }

      console.log('🔄 Refreshing token...');

      const { data } = await axios.post(
        `${API_BASE_URL}/auth/refresh`,
        { refreshToken }
      );

      // Store new tokens
      localStorage.setItem('accessToken', data.accessToken);
      localStorage.setItem('refreshToken', data.refreshToken);

      console.log('✅ Token refreshed successfully');

      return data.accessToken;
    } catch (error) {
      console.error('❌ Token refresh failed:', error);

      // Clear tokens and redirect to login
      localStorage.clear();
      window.location.href = '/login';

      throw error;
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

// ========================================
// REQUEST INTERCEPTOR (Proactive)
// ========================================

api.interceptors.request.use(
  async (config) => {
    let accessToken = localStorage.getItem('accessToken');

    // Proactive refresh - check if token will expire soon
    if (willExpireSoon(accessToken, 120)) {
      console.log('⚠️ Token will expire soon, refreshing proactively');
      accessToken = await refreshToken();
    }

    // Add token to request
    if (accessToken) {
      config.headers.Authorization = `Bearer ${accessToken}`;
    }

    return config;
  },
  (error) => Promise.reject(error)
);

// ========================================
// RESPONSE INTERCEPTOR (Reactive Fallback)
// ========================================

let failedQueue = [];

const processQueue = (error, token = null) => {
  failedQueue.forEach((prom) => {
    if (error) {
      prom.reject(error);
    } else {
      prom.resolve(token);
    }
  });
  failedQueue = [];
};

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    // If not 401 or already retried, reject
    if (error.response?.status !== 401 || originalRequest._retry) {
      return Promise.reject(error);
    }

    // If already refreshing, queue this request
    if (refreshPromise) {
      return new Promise((resolve, reject) => {
        failedQueue.push({ resolve, reject });
      })
        .then((token) => {
          originalRequest.headers.Authorization = `Bearer ${token}`;
          return api(originalRequest);
        })
        .catch((err) => Promise.reject(err));
    }

    // Mark as retried
    originalRequest._retry = true;

    console.log('⚠️ Got 401, refreshing reactively');

    try {
      const newToken = await refreshToken();

      // Process queued requests
      processQueue(null, newToken);

      // Retry original request
      originalRequest.headers.Authorization = `Bearer ${newToken}`;
      return api(originalRequest);
    } catch (refreshError) {
      processQueue(refreshError, null);
      return Promise.reject(refreshError);
    }
  }
);

export default api;
```

### Pros ✅
- ✅ **Best of both worlds** - Proactive + reactive
- ✅ **Robust** - Handles all edge cases
- ✅ **No failed requests** (usually) - Proactive refresh prevents 401
- ✅ **Fallback protection** - Reactive refresh catches missed cases
- ✅ **Production-ready** - Battle-tested pattern

### Cons ❌
- ❌ **More code** - Combines two strategies
- ❌ **Slightly complex** - More moving parts

### Best For
- ✅ **PRODUCTION APPLICATIONS** ← RECOMMENDED
- ✅ Mission-critical apps
- ✅ Apps with high API traffic
- ✅ When reliability is paramount

---

## Comparison & Recommendations

### Quick Comparison Table

| Strategy | Complexity | Performance | UX | Reliability | Best For |
|----------|-----------|-------------|----|-----------|----|
| **1. Reactive (401)** | ⭐ Low | ⭐⭐ Medium | ⭐⭐ Medium | ⭐⭐⭐ Good | Simple apps |
| **2. Proactive** | ⭐⭐ Medium | ⭐⭐⭐ Good | ⭐⭐⭐ Good | ⭐⭐⭐ Good | Production apps |
| **3. Silent Iframe** | ⭐⭐⭐ High | ⭐⭐ Medium | ⭐⭐⭐ Good | ⭐⭐ Medium | Strict security |
| **4. User Activity** | ⭐⭐⭐ High | ⭐⭐⭐ Good | ⭐⭐⭐ Good | ⭐⭐ Medium | Mobile apps |
| **5. Hybrid** | ⭐⭐ Medium | ⭐⭐⭐ Excellent | ⭐⭐⭐ Excellent | ⭐⭐⭐⭐ Excellent | **PRODUCTION** |

### Detailed Comparison

#### Failed Requests
- **Reactive:** ❌ 1 failed request per expiration
- **Proactive:** ✅ 0 failed requests
- **Iframe:** ✅ 0 failed requests
- **Activity:** ⚠️ Depends on user activity
- **Hybrid:** ✅ 0 failed requests (usually)

#### Server Load
- **Reactive:** ⭐⭐ Medium (only on 401)
- **Proactive:** ⭐⭐⭐ Higher (refreshes proactively)
- **Iframe:** ⭐⭐⭐ Higher (timer-based)
- **Activity:** ⭐⭐ Medium (smart refreshing)
- **Hybrid:** ⭐⭐ Medium (proactive with smart logic)

#### Code Complexity
- **Reactive:** ⭐ Simple (interceptor only)
- **Proactive:** ⭐⭐ Medium (JWT decode + interceptor)
- **Iframe:** ⭐⭐⭐ Complex (iframe + postMessage)
- **Activity:** ⭐⭐⭐ Complex (activity tracking)
- **Hybrid:** ⭐⭐ Medium (well-structured)

#### Edge Case Handling
- **Reactive:** ⭐⭐ Good (simple, works)
- **Proactive:** ⭐⭐ Good (clock skew issues)
- **Iframe:** ⭐⭐ Medium (browser compatibility)
- **Activity:** ⭐⭐ Medium (activity detection issues)
- **Hybrid:** ⭐⭐⭐ Excellent (handles all cases)

---

## Recommendations

### 🥇 **BEST FOR MOST CASES: Strategy 5 (Hybrid)**

**Why:**
- ✅ Combines proactive refresh (prevents 401s)
- ✅ Plus reactive fallback (handles edge cases)
- ✅ Production-ready and battle-tested
- ✅ Handles race conditions properly
- ✅ Reasonable complexity

**Use when:**
- Production web application
- User experience is important
- Reliability is critical

**Code:** See Strategy 5 implementation above

---

### 🥈 **RUNNER-UP: Strategy 2 (Proactive)**

**Why:**
- ✅ Good UX (no failed requests)
- ✅ Moderate complexity
- ✅ Clean server logs

**Use when:**
- You want simplicity with good UX
- Don't need fallback protection
- Client clocks are reliable

**Upgrade path:** Easy to add reactive fallback → becomes Hybrid

---

### 🥉 **SIMPLEST: Strategy 1 (Reactive)**

**Why:**
- ✅ Extremely simple
- ✅ Works everywhere
- ✅ Easy to understand

**Use when:**
- MVP / prototype
- Simple internal tools
- Low traffic applications

**Trade-off:** Accept 1 failed request every 15 minutes

---

### ⚠️ **SPECIAL CASES**

**Use Strategy 3 (Iframe) when:**
- Refresh token must be httpOnly cookie
- Strict security requirements
- Web-only (not mobile)

**Use Strategy 4 (Activity) when:**
- Battery life is critical (mobile)
- Long-lived sessions with idle periods
- You want smart refresh logic

---

## Production Implementation

### Recommended Setup (Hybrid)

**File Structure:**
```
src/
├── services/
│   ├── api.js              ← Axios instance with interceptors
│   ├── auth.js             ← Login, logout functions
│   └── tokenUtils.js       ← JWT decode, expiration check
├── App.jsx
└── index.js
```

**1. tokenUtils.js**
```javascript
import jwtDecode from 'jwt-decode';

export function getTokenExpiration(token) {
  if (!token) return null;

  try {
    const decoded = jwtDecode(token);
    return decoded.exp * 1000;
  } catch (error) {
    return null;
  }
}

export function willExpireSoon(token, bufferSeconds = 120) {
  const expiresAt = getTokenExpiration(token);

  if (!expiresAt) return true;

  const now = Date.now();
  const bufferMs = bufferSeconds * 1000;

  return expiresAt - now < bufferMs;
}

export function isTokenExpired(token) {
  const expiresAt = getTokenExpiration(token);

  if (!expiresAt) return true;

  return Date.now() >= expiresAt;
}
```

**2. api.js (Hybrid Strategy)**
```javascript
import axios from 'axios';
import { willExpireSoon } from './tokenUtils';

const API_BASE_URL = process.env.REACT_APP_API_URL || 'http://localhost:5000/api';

const api = axios.create({
  baseURL: API_BASE_URL
});

let refreshPromise = null;

async function refreshToken() {
  if (refreshPromise) return refreshPromise;

  refreshPromise = (async () => {
    try {
      const refreshToken = localStorage.getItem('refreshToken');

      if (!refreshToken) {
        throw new Error('No refresh token');
      }

      const { data } = await axios.post(
        `${API_BASE_URL}/auth/refresh`,
        { refreshToken }
      );

      localStorage.setItem('accessToken', data.accessToken);
      localStorage.setItem('refreshToken', data.refreshToken);

      return data.accessToken;
    } catch (error) {
      localStorage.clear();
      window.location.href = '/login';
      throw error;
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

// Request interceptor - Proactive refresh
api.interceptors.request.use(
  async (config) => {
    let accessToken = localStorage.getItem('accessToken');

    if (willExpireSoon(accessToken, 120)) {
      accessToken = await refreshToken();
    }

    if (accessToken) {
      config.headers.Authorization = `Bearer ${accessToken}`;
    }

    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor - Reactive fallback
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    if (error.response?.status === 401 && !originalRequest._retry) {
      originalRequest._retry = true;

      try {
        const newToken = await refreshToken();
        originalRequest.headers.Authorization = `Bearer ${newToken}`;
        return api(originalRequest);
      } catch (refreshError) {
        return Promise.reject(refreshError);
      }
    }

    return Promise.reject(error);
  }
);

export default api;
```

**3. auth.js**
```javascript
import api from './api';

export async function login(email, password) {
  const { data } = await api.post('/auth/login', {
    email,
    password
  });

  localStorage.setItem('accessToken', data.accessToken);
  localStorage.setItem('refreshToken', data.refreshToken);
  localStorage.setItem('user', JSON.stringify(data.user));

  return data;
}

export async function logout() {
  try {
    const refreshToken = localStorage.getItem('refreshToken');

    if (refreshToken) {
      await api.post('/auth/logout', {
        refreshToken,
        logoutAllDevices: false
      });
    }
  } catch (error) {
    console.error('Logout error:', error);
  } finally {
    localStorage.clear();
    window.location.href = '/login';
  }
}

export function getCurrentUser() {
  const userStr = localStorage.getItem('user');
  return userStr ? JSON.parse(userStr) : null;
}

export function isAuthenticated() {
  return !!localStorage.getItem('accessToken');
}
```

**4. Usage in Components**
```jsx
import { useEffect, useState } from 'react';
import api from './services/api';

function Dashboard() {
  const [data, setData] = useState([]);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      // Token refresh happens automatically!
      const response = await api.get('/dashboard/stats');
      setData(response.data);
    } catch (error) {
      console.error('Error:', error);
    }
  };

  return (
    <div>
      <h1>Dashboard</h1>
      {/* Your UI */}
    </div>
  );
}
```

---

## Summary

### 🎯 TL;DR - What Should I Use?

```
┌─────────────────────────────────────────────────────────┐
│ RECOMMENDED: Hybrid Strategy (Strategy 5)               │
│                                                         │
│ Proactive refresh (before expiration)                  │
│    +                                                    │
│ Reactive fallback (401 handling)                       │
│                                                         │
│ Result:                                                 │
│ ✅ No failed requests (usually)                         │
│ ✅ Handles all edge cases                               │
│ ✅ Production-ready                                     │
│ ✅ Battle-tested                                        │
└─────────────────────────────────────────────────────────┘
```

### Decision Tree

```
START
  ↓
Is this a prototype/MVP?
  ├─ YES → Use Strategy 1 (Reactive)
  │         Simple, works everywhere
  │
  └─ NO → Is UX critical?
          ├─ NO → Use Strategy 1 (Reactive)
          │        Good enough
          │
          └─ YES → Need httpOnly cookies?
                  ├─ YES → Use Strategy 3 (Iframe)
                  │         Most secure
                  │
                  └─ NO → Is it a mobile app?
                          ├─ YES → Use Strategy 4 (Activity)
                          │         Battery efficient
                          │
                          └─ NO → Use Strategy 5 (Hybrid) ← RECOMMENDED
                                   Best for production
```

### Final Recommendation

**For 95% of applications:** Use **Strategy 5 (Hybrid)**

**Rationale:**
1. ✅ Prevents 401 errors (proactive)
2. ✅ Handles edge cases (reactive)
3. ✅ Moderate complexity
4. ✅ Production-ready
5. ✅ Best UX

**Implementation time:** ~30 minutes
**Maintenance:** Low
**Reliability:** Excellent

---

## Additional Resources

- [JWT Best Practices](https://tools.ietf.org/html/rfc8725)
- [OAuth 2.0 Token Refresh](https://tools.ietf.org/html/rfc6749#section-1.5)
- [Axios Interceptors Docs](https://axios-http.com/docs/interceptors)

---

**Status:** ✅ Production ready - Choose your strategy and implement!
