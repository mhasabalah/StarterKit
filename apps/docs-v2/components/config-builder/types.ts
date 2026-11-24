// TypeScript shapes of the configuration builder's pure model (lib/config-builder/*.mjs, documented with JSDoc).

export type Option = { value: string; label?: string; hint?: string };
export type Condition = { path: string; equals?: unknown; in?: unknown[]; not?: unknown; truthy?: boolean };

export type Field = {
  path: string;
  section: string;
  moduleId: string;
  label: string;
  group: string;
  defaultValue: unknown;
  origin: 'base' | 'environment' | 'compose' | 'option';
  composeVariable?: string | null;
  control: string;
  options?: Option[];
  format?: 'array' | 'csv';
  unit?: string;
  min?: number;
  max?: number;
  step?: number;
  secret: boolean;
  secretFile?: string;
  secretKind?: 'random' | 'external';
  optional?: boolean;
  essential: boolean;
  visibleWhen: Condition[];
  description: string;
  guide: string | null;
  hint?: string;
  dictionary?: { path: string; entry: string; removable: boolean };
  added?: boolean;
};

export type State = {
  symbols: Record<string, string | boolean>;
  environment: string;
  values: Record<string, unknown>;
  removed: string[];
  added: string[];
};

export type Catalog = {
  schemaVersion: number;
  files: Record<string, string>;
  symbols: { id: string; label: string; modules: string[]; type: 'bool' | 'choice'; default: string | boolean; description: string; choices?: { value: string; description: string }[] }[];
  docs: Record<string, [string, string | null]>;
  sections: Record<string, { anchor: string; options?: string; guide?: string }>;
  secrets: Record<string, string>;
  composeSecrets: Record<string, string>;
  composeEnvironment: { path: string; variable: string | null; fallback: string }[];
};

export type Issue = { id: string; severity: 'blocking' | 'warning' | 'info'; title: string; detail: string; paths: string[]; guide?: string };
export type Warning = { path: string; text: string };
