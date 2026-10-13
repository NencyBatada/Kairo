export function SignalMotif({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 600 260" aria-hidden="true" className={className}>
      <path d="M0 214 C 120 210, 220 204, 300 190 S 470 128, 588 52 L 588 112 C 470 172, 380 204, 300 210 S 120 224, 0 228 Z" className="fill-primary/10" />
      <path d="M0 220 C 140 217, 260 215, 360 213 S 520 208, 588 205" fill="none" strokeWidth="2" strokeDasharray="6 8" strokeLinecap="round" className="stroke-subtle-foreground/60" />
      <path d="M0 220 C 120 216, 220 210, 300 197 S 470 140, 588 74" fill="none" strokeWidth="3" strokeLinecap="round" className="stroke-primary" />
      <circle cx="588" cy="74" r="7" className="fill-primary" />
    </svg>
  );
}
