import Image from 'next/image';
import { publicPath } from '@/lib/site';

/** Use the approved vector artwork copied from V1 by the content sync. */
export function BrandLogo() {
  return <span className="brand-logo" role="img" aria-label="StarterKit">
    <Image src={publicPath('/logo-light.svg')} width={140} height={34} alt="" unoptimized loading="eager" className="brand-logo-light" />
    <Image src={publicPath('/logo-dark.svg')} width={140} height={34} alt="" unoptimized loading="eager" className="brand-logo-dark" />
  </span>;
}
