# JWT Authentication - Frontend Implementation Guide

**Complete Integration Guide for React, Vue, Angular, and Vanilla JavaScript**

## Table of Contents
1. [Quick Start](#quick-start)
2. [API Endpoints](#api-endpoints)
3. [Token Storage Strategies](#token-storage-strategies)
4. [Login Implementation](#login-implementation)
5. [Automatic Token Refresh](#automatic-token-refresh)
6. [Logout Implementation](#logout-implementation)
7. [Protected Routes](#protected-routes)
8. [Error Handling](#error-handling)
9. [Security Best Practices](#security-best-practices)
10. [Complete Examples](#complete-examples)

---

## Quick Start

### Installation

```bash
# Using Axios (recommended)
npm install axios

# Or using native fetch (no installation needed)
```

### Basic Setup (5 minutes)

```javascript
// 1. Create auth service
// 2. Login user
// 3. Store tokens
// 4. Add automatic refresh
// 5. Use in your components
```

---

## API Endpoints

### Base URL
```javascript
const API_BASE_URL = 'http://localhost:5000/api';
// Production: 'https://api.example.com/api'
```

### Available Endpoints

| Endpoint | Method | Auth Required | Description |
|----------|--------|---------------|-------------|
| `/auth/login` | POST | ❌ No | Login and get tokens |
| `/auth/register` | POST | ❌ No | Register new user |
| `/auth/refresh` | POST | ❌ No | Refresh access token |
| `/auth/logout` | POST | ✅ Yes | Logout (single or all devices) |

---

## Token Storage Strategies

### Option 1: localStorage (Recommended for Web Apps)

**Pros:**
- ✅ Survives page refresh
- ✅ Simple to implement
- ✅ Works across tabs

**Cons:**
- ⚠️ Vulnerable to XSS attacks
- ⚠️ Accessible from JavaScript

**Use When:**
- You have proper XSS protection
- You need tokens to persist across sessions

```javascript
// Store tokens
localStorage.setItem('accessToken', accessToken);
localStorage.setItem('refreshToken', refreshToken);

// Get tokens
const accessToken = localStorage.getItem('accessToken');
const refreshToken = localStorage.getItem('refreshToken');

// Clear tokens
localStorage.removeItem('accessToken');
localStorage.removeItem('refreshToken');
// Or clear everything:
localStorage.clear();
```

### Option 2: sessionStorage (More Secure)

**Pros:**
- ✅ Cleared when tab closed
- ✅ Not shared across tabs
- ✅ Same API as localStorage

**Cons:**
- ⚠️ Lost on page refresh (in some cases)
- ⚠️ Still vulnerable to XSS

**Use When:**
- You want logout when browser closes
- You don't need cross-tab synchronization

```javascript
// Same API as localStorage
sessionStorage.setItem('accessToken', accessToken);
sessionStorage.setItem('refreshToken', refreshToken);
```

### Option 3: Memory Only (Most Secure)

**Pros:**
- ✅ Not accessible from XSS
- ✅ Automatically cleared on page close

**Cons:**
- ⚠️ Lost on page refresh
- ⚠️ Complex state management
- ⚠️ Requires Redux/Zustand/Context

**Use When:**
- Maximum security required
- You can handle state management

```javascript
// Using React Context or Zustand
const useAuthStore = create((set) => ({
  accessToken: null,
  refreshToken: null,
  setTokens: (access, refresh) => set({
    accessToken: access,
    refreshToken: refresh
  }),
  clearTokens: () => set({
    accessToken: null,
    refreshToken: null
  })
}));
```

### Recommendation

**For most web applications:** Use **localStorage** with:
- ✅ HTTPS only
- ✅ Content Security Policy (CSP)
- ✅ Proper XSS sanitization
- ✅ Short token expiration (already 15 min)

---

## Login Implementation

### Basic Login (Vanilla JavaScript)

```javascript
async function login(email, password) {
  try {
    const response = await fetch(`${API_BASE_URL}/auth/login`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ email, password })
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Login failed');
    }

    const data = await response.json();

    // Store tokens
    localStorage.setItem('accessToken', data.accessToken);
    localStorage.setItem('refreshToken', data.refreshToken);
    localStorage.setItem('user', JSON.stringify(data.user));

    return data;
  } catch (error) {
    console.error('Login error:', error);
    throw error;
  }
}

// Usage
try {
  const result = await login('user@example.com', 'password123');
  console.log('Logged in:', result.user);
  // Redirect to dashboard
  window.location.href = '/dashboard';
} catch (error) {
  alert(error.message);
}
```

### Login with Axios (Recommended)

```javascript
import axios from 'axios';

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json'
  }
});

async function login(email, password) {
  try {
    const { data } = await api.post('/auth/login', {
      email,
      password
    });

    // Store tokens
    localStorage.setItem('accessToken', data.accessToken);
    localStorage.setItem('refreshToken', data.refreshToken);
    localStorage.setItem('user', JSON.stringify(data.user));

    // Set default authorization header
    api.defaults.headers.common['Authorization'] = `Bearer ${data.accessToken}`;

    return data;
  } catch (error) {
    throw error.response?.data?.message || 'Login failed';
  }
}
```

### React Login Component

```jsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { login } from './authService';

function LoginForm() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      const result = await login(email, password);
      console.log('Login successful:', result.user);
      navigate('/dashboard');
    } catch (err) {
      setError(err.message || 'Login failed');
    } finally {
      setLoading(false);
    }
  };

  return (
    <form onSubmit={handleSubmit}>
      <h2>Login</h2>

      {error && <div className="error">{error}</div>}

      <div>
        <label>Email:</label>
        <input
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
      </div>

      <div>
        <label>Password:</label>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
      </div>

      <button type="submit" disabled={loading}>
        {loading ? 'Logging in...' : 'Login'}
      </button>
    </form>
  );
}

export default LoginForm;
```

### Vue Login Component

```vue
<template>
  <form @submit.prevent="handleSubmit">
    <h2>Login</h2>

    <div v-if="error" class="error">{{ error }}</div>

    <div>
      <label>Email:</label>
      <input v-model="email" type="email" required />
    </div>

    <div>
      <label>Password:</label>
      <input v-model="password" type="password" required />
    </div>

    <button type="submit" :disabled="loading">
      {{ loading ? 'Logging in...' : 'Login' }}
    </button>
  </form>
</template>

<script setup>
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { login } from './authService';

const router = useRouter();
const email = ref('');
const password = ref('');
const error = ref('');
const loading = ref(false);

const handleSubmit = async () => {
  error.value = '';
  loading.value = true;

  try {
    const result = await login(email.value, password.value);
    console.log('Login successful:', result.user);
    router.push('/dashboard');
  } catch (err) {
    error.value = err.message || 'Login failed';
  } finally {
    loading.value = false;
  }
};
</script>
```

---

## Automatic Token Refresh

### Why Automatic Refresh?

- ✅ Access tokens expire every 15 minutes
- ✅ Users shouldn't re-login every 15 minutes
- ✅ Seamless UX with automatic refresh

### Strategy: Axios Interceptor (Recommended)

This intercepts 401 errors, refreshes the token, and retries the request.

```javascript
import axios from 'axios';

const API_BASE_URL = 'http://localhost:5000/api';

// Create axios instance
const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json'
  }
});

// Request interceptor - Add access token to every request
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

// Response interceptor - Handle 401 and refresh token
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

    // If error is not 401 or already retried, reject
    if (error.response?.status !== 401 || originalRequest._retry) {
      return Promise.reject(error);
    }

    if (isRefreshing) {
      // If already refreshing, queue this request
      return new Promise((resolve, reject) => {
        failedQueue.push({ resolve, reject });
      })
        .then((token) => {
          originalRequest.headers.Authorization = `Bearer ${token}`;
          return api(originalRequest);
        })
        .catch((err) => Promise.reject(err));
    }

    originalRequest._retry = true;
    isRefreshing = true;

    const refreshToken = localStorage.getItem('refreshToken');

    if (!refreshToken) {
      // No refresh token, logout
      logout();
      return Promise.reject(error);
    }

    try {
      // Call refresh endpoint
      const { data } = await axios.post(
        `${API_BASE_URL}/auth/refresh`,
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

### Usage with Auto-Refresh

```javascript
import api from './api';

// Make API calls - token refresh happens automatically
async function getUserProfile() {
  try {
    const { data } = await api.get('/users/me');
    return data;
  } catch (error) {
    console.error('Error fetching profile:', error);
    throw error;
  }
}

async function updateProfile(updates) {
  try {
    const { data } = await api.put('/users/me', updates);
    return data;
  } catch (error) {
    console.error('Error updating profile:', error);
    throw error;
  }
}
```

### Fetch API with Auto-Refresh

```javascript
class ApiClient {
  constructor(baseURL) {
    this.baseURL = baseURL;
    this.isRefreshing = false;
    this.failedQueue = [];
  }

  async request(endpoint, options = {}) {
    const url = `${this.baseURL}${endpoint}`;
    const accessToken = localStorage.getItem('accessToken');

    const config = {
      ...options,
      headers: {
        'Content-Type': 'application/json',
        ...options.headers,
        ...(accessToken && { Authorization: `Bearer ${accessToken}` })
      }
    };

    try {
      const response = await fetch(url, config);

      if (response.status === 401) {
        // Token expired, try to refresh
        const newToken = await this.refreshToken();

        // Retry with new token
        config.headers.Authorization = `Bearer ${newToken}`;
        return await fetch(url, config);
      }

      return response;
    } catch (error) {
      throw error;
    }
  }

  async refreshToken() {
    if (this.isRefreshing) {
      // Wait for ongoing refresh
      return new Promise((resolve, reject) => {
        this.failedQueue.push({ resolve, reject });
      });
    }

    this.isRefreshing = true;

    try {
      const refreshToken = localStorage.getItem('refreshToken');

      if (!refreshToken) {
        throw new Error('No refresh token');
      }

      const response = await fetch(`${this.baseURL}/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken })
      });

      if (!response.ok) {
        throw new Error('Refresh failed');
      }

      const data = await response.json();

      // Store new tokens
      localStorage.setItem('accessToken', data.accessToken);
      localStorage.setItem('refreshToken', data.refreshToken);

      // Process queued requests
      this.failedQueue.forEach(({ resolve }) => resolve(data.accessToken));
      this.failedQueue = [];

      return data.accessToken;
    } catch (error) {
      this.failedQueue.forEach(({ reject }) => reject(error));
      this.failedQueue = [];

      // Logout
      localStorage.clear();
      window.location.href = '/login';

      throw error;
    } finally {
      this.isRefreshing = false;
    }
  }

  async get(endpoint) {
    const response = await this.request(endpoint, { method: 'GET' });
    return response.json();
  }

  async post(endpoint, body) {
    const response = await this.request(endpoint, {
      method: 'POST',
      body: JSON.stringify(body)
    });
    return response.json();
  }

  async put(endpoint, body) {
    const response = await this.request(endpoint, {
      method: 'PUT',
      body: JSON.stringify(body)
    });
    return response.json();
  }

  async delete(endpoint) {
    const response = await this.request(endpoint, { method: 'DELETE' });
    return response.json();
  }
}

// Usage
const api = new ApiClient('http://localhost:5000/api');

export default api;
```

---

## Logout Implementation

### Single Device Logout

```javascript
import api from './api';

async function logout() {
  try {
    const refreshToken = localStorage.getItem('refreshToken');

    if (refreshToken) {
      // Call logout endpoint
      await api.post('/auth/logout', {
        refreshToken,
        logoutAllDevices: false
      });
    }
  } catch (error) {
    console.error('Logout error:', error);
    // Continue anyway - clear local storage even if API fails
  } finally {
    // Clear local storage
    localStorage.removeItem('accessToken');
    localStorage.removeItem('refreshToken');
    localStorage.removeItem('user');

    // Redirect to login
    window.location.href = '/login';
  }
}
```

### Logout All Devices

```javascript
async function logoutAllDevices() {
  try {
    const refreshToken = localStorage.getItem('refreshToken');

    if (refreshToken) {
      await api.post('/auth/logout', {
        refreshToken,
        logoutAllDevices: true  // This is the key difference
      });
    }
  } catch (error) {
    console.error('Logout error:', error);
  } finally {
    // Clear local storage
    localStorage.clear();

    // Redirect to login
    window.location.href = '/login';
  }
}
```

### React Logout Button

```jsx
import { useNavigate } from 'react-router-dom';
import { logout, logoutAllDevices } from './authService';

function LogoutButtons() {
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const handleLogoutAll = async () => {
    if (window.confirm('Logout from all devices?')) {
      await logoutAllDevices();
      navigate('/login');
    }
  };

  return (
    <div>
      <button onClick={handleLogout}>
        Logout
      </button>

      <button onClick={handleLogoutAll} className="danger">
        Logout All Devices
      </button>
    </div>
  );
}
```

### Vue Logout Component

```vue
<template>
  <div>
    <button @click="handleLogout">Logout</button>
    <button @click="handleLogoutAll" class="danger">
      Logout All Devices
    </button>
  </div>
</template>

<script setup>
import { useRouter } from 'vue-router';
import { logout, logoutAllDevices } from './authService';

const router = useRouter();

const handleLogout = async () => {
  await logout();
  router.push('/login');
};

const handleLogoutAll = async () => {
  if (confirm('Logout from all devices?')) {
    await logoutAllDevices();
    router.push('/login');
  }
};
</script>
```

---

## Protected Routes

### React (with React Router)

```jsx
import { Navigate } from 'react-router-dom';

function PrivateRoute({ children }) {
  const accessToken = localStorage.getItem('accessToken');

  if (!accessToken) {
    return <Navigate to="/login" replace />;
  }

  return children;
}

// Usage in App.jsx
import { BrowserRouter, Routes, Route } from 'react-router-dom';

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginForm />} />
        <Route path="/register" element={<RegisterForm />} />

        {/* Protected routes */}
        <Route
          path="/dashboard"
          element={
            <PrivateRoute>
              <Dashboard />
            </PrivateRoute>
          }
        />
        <Route
          path="/profile"
          element={
            <PrivateRoute>
              <Profile />
            </PrivateRoute>
          }
        />
      </Routes>
    </BrowserRouter>
  );
}
```

### Vue (with Vue Router)

```javascript
// router/index.js
import { createRouter, createWebHistory } from 'vue-router';

const routes = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('../views/Login.vue')
  },
  {
    path: '/dashboard',
    name: 'Dashboard',
    component: () => import('../views/Dashboard.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/profile',
    name: 'Profile',
    component: () => import('../views/Profile.vue'),
    meta: { requiresAuth: true }
  }
];

const router = createRouter({
  history: createWebHistory(),
  routes
});

// Navigation guard
router.beforeEach((to, from, next) => {
  const accessToken = localStorage.getItem('accessToken');

  if (to.meta.requiresAuth && !accessToken) {
    next('/login');
  } else {
    next();
  }
});

export default router;
```

### Angular (with Guards)

```typescript
// auth.guard.ts
import { Injectable } from '@angular/core';
import { Router, CanActivate } from '@angular/router';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {
  constructor(private router: Router) {}

  canActivate(): boolean {
    const accessToken = localStorage.getItem('accessToken');

    if (!accessToken) {
      this.router.navigate(['/login']);
      return false;
    }

    return true;
  }
}

// app-routing.module.ts
import { AuthGuard } from './auth.guard';

const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: 'dashboard',
    component: DashboardComponent,
    canActivate: [AuthGuard]
  },
  {
    path: 'profile',
    component: ProfileComponent,
    canActivate: [AuthGuard]
  }
];
```

---

## Error Handling

### Handle Common Errors

```javascript
function handleApiError(error) {
  if (error.response) {
    // Server responded with error status
    switch (error.response.status) {
      case 400:
        return error.response.data.message || 'Invalid request';
      case 401:
        // Unauthorized - token expired or invalid
        logout();
        return 'Session expired. Please login again.';
      case 403:
        return 'You do not have permission to perform this action';
      case 404:
        return 'Resource not found';
      case 500:
        return 'Server error. Please try again later.';
      default:
        return error.response.data.message || 'An error occurred';
    }
  } else if (error.request) {
    // Request made but no response
    return 'Network error. Please check your connection.';
  } else {
    // Other errors
    return error.message || 'An unexpected error occurred';
  }
}

// Usage
try {
  const result = await api.get('/users/me');
  console.log(result);
} catch (error) {
  const errorMessage = handleApiError(error);
  alert(errorMessage);
}
```

### React Error Boundary

```jsx
import { Component } from 'react';

class ErrorBoundary extends Component {
  constructor(props) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error) {
    return { hasError: true, error };
  }

  componentDidCatch(error, errorInfo) {
    console.error('Error caught by boundary:', error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="error-page">
          <h1>Something went wrong</h1>
          <p>{this.state.error?.message}</p>
          <button onClick={() => window.location.reload()}>
            Reload Page
          </button>
        </div>
      );
    }

    return this.props.children;
  }
}

// Usage
<ErrorBoundary>
  <App />
</ErrorBoundary>
```

---

## Security Best Practices

### 1. Always Use HTTPS in Production ✅

```javascript
// Development
const API_BASE_URL = 'http://localhost:5000/api';

// Production
const API_BASE_URL = process.env.NODE_ENV === 'production'
  ? 'https://api.example.com/api'
  : 'http://localhost:5000/api';
```

### 2. Never Log Tokens ❌

```javascript
// ❌ BAD
console.log('Access token:', accessToken);

// ✅ GOOD
console.log('User logged in successfully');
```

### 3. Clear Tokens on Logout ✅

```javascript
// ✅ Clear everything
localStorage.removeItem('accessToken');
localStorage.removeItem('refreshToken');
localStorage.removeItem('user');

// Or clear all localStorage
localStorage.clear();
```

### 4. Validate Tokens Before Use ✅

```javascript
function isTokenValid() {
  const accessToken = localStorage.getItem('accessToken');

  if (!accessToken) {
    return false;
  }

  try {
    // Decode JWT (don't validate signature, backend does that)
    const payload = JSON.parse(atob(accessToken.split('.')[1]));
    const expiry = payload.exp * 1000; // Convert to milliseconds

    // Check if token expired
    return Date.now() < expiry;
  } catch (error) {
    return false;
  }
}
```

### 5. Content Security Policy (CSP) ✅

```html
<!-- Add to your HTML <head> -->
<meta
  http-equiv="Content-Security-Policy"
  content="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline';"
/>
```

### 6. XSS Protection ✅

```javascript
// Always sanitize user input
import DOMPurify from 'dompurify';

const cleanInput = DOMPurify.sanitize(userInput);
```

### 7. Don't Store Sensitive Data in JWT ⚠️

```javascript
// ❌ BAD - Backend should NOT include this
{
  "userId": "123",
  "email": "user@example.com",
  "creditCard": "1234-5678-9012-3456"  // NEVER!
}

// ✅ GOOD
{
  "userId": "123",
  "email": "user@example.com",
  "roles": ["user"]
}
```

---

## Complete Examples

### Complete Auth Service (authService.js)

```javascript
import axios from 'axios';

const API_BASE_URL = 'http://localhost:5000/api';

// Create axios instance
const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json'
  }
});

// Add access token to requests
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

// Handle token refresh on 401
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

    if (error.response?.status !== 401 || originalRequest._retry) {
      return Promise.reject(error);
    }

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

    originalRequest._retry = true;
    isRefreshing = true;

    const refreshToken = localStorage.getItem('refreshToken');

    if (!refreshToken) {
      logout();
      return Promise.reject(error);
    }

    try {
      const { data } = await axios.post(
        `${API_BASE_URL}/auth/refresh`,
        { refreshToken }
      );

      localStorage.setItem('accessToken', data.accessToken);
      localStorage.setItem('refreshToken', data.refreshToken);

      api.defaults.headers.common['Authorization'] = `Bearer ${data.accessToken}`;

      processQueue(null, data.accessToken);

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

// Auth functions
export async function login(email, password) {
  const { data } = await api.post('/auth/login', { email, password });

  localStorage.setItem('accessToken', data.accessToken);
  localStorage.setItem('refreshToken', data.refreshToken);
  localStorage.setItem('user', JSON.stringify(data.user));

  return data;
}

export async function register(userData) {
  const { data } = await api.post('/auth/register', userData);

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

export async function logoutAllDevices() {
  try {
    const refreshToken = localStorage.getItem('refreshToken');

    if (refreshToken) {
      await api.post('/auth/logout', {
        refreshToken,
        logoutAllDevices: true
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

export default api;
```

### React Complete Example

```jsx
// App.jsx
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import Profile from './pages/Profile';
import { isAuthenticated } from './services/authService';

function PrivateRoute({ children }) {
  return isAuthenticated() ? children : <Navigate to="/login" replace />;
}

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />

        <Route
          path="/dashboard"
          element={
            <PrivateRoute>
              <Dashboard />
            </PrivateRoute>
          }
        />

        <Route
          path="/profile"
          element={
            <PrivateRoute>
              <Profile />
            </PrivateRoute>
          }
        />

        <Route path="/" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
```

```jsx
// pages/Login.jsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { login } from '../services/authService';

function Login() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      await login(email, password);
      navigate('/dashboard');
    } catch (err) {
      setError(err.response?.data?.message || 'Login failed');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-container">
      <form onSubmit={handleSubmit}>
        <h2>Login</h2>

        {error && <div className="error">{error}</div>}

        <div>
          <label>Email:</label>
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
        </div>

        <div>
          <label>Password:</label>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </div>

        <button type="submit" disabled={loading}>
          {loading ? 'Logging in...' : 'Login'}
        </button>
      </form>
    </div>
  );
}

export default Login;
```

```jsx
// pages/Dashboard.jsx
import { useEffect, useState } from 'react';
import { logout, getCurrentUser } from '../services/authService';
import api from '../services/authService';

function Dashboard() {
  const [user, setUser] = useState(null);
  const [data, setData] = useState([]);

  useEffect(() => {
    const currentUser = getCurrentUser();
    setUser(currentUser);

    loadDashboardData();
  }, []);

  const loadDashboardData = async () => {
    try {
      // This will auto-refresh token if needed
      const { data } = await api.get('/dashboard/stats');
      setData(data);
    } catch (error) {
      console.error('Error loading dashboard:', error);
    }
  };

  const handleLogout = async () => {
    await logout();
  };

  return (
    <div>
      <header>
        <h1>Dashboard</h1>
        <div>
          <span>Welcome, {user?.firstName}!</span>
          <button onClick={handleLogout}>Logout</button>
        </div>
      </header>

      <main>
        {/* Your dashboard content */}
        <h2>Dashboard Stats</h2>
        <pre>{JSON.stringify(data, null, 2)}</pre>
      </main>
    </div>
  );
}

export default Dashboard;
```

---

## Testing the Implementation

### Manual Testing Checklist

1. **Login Flow**
   - [ ] Login with valid credentials
   - [ ] Tokens stored in localStorage
   - [ ] Redirected to dashboard
   - [ ] User data displayed correctly

2. **Token Refresh**
   - [ ] Wait 15 minutes
   - [ ] Make API call
   - [ ] Token automatically refreshed
   - [ ] Request succeeds

3. **Logout Flow**
   - [ ] Click logout button
   - [ ] Tokens cleared from localStorage
   - [ ] Redirected to login
   - [ ] Cannot access protected routes

4. **Logout All Devices**
   - [ ] Login on 2 different browsers
   - [ ] Click "Logout all devices" on one
   - [ ] Both sessions invalidated
   - [ ] Both browsers redirect to login

5. **Protected Routes**
   - [ ] Try accessing /dashboard without login
   - [ ] Redirected to /login
   - [ ] After login, can access /dashboard

### API Testing with cURL

```bash
# 1. Login
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "user@example.com",
    "password": "password123"
  }'

# Save the tokens from response

# 2. Make authenticated request
curl -X GET http://localhost:5000/api/users/me \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN"

# 3. Refresh token
curl -X POST http://localhost:5000/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{
    "refreshToken": "YOUR_REFRESH_TOKEN"
  }'

# 4. Logout
curl -X POST http://localhost:5000/api/auth/logout \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -d '{
    "refreshToken": "YOUR_REFRESH_TOKEN",
    "logoutAllDevices": false
  }'
```

---

## Troubleshooting

### Issue: Token Refresh Loop

**Symptom:** Endless refresh requests

**Solution:**
```javascript
// Add _retry flag to prevent infinite loop
if (originalRequest._retry) {
  return Promise.reject(error);
}
originalRequest._retry = true;
```

### Issue: CORS Errors

**Symptom:** `Access-Control-Allow-Origin` error

**Solution:** Backend must allow your frontend origin:
```csharp
// Backend (already configured)
services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        builder => builder
            .WithOrigins("http://localhost:3000")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});
```

### Issue: Token Not Sent in Requests

**Symptom:** 401 errors even with valid token

**Solution:** Check interceptor is adding token:
```javascript
// Request interceptor
api.interceptors.request.use(
  (config) => {
    const accessToken = localStorage.getItem('accessToken');
    if (accessToken) {
      config.headers.Authorization = `Bearer ${accessToken}`;
    }
    return config;
  }
);
```

### Issue: Tokens Not Persisting

**Symptom:** Logged out on page refresh

**Solution:** Use localStorage, not memory:
```javascript
// ✅ GOOD
localStorage.setItem('accessToken', token);

// ❌ BAD
let accessToken = token; // Lost on refresh
```

---

## Summary

### What You Get
✅ Complete authentication system
✅ Automatic token refresh (seamless UX)
✅ Logout single device
✅ Logout all devices
✅ Protected routes
✅ Error handling
✅ Security best practices

### Implementation Time
- Basic setup: **30 minutes**
- With auto-refresh: **1 hour**
- Complete with all features: **2-3 hours**

### Next Steps
1. Copy the `authService.js` file
2. Implement login/logout UI
3. Add protected routes
4. Test the flow
5. Deploy to production

**Status:** ✅ Ready to integrate!
