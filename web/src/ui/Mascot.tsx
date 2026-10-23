type Mood = 'smile' | 'happy' | 'hungry'

/** The orange blob. smile = default/logo, happy = results found, hungry = upload dropzone. */
export function Mascot({ size = 48, mood = 'smile', color = '#FF6B2C', cheeks = true, tilt = 0, className }: {
  size?: number
  mood?: Mood
  color?: string
  cheeks?: boolean
  tilt?: number
  className?: string
}) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" aria-hidden="true" className={className}
      style={tilt ? { transform: `rotate(${tilt}deg)` } : undefined}>
      <path d="M24 3c12 0 21 8 21 20s-8 22-21 22S3 36 3 23 12 3 24 3z" fill={color} />
      <ellipse cx="17" cy="21" rx="4" ry="5" fill="#2A1B0F" />
      <ellipse cx="31" cy="21" rx="4" ry="5" fill="#2A1B0F" />
      <circle cx="18.3" cy="19.2" r="1.4" fill="#FFFFFF" />
      <circle cx="32.3" cy="19.2" r="1.4" fill="#FFFFFF" />
      {mood === 'smile' && <path d="M18 31q6 5 12 0" stroke="#2A1B0F" strokeWidth="3" fill="none" strokeLinecap="round" />}
      {mood === 'happy' && <path d="M17 30q7 7 14 0z" fill="#2A1B0F" />}
      {mood === 'hungry' && <ellipse cx="24" cy="32" rx="5" ry="5.5" fill="#2A1B0F" />}
      {cheeks && <ellipse cx="11" cy="28" rx="3.5" ry="2" fill="#FF9F78" />}
      {cheeks && <ellipse cx="37" cy="28" rx="3.5" ry="2" fill="#FF9F78" />}
    </svg>
  )
}
