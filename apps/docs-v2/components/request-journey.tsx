'use client';
import Link from 'next/link';
import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react';
import { ArrowRight, ChevronLeft, ChevronRight, Pause, Play } from 'lucide-react';

/**
 * One command request, station by station. Every station is a real boundary in the order the code runs it
 * (src/Api/Program.cs, Shared.Core mediator behaviours and save interceptors; see /architecture/request-lifecycle/).
 */
type Station = { label: string; title: string; code: string; body: string; url: string; read: string; ends: [string, string][]; note?: string };
type Stage = { name: string; stations: Station[] };

const stages: Stage[] = [
  {
    name: 'Host pipeline',
    stations: [
      { label: 'Headers', title: 'Trusted forwarded headers', code: 'UseTrustedForwardedHeaders()', body: 'Restores the client address and scheme, but only from proxies the configuration trusts. Every later policy depends on it, so it runs first.', url: '/architecture/request-lifecycle/#http-pipeline', read: 'HTTP pipeline', ends: [], note: 'Nothing stops here; it only restores facts later stages rely on.' },
      { label: 'Errors', title: 'CORS, then one error handler', code: 'UseConfigurableCors() · UseErrorHandling()', body: 'The origin policy, then correlation ids and the single exception handler. It sits outside authentication, so a 401 or 403 still returns the JSON envelope.', url: '/architecture/errors-and-responses/', read: 'Errors and responses', ends: [['500', 'Unhandled failure: a generic message, the full exception logged']], note: 'A CORS preflight also finishes here.' },
      { label: 'Identity', title: 'Authenticate the caller', code: 'BearerOrApiKey', body: 'A JWT or an API key. A key carries its owner’s permissions intersected with its scopes, so it can never do more than the person who made it.', url: '/identity/', read: 'Identity', ends: [['401', 'No valid token or key, or a revoked session']] },
      { label: 'Tenant', title: 'Resolve the tenant', code: 'UseMultiTenancy() → TenantScope', body: 'Runs after authentication because it needs the caller, and before authorization because membership policies need the tenant. Everything downstream reads the ambient scope.', url: '/multitenancy/overview/', read: 'Multi-tenancy', ends: [['503', 'A write while the tenant is migrating']] },
      { label: 'Policy', title: 'Authorize, then rate limit', code: '.RequireAuthorization() · .RequireRateLimiting("create")', body: 'Every endpoint declares its policy; an architecture test fails the build when one does not. Rate limits partition by user, so they run after authentication.', url: '/reference/endpoint-authorization/', read: 'Endpoint authorization', ends: [['403', 'The policy denies this caller'], ['429', 'This user is over the rate limit']] },
    ],
  },
  {
    name: 'Inside the module',
    stations: [
      { label: 'Endpoint', title: 'A Carter endpoint sends a command', code: 'POST /api/v1/products → ISender.Send(CreateProductCommand)', body: 'The endpoint binds the request and hands it to the owned mediator. It holds no business logic and never reaches into another module.', url: '/architecture/request-lifecycle/#mediator-and-handler', read: 'Mediator and handler', ends: [['409', 'idempotency_in_progress (opt-in filter)'], ['403', 'feature_not_in_plan'], ['429', 'quota_exceeded']] },
      { label: 'Pipeline', title: 'Mediator behaviours', code: 'Telemetry → Validation → Concurrency → Logging → Events', body: 'Validators are discovered and run before the handler, a stale version becomes a 409, and domain events are held until the work commits.', url: '/architecture/request-lifecycle/#mediator-and-handler', read: 'Mediator pipeline', ends: [['400', 'Validation errors, one per rule'], ['409', 'The version you sent is stale']] },
      { label: 'Handler', title: 'The aggregate decides', code: 'IProductRepository · Product.Create(...)', body: 'The command handler loads through the aggregate’s repository and calls its methods. Invariants live in the entity, not in the endpoint or the handler.', url: '/architecture/designpatterns/', read: 'Design patterns', ends: [['404', 'NotFoundException'], ['409', 'AlreadyExists or another ConflictException']] },
    ],
  },
  {
    name: 'One transaction',
    stations: [
      { label: 'Save', title: 'Interceptors in one SaveChanges', code: 'Auditable → Events → Tenant → AuditTrail → Outbox', body: 'Version and audit stamps, the tenant id (a cross-tenant write is refused), the audit trail and the outbox rows are written in the same transaction as the change.', url: '/architecture/request-lifecycle/#save-commit-and-dispatch', read: 'Save, commit and dispatch', ends: [['409', 'Another save changed the row first']], note: 'A cross-tenant write is refused before it reaches the database.' },
      { label: 'Commit', title: 'Domain events after commit', code: 'IDomainEventDispatcher', body: 'Only events from committed saves are published, after the outermost unit of work. A rolled-back command raises nothing.', url: '/architecture/messaging/', read: 'Messaging', ends: [], note: 'Nothing; events wait for the commit and are dropped on a rollback.' },
    ],
  },
  {
    name: 'Leaving the request',
    stations: [
      { label: 'Reply', title: 'One response envelope', code: 'ApiResponse<T> { isSuccess, data, errors, traceId }', body: 'Success and failure share one shape. Typed exceptions map to status codes in one place, with an errorCode clients can branch on.', url: '/architecture/errors-and-responses/#envelope-fields', read: 'Envelope fields', ends: [['2xx', 'isSuccess: true, with data'], ['4xx', 'The same shape, with errors[], traceId and an errorCode']] },
      { label: 'Outbox', title: 'Integration events leave through the outbox', code: 'AddIntegrationEvent(...) → outbox row → bus', body: 'The relay delivers at least once to the bus. Other modules consume the event through its contract and re-read state through ISender; none of them touches this module’s tables.', url: '/architecture/messaging/#outbox', read: 'The outbox', ends: [], note: 'Never fails the request: failed deliveries retry, then dead-letter after Messaging:Outbox:MaxAttempts.' },
    ],
  },
];

const stations = stages.flatMap((stage, s) => stage.stations.map((station) => ({ ...station, stage: stage.name, stageIndex: s })));
/** Long enough to read a station: a title, a call, about thirty words and up to three outcomes. */
const STEP_MS = 8000;

export function RequestJourney() {
  const [current, setCurrent] = useState(0);
  const [playing, setPlaying] = useState(true);
  const [held, setHeld] = useState(false);
  const [visible, setVisible] = useState(false);
  const [still, setStill] = useState(false);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const media = matchMedia('(prefers-reduced-motion: reduce)');
    const sync = () => { setStill(media.matches); if (media.matches) setPlaying(false); };
    sync();
    media.addEventListener('change', sync);
    const element = root.current;
    if (!element) return () => media.removeEventListener('change', sync);
    const observer = new IntersectionObserver(([entry]) => setVisible(entry.isIntersecting), { threshold: 0.35 });
    observer.observe(element);
    return () => { media.removeEventListener('change', sync); observer.disconnect(); };
  }, []);

  const running = playing && visible && !held;
  useEffect(() => {
    if (!running) return;
    const timer = window.setTimeout(() => setCurrent((index) => (index + 1) % stations.length), STEP_MS);
    return () => window.clearTimeout(timer);
  }, [running, current]);

  const go = useCallback((index: number) => { setCurrent((index + stations.length) % stations.length); setPlaying(false); }, []);
  const station = stations[current];

  return <div ref={root} className="journey" data-running={running || undefined} data-still={still || undefined}
    style={{ '--step': current, '--steps': stations.length, '--step-ms': `${STEP_MS}ms` } as CSSProperties}
    onPointerEnter={() => setHeld(true)} onPointerLeave={() => setHeld(false)}
    onFocus={() => setHeld(true)} onBlur={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node)) setHeld(false); }}>
    <div className="journey-track" role="group" aria-label="Stations of a request, in the order they run">
      {stages.map((stage, s) => <span key={stage.name} className="journey-stage-name" aria-hidden="true"
        style={{ '--span': stage.stations.length } as CSSProperties} data-active={station.stageIndex === s || undefined}>{stage.name}</span>)}
      {stations.map((item, index) => <button key={item.title} type="button" className="journey-station"
        aria-current={index === current ? 'step' : undefined} data-passed={index < current || undefined} onClick={() => go(index)}
        aria-label={`${index + 1}. ${item.stage}: ${item.title}`}>
        <span className="journey-num" aria-hidden="true">{String(index + 1).padStart(2, '0')}</span>
        <span className="journey-label" aria-hidden="true">{item.label}</span>
      </button>)}
      <span className="journey-rail" aria-hidden="true"><span className="journey-fill" /><span className="journey-token" /></span>
    </div>

    <div className="journey-panel">
      {/* Every card shares one grid cell, so the panel keeps the tallest card's height and nothing below it jumps. */}
      <div className="journey-live" aria-live={playing ? 'off' : 'polite'}>
        {stations.map((item, index) => <div key={item.title} className="journey-card" data-current={index === current || undefined}
          aria-hidden={index === current ? undefined : true}>
          <h3>{item.title}</h3>
          <code className="journey-code">{item.code}</code>
          <p className="journey-body">{item.body}</p>
          <Link href={item.url} className="home-more" tabIndex={index === current ? undefined : -1}>{item.read}<ArrowRight size={15} aria-hidden="true" /></Link>
          <div className="journey-ends">
            <h4>Can end the request here</h4>
            {item.ends.length > 0 && <ul>{item.ends.map(([status, reason]) => <li key={status + reason}>
              <span className="journey-status" data-ok={status.startsWith('2') || undefined}>{status}</span><span>{reason}</span>
            </li>)}</ul>}
            {item.note && <p>{item.note}</p>}
          </div>
        </div>)}
      </div>
      <div className="journey-foot">
        <div className="journey-controls">
          <button type="button" onClick={() => go(current - 1)} aria-label="Previous station"><ChevronLeft size={16} aria-hidden="true" /></button>
          <button type="button" className="journey-play" onClick={() => setPlaying((value) => !value)}>
            {playing ? <Pause size={14} aria-hidden="true" /> : <Play size={14} aria-hidden="true" />}
            <span>{playing ? 'Pause' : 'Play'}</span>
          </button>
          <button type="button" onClick={() => go(current + 1)} aria-label="Next station"><ChevronRight size={16} aria-hidden="true" /></button>
        </div>
        <p className="journey-position"><span>{station.stage}</span><span aria-hidden="true">·</span><span>{current + 1} of {stations.length}</span></p>
        <p className="journey-hint">{still ? 'Step through with the arrows, or choose a station.' : 'Moves one station at a time while on screen. Choose a station or press Pause to stay on it.'}</p>
      </div>
    </div>
  </div>;
}
