import type { ReactNode } from 'react'
import { Button, Textarea } from '../components/ui'

export type ChatBubble = { id: string; mine: boolean; text: string }

/** Shared message-thread renderer for Capture Assist (AI ephemeral + human persisted). `mine` aligns
 * a bubble to the right (the current viewer) or left (the other party). */
export function ChatBubbles({ items, emptyText, footer }: {
  items: ChatBubble[]
  emptyText?: string
  footer?: ReactNode
}) {
  return (
    <div className="min-h-0 flex-1 space-y-3 overflow-auto p-4">
      {items.length === 0 && emptyText && <p className="text-sm text-slate-400">{emptyText}</p>}
      {items.map((m) => (
        <div key={m.id} className={m.mine ? 'text-right' : 'text-left'}>
          <div
            className={
              m.mine
                ? 'inline-block max-w-[85%] whitespace-pre-wrap rounded-lg bg-indigo-600 px-3 py-2 text-left text-sm text-white'
                : 'inline-block max-w-[85%] whitespace-pre-wrap rounded-lg bg-slate-100 px-3 py-2 text-sm text-slate-800 dark:bg-slate-800 dark:text-slate-100'
            }
          >
            {m.text}
          </div>
        </div>
      ))}
      {footer}
    </div>
  )
}

/** Shared composer: a textarea that sends on Enter (Shift+Enter for newline) plus a Send button. */
export function ChatComposer({ value, onChange, onSend, disabled, sendDisabled, placeholder }: {
  value: string
  onChange: (v: string) => void
  onSend: () => void
  disabled?: boolean
  sendDisabled?: boolean
  placeholder?: string
}) {
  return (
    <div className="border-t border-slate-200 p-3 dark:border-slate-800">
      <div className="flex items-end gap-2">
        <Textarea
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault()
              onSend()
            }
          }}
          placeholder={placeholder}
          className="min-h-10"
          disabled={disabled}
        />
        <Button variant="primary" disabled={disabled || sendDisabled || !value.trim()} onClick={onSend}>
          Send
        </Button>
      </div>
    </div>
  )
}
