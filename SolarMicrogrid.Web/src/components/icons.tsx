import type { ReactNode, SVGProps } from 'react'

type IconProps = SVGProps<SVGSVGElement>

function Icon({ children, ...props }: IconProps & { children: ReactNode }) {
  return (
    <svg
      width={20}
      height={20}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...props}
    >
      {children}
    </svg>
  )
}

export const HomeIcon = (p: IconProps) => <Icon {...p}><path d="M3 11.5 12 4l9 7.5" /><path d="M5 10v10h14V10M9 20v-6h6v6" /></Icon>
export const UsersIcon = (p: IconProps) => <Icon {...p}><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" /></Icon>
export const SunIcon = (p: IconProps) => <Icon {...p}><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.93 4.93l1.42 1.42M17.65 17.65l1.42 1.42M2 12h2M20 12h2M4.93 19.07l1.42-1.42M17.65 6.35l1.42-1.42" /></Icon>
export const MoonIcon = (p: IconProps) => <Icon {...p}><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" /></Icon>
export const CalendarIcon = (p: IconProps) => <Icon {...p}><rect x="3" y="5" width="18" height="16" rx="2" /><path d="M16 3v4M8 3v4M3 11h18" /></Icon>
export const DashboardIcon = (p: IconProps) => <Icon {...p}><rect x="3" y="3" width="7" height="7" rx="2" /><rect x="14" y="3" width="7" height="7" rx="2" /><rect x="3" y="14" width="7" height="7" rx="2" /><rect x="14" y="14" width="7" height="7" rx="2" /></Icon>
export const BoltIcon = (p: IconProps) => <Icon {...p}><path d="M13 2 3 14h8l-1 8 10-12h-8z" /></Icon>
export const HistoryIcon = (p: IconProps) => <Icon {...p}><path d="M3 12a9 9 0 1 0 3-6.7L3 8" /><path d="M3 3v5h5M12 7v5l3 2" /></Icon>
export const ShieldIcon = (p: IconProps) => <Icon {...p}><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10" /><path d="m9 12 2 2 4-4" /></Icon>
export const SearchIcon = (p: IconProps) => <Icon {...p}><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></Icon>
export const MapPinIcon = (p: IconProps) => <Icon {...p}><path d="M20 10c0 5-8 12-8 12S4 15 4 10a8 8 0 1 1 16 0z" /><circle cx="12" cy="10" r="2" /></Icon>
export const AlertIcon = (p: IconProps) => <Icon {...p}><path d="M10.3 3.4 2.2 18a2 2 0 0 0 1.7 3h16.2a2 2 0 0 0 1.7-3L13.7 3.4a2 2 0 0 0-3.4 0z" /><path d="M12 9v4M12 17h.01" /></Icon>
export const CheckIcon = (p: IconProps) => <Icon {...p}><path d="m5 12 4 4L19 6" /></Icon>
export const MenuIcon = (p: IconProps) => <Icon {...p}><path d="M4 6h16M4 12h16M4 18h16" /></Icon>
export const PhoneIcon = (p: IconProps) => <Icon {...p}><rect x="6" y="2" width="12" height="20" rx="3" /><path d="M10 5h4M11 18h2" /></Icon>
export const GridIcon = (p: IconProps) => <Icon {...p}><path d="M4 4h16v16H4zM4 12h16M12 4v16" /></Icon>
export const InboxIcon = (p: IconProps) => <Icon {...p}><path d="M22 12h-6l-2 3h-4l-2-3H2" /><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" /></Icon>
export const LockIcon = (p: IconProps) => <Icon {...p}><rect x="4" y="11" width="16" height="10" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /></Icon>
export const ArrowRightIcon = (p: IconProps) => <Icon {...p}><path d="M5 12h14M13 6l6 6-6 6" /></Icon>
export const ArrowLeftIcon = (p: IconProps) => <Icon {...p}><path d="M19 12H5M11 18l-6-6 6-6" /></Icon>
export const LogOutIcon = (p: IconProps) => <Icon {...p}><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" /></Icon>
export const CompassIcon = (p: IconProps) => <Icon {...p}><circle cx="12" cy="12" r="9" /><path d="m15.5 8.5-2 5-5 2 2-5z" /></Icon>
export const ClockIcon = (p: IconProps) => <Icon {...p}><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></Icon>
export const BatteryIcon = (p: IconProps) => <Icon {...p}><rect x="2" y="7" width="18" height="10" rx="2" /><path d="M22 11v2M6 11v2M10 11v2" /></Icon>
export const QrIcon = (p: IconProps) => <Icon {...p}><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><path d="M14 14h3v3M21 14v.01M14 21h.01M17.5 21H21v-3.5" /></Icon>
export const MailIcon = (p: IconProps) => <Icon {...p}><rect x="3" y="5" width="18" height="14" rx="2.5" /><path d="m4 7 8 6 8-6" /></Icon>
export const LeafIcon = (p: IconProps) => <Icon {...p}><path d="M5 19c0-8 5-14 15-14 0 10-6 15-14 15" /><path d="M5 19c3-4 6-6.5 10-8.5" /></Icon>
export const MessageIcon = (p: IconProps) => <Icon {...p}><path d="M21 12a8.5 8.5 0 0 1-12.4 7.6L3 21l1.5-5.2A8.5 8.5 0 1 1 21 12z" /></Icon>
export const CloseIcon = (p: IconProps) => <Icon {...p}><path d="M6 6l12 12M18 6 6 18" /></Icon>
export const ChevronDownIcon = (p: IconProps) => <Icon {...p}><path d="m6 9 6 6 6-6" /></Icon>
export const ScaleIcon = (p: IconProps) => <Icon {...p}><path d="M12 3v18M7 21h10M5 7h14" /><path d="m5 7-3 6a3 3 0 0 0 6 0zM19 7l-3 6a3 3 0 0 0 6 0z" /></Icon>
export const EyeIcon = (p: IconProps) => <Icon {...p}><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></Icon>
export const StationIcon = (p: IconProps) => <Icon {...p}><path d="M4 14h16l-2-8H6z" /><path d="M8 6l-.8 8M16 6l.8 8M5 10h14M12 14v7M8 21h8" /></Icon>

/** The SolarGrid app icon, matching the Android launcher icon (ic_launcher.xml). */
export const SolarGridLogoIcon = (p: IconProps) => (
  <svg
    width={32}
    height={32}
    viewBox="0 0 108 108"
    aria-hidden="true"
    focusable="false"
    {...p}
  >
    <rect width="108" height="108" rx="24" fill="#0B3D2E" />
    <circle cx="54" cy="35" r="17" fill="#F4B942" />
    <path d="M28 62h52v9H28zM35 76h38v9H35z" fill="#FFFFFF" />
  </svg>
)
