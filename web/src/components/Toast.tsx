import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { cx } from './ui'

type ToastTone = 'success' | 'error'
interface ToastItem { id: number; tone: ToastTone; message: string }

interface ToastApi {
  success: (message: string) => void
  error: (message: string) => void
}

const ToastContext = createContext<ToastApi | null>(null)

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used within a ToastProvider')
  return ctx
}

let nextId = 1

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([])

  const remove = useCallback((id: number) => {
    setToasts((current) => current.filter((t) => t.id !== id))
  }, [])

  const push = useCallback((tone: ToastTone, message: string) => {
    const id = nextId++
    setToasts((current) => [...current, { id, tone, message }])
    setTimeout(() => remove(id), 4000)
  }, [remove])

  const api = useMemo<ToastApi>(
    () => ({ success: (m) => push('success', m), error: (m) => push('error', m) }),
    [push],
  )

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="fixed bottom-4 right-4 z-[60] flex w-80 flex-col gap-2" aria-live="polite">
        {toasts.map((t) => (
          <div
            key={t.id}
            role="status"
            className={cx(
              'flex items-start justify-between gap-3 rounded-md border px-3 py-2 text-sm shadow-sm',
              t.tone === 'error'
                ? 'border-red-200 bg-red-50 text-red-700 dark:border-red-900 dark:bg-red-950/60 dark:text-red-300'
                : 'border-green-200 bg-green-50 text-green-700 dark:border-green-900 dark:bg-green-950/60 dark:text-green-300',
            )}
          >
            <span className="min-w-0 break-words">{t.message}</span>
            <button onClick={() => remove(t.id)} className="shrink-0 text-current/60 hover:text-current">✕</button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}
