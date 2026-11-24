// The hand-written layer of the configuration builder. The keys, defaults and descriptions come from the real files
// at sync time (scripts/config-catalog.mjs); this file only adds what JSON cannot say: which module owns a section,
// which values a key accepts, what is a secret, when a key matters, and the option-only keys worth exposing.
// tests/config-catalog.test.mjs fails when anything here names a key, enum member or section that no longer exists.

/** @typedef {'foundations' | 'connected' | 'samples' | 'api'} Family */
/**
 * @typedef {object} ModuleDef
 * @property {string} id
 * @property {string} name
 * @property {string} symbol  two-letter chart symbol
 * @property {Family} family
 * @property {number} [order]  ModuleOrder; the platform group has none
 * @property {string} url  docs route of the module
 * @property {string} [include]  template expression that includes the module; absent = always included
 * @property {string[]} sections  top-level configuration sections the module owns
 * @property {string} description
 */

/** Startup order, as on the module chart. Shipping has no configuration of its own. */
/** @type {ModuleDef[]} */
export const modules = [
  {
    id: 'Platform', name: 'Host & platform', symbol: 'Hs', family: 'api', url: '/architecture/overview/',
    sections: ['AppOptions', 'ConnectionStrings', 'Database', 'RequestTimeouts', 'Messaging', 'BotProtection', 'Hangfire', 'RateLimiting', 'ForwardedHeaders', 'Paging', 'Cors', 'Redis', 'ObservabilityOptions', 'HealthOptions', 'Logging', 'AllowedHosts'],
    description: 'Database, messaging, jobs, rate limits, proxies, cache and telemetry.',
  },
  {
    id: 'Identity', name: 'Identity', symbol: 'Id', family: 'foundations', order: 0, url: '/identity/',
    sections: ['JwtSettings', 'Authentication', 'IdentitySettings', 'Identity', 'LoginPolicy', 'PhoneVerification', 'SmsSettings', 'EmailConfirmation', 'PasswordReset', 'Application'],
    description: 'Tokens, sign-in, 2FA, passkeys, SSO and SMS.',
  },
  {
    id: 'Tenancy', name: 'Tenancy', symbol: 'Tn', family: 'foundations', order: 100, url: '/modules/tenancy/',
    sections: ['MultiTenancy'], description: 'Tenants, isolation, resolution and impersonation.',
  },
  {
    id: 'Attachment', name: 'Attachment', symbol: 'At', family: 'foundations', order: 200, url: '/attachment/',
    sections: ['Attachment'], description: 'File storage, validation, malware scanning and images.',
  },
  {
    id: 'Catalog', name: 'Catalog', symbol: 'Ct', family: 'samples', order: 300, url: '/modules/catalog/', include: 'sample',
    sections: ['Catalog'], description: 'The sample slice: products, categories and search.',
  },
  {
    id: 'Subscriptions', name: 'Subscriptions', symbol: 'Sb', family: 'foundations', order: 400, url: '/modules/subscriptions/',
    sections: ['Subscriptions', 'PaymentInstructions'], description: 'Plans, payment gateways, invoices and dunning.',
  },
  {
    id: 'Webhooks', name: 'Webhooks', symbol: 'Wh', family: 'connected', order: 500, url: '/modules/webhooks/',
    sections: ['Webhooks'], description: 'Outbound customer webhooks and retries.',
  },
  {
    id: 'DataTransfer', name: 'DataTransfer', symbol: 'Dt', family: 'connected', order: 600, url: '/modules/datatransfer/',
    sections: ['DataTransfer'], description: 'Bulk import/export jobs and their limits.',
  },
  {
    id: 'Search', name: 'Search', symbol: 'Se', family: 'connected', order: 700, url: '/modules/search/', include: 'search',
    sections: ['Search'], description: 'Full-text search on PostgreSQL or Meilisearch.',
  },
  {
    id: 'Shipping', name: 'Shipping', symbol: 'Sh', family: 'samples', order: 700, url: '/modules/shipping/', include: 'sample && agentic',
    sections: [], description: 'The agent sample. It has no settings of its own.',
  },
  {
    id: 'Agentic', name: 'Agentic', symbol: 'Ag', family: 'connected', order: 800, url: '/modules/agentic/', include: 'agentic',
    sections: ['Agentic'], description: 'AI agents: models, providers, limits, knowledge and MCP.',
  },
  {
    id: 'Notification', name: 'Notification', symbol: 'Nt', family: 'connected', order: 900, url: '/modules/notification/',
    sections: ['Email', 'Notification', 'Inbox', 'Realtime', 'Push', 'WhatsApp', 'whatsapp_access_token', 'whatsapp_phone_number_id'],
    description: 'Email, WhatsApp, the in-app inbox, real time and push.',
  },
];

/**
 * The template options the module picker offers, in the order `dotnet new starterkit` lists them. Their values,
 * defaults and descriptions are read from .template.config/template.json at sync time; these are the labels.
 */
export const templateOptions = [
  { symbol: 'sample', label: 'Catalog & Shipping samples', modules: ['Catalog', 'Shipping'] },
  { symbol: 'search', label: 'Search', modules: ['Search'] },
  { symbol: 'agentic', label: 'Agentic (AI agents)', modules: ['Agentic', 'Shipping'] },
  { symbol: 'multitenancy', label: 'Multi-tenancy' },
  { symbol: 'transport', label: 'Production message transport' },
  { symbol: 'storage', label: 'Attachment storage' },
];

/**
 * Target environments. The file is what ASP.NET Core layers over appsettings.json for that environment name.
 * `relaxed` mirrors ProductionConfigurationGuard: placeholders and InMemory messaging are tolerated there.
 */
export const environments = [
  { id: 'Development', label: 'Development', file: 'development', relaxed: true, hint: 'dotnet run, Aspire and the dev compose stack. Placeholders are tolerated.' },
  { id: 'Docker', label: 'Docker (Compose)', file: 'docker', relaxed: false, hint: 'The shipped deployment: docker-compose.yml runs the API with ASPNETCORE_ENVIRONMENT=Docker.' },
  { id: 'Production', label: 'Production (custom host)', file: null, relaxed: false, hint: 'Kubernetes, App Service or a VM with ASPNETCORE_ENVIRONMENT=Production: only appsettings.json is shipped.' },
];

/** Leaf names that hold credentials. Secrets are never exported to JSON or environment files. */
export const secretLeafPattern = /(^|:)(Password|SmtpPassword|SecretKey|SecretKeys|PreviousSecretKeys|Secret|ApiKey|SecretAccessKey|AuthToken|ClientSecret|AppSecret|AccessToken|HmacKey|OtpHmacKey|HostConnectionString|MigrationConnectionString)$/;

/**
 * @typedef {{ value: string, label?: string, hint?: string }} Option
 * @typedef {{ path: string, equals?: unknown, in?: unknown[], not?: unknown, truthy?: boolean }} Condition
 * @typedef {object} FieldSpec
 * @property {'enum' | 'multi' | 'ordered' | 'tags' | 'text' | 'number' | 'boolean' | 'timespan' | 'cron' | 'color'} [control]
 * @property {Option[]} [options]
 * @property {string} [optionsFrom]  dictionary whose entry names are the options (e.g. 'Agentic:Providers')
 * @property {string} [enumSource]  `src/...cs#EnumName`: the C# enum the options must equal (tested)
 * @property {'array' | 'csv'} [format]  how a multi-select is stored (default array)
 * @property {boolean} [secret]
 * @property {string} [secretFile]  Docker secret name when it is not in the Secrets.md table
 * @property {'random' | 'external'} [secretKind]  random secrets get an `openssl rand` line
 * @property {boolean} [optional]  a secret that may stay empty
 * @property {boolean} [essential]  shown without "Show all settings"
 * @property {Condition | Condition[]} [visibleWhen]  all must hold; `$1` is the first `*` of the pattern
 * @property {string} [hint]  one extra line under the description
 * @property {number} [min]
 * @property {number} [max]
 * @property {number} [step]
 */

const o = (...values) => values.map((value) => (typeof value === 'string' ? { value } : value));

/** Ordered: every matching pattern contributes, later entries win. `*` matches one key segment. */
/** @type {[string, FieldSpec][]} */
export const fields = [
  // ── Host & platform ──
  ['AppOptions:Name', { essential: true }],
  ['ConnectionStrings:starterkit', { essential: true, secret: true, secretFile: 'postgres_password', secretKind: 'random', hint: 'In Docker the connection string is built from POSTGRES_HOST/PORT/DB/USER and the postgres_password secret.' }],
  ['ConnectionStrings:seq', { essential: true }],
  ['Database:MigrateOnStartup', { essential: true, hint: 'Production applies migrations with the one-shot --migrate-only run, not at boot.' }],
  ['Database:MigrationConnectionString', { essential: true, secret: true, secretFile: 'migration_connection_string', secretKind: 'external', optional: true, hint: 'Optional: a privileged connection for --migrate-only. Empty uses the runtime connection.' }],
  ['Messaging:Transport', { essential: true, control: 'enum', enumSource: 'src/Shared/Shared.Messaging/Extensions/MessagingOptions.cs#MessagingTransport', options: o(
    { value: 'InMemory', label: 'In memory', hint: 'Dev and tests only. Not durable: refused outside Development.' },
    { value: 'PostgreSql', label: 'PostgreSQL', hint: 'Durable queues in the existing database. The production default.' },
    { value: 'RabbitMq', label: 'RabbitMQ', hint: 'A broker. Add docker-compose.rabbitmq.yml.' }) }],
  ['Messaging:PostgreSql:*', { visibleWhen: { path: 'Messaging:Transport', equals: 'PostgreSql' } }],
  ['Messaging:RabbitMq:*', { visibleWhen: { path: 'Messaging:Transport', equals: 'RabbitMq' } }],
  ['Messaging:RabbitMq:Host', { essential: true }],
  ['Messaging:RabbitMq:Password', { essential: true, secretKind: 'random' }],
  ['BotProtection:Enabled', { essential: true }],
  ['BotProtection:*', { visibleWhen: { path: 'BotProtection:Enabled', truthy: true } }],
  ['BotProtection:Enabled', { visibleWhen: [] }],
  ['BotProtection:Provider', { essential: true, control: 'enum', enumSource: 'src/Shared/Shared.Core/BotProtection/BotProtectionOptions.cs#BotProtectionProvider', options: o({ value: 'None', hint: 'No challenge.' }, { value: 'Turnstile', label: 'Cloudflare Turnstile' }) }],
  ['BotProtection:SiteKey', { essential: true }],
  ['BotProtection:SecretKey', { essential: true, secretKind: 'external' }],
  ['BotProtection:AllowedHostnames', { essential: true, control: 'tags' }],
  ['BotProtection:Actions', { control: 'multi', options: o('register', 'login', 'forgot-password', 'otp-resend'), hint: 'Empty = every protected action.' }],
  ['Hangfire:Servers:*:Enabled', { essential: true }],
  ['Hangfire:Servers:*:WorkerCount', { essential: true, min: 1, max: 100 }],
  ['Hangfire:Servers:*:WorkerCount', { visibleWhen: { path: 'Hangfire:Servers:$1:Enabled', truthy: true } }],
  ['Hangfire:DashboardAuth:Enabled', { essential: true }],
  ['Hangfire:DashboardAuth:Username', { essential: true, visibleWhen: { path: 'Hangfire:DashboardAuth:Enabled', truthy: true } }],
  ['Hangfire:DashboardAuth:Password', { essential: true, secretKind: 'random', visibleWhen: { path: 'Hangfire:DashboardAuth:Enabled', truthy: true } }],
  ['Hangfire:DashboardAuth:AllowLocalRequestsWithoutAuthentication', { visibleWhen: { path: 'Hangfire:DashboardAuth:Enabled', truthy: true } }],
  ['RateLimiting:Policies:*:PermitLimit', { essential: true, min: 0 }],
  ['RateLimiting:Policies:*:PartitionBy', { control: 'enum', enumSource: 'src/Shared/Shared.Core/Extensions/RateLimitingExtensions.cs#RateLimitPartitionBy', options: o(
    { value: 'UserOrIp', label: 'User, else IP' }, { value: 'Ip', label: 'IP address' }, { value: 'IpAndUser', label: 'IP and user' }, { value: 'Tenant', label: 'Tenant' }) }],
  ['ForwardedHeaders:Enabled', { essential: true }],
  ['ForwardedHeaders:*', { visibleWhen: { path: 'ForwardedHeaders:Enabled', truthy: true } }],
  ['ForwardedHeaders:Enabled', { visibleWhen: [] }],
  ['ForwardedHeaders:KnownProxies', { essential: true, control: 'tags', hint: 'IP addresses of your reverse proxies, e.g. 10.0.0.5.' }],
  ['ForwardedHeaders:KnownNetworks', { essential: true, control: 'tags', hint: 'CIDR ranges, e.g. 10.0.0.0/8.' }],
  ['ForwardedHeaders:TrustPrivateNetworks', { essential: true }],
  ['Cors:AllowedOrigins', { essential: true, control: 'tags', hint: 'Full origins: scheme, host and port, no path.' }],
  ['Cors:AllowCredentials', { essential: true }],
  ['Cors:AllowedMethods', { control: 'tags' }],
  ['Cors:AllowedHeaders', { control: 'tags' }],
  ['Redis:Enabled', { essential: true }],
  ['Redis:*', { visibleWhen: { path: 'Redis:Enabled', truthy: true } }],
  ['Redis:Enabled', { visibleWhen: [] }],
  ['Redis:Host', { essential: true }],
  ['Redis:Port', { essential: true }],
  ['Redis:Password', { essential: true, secretFile: 'redis_password', secretKind: 'random' }],
  ['Redis:UseSsl', { essential: true }],
  ['ObservabilityOptions:ServiceName', { essential: true }],
  ['ObservabilityOptions:UsePrometheusExporter', { essential: true }],
  ['ObservabilityOptions:UseOTLPExporter', { essential: true }],
  ['ObservabilityOptions:OTLPOptions:*', { essential: true, visibleWhen: { path: 'ObservabilityOptions:UseOTLPExporter', truthy: true } }],
  ['ObservabilityOptions:JaegerOptions:*', { visibleWhen: { path: 'ObservabilityOptions:UseJaegerExporter', truthy: true } }],
  ['ObservabilityOptions:ZipkinOptions:*', { visibleWhen: { path: 'ObservabilityOptions:UseZipkinExporter', truthy: true } }],
  ['Logging:LogLevel:*', { control: 'enum', options: o('Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical', 'None') }],
  ['Logging:LogLevel:Default', { essential: true }],
  ['AllowedHosts', { essential: true, hint: 'Semicolon-separated host names; * accepts any.' }],

  // ── Identity ──
  ['JwtSettings:SecretKey', { essential: true, secretFile: 'jwt_secret', secretKind: 'random', hint: 'At least 32 characters.' }],
  ['JwtSettings:PreviousSecretKeys', { secretKind: 'external', optional: true, hint: 'Old keys still accepted while tokens signed with them expire.' }],
  ['JwtSettings:Issuer', { essential: true }],
  ['JwtSettings:Audience', { essential: true }],
  ['JwtSettings:AccessTokenExpirationMinutes', { essential: true, min: 1 }],
  ['JwtSettings:RefreshTokenExpirationDays', { essential: true, min: 1 }],
  ['Authentication:Google:Enabled', { essential: true }],
  ['Authentication:Google:ClientId', { essential: true, visibleWhen: { path: 'Authentication:Google:Enabled', truthy: true } }],
  ['Authentication:Google:ClientSecret', { essential: true, secretKind: 'external', visibleWhen: { path: 'Authentication:Google:Enabled', truthy: true } }],
  ['Authentication:Facebook:Enabled', { essential: true }],
  ['Authentication:Facebook:AppId', { essential: true, visibleWhen: { path: 'Authentication:Facebook:Enabled', truthy: true } }],
  ['Authentication:Facebook:AppSecret', { essential: true, secretKind: 'external', visibleWhen: { path: 'Authentication:Facebook:Enabled', truthy: true } }],
  ['IdentitySettings:SignIn:*', { essential: true }],
  ['IdentitySettings:Password:RequiredLength', { essential: true, min: 6, max: 128 }],
  ['Identity:OtpHmacKey', { essential: true, secretFile: 'identity_otp_hmac_key', secretKind: 'random', hint: 'Required outside Development: a random secret of at least 32 characters.' }],
  ['Identity:TwoFactor:RequiredForRoles', { essential: true, control: 'multi', options: o('SuperAdmin', 'Admin', 'Finance', 'Support', 'User') }],
  ['Identity:TwoFactor:EnforcementMode', { essential: true, control: 'enum', enumSource: 'src/Modules/Identity/Identity/Options/TwoFactorOptions.cs#TwoFactorEnforcementMode', options: o(
    { value: '', label: 'Default', hint: 'Enforce outside Development and Testing, Warn there.' }, { value: 'Warn', hint: 'Privileged users without 2FA are warned.' }, { value: 'Enforce', hint: 'Privileged users must enrol before they can work.' }) }],
  ['Identity:Passkeys:Enabled', { essential: true }],
  ['Identity:Passkeys:*', { visibleWhen: { path: 'Identity:Passkeys:Enabled', truthy: true } }],
  ['Identity:Passkeys:Enabled', { visibleWhen: [] }],
  ['Identity:Passkeys:ServerDomain', { essential: true, hint: 'The relying-party id: your site\'s registrable domain, without scheme.' }],
  ['Identity:Passkeys:AllowedOrigins', { essential: true, control: 'tags' }],
  ['Identity:Sso:Enabled', { essential: true }],
  ['Identity:Sso:*', { visibleWhen: { path: 'Identity:Sso:Enabled', truthy: true } }],
  ['Identity:Sso:Enabled', { visibleWhen: [] }],
  ['Identity:Sso:PublicBaseUrl', { essential: true }],
  ['Identity:Sso:AllowedReturnOrigins', { essential: true, control: 'tags' }],
  ['Identity:DataProtection:KeyEncryption', { essential: true, control: 'enum', enumSource: 'src/Modules/Identity/Identity/Options/DataProtectionKeyOptions.cs#DataProtectionKeyEncryption', options: o(
    { value: '', label: 'Default', hint: 'Certificate when one is configured, otherwise none.' }, { value: 'None' }, { value: 'Certificate' }, { value: 'AzureKeyVault', label: 'Azure Key Vault' }, { value: 'AwsKms', label: 'AWS KMS' }) }],
  ['Identity:DataProtection:RequireKeyEncryption', { essential: true }],
  ['Identity:DataProtection:Certificate:*', { visibleWhen: { path: 'Identity:DataProtection:KeyEncryption', in: ['', 'Certificate'] } }],
  ['Identity:DataProtection:Certificate:StoreLocation', { control: 'enum', options: o('CurrentUser', 'LocalMachine') }],
  ['Identity:DataProtection:Certificate:PreviousPaths', { control: 'tags' }],
  ['SmsSettings:Provider', { essential: true, control: 'enum', enumSource: 'src/Modules/Identity/Identity/Options/SmsOptions.cs#SmsProvider', options: o(
    { value: 'Mock', hint: 'Logs the code. Development only.' }, { value: 'Twilio' }, { value: 'AwsSns', label: 'AWS SNS' }) }],
  ['SmsSettings:Twilio:*', { essential: true, visibleWhen: { path: 'SmsSettings:Provider', equals: 'Twilio' } }],
  ['SmsSettings:AwsSns:*', { essential: true, visibleWhen: { path: 'SmsSettings:Provider', equals: 'AwsSns' } }],
  ['SmsSettings:Twilio:AuthToken', { secretKind: 'external' }],
  ['SmsSettings:AwsSns:SecretKey', { secretKind: 'external' }],
  ['LoginPolicy:*', { essential: true }],
  ['Application:FrontendBaseUrl', { essential: true }],
  ['EmailConfirmation:FrontendBaseUrl', { essential: true }],
  ['PasswordReset:FrontendBaseUrl', { essential: true }],

  // ── Tenancy ──
  ['MultiTenancy:Enabled', { essential: true, hint: 'Set by the multitenancy template option; it can be switched later.' }],
  ['MultiTenancy:*', { visibleWhen: { path: 'MultiTenancy:Enabled', truthy: true } }],
  ['MultiTenancy:Enabled', { visibleWhen: [] }],
  ['MultiTenancy:DefaultIsolation', { essential: true, control: 'enum', enumSource: 'src/Shared/Shared.Core/MultiTenancy/TenantModels.cs#TenantIsolation', options: o(
    { value: 'Shared', hint: 'All tenants in one database, isolated by filters and row-level security.' }, { value: 'Dedicated', hint: 'A database per tenant.' }) }],
  ['MultiTenancy:SuspendedTenantMode', { essential: true, control: 'enum', enumSource: 'src/Shared/Shared.Core/MultiTenancy/TenantModels.cs#SuspendedTenantMode', options: o(
    { value: 'Block', hint: 'Every request of a suspended tenant is refused.' }, { value: 'ReadOnly', label: 'Read only', hint: 'Reads work, writes are refused.' }) }],
  ['MultiTenancy:AllowSelfServiceSignup', { essential: true }],
  ['MultiTenancy:Resolution:RootDomain', { essential: true, hint: 'e.g. api.example.com makes acme.api.example.com tenant "acme". Empty turns subdomains off.' }],
  ['MultiTenancy:Resolution:EnableHeader', { essential: true }],
  ['MultiTenancy:Resolution:EnableHost', { essential: true }],
  ['MultiTenancy:Database:EnableRowLevelSecurity', { essential: true }],
  ['MultiTenancy:Database:RlsBypass', { essential: true, control: 'enum', enumSource: 'src/Shared/Shared.Core/MultiTenancy/MultiTenancyOptions.cs#RlsBypassMode', options: o(
    { value: 'SessionVariable', label: 'Session variable', hint: 'Works with one database role.' }, { value: 'Role', label: 'BYPASSRLS role', hint: 'Recommended in production: the runtime role cannot lift RLS.' }) }],
  ['MultiTenancy:Database:HostConnectionString', { essential: true, secretKind: 'external', visibleWhen: { path: 'MultiTenancy:Database:RlsBypass', equals: 'Role' } }],
  ['MultiTenancy:Database:DedicatedDatabaseNameFormat', { visibleWhen: { path: 'MultiTenancy:DefaultIsolation', equals: 'Dedicated' } }],
  ['MultiTenancy:Database:MaxDedicatedPoolSize', { visibleWhen: { path: 'MultiTenancy:DefaultIsolation', equals: 'Dedicated' } }],
  ['MultiTenancy:Database:MigrateDedicatedOnStartup', { visibleWhen: { path: 'MultiTenancy:DefaultIsolation', equals: 'Dedicated' } }],
  ['MultiTenancy:Impersonation:Enabled', { essential: true }],
  ['MultiTenancy:Impersonation:MaxMinutes', { visibleWhen: { path: 'MultiTenancy:Impersonation:Enabled', truthy: true } }],
  ['MultiTenancy:Cors:FrontendRootDomain', { essential: true }],

  // ── Attachment ──
  ['Attachment:Storage:Provider', { essential: true, control: 'enum', enumSource: 'src/Modules/Attachment/Attachment/Infrastructure/Configuration/AttachmentOptions.cs#StorageProvider', options: o(
    { value: 'Local', label: 'Local disk', hint: 'A mounted volume. Fine for one server.' }, { value: 'AzureBlob', label: 'Azure Blob Storage' }, { value: 'S3', label: 'Amazon S3 (or compatible)' }) }],
  ['Attachment:Storage:Local:*', { essential: true, visibleWhen: { path: 'Attachment:Storage:Provider', equals: 'Local' } }],
  ['Attachment:Storage:AzureBlob:*', { essential: true, visibleWhen: { path: 'Attachment:Storage:Provider', equals: 'AzureBlob' } }],
  ['Attachment:Storage:AzureBlob:ConnectionString', { secret: true, secretKind: 'external' }],
  ['Attachment:Storage:S3:*', { visibleWhen: { path: 'Attachment:Storage:Provider', equals: 'S3' } }],
  ['Attachment:Storage:S3:BucketName', { essential: true }],
  ['Attachment:Storage:S3:Region', { essential: true }],
  ['Attachment:Storage:S3:AccessKeyId', { essential: true, hint: 'Leave empty to use the instance role or AWS_* environment variables.' }],
  ['Attachment:Storage:S3:SecretAccessKey', { essential: true, secretKind: 'external' }],
  ['Attachment:Storage:S3:UsePublicAccess', { essential: true }],
  ['Attachment:Storage:S3:CloudFrontDomain', { essential: true }],
  ['Attachment:Storage:S3:StorageClass', { control: 'enum', options: o('STANDARD', 'INTELLIGENT_TIERING', 'STANDARD_IA', 'ONEZONE_IA', 'GLACIER_IR') }],
  ['Attachment:Validation:MaxFileSizeBytes', { essential: true }],
  ['Attachment:Validation:AllowedExtensions', { essential: true, control: 'tags' }],
  ['Attachment:Validation:BlockedExtensions', { control: 'tags' }],
  ['Attachment:Validation:Allowed*Types', { control: 'tags' }],
  ['Attachment:Validation:EnableVirusScanning', { essential: true }],
  ['Attachment:Validation:RequireVirusScanning', { essential: true, hint: 'Recommended in production. Uploads fail closed when no scanner answers.' }],
  ['Attachment:Validation:Scanner:Provider', { essential: true, control: 'enum', enumSource: 'src/Modules/Attachment/Attachment/Infrastructure/Configuration/AttachmentOptions.cs#MalwareScannerProvider', options: o(
    { value: 'None', hint: 'No scanning.' }, { value: 'ClamAv', label: 'ClamAV (clamd)', hint: 'Add docker-compose.clamav.yml.' }) }],
  ['Attachment:Validation:Scanner:*', { visibleWhen: { path: 'Attachment:Validation:Scanner:Provider', equals: 'ClamAv' } }],
  ['Attachment:Validation:Scanner:Provider', { visibleWhen: [] }],
  ['Attachment:Validation:Scanner:Host', { essential: true }],
  ['Attachment:Validation:Scanner:Port', { essential: true }],
  ['Attachment:Image:Thumbnail*', { visibleWhen: { path: 'Attachment:Image:EnableThumbnailGeneration', truthy: true } }],

  // ── Catalog ──
  ['Catalog:*', { essential: true }],
  ['Catalog:Search:*', { essential: true }],

  // ── Subscriptions ──
  ['Subscriptions:Pdf:License', { essential: true, control: 'enum', options: o(
    { value: 'Community', hint: 'QuestPDF Community: free under its revenue threshold.' }, { value: 'Professional' }, { value: 'Enterprise' }) }],
  ['Subscriptions:PaymentGateways:*:Enabled', { essential: true }],
  ['Subscriptions:PaymentGateways:*:*', { visibleWhen: { path: 'Subscriptions:PaymentGateways:$1:Enabled', truthy: true } }],
  ['Subscriptions:PaymentGateways:*:Enabled', { visibleWhen: [] }],
  ['Subscriptions:PaymentGateways:*:Mode', { essential: true, control: 'enum', enumSource: 'src/Modules/Subscriptions/Subscriptions/Options/SubscriptionOptions.cs#PaymentGatewayMode', options: o(
    { value: 'Sandbox', hint: 'Test keys only.' }, { value: 'Live', hint: 'Live keys only.' }) }],
  ['Subscriptions:PaymentGateways:*:ApiKey', { essential: true, secretKind: 'external' }],
  ['Subscriptions:PaymentGateways:*:Secret', { essential: true, secretKind: 'external' }],
  ['Subscriptions:PaymentGateways:*:PublicKey', { essential: true }],
  ['Subscriptions:PaymentGateways:*:AllowSandboxInProduction', { essential: true }],
  ['Subscriptions:PaymentGateways:*:IntegrationIds', { control: 'tags' }],
  ['Subscriptions:PaymentGateways:*:NotificationUrl', { essential: true }],
  ['Subscriptions:GracePeriodDays', { essential: true }],
  ['Subscriptions:Tax:*', { essential: true }],
  ['Subscriptions:Tax:Rate', { min: 0, max: 1, step: 0.01, hint: 'A fraction: 0.15 is 15%.' }],
  ['Subscriptions:Dunning:Enabled', { essential: true }],
  ['Subscriptions:Dunning:ScheduleDays', { control: 'tags', visibleWhen: { path: 'Subscriptions:Dunning:Enabled', truthy: true } }],
  ['Subscriptions:Dunning:FinalNoticeDaysBeforeGraceEnd', { visibleWhen: { path: 'Subscriptions:Dunning:Enabled', truthy: true } }],
  ['Subscriptions:Invoice:*', { essential: true }],
  ['PaymentInstructions:*', { essential: true }],

  // ── Webhooks / DataTransfer ──
  ['Webhooks:RequireHttps', { essential: true }],
  ['Webhooks:AllowPrivateNetworks', { essential: true, hint: 'Keep false in production: endpoints could reach your internal network.' }],
  ['Webhooks:MaxAttempts', { essential: true }],
  ['Webhooks:MaxEndpointsPerTenant', { essential: true }],
  ['DataTransfer:BatchSize', { essential: true }],
  ['DataTransfer:Limits:MaxFileBytes', { essential: true }],
  ['DataTransfer:Limits:MaxRows', { essential: true }],
  ['DataTransfer:Limits:MaxExportRows', { essential: true }],

  // ── Search ──
  ['Search:Engine', { essential: true, control: 'enum', enumSource: 'src/Modules/Search/Search/Options/SearchOptions.cs#SearchEngineKind', options: o(
    { value: 'Postgres', label: 'PostgreSQL', hint: 'Full-text search in the existing database. No new service.' }, { value: 'Meilisearch', hint: 'A search server. Add docker-compose.meilisearch.yml.' }) }],
  ['Search:Postgres:*', { visibleWhen: { path: 'Search:Engine', equals: 'Postgres' } }],
  ['Search:Meilisearch:*', { essential: true, visibleWhen: { path: 'Search:Engine', equals: 'Meilisearch' } }],
  ['Search:Meilisearch:ApiKey', { secretKind: 'external', hint: 'A scoped key, never the master key.' }],
  ['Search:Query:Fuzzy:Enabled', { essential: true }],
  ['Search:Query:Fuzzy:*', { visibleWhen: { path: 'Search:Query:Fuzzy:Enabled', truthy: true } }],
  ['Search:Query:Fuzzy:Enabled', { visibleWhen: [] }],
  ['Search:Analytics:Enabled', { essential: true }],
  ['Search:Analytics:*', { visibleWhen: { path: 'Search:Analytics:Enabled', truthy: true } }],
  ['Search:Analytics:Enabled', { visibleWhen: [] }],

  // ── Agentic ──
  ['Agentic:Enabled', { essential: true, hint: 'The module is included either way. This turns agent runs on.' }],
  ['Agentic:*', { visibleWhen: { path: 'Agentic:Enabled', truthy: true } }],
  ['Agentic:Enabled', { visibleWhen: [] }],
  ['Agentic:DefaultModel', { essential: true, control: 'enum', optionsFrom: 'Agentic:Models' }],
  ['Agentic:Models:*:Provider', { essential: true, control: 'enum', optionsFrom: 'Agentic:Providers' }],
  ['Agentic:Models:*:ModelId', { essential: true }],
  ['Agentic:Models:*:MaxOutputTokens', { essential: true }],
  ['Agentic:Providers:*:Kind', { essential: true, control: 'enum', enumSource: 'src/Modules/Agentic/Agentic/Options/AgenticOptions.cs#AgentProviderKind', options: o(
    { value: 'Scripted', hint: 'Deterministic scripts for tests and demos. Refused in Production.' },
    { value: 'OpenAiCompatible', label: 'OpenAI-compatible', hint: 'OpenAI, Azure OpenAI v1, Ollama, vLLM...' },
    { value: 'Anthropic' }) }],
  ['Agentic:Providers:*:Endpoint', { essential: true, visibleWhen: { path: 'Agentic:Providers:$1:Kind', not: 'Scripted' } }],
  ['Agentic:Providers:*:ApiKey', { essential: true, secretKind: 'external', visibleWhen: { path: 'Agentic:Providers:$1:Kind', not: 'Scripted' } }],
  ['Agentic:Telemetry:CaptureContent', { essential: true, hint: 'Logs prompts and tool content. Keep off unless you are debugging.' }],
  ['Agentic:Approvals:RequireDifferentApprover', { essential: true }],
  ['Agentic:Knowledge:Enabled', { essential: true }],
  ['Agentic:Knowledge:*', { visibleWhen: [{ path: 'Agentic:Enabled', truthy: true }, { path: 'Agentic:Knowledge:Enabled', truthy: true }] }],
  ['Agentic:Knowledge:Enabled', { visibleWhen: { path: 'Agentic:Enabled', truthy: true } }],
  ['Agentic:Knowledge:EmbeddingProvider', { essential: true, control: 'enum', optionsFrom: 'Agentic:Providers' }],
  ['Agentic:Knowledge:VectorIndex', { essential: true, control: 'enum', enumSource: 'src/Modules/Agentic/Agentic/Runtime/Knowledge/KnowledgeVectorIndexStep.cs#KnowledgeVectorIndexKind', options: o(
    { value: 'None', hint: 'Exact search. Fine for small corpora.' }, { value: 'Hnsw', label: 'HNSW', hint: 'An approximate index for large corpora.' }) }],
  ['Agentic:Mcp:Enabled', { essential: true }],
  ['Agentic:Streaming:Enabled', { essential: true }],

  // ── Notification ──
  ['Email:Provider', { essential: true, control: 'enum', enumSource: 'src/Modules/Notification/Notification/Options/EmailOptions.cs#EmailProvider', options: o(
    { value: 'Auto', hint: 'SMTP when a host is configured, otherwise no-op.' }, { value: 'Smtp', label: 'SMTP' }, { value: 'NoOp', label: 'No-op', hint: 'Nothing is sent.' }) }],
  ['Email:Smtp*', { essential: true, visibleWhen: { path: 'Email:Provider', not: 'NoOp' } }],
  ['Email:UseTls', { essential: true, visibleWhen: { path: 'Email:Provider', not: 'NoOp' } }],
  ['Email:SmtpPassword', { secretFile: 'smtp_password', secretKind: 'external' }],
  ['Email:From*', { essential: true }],
  ['Email:BrandName', { essential: true }],
  ['Email:AppBaseUrl', { essential: true }],
  ['Email:SupportEmail', { essential: true }],
  ['Email:AllowedLinkHosts', { control: 'tags' }],
  ['Email:Theme:*Color', { control: 'color' }],
  ['Email:Theme:PrimaryColor', { essential: true }],
  ['Email:Theme:AccentColor', { essential: true }],
  ['Email:Theme:LogoUrl', { essential: true }],
  ['Notification:DefaultChannelOrder', { essential: true, control: 'ordered', options: o(
    { value: 'WhatsApp' }, { value: 'Email' }, { value: 'Sms', label: 'SMS' }), hint: 'The fallback chain: the next channel is tried when one fails. In-app and push are added on top, not chained.' }],
  ['Realtime:Enabled', { essential: true }],
  ['Realtime:*', { visibleWhen: { path: 'Realtime:Enabled', truthy: true } }],
  ['Realtime:Enabled', { visibleWhen: [] }],
  ['Realtime:Transports', { essential: true, control: 'multi', format: 'csv', options: o({ value: 'WebSockets' }, { value: 'ServerSentEvents', label: 'Server-sent events' }, { value: 'LongPolling', label: 'Long polling' }) }],
  ['Realtime:UseRedisBackplane', { essential: true }],
  ['Push:Provider', { essential: true, control: 'enum', enumSource: 'src/Modules/Notification/Notification/Options/PushOptions.cs#PushProvider', options: o(
    { value: 'Auto', hint: 'FCM when a project id and credentials are configured, otherwise no-op.' }, { value: 'Fcm', label: 'Firebase Cloud Messaging' }, { value: 'NoOp', label: 'No-op' }) }],
  ['Push:ProjectId', { essential: true, visibleWhen: { path: 'Push:Provider', not: 'NoOp' } }],
  ['Push:Apns:Enabled', { essential: true }],
  ['Push:Apns:*', { visibleWhen: { path: 'Push:Apns:Enabled', truthy: true } }],
  ['Push:Apns:Enabled', { visibleWhen: [] }],
  ['WhatsApp:PhoneNumberId', { essential: true }],
  ['WhatsApp:AccessToken', { essential: true, secretKind: 'external' }],
  ['WhatsApp:Templates:*:LanguageCode', { hint: 'A Meta locale, e.g. en_US or ar.' }],
  ['WhatsApp:Templates:*:BodyParameters', { control: 'tags' }],
  ['whatsapp_access_token', { secret: true }],
];

/**
 * Keys the options classes read but appsettings.json does not list. They start unset (the option default applies)
 * and are exported only when changed.
 * @type {{ path: string, default: unknown, description: string, guide: string }[]}
 */
export const extras = [
  { path: 'Identity:OtpHmacKey', default: '', description: 'HMAC key used to hash one-time codes before they are stored.', guide: '/identity/' },
  { path: 'Identity:TwoFactor:EnforcementMode', default: '', description: 'Whether privileged roles without 2FA are warned or blocked.', guide: '/identity/permissions-2fa-and-admin-audit/' },
  { path: 'Identity:DataProtection:KeyEncryption', default: '', description: 'How the ASP.NET Core data-protection key ring is encrypted at rest.', guide: '/deployment/secrets/' },
  { path: 'MultiTenancy:Database:RlsBypass', default: 'SessionVariable', description: 'How host-scope work bypasses row-level security.', guide: '/multitenancy/isolation-and-dedicated-databases/' },
  { path: 'MultiTenancy:Database:HostConnectionString', default: '', description: 'Role mode only: the connection string of the BYPASSRLS host role.', guide: '/multitenancy/isolation-and-dedicated-databases/' },
  { path: 'Subscriptions:PaymentGateways:Stripe:AllowSandboxInProduction', default: false, description: 'Allow a Sandbox gateway in Production (a demo stack).', guide: '/subscriptions/online-payments/' },
  { path: 'Subscriptions:PaymentGateways:Paymob:Mode', default: 'Sandbox', description: 'Sandbox takes test keys only; Live takes live keys only.', guide: '/subscriptions/online-payments/' },
  { path: 'Subscriptions:PaymentGateways:Paymob:AllowSandboxInProduction', default: false, description: 'Allow a Sandbox gateway in Production (a demo stack).', guide: '/subscriptions/online-payments/' },
];

/**
 * Named collections a user may add entries to. `item` is the shape of a new entry (the option defaults).
 * @type {Record<string, { label: string, placeholder: string, item: Record<string, unknown>, removable?: boolean }>}
 */
export const dictionaries = {
  'Agentic:Providers': { label: 'provider', placeholder: 'openai', removable: true, item: { Kind: 'OpenAiCompatible', Endpoint: '', ApiKey: '', TimeoutSeconds: 120, MaxRetries: 2 } },
  'Agentic:Models': { label: 'model', placeholder: 'gpt', removable: true, item: { Provider: '', ModelId: '', MaxOutputTokens: 4096, InputPricePerMillionTokens: 0, OutputPricePerMillionTokens: 0, Streaming: true } },
  'WhatsApp:Templates': { label: 'template', placeholder: 'order_shipped', removable: true, item: { MetaTemplateName: '', LanguageCode: 'en_US', BodyParameters: [] } },
};

/**
 * Secrets a deployment needs even when no field shows them, because the base value is a placeholder or the option
 * validator requires them outside Development.
 */
export const requiredSecrets = ['JwtSettings:SecretKey', 'Identity:OtpHmacKey', 'ConnectionStrings:starterkit'];
