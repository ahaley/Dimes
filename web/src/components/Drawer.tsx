import { useEffect, useState, type ReactNode } from 'react'
import { Button, cx } from './ui'

/** Right-anchored slide-over panel. Full-width on phones and small tablets, ~440px on md+. Esc and
 * overlay-click dismiss. Slide animation respects prefers-reduced-motion. */
export function Drawer({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const [shown, setShown] = useState(false)

  useEffect(() => {
    setShown(true)
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/40" onClick={onClose}>
      <aside
        role="dialog"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
        className={cx(
          'flex h-full w-full flex-col bg-white shadow-xl md:w-[440px] dark:bg-slate-900',
          'transition-transform duration-200 ease-out motion-reduce:transition-none',
          shown ? 'translate-x-0' : 'translate-x-full',
        )}
      >
        <header className="flex items-center justify-between border-b border-slate-200 px-5 py-3 dark:border-slate-800">
          <h2 className="text-base font-semibold text-slate-800 dark:text-slate-100">{title}</h2>
          <Button variant="subtle" onClick={onClose}>✕</Button>
        </header>
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
      </aside>
    </div>
  )
}
