import { HomeLayout } from 'fumadocs-ui/layouts/home';
import { baseOptions } from '@/lib/layout';
export default function Layout({ children }: { children: React.ReactNode }) { return <HomeLayout {...baseOptions}>{children}</HomeLayout>; }
