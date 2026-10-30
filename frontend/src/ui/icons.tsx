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

const stroke = (size: number, p: P) => ({
  ...base(size, p),
  viewBox: '0 0 16 16',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.6,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
});

export const IconHistory = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M2.5 8a5.5 5.5 0 1 0 1.6-3.9M2.3 2.2v2.6h2.6M8 5v3.2l2.1 1.4" />
  </svg>
);
export const IconReview = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M2.5 3.5h11v7.5H8l-3 2.5V11H2.5zM5.5 7.2l1.6 1.5 3.4-3.2" />
  </svg>
);
export const IconCheck = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M3 8.5l3.2 3L13 4.5" />
  </svg>
);
export const IconSettings = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <circle cx="8" cy="8" r="2.2" />
    <path d="M8 1.8v1.8M8 12.4v1.8M1.8 8h1.8M12.4 8h1.8M3.6 3.6l1.3 1.3M11.1 11.1l1.3 1.3M3.6 12.4l1.3-1.3M11.1 4.9l1.3-1.3" />
  </svg>
);
export const IconTrash = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M2.8 4.2h10.4M6.3 4.2V2.6h3.4v1.6M4.2 4.2l.7 9.2h6.2l.7-9.2M6.7 6.8v4.2M9.3 6.8v4.2" />
  </svg>
);
export const IconBot = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <rect x="2.5" y="5" width="11" height="8" rx="2" />
    <path d="M8 2.2V5M5.8 8.6v.8M10.2 8.6v.8" />
  </svg>
);

/* ---- documentation pages ---- */

export const IconBook = ({ size = 18, ...p }: P) => (
  <svg
    {...base(size, p)}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
  >
    <path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H20v15H6.5A2.5 2.5 0 0 0 4 20.5z" />
    <path d="M4 20.5A2.5 2.5 0 0 0 6.5 23H20v-5" />
    <path d="M9 7.5h7M9 11h5" />
  </svg>
);
export const IconDoc = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M4 1.8h5.5L12.5 5v9.2H4z" />
    <path d="M9.5 1.8V5h3M6 8.5h4.5M6 11h3" />
  </svg>
);
export const IconFolder = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M1.8 4.2c0-.6.4-1 1-1h3.4l1.4 1.6h5.6c.6 0 1 .4 1 1v6.9c0 .6-.4 1-1 1H2.8c-.6 0-1-.4-1-1z" />
  </svg>
);
export const IconChevron = ({ size = 14, ...p }: P) => (
  <svg {...stroke(size, p)} strokeWidth={1.8}>
    <path d="M4.5 6.2 8 9.8l3.5-3.6" />
  </svg>
);
export const IconGrip = ({ size = 14, ...p }: P) => (
  <svg {...base(size, p)} viewBox="0 0 12 16" fill="currentColor">
    <circle cx="4" cy="4" r="1.2" />
    <circle cx="8" cy="4" r="1.2" />
    <circle cx="4" cy="8" r="1.2" />
    <circle cx="8" cy="8" r="1.2" />
    <circle cx="4" cy="12" r="1.2" />
    <circle cx="8" cy="12" r="1.2" />
  </svg>
);
export const IconCopy = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <rect x="5.2" y="5.2" width="8.6" height="8.6" rx="1.6" />
    <path d="M10.8 5.2V3.6c0-.9-.7-1.6-1.6-1.6H3.6C2.7 2 2 2.7 2 3.6v5.6c0 .9.7 1.6 1.6 1.6h1.6" />
  </svg>
);
export const IconCode = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)} strokeWidth={1.7}>
    <path d="M5.5 4.5 2 8l3.5 3.5M10.5 4.5 14 8l-3.5 3.5" />
  </svg>
);
export const IconNote = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)} strokeWidth={1.7}>
    <circle cx="8" cy="8" r="6.3" />
    <path d="M8 7.2v4M8 4.8v.1" />
  </svg>
);
export const IconBulb = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M6 12.5h4M6.6 14.5h2.8M5.2 9.8A4.4 4.4 0 1 1 10.8 9.8c-.6.5-.8 1-.8 1.7H6c0-.7-.2-1.2-.8-1.7z" />
  </svg>
);
export const IconLink = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)} strokeWidth={1.7}>
    <path d="M6.8 9.2a2.8 2.8 0 0 0 4 0l2.2-2.2a2.8 2.8 0 0 0-4-4l-.9.9" />
    <path d="M9.2 6.8a2.8 2.8 0 0 0-4 0L3 9a2.8 2.8 0 0 0 4 4l.9-.9" />
  </svg>
);
export const IconList = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)} strokeWidth={1.7}>
    <path d="M6 4h8M6 8h8M6 12h8" />
    <circle cx="2.6" cy="4" r=".6" fill="currentColor" />
    <circle cx="2.6" cy="8" r=".6" fill="currentColor" />
    <circle cx="2.6" cy="12" r=".6" fill="currentColor" />
  </svg>
);
export const IconOList = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M6.5 4h7.5M6.5 8h7.5M6.5 12h7.5" />
    <path d="M2 2.8h1v2.6M1.8 5.4h1.6M1.8 7.4c.3-.4.8-.6 1.2-.4.5.2.5.8.1 1.2L1.8 9.6h1.8" strokeWidth={1.2} />
  </svg>
);
export const IconTable = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <rect x="1.8" y="2.6" width="12.4" height="10.8" rx="1.4" />
    <path d="M1.8 6.2h12.4M1.8 9.8h12.4M6.2 6.2v7.2" />
  </svg>
);
export const IconCallout = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <rect x="1.8" y="2.6" width="12.4" height="10.8" rx="1.6" />
    <path d="M4.6 5.8v4.4M7 6.4h4.6M7 9.6h3.2" />
  </svg>
);
export const IconMenu = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)} strokeWidth={1.8}>
    <path d="M2.5 4h11M2.5 8h11M2.5 12h11" />
  </svg>
);
export const IconEdit = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M10.5 2.5l3 3-8 8H2.5v-3z" />
    <path d="M9 4l3 3" />
  </svg>
);
export const IconSend = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M14 2 7 9M14 2l-4.5 12L7 9 2 6.5z" />
  </svg>
);
export const IconDomain = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <rect x="1.8" y="1.8" width="12.4" height="12.4" rx="2" />
    <path d="M1.8 6.2h12.4M6.2 6.2v8" />
  </svg>
);
export const IconEyeOff = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M2 2l12 12M6.6 3.7A6.8 6.8 0 0 1 8 3.5c4 0 6.5 4.5 6.5 4.5a11 11 0 0 1-1.9 2.4M10 11.9a6 6 0 0 1-2 .6c-4 0-6.5-4.5-6.5-4.5a11.5 11.5 0 0 1 2.6-3" />
  </svg>
);
export const IconUser = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <circle cx="8" cy="5.5" r="2.6" />
    <path d="M3 13.8c.6-2.6 2.6-4 5-4s4.4 1.4 5 4" />
  </svg>
);
export const IconUsers = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <circle cx="6" cy="5.5" r="2.3" />
    <path d="M1.8 13.5c.5-2.4 2.2-3.7 4.2-3.7s3.7 1.3 4.2 3.7" />
    <circle cx="11.5" cy="6" r="1.8" />
    <path d="M11 9.8c1.7 0 3 1 3.3 3" />
  </svg>
);
export const IconShield = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <path d="M8 1.8l5.2 2v4c0 3.2-2.2 5.4-5.2 6.4-3-1-5.2-3.2-5.2-6.4v-4z" />
  </svg>
);
export const IconShare = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <circle cx="5.5" cy="5.5" r="2.2" />
    <path d="M1.8 13c.5-2.2 1.9-3.4 3.7-3.4 1.2 0 2.2.5 2.9 1.4M12 8.5v5M9.5 11h5" />
  </svg>
);
export const IconClock = ({ size = 16, ...p }: P) => (
  <svg {...stroke(size, p)}>
    <circle cx="8" cy="8" r="6" />
    <path d="M8 4.8V8l2.2 1.4" />
  </svg>
);
