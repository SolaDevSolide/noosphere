import type { SVGProps } from 'react';
import type { FlowType } from '../api/types';

type P = SVGProps<SVGSVGElement> & { size?: number };
const base = (size: number, p: P) => ({
  width: size,
  height: size,
  'aria-hidden': true as const,
  focusable: false as const,
  ...p,
});

export const IconApi = ({ size = 16, ...p }: P) => (
  <svg
    {...base(size, p)}
    viewBox="0 0 16 16"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.7"
    strokeLinecap="round"
    strokeLinejoin="round"
  >
    <path d="M2 5.5h10M9.5 3l2.5 2.5L9.5 8M14 10.5H4M6.5 8L4 10.5 6.5 13" />
  </svg>
);
export const IconEvent = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 16 16" fill="currentColor">
    <path d="M9.2 1.2L3 9h4.2l-1 5.8L13 6.8H8.6z" />
  </svg>
);
export const IconBatch = ({ size = 16, ...p }: P) => (
  <svg
    {...base(size, p)}
    viewBox="0 0 16 16"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.6"
    strokeLinejoin="round"
  >
    <path d="M4 1.8h5.5L12.5 5v9.2H4z" />
    <path d="M9.5 1.8V5h3M6 8.5h4.5M6 11h4.5" strokeLinecap="round" />
  </svg>
);
export const IconLock = ({ size = 14, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
    <rect x="3" y="7" width="10" height="7.5" rx="1.5" />
    <path d="M5.5 7V5a2.5 2.5 0 0 1 5 0v2" />
  </svg>
);
export const IconWarn = ({ size = 14, ...p }: P) => (
  <svg
    {...base(size, p)}
    viewBox="0 0 16 16"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.7"
    strokeLinecap="round"
    strokeLinejoin="round"
  >
    <path d="M8 1.8l6.5 11.7h-13z" />
    <path d="M8 6.5v3.2M8 11.8v.1" />
  </svg>
);
export const IconData = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6">
    <ellipse cx="8" cy="4" rx="5" ry="2" />
    <path d="M3 4v8c0 1.1 2.2 2 5 2s5-.9 5-2V4M3 8c0 1.1 2.2 2 5 2s5-.9 5-2" />
  </svg>
);
export const IconTeam = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round">
    <circle cx="6" cy="5.5" r="2.3" />
    <path d="M1.8 13.5c.5-2.4 2.2-3.7 4.2-3.7s3.7 1.3 4.2 3.7" />
    <circle cx="11.5" cy="6" r="1.8" />
    <path d="M11 9.8c1.7 0 3 1 3.3 3" />
  </svg>
);
export const IconApp = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6">
    <rect x="2" y="2.5" width="12" height="11" rx="2" />
    <path d="M2 6h12" />
  </svg>
);
export const IconLogo = ({ size = 18, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
    <circle cx="12" cy="12" r="3" />
    <circle cx="4" cy="6" r="2" />
    <circle cx="20" cy="6" r="2" />
    <circle cx="20" cy="18" r="2" />
    <path d="M6 7l3.4 3M18 7l-3.4 3M18 17l-3.4-3" />
  </svg>
);
export const IconSearch = ({ size = 18, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
    <circle cx="11" cy="11" r="7" />
    <path d="M20 20l-3.5-3.5" />
  </svg>
);
export const IconFilter = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
    <path d="M4 6h16M7 12h10M10 18h4" />
  </svg>
);
export const IconMoon = ({ size = 18, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
    <path d="M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5z" />
  </svg>
);
export const IconSun = ({ size = 18, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
  </svg>
);
export const IconGrid = ({ size = 14, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
    <rect x="3" y="3" width="7" height="7" rx="1.5" />
    <rect x="14" y="3" width="7" height="7" rx="1.5" />
    <rect x="3" y="14" width="7" height="7" rx="1.5" />
    <rect x="14" y="14" width="7" height="7" rx="1.5" />
  </svg>
);
export const IconReset = ({ size = 16, ...p }: P) => (
  <svg
    {...base(size, p)}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
  >
    <path d="M3 12a9 9 0 109-9 9.7 9.7 0 00-6.7 2.8L3 8" />
    <path d="M3 3v5h5" />
  </svg>
);
export const IconSliders = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
    <path d="M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1" />
    <circle cx="15" cy="6" r="2" />
    <circle cx="9" cy="12" r="2" />
    <circle cx="17" cy="18" r="2" />
  </svg>
);
export const IconPlus = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
    <path d="M12 5v14M5 12h14" />
  </svg>
);
export const IconMinus = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
    <path d="M5 12h14" />
  </svg>
);
export const IconFit = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
    <path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" />
  </svg>
);
export const IconInfo = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <circle cx="12" cy="12" r="9" />
    <path d="M12 8v5M12 16v.5" />
  </svg>
);
export const IconX = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
    <path d="M6 6l12 12M18 6L6 18" />
  </svg>
);
export const IconDownload = ({ size = 16, ...p }: P) => (
  <svg
    {...base(size, p)}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
  >
    <path d="M12 4v11M7 10l5 5 5-5M5 20h14" />
  </svg>
);
export const IconUpload = ({ size = 16, ...p }: P) => (
  <svg
    {...base(size, p)}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
  >
    <path d="M12 20V9M7 14l5-5 5 5M5 4h14" />
  </svg>
);
export const IconEye = ({ size = 16, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
    <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" />
    <circle cx="12" cy="12" r="3" />
  </svg>
);

export function TypeIcon({ type, size }: { type: FlowType; size?: number }) {
  return type === 'api' ? (
    <IconApi size={size} />
  ) : type === 'event' ? (
    <IconEvent size={size} />
  ) : (
    <IconBatch size={size} />
  );
}
