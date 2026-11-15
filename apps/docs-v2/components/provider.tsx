'use client';
import { RootProvider } from 'fumadocs-ui/provider/next';
import type { ReactNode } from 'react';
import { DocumentationSearch } from './search';

export function Provider({ children }: { children: ReactNode }) {
  return <RootProvider search={{ SearchDialog: DocumentationSearch }} theme={{ defaultTheme: 'system' }}>{children}</RootProvider>;
}
