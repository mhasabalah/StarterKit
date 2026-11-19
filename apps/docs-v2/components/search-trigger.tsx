'use client';
import { Search } from 'lucide-react';
import { useSearchContext } from 'fumadocs-ui/contexts/search';

/** The home page's primary action: the same dialog as Ctrl K, at the size of the reader's question. */
export function SearchTrigger() {
  const { setOpenSearch, hotKey } = useSearchContext();
  return <button type="button" className="search-trigger" onClick={() => setOpenSearch(true)}>
    <Search size={18} aria-hidden="true" />
    <span>Search modules, rules and commands</span>
    <span className="search-trigger-keys" aria-hidden="true">{hotKey.map((key, index) => <kbd key={index}>{key.display}</kbd>)}</span>
  </button>;
}
