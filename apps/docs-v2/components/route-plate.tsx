/**
 * A small printed plate above each way in: what the reader will actually see at the end of the path.
 * Paths come from the template (`dotnet new starterkit`) and the slice layout in CLAUDE.md. Decorative
 * for assistive tech: the link's heading and description already say where it goes.
 */
const lines = (rows: string[]) => rows.map((row) => <span key={row} className="plate-line">{row}</span>);

export function RoutePlate({ kind }: { kind: 'start' | 'architecture' | 'module' }) {
  if (kind === 'architecture') {
    return <span className="plate plate-stack" aria-hidden="true">
      <span className="plate-layer"><b>Host</b><small>src/Api · one process</small></span>
      <span className="plate-layer plate-modules">
        {['Id', 'Tn', 'At', 'Sb', 'Wh', 'Nt'].map((symbol) => <span key={symbol}>{symbol}</span>)}
      </span>
      <span className="plate-layer plate-contracts"><b>Contracts</b><small>ISender · integration events</small></span>
    </span>;
  }
  if (kind === 'module') {
    return <span className="plate plate-code" aria-hidden="true">{lines([
      'Domains/Order/',
      '├ Models/Order.cs',
      '├ Features/CreateOrder/',
      '│  ├ CreateOrderHandler.cs',
      '│  └ CreateOrderEndPoint.cs',
      '└ Events/',
    ])}</span>;
  }
  return <span className="plate plate-start" aria-hidden="true">
    <span className="plate-prompt"><i>$</i> dotnet new starterkit -n Acme</span>
    <span className="plate-cells">
      {[['Api', 'host, endpoints'], ['Modules', 'one folder each'], ['Shared', 'contracts, core'], ['Aspire', 'local stack']].map(([name, role]) =>
        <span key={name}><b>src/{name}</b><small>{role}</small></span>)}
    </span>
  </span>;
}
