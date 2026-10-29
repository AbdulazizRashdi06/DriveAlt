import type { SVGProps } from 'react'

type P = SVGProps<SVGSVGElement> & { size?: number }

function Icon({ size = 18, children, strokeWidth = 2.5, fill = 'none', ...rest }: P & { children: React.ReactNode }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill={fill} stroke="currentColor" strokeWidth={strokeWidth}
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" {...rest}>
      {children}
    </svg>
  )
}

export const IconChat = (p: P) => <Icon {...p}><path d="M4 5h16v11H9l-5 4z" /></Icon>
export const IconGrid = (p: P) => <Icon {...p}><path d="M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z" /></Icon>
export const IconBookmark = ({ filled, ...p }: P & { filled?: boolean }) =>
  <Icon {...p} fill={filled ? 'currentColor' : 'none'}><path d="M6 4h12v16l-6-4-6 4z" /></Icon>
export const IconUpload = (p: P) => <Icon {...p}><path d="M12 16V4M6 10l6-6 6 6M4 20h16" /></Icon>
export const IconCheck = (p: P) => <Icon {...p}><path d="M4 12l5 5L20 6" /></Icon>
export const IconPlus = (p: P) => <Icon strokeWidth={3} {...p}><path d="M12 5v14M5 12h14" /></Icon>
export const IconMenu = (p: P) => <Icon {...p}><path d="M4 7h16M4 12h16M4 17h10" /></Icon>
export const IconSend = (p: P) => <Icon strokeWidth={3} {...p}><path d="M12 19V5M5 12l7-7 7 7" /></Icon>
export const IconExpand = (p: P) => <Icon {...p}><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" /></Icon>
export const IconExternal = (p: P) => <Icon {...p}><path d="M14 4h6v6M20 4l-9 9M18 14v6H4V6h6" /></Icon>
export const IconClose = (p: P) => <Icon strokeWidth={3} {...p}><path d="M6 6l12 12M18 6L6 18" /></Icon>
export const IconLeft = (p: P) => <Icon strokeWidth={3} {...p}><path d="M15 5l-7 7 7 7" /></Icon>
export const IconRight = (p: P) => <Icon strokeWidth={3} {...p}><path d="M9 5l7 7-7 7" /></Icon>
export const IconMinus = (p: P) => <Icon strokeWidth={3} {...p}><path d="M5 12h14" /></Icon>
export const IconSearch = (p: P) => <Icon {...p}><circle cx="11" cy="11" r="7" /><path d="M20 20l-3.5-3.5" /></Icon>
export const IconDownload = (p: P) => <Icon {...p}><path d="M12 4v12M6 10l6 6 6-6M4 20h16" /></Icon>
export const IconTrash = (p: P) => <Icon {...p}><path d="M5 7h14M10 7V4h4v3M7 7l1 13h8l1-13" /></Icon>
export const IconPointer = (p: P) => <Icon {...p}><path d="M5 3l14 8-6 2-3 6z" /></Icon>
export const IconFrame = (p: P) => <Icon {...p}><path d="M4 8V4h4M16 4h4v4M20 16v4h-4M8 20H4v-4" /></Icon>
export const IconFolder = (p: P) => <Icon {...p}><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" /></Icon>
export const IconCamera = (p: P) => <Icon {...p}><path d="M4 8h3l2-3h6l2 3h3v11H4z" /><circle cx="12" cy="13" r="3.5" /></Icon>
export const IconRefresh = (p: P) => <Icon {...p}><path d="M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7" /></Icon>
export const IconBack = (p: P) => <Icon strokeWidth={3} {...p}><path d="M19 12H5M11 6l-6 6 6 6" /></Icon>
export const IconEye = (p: P) => <Icon {...p}><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></Icon>
export const IconLock = (p: P) => <Icon {...p}><rect x="5" y="11" width="14" height="9" rx="2" /><path d="M8 11V8a4 4 0 0 1 8 0v3" /></Icon>
export const IconFlag = (p: P) => <Icon {...p}><path d="M5 21V4c5-4 9 4 14 0v10c-5 4-9-4-14 0" /></Icon>
export const IconShare = (p: P) => <Icon {...p}><path d="M10 13a5 5 0 0 0 7.07 0l2.83-2.83a5 5 0 0 0-7.07-7.07L11 4.93" /><path d="M14 11a5 5 0 0 0-7.07 0L4.1 13.83a5 5 0 0 0 7.07 7.07L13 19.07" /></Icon>
