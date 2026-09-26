import type { SVGProps } from 'react'

interface CaveLogoMarkProps extends SVGProps<SVGSVGElement> {
  size?: number
}

/** Original CAVE product mark: a compact, directional architecture flow. */
export function CaveLogoMark({ size = 24, ...props }: CaveLogoMarkProps) {
  return (
    <svg
      aria-hidden="true"
      focusable="false"
      viewBox="0 0 64 64"
      width={size}
      height={size}
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      {...props}
    >
      <path
        d="M21 32h7c7.5 0 8-18 16-18M28 32c7.5 0 8 18 16 18"
        stroke="currentColor"
        strokeWidth="4.5"
        strokeLinecap="round"
      />
      <rect x="3" y="23" width="18" height="18" rx="5" fill="currentColor" />
      <rect x="44" y="5" width="17" height="17" rx="5" fill="currentColor" />
      <rect x="44" y="42" width="17" height="17" rx="5" fill="currentColor" />
    </svg>
  )
}
