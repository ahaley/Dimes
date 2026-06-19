import type { DimesClient } from './client'

/** Mount a minimal floating "Feedback" button that prompts for a message and captures it as explicit
 * feedback. Intentionally dependency-free and vanilla-DOM so it drops into any host app. Returns a
 * disposer. No-op outside the browser. */
export function mountFeedbackWidget(client: DimesClient, opts: { label?: string } = {}): () => void {
  if (typeof document === 'undefined') return () => {}

  const label = opts.label ?? 'Feedback'
  const button = document.createElement('button')
  button.textContent = label
  button.setAttribute('data-dimes', 'feedback-button')
  button.style.cssText =
    'position:fixed;right:16px;bottom:16px;z-index:2147483647;padding:8px 14px;border-radius:9999px;' +
    'border:none;background:#4f46e5;color:#fff;cursor:pointer;font:500 14px system-ui,sans-serif;' +
    'box-shadow:0 2px 8px rgba(0,0,0,.2)'

  const onClick = () => {
    const message = window.prompt(`${label}: describe the problem or idea`)
    if (message && message.trim()) {
      client.captureFeedback(message.trim(), { via: 'widget' })
    }
  }

  button.addEventListener('click', onClick)
  document.body.appendChild(button)
  return () => {
    button.removeEventListener('click', onClick)
    button.remove()
  }
}
