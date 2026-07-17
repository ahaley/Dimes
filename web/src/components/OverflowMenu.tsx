import { useEffect, useState, type ReactNode } from 'react'
import { Button, cx } from './ui'

export type MenuAction = {
  label: ReactNode
  onClick: () => void
  disabled?: boolean
  title?: string
  danger?: boolean
  /** Optional right-aligned content for the item, e.g. a count badge. */
  trailing?: ReactNode
}

/**
 * A dropdown of actions with a click-away backdrop. The trigger is ⋯ by default — the board toolbar's
 * "more" menu and the site-settings row actions both use it that way — but `trigger` takes any content,
 * which is how the toolbar's `Export ▾` menu is built. (The Change-card status menu is deliberately
 * separate: it lives inside a draggable card and needs extra drag-sensor guards on every surface.)
 */
export function OverflowMenu({
  actions, label, align = 'right', variant = 'default', trigger = '⋯', width = 'default',
}: {
  actions: MenuAction[]
  /** Accessible name for the trigger. Also the visible text when `trigger` is omitted. */
  label: string
  align?: 'left' | 'right'
  variant?: 'default' | 'subtle'
  /** Trigger content. Defaults to the ⋯ glyph. */
  trigger?: ReactNode
  /** 'wide' fits items carrying a description line under the label. */
  width?: 'default' | 'wide'
}) {
  const [open, setOpen] = useState(false)

  // Esc closes. Cheap here, and this is no longer only a phone-sized fallback: as the Export trigger it
  // is a primary control a keyboard user can land on.
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])

  return (
    <div className="relative">
      <Button
        variant={variant}
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
      >
        {trigger}
      </Button>
      {open && (
        <>
          <div className="fixed inset-0 z-10" onClick={() => setOpen(false)} />
          <div
            role="menu"
            className={cx(
              'absolute top-full z-20 mt-1 max-w-[calc(100vw-2rem)] overflow-hidden rounded-md border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-800',
              width === 'wide' ? 'w-64' : 'w-48',
              align === 'right' ? 'right-0' : 'left-0',
            )}
          >
            {actions.map((a, i) => (
              <button
                key={i}
                role="menuitem"
                disabled={a.disabled}
                title={a.title}
                onClick={() => { setOpen(false); a.onClick() }}
                className={cx(
                  'flex w-full items-center justify-between gap-2 px-3 py-2.5 text-left text-sm hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50 dark:hover:bg-slate-700',
                  a.danger ? 'text-red-600 dark:text-red-400' : 'text-slate-700 dark:text-slate-200',
                )}
              >
                <span>{a.label}</span>
                {a.trailing}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
