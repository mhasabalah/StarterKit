import defaultComponents from 'fumadocs-ui/mdx';
import { Callout, CalloutContainer, CalloutTitle, CalloutDescription } from 'fumadocs-ui/components/callout';
import { CodeBlock, Pre, CodeBlockTabs, CodeBlockTabsList, CodeBlockTabsTrigger, CodeBlockTab } from 'fumadocs-ui/components/codeblock';
import { Tabs, Tab } from 'fumadocs-ui/components/tabs';
import { Steps, Step } from 'fumadocs-ui/components/steps';
import { Cards, Card } from 'fumadocs-ui/components/card';
import { Accordion, Accordions } from 'fumadocs-ui/components/accordion';
import { Files, Folder, File } from 'fumadocs-ui/components/files';
import { Mermaid } from '@/components/mermaid';
import { ArchitectureView } from '@/components/architecture-view';
import type { MDXComponents } from 'mdx/types';

export function getMDXComponents(components?: MDXComponents): MDXComponents {
  return { ...defaultComponents, Callout, CalloutContainer, CalloutTitle, CalloutDescription, CodeBlock, Pre, CodeBlockTabs, CodeBlockTabsList, CodeBlockTabsTrigger, CodeBlockTab, Tabs, Tab, Steps, Step, Cards, Card, Accordion, Accordions, Files, Folder, File, Mermaid, ArchitectureView, ...components };
}
