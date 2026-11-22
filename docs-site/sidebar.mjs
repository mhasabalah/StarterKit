// Explicit inventory: new wiki pages must be deliberately placed in the reader journey.
// ADR 0000 is last: it is an authoring aid, not an architectural decision.
export const sections = [
  {
    "label": "Start here",
    "items": [
      {
        "source": "README.md",
        "label": "Overview",
        "order": 1
      },
      {
        "source": "Getting-Started/New-Project.md",
        "label": "New Project",
        "order": 2
      },
      {
        "source": "Getting-Started/Templates.md",
        "label": "Templates",
        "order": 3
      },
      {
        "source": "Getting-Started/Changing-The-Template.md",
        "label": "Changing The Template",
        "order": 4
      },
      {
        "source": "Reference/Features.md",
        "label": "Feature catalog",
        "order": 5
      }
    ]
  },
  {
    "label": "Concepts",
    "items": [
      {
        "label": "Architecture",
        "items": [
          {
            "source": "Architecture/Overview.md",
            "label": "Overview",
            "order": 1
          },
          {
            "source": "Architecture/Request-Lifecycle.md",
            "label": "Request lifecycle",
            "order": 2
          },
          {
            "source": "Architecture/Errors-and-Responses.md",
            "label": "Errors and responses",
            "order": 3
          },
          {
            "source": "Architecture/Concurrency-and-Versions.md",
            "label": "Concurrency and versions",
            "order": 4
          },
          {
            "source": "Architecture/ProjectStructure.md",
            "label": "Project Structure",
            "order": 5
          },
          {
            "source": "Architecture/DesignPatterns.md",
            "label": "Design Patterns",
            "order": 6
          },
          {
            "source": "Architecture/Messaging.md",
            "label": "Messaging",
            "order": 7
          },
          {
            "source": "Architecture/DatabaseConventions.md",
            "label": "Database Conventions",
            "order": 8
          },
          {
            "source": "Architecture/ApiGuidelines.md",
            "label": "API guidelines",
            "order": 9
          },
          {
            "source": "Architecture/CodingStandards.md",
            "label": "Coding Standards",
            "order": 10
          },
          {
            "source": "Architecture/Observability.md",
            "label": "Observability",
            "order": 11
          },
          {
            "source": "Architecture/Feature-Flags.md",
            "label": "Feature Flags",
            "order": 12
          },
          {
            "source": "Architecture/TechnologyStack.md",
            "label": "Technology Stack",
            "order": 13
          },
          {
            "source": "Architecture/Agent-Security.md",
            "label": "Agent Security Model",
            "order": 14
          }
        ]
      },
      {
        "label": "Multi-tenancy",
        "items": [
          {
            "source": "MultiTenancy/Overview.md",
            "label": "Overview",
            "order": 1
          },
          {
            "source": "MultiTenancy/Branches-and-Scoping.md",
            "label": "Branches and Scoping",
            "order": 2
          },
          {
            "source": "MultiTenancy/Isolation-and-Dedicated-Databases.md",
            "label": "Isolation and Dedicated Databases",
            "order": 3
          },
          {
            "source": "MultiTenancy/Operations.md",
            "label": "Operations",
            "order": 4
          },
          {
            "source": "MultiTenancy/Adding-a-Tenant-Aware-Module.md",
            "label": "Adding a Tenant Aware Module",
            "order": 5
          }
        ]
      }
    ]
  },
  {
    "label": "Modules",
    "items": [
      {
        "source": "Modules/Catalog.md",
        "label": "Catalog (reference)",
        "order": 1
      },
      {
        "source": "Modules/Tenancy.md",
        "label": "Tenancy",
        "order": 2
      },
      {
        "source": "Modules/Subscriptions.md",
        "label": "Subscriptions",
        "order": 3
      },
      {
        "source": "Modules/Notification.md",
        "label": "Notification",
        "order": 4
      },
      {
        "source": "Modules/Notification-Realtime-and-Push.md",
        "label": "Notification Realtime and Push",
        "order": 5
      },
      {
        "source": "Modules/Notification-Email-Design-System.md",
        "label": "Notification Email Design System",
        "order": 6
      },
      {
        "source": "Modules/Webhooks.md",
        "label": "Webhooks",
        "order": 7
      },
      {
        "source": "Modules/DataTransfer.md",
        "label": "Data Transfer",
        "order": 8
      },
      {
        "source": "Modules/Agentic.md",
        "label": "Agentic",
        "order": 9
      },
      {
        "source": "Modules/Shipping.md",
        "label": "Shipping (agent sample)",
        "order": 10
      },
      {
        "label": "Identity",
        "items": [
          {
            "source": "Identity/README.md",
            "label": "Overview",
            "order": 1
          },
          {
            "source": "Identity/Users-and-Admins-API.md",
            "label": "Users and Admins API",
            "order": 2
          },
          {
            "source": "Identity/Permissions-2FA-and-Admin-Audit.md",
            "label": "Permissions 2FA and Admin Audit",
            "order": 3
          },
          {
            "source": "Identity/Passkeys.md",
            "label": "Passkeys",
            "order": 4
          },
          {
            "source": "Identity/Enterprise-SSO.md",
            "label": "Enterprise SSO",
            "order": 5
          },
          {
            "source": "Identity/SCIM.md",
            "label": "SCIM",
            "order": 6
          },
          {
            "source": "Identity/Frontend-Integration.md",
            "label": "Frontend Integration",
            "order": 7
          },
          {
            "source": "Identity/Bot-Protection.md",
            "label": "Bot Protection",
            "order": 8
          },
          {
            "source": "Identity/Replacing-the-Identity-Provider.md",
            "label": "Replacing the Identity Provider",
            "order": 9
          },
          {
            "source": "Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md",
            "label": "JWT technical guide",
            "order": 10
          },
          {
            "source": "Identity/jwt/JWT-AUTH-FRONTEND-GUIDE.md",
            "label": "JWT frontend guide",
            "order": 11
          },
          {
            "source": "Identity/jwt/TokenRefreshStrategies.md",
            "label": "Token Refresh Strategies",
            "order": 12
          }
        ]
      },
      {
        "label": "Attachments",
        "items": [
          {
            "source": "Attachment/README.md",
            "label": "Overview",
            "order": 1
          },
          {
            "source": "Attachment/Uploads.md",
            "label": "Uploads",
            "order": 2
          },
          {
            "source": "Attachment/Storage-Providers.md",
            "label": "Storage Providers",
            "order": 3
          },
          {
            "source": "Attachment/Image-Processing.md",
            "label": "Image Processing",
            "order": 4
          },
          {
            "source": "Attachment/Security.md",
            "label": "Security",
            "order": 5
          },
          {
            "source": "Attachment/Reliability.md",
            "label": "Reliability",
            "order": 6
          },
          {
            "source": "Attachment/Configuration.md",
            "label": "Configuration",
            "order": 7
          }
        ]
      },
      {
        "label": "Subscriptions",
        "items": [
          {
            "source": "Subscriptions/Billing.md",
            "label": "Billing",
            "order": 1
          },
          {
            "source": "Subscriptions/Online-Payments.md",
            "label": "Online Payments",
            "order": 2
          },
          {
            "source": "Subscriptions/PaymentMethods-Frontend-Integration.md",
            "label": "Payment Methods Frontend Integration",
            "order": 3
          }
        ]
      },
      {
        "label": "Shared services",
        "items": [
          {
            "source": "Shared/README.md",
            "label": "HTTP context service",
            "order": 1
          }
        ]
      }
    ]
  },
  {
    "label": "Operations",
    "items": [
      {
        "label": "Deployment",
        "items": [
          {
            "source": "deployment/README.md",
            "label": "Overview",
            "order": 1
          },
          {
            "source": "deployment/Deploy-to-VPS.md",
            "label": "Deploy to VPS",
            "order": 2
          },
          {
            "source": "deployment/CI-CD.md",
            "label": "CI/CD",
            "order": 3
          },
          {
            "source": "deployment/Self-Hosted-Runner.md",
            "label": "Self Hosted Runner",
            "order": 4
          },
          {
            "source": "deployment/Secrets.md",
            "label": "Secrets",
            "order": 5
          },
          {
            "source": "deployment/Backups.md",
            "label": "Backups",
            "order": 6
          },
          {
            "source": "deployment/Database-Migrations.md",
            "label": "Database Migrations",
            "order": 7
          },
          {
            "source": "deployment/Docker-Compose.md",
            "label": "Docker Compose",
            "order": 8
          },
          {
            "source": "deployment/Nginx.md",
            "label": "Nginx",
            "order": 9
          },
          {
            "source": "deployment/Observability.md",
            "label": "Observability",
            "order": 10
          },
          {
            "source": "deployment/Seq-Security.md",
            "label": "Seq Security",
            "order": 11
          },
          {
            "source": "deployment/Production-Testing.md",
            "label": "Production Testing",
            "order": 12
          },
          {
            "source": "deployment/Agentic-Operations.md",
            "label": "Agentic Operations",
            "order": 13
          }
        ]
      },
      {
        "label": "Infrastructure",
        "items": [
          {
            "source": "Infrastructure/Aspire.md",
            "label": "Aspire",
            "order": 1
          },
          {
            "source": "Infrastructure/Redis.md",
            "label": "Redis",
            "order": 2
          }
        ]
      },
      {
        "label": "Testing",
        "items": [
          {
            "source": "Testing/Testing-Strategy.md",
            "label": "Testing Strategy",
            "order": 1
          },
          {
            "source": "Testing/Load-Testing.md",
            "label": "Load Testing",
            "order": 2
          }
        ]
      }
    ]
  },
  {
    "label": "Reference",
    "items": [
      {
        "source": "Reference/README.md",
        "label": "Overview",
        "order": 1
      },
      {
        "source": "Reference/Configuration.md",
        "label": "Configuration",
        "order": 8
      },
      {
        "source": "Reference/Endpoint-Authorization.md",
        "label": "Endpoint authorization",
        "order": 9
      },
      {
        "source": "Reference/Architecture-Guardrails.md",
        "label": "Architecture guardrails",
        "order": 10
      },
      {
        "source": "Reference/Diagram-Style.md",
        "label": "Diagram style",
        "order": 7
      },
      {
        "source": "Reference/Migrations.md",
        "label": "Migrations",
        "order": 2
      },
      {
        "source": "Reference/Third-Party-Licenses.md",
        "label": "Third Party Licenses",
        "order": 3
      },
      {
        "source": "Reference/Troubleshooting-PostgreSQL-Authentication.md",
        "label": "Troubleshooting Postgre SQL Authentication",
        "order": 4
      },
      {
        "source": "Reference/Brand.md",
        "label": "Brand",
        "order": 6
      },
      {
        "source": "Reference/Template-Backlog.md",
        "label": "Template Backlog",
        "order": 5
      }
    ]
  },
  {
    "label": "Decisions",
    "items": [
      {
        "source": "Decisions/README.md",
        "label": "Architecture decisions",
        "order": 1
      },
      {
        "source": "Decisions/0001-modular-monolith-schema-per-module.md",
        "label": "0001 modular monolith schema per module",
        "order": 2
      },
      {
        "source": "Decisions/0002-carter-minimal-apis.md",
        "label": "0002 carter minimal apis",
        "order": 3
      },
      {
        "source": "Decisions/0003-exceptions-not-result-type.md",
        "label": "0003 exceptions not result type",
        "order": 4
      },
      {
        "source": "Decisions/0004-outbox-and-messaging-transports.md",
        "label": "0004 outbox and messaging transports",
        "order": 5
      },
      {
        "source": "Decisions/0005-tenant-isolation-model.md",
        "label": "0005 tenant isolation model",
        "order": 6
      },
      {
        "source": "Decisions/0006-identity-behind-userdirectory-port.md",
        "label": "0006 identity behind userdirectory port",
        "order": 7
      },
      {
        "source": "Decisions/0007-owned-mediator-port.md",
        "label": "0007 owned mediator port",
        "order": 8
      },
      {
        "source": "Decisions/0008-feature-flags.md",
        "label": "0008 feature flags",
        "order": 9
      },
      {
        "source": "Decisions/0009-outbound-webhooks.md",
        "label": "0009 outbound webhooks",
        "order": 10
      },
      {
        "source": "Decisions/0011-audit-trail.md",
        "label": "0011 audit trail",
        "order": 11
      },
      {
        "source": "Decisions/0012-realtime-inbox-and-push.md",
        "label": "0012 realtime inbox and push",
        "order": 12
      },
      {
        "source": "Decisions/0013-enterprise-sso-and-scim.md",
        "label": "0013 enterprise sso and scim",
        "order": 13
      },
      {
        "source": "Decisions/0014-passkeys-on-built-in-identity.md",
        "label": "0014 passkeys on built in identity",
        "order": 14
      },
      {
        "source": "Decisions/0015-malware-scanning-clamd-fail-closed.md",
        "label": "0015 malware scanning clamd fail closed",
        "order": 15
      },
      {
        "source": "Decisions/0016-bulk-data-transfer-on-hangfire.md",
        "label": "0016 bulk data transfer on hangfire",
        "order": 16
      },
      {
        "source": "Decisions/0017-money-precision-and-per-currency-rounding.md",
        "label": "0017 money precision and per currency rounding",
        "order": 17
      },
      {
        "source": "Decisions/0018-off-session-dunning-retries-on-saved-cards.md",
        "label": "0018 off session dunning retries on saved cards",
        "order": 18
      },
      {
        "source": "Decisions/0019-module-manifests-and-discovery.md",
        "label": "0019 module manifests and discovery",
        "order": 19
      },
      {
        "source": "Decisions/0020-dotnet-new-templates.md",
        "label": "0020 dotnet new templates",
        "order": 20
      },
      {
        "source": "Decisions/0021-aggregate-version-covers-child-collections.md",
        "label": "0021 aggregate version covers child collections",
        "order": 21
      },
      {
        "source": "Decisions/0022-production-message-transport.md",
        "label": "0022 production message transport",
        "order": 22
      },
      {
        "source": "Decisions/0023-durable-sagas-on-module-dbcontexts.md",
        "label": "0023 durable sagas on module dbcontexts",
        "order": 23
      },
      {
        "source": "Decisions/0024-documentation-site.md",
        "label": "0024 documentation site",
        "order": 24
      },
      {
        "source": "Decisions/0025-agentic-module-and-model-seam.md",
        "label": "0025 agentic module and model seam",
        "order": 25
      },
      {
        "source": "Decisions/0026-durable-agent-runs-on-hangfire.md",
        "label": "0026 durable agent runs on hangfire",
        "order": 26
      },
      {
        "source": "Decisions/0027-agent-approvals-triggers-and-policies.md",
        "label": "0027 agent approvals triggers and policies",
        "order": 27
      },
      {
        "source": "Decisions/0028-agent-knowledge-and-memory-on-pgvector.md",
        "label": "0028 agent knowledge and memory on pgvector",
        "order": 28
      },
      {
        "source": "Decisions/0029-agent-conversations.md",
        "label": "0029 agent conversations",
        "order": 29
      },
      {
        "source": "Decisions/0030-streaming-agent-answers.md",
        "label": "0030 streaming agent answers",
        "order": 30
      },
      {
        "source": "Decisions/0031-conversation-summaries.md",
        "label": "0031 conversation summaries",
        "order": 31
      },
      {
        "source": "Decisions/0000-template.md",
        "label": "ADR template",
        "order": 99
      }
    ]
  }
];

export function outputPath(source) {
  if (source === 'README.md') return 'overview.md';
  return source.toLowerCase().replace(/(^|\/)readme\.md$/, '$1index.md');
}
export function pageSlug(source) {
  return outputPath(source).replace(/\.md$/, '').replace(/(^|\/)index$/, '');
}
export const pages = sections.flatMap(function flatten(item) {
  return item.items ? item.items.flatMap(flatten) : [item];
});
export const sidebar = sections.map(function convert(item) {
  return item.items
    ? { label: item.label, items: item.items.map(convert) }
    : { label: item.label, slug: pageSlug(item.source) };
});
export const base = '/' + (process.env.DOCS_BASE || '/').split('/').filter(Boolean).join('/') + '/';
export const siteBase = base === '//' ? '/' : base;
export const repository = process.env.GITHUB_REPOSITORY || 'mhasabalah/StarterKit';
