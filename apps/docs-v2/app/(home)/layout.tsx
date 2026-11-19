import { HomeLayout } from 'fumadocs-ui/layouts/home';
import { baseOptions } from '@/lib/layout';
import { HomeHeader } from '@/components/site-header';
export default function Layout({ children }: { children: React.ReactNode }) { return <HomeLayout {...baseOptions} slots={{ header: HomeHeader }}>{children}</HomeLayout>; }
