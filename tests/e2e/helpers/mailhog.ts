const MAILHOG_URL = process.env.MAILHOG_URL ?? 'http://localhost:8025';

/** Decode quoted-printable soft line breaks and hex escapes. */
function decodeQuotedPrintable(input: string): string {
  return input
    .replace(/=\r?\n/g, '')             // soft line breaks
    .replace(/=([0-9A-Fa-f]{2})/g, (_, hex) => String.fromCharCode(parseInt(hex, 16)));
}

function escapeRegex(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Decode a MIME body part based on its Content-Transfer-Encoding. */
function decodeMimePart(body: string, encoding: string | undefined): string {
  const enc = (encoding ?? '').toLowerCase().trim();
  if (enc === 'base64') {
    return Buffer.from(body.replace(/\s/g, ''), 'base64').toString('utf-8');
  }
  if (enc === 'quoted-printable') {
    return decodeQuotedPrintable(body);
  }
  return body;
}

/**
 * Recursively extract text from a MIME body, returning only text/plain and text/html parts.
 * Ignores binary attachments (image/*, application/*, etc.).
 */
function extractTextParts(raw: string, contentType: string): string {
  const boundaryMatch = contentType.match(/boundary="?([^";\s]+)"?/i);

  if (boundaryMatch) {
    const boundary = boundaryMatch[1];
    const parts = raw.split(new RegExp(`--${escapeRegex(boundary)}`));
    const collected: string[] = [];

    for (const part of parts) {
      if (!part || part.trim() === '--') continue;
      const sep = part.match(/\r?\n\r?\n/);
      if (!sep || sep.index === undefined) continue;
      const headerSection = part.slice(0, sep.index);
      const partBody = part.slice(sep.index + sep[0].length);

      const partContentType = (headerSection.match(/Content-Type:\s*([^\r\n]+)/i)?.[1] ?? '').trim();
      const encoding = headerSection.match(/Content-Transfer-Encoding:\s*(\S+)/i)?.[1];

      if (/^multipart\//i.test(partContentType)) {
        // Recurse into nested multipart
        collected.push(extractTextParts(partBody, partContentType));
      } else if (/^text\/(plain|html)/i.test(partContentType)) {
        collected.push(decodeMimePart(partBody, encoding));
      }
      // Skip binary parts (application/*, image/*, etc.)
    }

    return collected.join('\n');
  }

  // Single-part: return as-is (caller handles encoding)
  return raw;
}

interface MailhogMessage {
  ID: string;
  From: { Mailbox: string; Domain: string };
  To: Array<{ Mailbox: string; Domain: string }>;
  Content: {
    Headers: Record<string, string[]>;
    Body: string;
  };
  Created: string;
  Raw?: { From: string; To: string[]; Data: string };
}

interface MailhogResponse {
  total: number;
  count: number;
  start: number;
  items: MailhogMessage[];
}

export class MailhogClient {
  private baseUrl: string;

  constructor(baseUrl = MAILHOG_URL) {
    this.baseUrl = baseUrl;
  }

  async getAll(): Promise<MailhogMessage[]> {
    const res = await fetch(`${this.baseUrl}/api/v2/messages?limit=100`);
    if (!res.ok) throw new Error(`Mailhog API error: ${res.status}`);
    const data: MailhogResponse = await res.json();
    return data.items ?? [];
  }

  async searchByRecipient(email: string): Promise<MailhogMessage[]> {
    const res = await fetch(
      `${this.baseUrl}/api/v2/search?kind=to&query=${encodeURIComponent(email)}`
    );
    if (!res.ok) throw new Error(`Mailhog search error: ${res.status}`);
    const data: MailhogResponse = await res.json();
    return data.items ?? [];
  }

  /** Poll until at least one email arrives for the given recipient, or timeout. */
  async waitForEmail(
    recipientEmail: string,
    opts: { timeoutMs?: number; pollMs?: number; subjectContains?: string } = {}
  ): Promise<MailhogMessage> {
    const { timeoutMs = 10_000, pollMs = 500, subjectContains } = opts;
    const deadline = Date.now() + timeoutMs;

    while (Date.now() < deadline) {
      const messages = await this.searchByRecipient(recipientEmail);
      const match = subjectContains
        ? messages.find((m) =>
            (m.Content.Headers['Subject']?.[0] ?? '').includes(subjectContains)
          )
        : messages[0];

      if (match) return match;
      await new Promise((r) => setTimeout(r, pollMs));
    }

    throw new Error(
      `No email arrived for ${recipientEmail}${subjectContains ? ` with subject containing "${subjectContains}"` : ''} within ${timeoutMs}ms`
    );
  }

  getSubject(msg: MailhogMessage): string {
    return msg.Content.Headers['Subject']?.[0] ?? '';
  }

  /** Returns the decoded text of the email body (text/plain + text/html only, ignores attachments). */
  getBody(msg: MailhogMessage): string {
    const raw = msg.Content.Body ?? msg.Raw?.Data ?? '';
    const contentType = msg.Content.Headers['Content-Type']?.[0] ?? '';

    if (/^multipart\//i.test(contentType)) {
      return extractTextParts(raw, contentType);
    }

    // Single-part message
    const encoding = msg.Content.Headers['Content-Transfer-Encoding']?.[0];
    return decodeMimePart(raw, encoding);
  }

  /** Returns the full raw RFC2822 message (headers + body). */
  getRawBody(msg: MailhogMessage): string {
    return msg.Raw?.Data ?? msg.Content.Body ?? '';
  }

  async deleteAll(): Promise<void> {
    await fetch(`${this.baseUrl}/api/v1/messages`, { method: 'DELETE' });
  }

  async isReachable(): Promise<boolean> {
    try {
      const res = await fetch(`${this.baseUrl}/api/v2/messages?limit=1`);
      return res.ok;
    } catch {
      return false;
    }
  }
}
