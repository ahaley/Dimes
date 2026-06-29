import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'

export function cx(...parts: Array<string | false | null | undefined>): string {
  return parts.filter(Boolean).join(' ')
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'default' | 'subtle' | 'danger'
}
export function Button({ variant = 'default', className, ...props }: ButtonProps) {
  const styles: Record<string, string> = {
    primary: 'bg-indigo-600 text-white hover:bg-indigo-500 border-transparent dark:bg-indigo-500 dark:hover:bg-indigo-400',
    default: 'bg-white text-slate-800 hover:bg-slate-50 border-slate-300 dark:bg-slate-800 dark:text-slate-100 dark:hover:bg-slate-700 dark:border-slate-600',
    subtle: 'bg-transparent text-slate-600 hover:bg-slate-100 border-transparent dark:text-slate-300 dark:hover:bg-slate-800',
    danger: 'bg-white text-red-600 hover:bg-red-50 border-red-300 dark:bg-slate-800 dark:text-red-400 dark:border-red-900 dark:hover:bg-red-950/40',
  }
  return (
    <button
      {...props}
      className={cx(
        'inline-flex items-center justify-center rounded-md border px-3 py-2 text-sm font-medium sm:py-1.5',
        'disabled:opacity-50 disabled:cursor-not-allowed transition-colors',
        styles[variant],
        className,
      )}
    />
  )
}

export function Badge({ children, tone = 'slate' }: { children: ReactNode; tone?: string }) {
  const tones: Record<string, string> = {
    slate: 'bg-slate-100 text-slate-700 dark:bg-slate-700 dark:text-slate-200',
    indigo: 'bg-indigo-100 text-indigo-700 dark:bg-indigo-500/20 dark:text-indigo-300',
    green: 'bg-green-100 text-green-700 dark:bg-green-500/20 dark:text-green-300',
    amber: 'bg-amber-100 text-amber-800 dark:bg-amber-500/20 dark:text-amber-300',
    red: 'bg-red-100 text-red-700 dark:bg-red-500/20 dark:text-red-300',
    violet: 'bg-violet-100 text-violet-700 dark:bg-violet-500/20 dark:text-violet-300',
  }
  return (
    <span className={cx('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium', tones[tone] ?? tones.slate)}>
      {children}
    </span>
  )
}

export function Card({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx('rounded-lg border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900', className)}>{children}</div>
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block space-y-1">
      <span className="text-xs font-medium text-slate-600 dark:text-slate-300">{label}</span>
      {children}
    </label>
  )
}

// text-base on phones keeps inputs at 16px so iOS Safari doesn't zoom the page on focus; the smaller
// desktop size returns at sm+. Vertical padding matches Button so inline input+button rows align.
const inputCx = 'w-full rounded-md border border-slate-300 bg-white px-2.5 py-2 text-base outline-none focus:border-indigo-500 sm:py-1.5 sm:text-sm dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100'

export function TextInput(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={cx(inputCx, props.className)} />
}

export function Textarea(props: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea {...props} className={cx(inputCx, 'min-h-20', props.className)} />
}

export function Select(props: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...props} className={cx(inputCx, 'pr-8', props.className)} />
}

export function Modal({ title, onClose, children, wide }: { title: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  return (
    <div
      className="fixed inset-0 z-50 flex justify-center overflow-y-auto bg-slate-900/50 sm:items-start sm:p-4 dark:bg-black/70"
      onClick={onClose}
    >
      <div
        className={cx(
          // Full-screen sheet on phones (edge-to-edge, fills the viewport); a centered, bounded card
          // from sm up. The border + ring lift that card off the dimmed backdrop — in dark mode a
          // borderless slate-900 panel blends into the darkened page, so the edge matters most there.
          'flex min-h-full w-full flex-col bg-white shadow-2xl dark:bg-slate-900',
          'sm:mt-10 sm:mb-10 sm:max-h-[calc(100vh-5rem)] sm:min-h-0 sm:rounded-xl sm:border sm:border-slate-200 sm:ring-1 sm:ring-black/5 dark:sm:border-slate-700 dark:sm:ring-white/10',
          wide ? 'sm:max-w-3xl' : 'sm:max-w-lg',
        )}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex shrink-0 items-center justify-between border-b border-slate-200 px-5 py-3 dark:border-slate-800">
          <h2 className="text-base font-semibold text-slate-800 dark:text-slate-100">{title}</h2>
          <Button variant="subtle" onClick={onClose}>✕</Button>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
      </div>
    </div>
  )
}

export function ErrorText({ error }: { error: unknown }) {
  if (!error) return null
  const message = error instanceof Error ? error.message : String(error)
  return <p className="text-sm text-red-600 dark:text-red-400">{message}</p>
}
