import { useState, type ReactNode } from 'react'
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
 * A ⋯ trigger that toggles a dropdown of actions, with a click-away backdrop. Shared by the board
 * toolbar's "more" menu and the site-settings row actions. (The Change-card status menu is deliberately
 * separate: it lives inside a draggable card and needs extra drag-sensor guards on every surface.)
 */
export function OverflowMenu({
  actions, label, align = 'right', variant = 'default',
}: {
  actions: MenuAction[]
  label: string
  align?: 'left' | 'right'
  variant?: 'default' | 'subtle'
}) {
  const [open, setOpen] = useState(false)
  return (
    <div className="relative">
      <Button variant={variant} aria-label={label} aria-haspopup="menu" onClick={() => setOpen((v) => !v)}>⋯</Button>
      {open && (
        <>
          <div className="fixed inset-0 z-10" onClick={() => setOpen(false)} />
          <div
            className={cx(
              'absolute top-full z-20 mt-1 w-48 max-w-[calc(100vw-2rem)] overflow-hidden rounded-md border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-800',
              align === 'right' ? 'right-0' : 'left-0',
            )}
          >
            {actions.map((a, i) => (
              <button
                key={i}
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
