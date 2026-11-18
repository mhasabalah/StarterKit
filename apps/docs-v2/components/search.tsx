'use client';
import { useState } from 'react';
import { useFetchSearch } from 'fumadocs-core/search/client';
import { SearchDialog, SearchDialogOverlay, SearchDialogContent, SearchDialogHeader,
  SearchDialogIcon, SearchDialogInput, SearchDialogClose, SearchDialogList,
  TagsList, TagsListItem, type SharedProps } from 'fumadocs-ui/components/dialog/search';
import { publicPath } from '@/lib/site';

export function DocumentationSearch(props: SharedProps) {
  const [tag, setTag] = useState<string | undefined>('all');
  const search = useFetchSearch({ api: publicPath('/api/search/'), tag, delayMs: 150 });
  return <SearchDialog {...search} {...props}>
    <SearchDialogOverlay />
    <SearchDialogContent className="docs-search">
      <SearchDialogHeader className="docs-search-header"><SearchDialogIcon /><SearchDialogInput /><SearchDialogClose /></SearchDialogHeader>
      <TagsList tag={tag} onTagChange={setTag} allowClear={false} aria-label="Search filters" className="p-3">
        <TagsListItem value="all" tabIndex={0}>All pages</TagsListItem>
        <TagsListItem value="docs" tabIndex={0}>Documentation</TagsListItem>
        <TagsListItem value="getting-started" tabIndex={0}>Getting started</TagsListItem>
        <TagsListItem value="architecture" tabIndex={0}>Architecture</TagsListItem>
        <TagsListItem value="api" tabIndex={0}>API reference</TagsListItem>
      </TagsList>
      <SearchDialogList />
    </SearchDialogContent>
  </SearchDialog>;
}
