import type { ReactNode } from 'react'

// Matches bare http(s) URLs. The negated trailing class keeps common sentence punctuation
// (".", ",", ")") from being swallowed into the link when a URL ends a sentence.
const URL_RE = /https?:\/\/[^\s<]+[^\s<.,:;!?)'"\]]/gi

/**
 * Renders free-form text with any embedded http(s) URLs turned into clickable links. Everything is
 * emitted as React children (never dangerouslySetInnerHTML) and only http/https anchors are produced,
 * so there's no injection surface. Wrap it in whatever block element the caller already uses (e.g. a
 * `whitespace-pre-wrap` <p>) — this only transforms the inline text.
 */
export function Linkify({ text }: { text: string }): ReactNode {
  if (!text) return text

  const nodes: ReactNode[] = []
  let lastIndex = 0
  let key = 0
  // Fresh matcher state per call — a module-level /g regex carries lastIndex between uses otherwise.
  const re = new RegExp(URL_RE.source, 'gi')
  let match: RegExpExecArray | null
  while ((match = re.exec(text)) !== null) {
    const url = match[0]
    if (match.index > lastIndex) {
      nodes.push(text.slice(lastIndex, match.index))
    }
    nodes.push(
      <a
        key={key++}
        href={url}
        target="_blank"
        rel="noreferrer"
        className="text-indigo-600 hover:underline dark:text-indigo-400"
      >
        {url}
      </a>,
    )
    lastIndex = match.index + url.length
  }
  if (lastIndex < text.length) {
    nodes.push(text.slice(lastIndex))
  }

  return nodes
}
