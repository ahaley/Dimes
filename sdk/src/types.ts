export type ObservationKind =
  | 'ExplicitFeedback'
  | 'SolicitedFeedback'
  | 'BehavioralFriction'
  | 'TechnicalError'

/** The body the Dimes ingest endpoint expects. */
export interface IngestBody {
  kind: ObservationKind
  payload: string
  contextMetadata?: string | null
  fingerprint?: string | null
}

/** Minimal transport surface — real `fetch` adapts to this; tests inject a fake. */
export type FetchLike = (input: string, init: RequestInit) => Promise<{ ok: boolean; status: number }>

export interface UserContext {
  userId?: string
  role?: string
  segment?: string
}

export interface Breadcrumb {
  t: number
  type: string
  message: string
}

export interface ContextMetadata {
  route?: string
  appVersion?: string
  release?: string
  userAgent?: string
  viewport?: { w: number; h: number }
  user?: UserContext
  breadcrumbs?: Breadcrumb[]
  [k: string]: unknown
}

/** Normalized event handed to `beforeSend` before serialization. */
export interface CaptureEvent {
  kind: ObservationKind
  payload: Record<string, unknown>
  fingerprint?: string
  context: ContextMetadata
}

export interface CaptureInput {
  kind: ObservationKind
  payload: Record<string, unknown> | string
  fingerprint?: string
}

export interface DimesConfig {
  /** Base URL of the Dimes API, e.g. https://dimes.internal */
  endpoint: string
  /** The configured observation source id this client posts to. */
  sourceId: string
  appVersion?: string
  release?: string
  /** Master switch; when false nothing is captured (e.g. before consent). Default true. */
  enabled?: boolean
  /** 0..1 sampling applied to passive signals. Explicit feedback always sends. Default 1. */
  sampleRate?: number
  /** Auto-capture window errors / unhandled rejections. Default true. */
  captureErrors?: boolean
  /** Auto-capture rage clicks. Default true. */
  captureRageClicks?: boolean
  maxBreadcrumbs?: number
  /** Inspect / redact / drop each event (return null to drop). */
  beforeSend?: (event: CaptureEvent) => CaptureEvent | null
  /** Override transport (tests / custom delivery). */
  transport?: FetchLike
  debug?: boolean
}
