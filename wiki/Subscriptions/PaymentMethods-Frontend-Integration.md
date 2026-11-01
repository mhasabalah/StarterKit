# Payment Methods — Frontend Integration Guide

## Endpoint Map

All endpoints are tagged **Payment Methods** in Swagger.

| Method | Route | Auth | Returns |
|---|---|---|---|
| `GET` | `/api/v1/payment-methods` | Anonymous | Active methods only |
| `GET` | `/api/v1/payment-instructions` | Anonymous | Support contacts only |
| `GET` | `/api/v1/admin/payment-methods` | Admin | All methods (active + inactive) |
| `POST` | `/api/v1/admin/payment-methods` | Admin | Create a method |
| `PUT` | `/api/v1/admin/payment-methods/{id}` | Admin | Update a method |
| `DELETE` | `/api/v1/admin/payment-methods/{id}` | Admin | Delete a method |
| `PUT` | `/api/v1/admin/payment-instructions` | Admin | Upsert support contacts |

---

## Public Endpoints

### GET `/api/v1/payment-methods`

Active payment methods only, sorted by `sortOrder`. Anonymous — call on page load.

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "type": "BankTransfer",
    "displayName": "StarterKit Bank",
    "value": "EG123456789",
    "description": "Use the application email in the transfer note.",
    "metadata": "{\"bankName\":\"StarterKit Bank\",\"accountName\":\"StarterKit Ltd\",\"accountNumber\":\"123456789\",\"iban\":\"EG123456789\",\"swift\":\"XXXXXXXX\"}",
    "sortOrder": 1,
    "isActive": true
  },
  {
    "id": "...",
    "type": "VodafoneCash",
    "displayName": "Vodafone Cash",
    "value": "+20 100 000 0000",
    "description": null,
    "metadata": null,
    "sortOrder": 2,
    "isActive": true
  }
]
```

### GET `/api/v1/payment-instructions`

Support contact info only. Anonymous — call separately from methods.

```json
{
  "supportWhatsApp": "+201234567890",
  "supportEmail": "support@example.com"
}
```

---

## Admin Endpoints

### GET `/api/v1/admin/payment-methods`

Returns **all** methods including inactive ones. Use this to populate the admin management list.

Same shape as the public endpoint but includes methods where `isActive: false`.

### POST `/api/v1/admin/payment-methods`

```json
{
  "type": "BankTransfer",
  "displayName": "StarterKit Bank",
  "value": "EG123456789",
  "description": "Use the application email in the transfer note.",
  "metadata": "{\"bankName\":\"StarterKit Bank\",\"accountName\":\"StarterKit Ltd\",\"accountNumber\":\"123456789\",\"iban\":\"EG123456789\",\"swift\":\"XXXXXXXX\"}",
  "sortOrder": 1
}
```

Response: `201 Created` → `{ "data": { "id": "guid" } }`

### PUT `/api/v1/admin/payment-methods/{id}`

Same body as POST, plus `"isActive": true/false`.

### DELETE `/api/v1/admin/payment-methods/{id}`

Response: `200 OK`

### PUT `/api/v1/admin/payment-instructions`

```json
{
  "supportWhatsApp": "+201234567890",
  "supportEmail": "support@example.com"
}
```

---

## The `type` + `metadata` Pattern

`type` is a free string — the frontend switches on it to decide which fields to render.
`metadata` is a **JSON string** — parse it with `JSON.parse(method.metadata)` (check for null first).
`value` is the **main copyable field** — IBAN for bank transfer, phone number for wallets.

---

## Known Types and Their Fields

### `BankTransfer`

| Field | Source | Notes |
|---|---|---|
| `displayName` | top-level | Card title |
| `value` | top-level | IBAN — put in Copy button |
| `description` | top-level | Note shown below card |
| `metadata.bankName` | metadata | Full bank name |
| `metadata.accountName` | metadata | Account holder name |
| `metadata.accountNumber` | metadata | Account number |
| `metadata.iban` | metadata | Same as `value` |
| `metadata.swift` | metadata | SWIFT/BIC code |

```
┌─────────────────────────────────────┐
│ 🏦 StarterKit Bank                     │
│ Account Name: StarterKit for R.E.      │
│ Account No:   123456789             │
│ IBAN:         EG123456789   [Copy]  │
│ SWIFT:        XXXXXXXX              │
│ ⚠ Use application email in note    │
└─────────────────────────────────────┘
```

### `VodafoneCash` / `InstaPay`

`metadata` is null — only `value` (phone/ID) and `displayName` are used.

```
┌─────────────────────────────────────┐
│ 📱 Vodafone Cash                    │
│ Send to: +20 100 000 0000   [Copy] │
└─────────────────────────────────────┘
```

---

## User-Facing Payment Page

Fetch both endpoints independently on page load:

```typescript
const [methods, instructions] = await Promise.all([
  fetch('/api/v1/payment-methods').then(r => r.json()),
  fetch('/api/v1/payment-instructions').then(r => r.json()),
]);

// methods is already filtered to active only — no client-side filtering needed
// instructions has supportWhatsApp and supportEmail
```

Render each method by switching on `type`:

```typescript
function PaymentMethodCard({ method }) {
  const meta = method.metadata ? JSON.parse(method.metadata) : {};

  switch (method.type) {
    case 'BankTransfer':   return <BankTransferCard method={method} meta={meta} />;
    case 'VodafoneCash':   return <WalletCard method={method} />;
    case 'InstaPay':       return <InstaPayCard method={method} />;
    default:
      // Graceful fallback — shows value with a copy button for any unknown type
      return <GenericCard method={method} />;
  }
}
```

Always implement the `GenericCard` fallback. If admin adds a new type in the DB the frontend won't crash.

---

## Admin List Page

```typescript
// Use the admin endpoint — shows all methods including inactive
const methods = await fetch('/api/v1/admin/payment-methods', {
  headers: { Authorization: `Bearer ${token}` }
}).then(r => r.json());

// Show inactive methods with a greyed badge — don't hide them
```

---

## Admin Add / Edit Form

### Step flow

```
Step 1 — Pick type
  [ BankTransfer ]  [ VodafoneCash ]  [ InstaPay ]  [ + Custom ]

Step 2 — Type-specific fields (swap when type changes)

  BankTransfer:               VodafoneCash / InstaPay:
  ─────────────               ────────────────────────
  Bank Name      [input]      Phone / ID     [input]  → value
  Account Name   [input]
  Account No.    [input]
  IBAN           [input]  → value
  SWIFT          [input]

Step 3 — Shared fields (always visible)
  Display Name   [input]
  Notes          [input]   → description
  Sort Order     [number]
  Active         [toggle]  (Edit only)
```

### Building the request body on submit

```typescript
// BankTransfer
const body = {
  type: 'BankTransfer',
  displayName: form.displayName,
  value: form.iban,                    // IBAN is the copyable field
  description: form.notes ?? null,
  metadata: JSON.stringify({
    bankName: form.bankName,
    accountName: form.accountName,
    accountNumber: form.accountNumber,
    iban: form.iban,
    swift: form.swift,
  }),
  sortOrder: form.sortOrder,
  isActive: form.isActive,             // PUT only
};

// VodafoneCash / InstaPay
const body = {
  type: form.type,
  displayName: form.displayName,
  value: form.phoneOrId,               // phone/ID is the copyable field
  description: form.notes ?? null,
  metadata: null,
  sortOrder: form.sortOrder,
  isActive: form.isActive,             // PUT only
};
```

### Populating fields on Edit

```typescript
const method = methods.find(m => m.id === selectedId);
const meta = method.metadata ? JSON.parse(method.metadata) : {};

form.type        = method.type;
form.displayName = method.displayName;
form.description = method.description;
form.sortOrder   = method.sortOrder;
form.isActive    = method.isActive;

if (method.type === 'BankTransfer') {
  form.bankName      = meta.bankName;
  form.accountName   = meta.accountName;
  form.accountNumber = meta.accountNumber;
  form.iban          = meta.iban;
  form.swift         = meta.swift;
} else {
  form.phoneOrId = method.value;
}
```

---

## Sort Order

Methods are returned already sorted by `sortOrder` ascending from the backend — no client-side sorting needed. Lower number = displayed first on the user page.
