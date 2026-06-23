export type Theme = 'light' | 'sepia' | 'dark' | 'division'

const THEME_KEY = 'dimes.theme'

// Themes whose surfaces are dark: they add the `.dark` class so every existing `dark:` utility applies;
// a `data-theme` attribute then re-tints the palette on top (see index.css).
const DARK_THEMES: ReadonlySet<Theme> = new Set<Theme>(['dark', 'division'])

/** The selectable themes, in picker order. */
export const THEMES: { value: Theme; label: string }[] = [
  { value: 'light', label: 'Light' },
  { value: 'sepia', label: 'Sepia' },
  { value: 'dark', label: 'Dark' },
  { value: 'division', label: 'Division' },
]

/** Saved preference if any, else the OS preference. */
export function getInitialTheme(): Theme {
  const saved = localStorage.getItem(THEME_KEY)
  if (THEMES.some((t) => t.value === saved)) return saved as Theme
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

/** Apply the theme to <html> (the `.dark` class for dark-family themes plus a `data-theme` attribute
 * that re-tints the Tailwind color tokens) and persist the choice. */
export function applyTheme(theme: Theme): void {
  const root = document.documentElement
  root.classList.toggle('dark', DARK_THEMES.has(theme))
  root.dataset.theme = theme
  localStorage.setItem(THEME_KEY, theme)
}
