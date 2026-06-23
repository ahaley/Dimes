import { useState } from 'react'
import { THEMES, type Theme } from '../theme'
import { cx } from '../components/ui'

// Page-background + accent preview for each theme's swatch (purely presentational; mirrors the
// token values in index.css). Light/dark use Tailwind's defaults.
const SWATCH: Record<Theme, { bg: string; accent: string }> = {
  light: { bg: '#f8fafc', accent: '#4f46e5' },
  sepia: { bg: '#f4ecd8', accent: '#2f7a6f' },
  dark: { bg: '#0f172a', accent: '#6366f1' },
  division: { bg: '#121417', accent: '#ff7a1a' },
}

function Swatch({ theme }: { theme: Theme }) {
  const { bg, accent } = SWATCH[theme]
  return (
    <span
      aria-hidden
      className="inline-block h-3.5 w-3.5 shrink-0 rounded-full border border-black/10 dark:border-white/15"
      style={{ background: `linear-gradient(135deg, ${bg} 50%, ${accent} 50%)` }}
    />
  )
}

/** Header control to choose among the available themes — a compact dropdown (mirrors the ChangeCard
 * status-menu pattern: toggle button + full-screen backdrop + absolutely-positioned panel). */
export function ThemePicker({ theme, onChange }: { theme: Theme; onChange: (t: Theme) => void }) {
  const [open, setOpen] = useState(false)
  const label = THEMES.find((t) => t.value === theme)?.label ?? 'Theme'
  return (
    <div className="relative">
      <button
        onClick={() => setOpen((v) => !v)}
        title={`Theme: ${label}`}
        aria-label="Choose theme"
        aria-haspopup="menu"
        aria-expanded={open}
        className="flex items-center gap-1.5 rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800 dark:hover:text-slate-200"
      >
        <Swatch theme={theme} />
        <span className="text-xs text-slate-400" aria-hidden>▾</span>
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-30" onClick={() => setOpen(false)} />
          <div
            role="menu"
            className="absolute right-0 top-9 z-40 w-44 overflow-hidden rounded-md border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-800"
          >
            {THEMES.map((t) => (
              <button
                key={t.value}
                role="menuitemradio"
                aria-checked={t.value === theme}
                onClick={() => { onChange(t.value); setOpen(false) }}
                className={cx(
                  'flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-slate-50 dark:hover:bg-slate-700',
                  t.value === theme
                    ? 'font-medium text-slate-900 dark:text-slate-100'
                    : 'text-slate-700 dark:text-slate-200',
                )}
              >
                <Swatch theme={t.value} />
                <span className="flex-1">{t.label}</span>
                {t.value === theme && <span className="text-indigo-600 dark:text-indigo-400" aria-hidden>✓</span>}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
