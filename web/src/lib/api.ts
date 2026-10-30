export type Role = 'student' | 'uploader'

export interface Me {
  id: string
  uniId: string
  email: string
  role: Role
  initials: string
}

export interface AppConfig {
  name: string
  emailDomain: string
  aiOnline: boolean
}

export interface Course {
  id: number
  slug: string
  name: string
  semester: number
  group: string
  color: string
  aliases: string[]
  files: number
}

export type MaterialType = 'slides' | 'past_paper' | 'quiz' | 'exercise' | 'lab' | 'notes' | 'other'

export interface FileDto {
  id: string
  title: string
  originalName: string
  sourceKind: 'pdf' | 'office' | 'images'
  sizeBytes: number
  pageCount: number
  status: 'uploaded' | 'queued' | 'splitting' | 'reading' | 'tagging' | 'cropping' | 'ready' | 'failed'
  step: number
  progress: number
  error: string | null
  courseId: number | null
  courseSlug: string | null
  courseName: string | null
  courseColor: string | null
  materialType: MaterialType
  year: number | null
  assessmentLabel: string | null
  folder: string | null
  guessed: string[]
  createdAt: string
  thumbUrl: string | null
  parts: number
}

export interface Box { x: number; y: number; w: number; h: number }

export interface ResultCard {
  ref: string
  kind: 'question' | 'answer' | 'slide' | 'page' | 'file'
  imageUrl: string
  pageImageUrl: string
  thumbUrl: string
  fileId: string
  pageId: string
  regionId: string | null
  fileTitle: string
  fileName: string
  sourceKind: FileDto['sourceKind']
  courseSlug: string
  courseName: string
  courseColor: string
  semester: number
  materialType: MaterialType
  year: number | null
  assessmentLabel: string | null
  pageNumber: number
  pageCount: number
  questionNumber: string | null
  tags: string[]
  box: Box | null
  width: number
  height: number
  saved: boolean
  /** Screenshot of this question's answer, cropped from the original file (never typed out). */
  answerRef: string | null
  answerImageUrl: string | null
}

export interface Conversation { id: string; title: string; updatedAt: string }

export interface ChatMessage {
  id: string
  role: 'user' | 'bot'
  text: string
  refused: boolean
  createdAt: string
  results: ResultCard[]
  /** Results held back behind "show more". */
  moreCount?: number
  /** Choices of a clarifying question; count is null while that option's search is still running. */
  options?: ChatOption[] | null
}

export interface ChatOption { label: string; count: number | null }

export interface PageRegion {
  saved?: boolean
  id: string
  questionNumber: string | null
  x: number
  y: number
  w: number
  h: number
}

export interface PageThumb {
  saved?: boolean
  id: string
  number: number
  kind: string
  thumbUrl: string
  pageUrl: string
  tags: string[]
  questions: number
  width: number
  height: number
  regions?: PageRegion[]
}

export interface UploadCheckItem {
  fileName: string
  sizeBytes: number
  sha256?: string
  courseId?: number | null
}

export interface UploadCheckResult {
  index: number
  kind: 'none' | 'exact' | 'similar'
  existing: FileDto | null
}

export interface CourseListing {
  course: Course
  types: Record<string, number>
  folders: { name: string; path: string; files: number }[]
  files: FileDto[]
  tags: { tag: string; pages: number }[]
  years: number[]
}

export interface RegionDto {
  id: string
  label: 'question' | 'answer' | 'other'
  x: number
  y: number
  w: number
  h: number
  questionNumber: string | null
  text: string
  order: number
  cropUrl: string | null
}

export interface InspectPage {
  id: string
  number: number
  width: number
  height: number
  kind: string
  text: string
  aiSummary: string
  topic: string | null
  tags: string[]
  year: number | null
  error: string | null
  editedAt: string | null
  originalUrl: string
  pageUrl: string
  regions: RegionDto[]
}

export interface SavedGroup { course: string; name: string; color: string; items: ResultCard[] }

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const init: RequestInit = { method, credentials: 'same-origin', headers: {} }
  if (body instanceof FormData) init.body = body
  else if (body !== undefined) {
    init.body = JSON.stringify(body)
    ;(init.headers as Record<string, string>)['content-type'] = 'application/json'
  }
  const res = await fetch(url, init)
  if (!res.ok) {
    let message = res.status === 401 ? 'Please sign in again.' : `Something went wrong (${res.status}).`
    try {
      const j = await res.json()
      if (j?.error) message = j.error
    } catch { /* not JSON */ }
    throw new ApiError(res.status, message)
  }
  if (res.status === 204) return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body),
  put: <T>(url: string, body?: unknown) => request<T>('PUT', url, body),
  patch: <T>(url: string, body?: unknown) => request<T>('PATCH', url, body),
  del: <T>(url: string) => request<T>('DELETE', url),
}

export interface StreamHandlers {
  onStatus?: (text: string) => void
  onMessage: (data: { conversation: Conversation; message: ChatMessage }) => void
  onOptions?: (data: { messageId: string; options: ChatOption[] }) => void
}

/** What a send is: typed text, a chip of the last clarifying question, or "show more" on the last results. */
export interface SendExtra { option?: number; more?: boolean }

/**
 * POSTs a chat message and reads the Server-Sent Events stream: status lines, then the bot message, then (after a
 * clarifying question) option search updates.
 */
export async function sendChat(conversationId: string, text: string, h: StreamHandlers, extra: SendExtra = {}, signal?: AbortSignal) {
  const res = await fetch(`/api/chats/${conversationId}/messages`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ text, ...extra }),
    signal,
  })
  if (!res.ok || !res.body) throw new ApiError(res.status, 'The bot tripped over something. Try again?')
  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })
    let idx
    while ((idx = buffer.indexOf('\n\n')) >= 0) {
      const chunk = buffer.slice(0, idx)
      buffer = buffer.slice(idx + 2)
      const event = /^event: (.*)$/m.exec(chunk)?.[1]
      const data = /^data: (.*)$/m.exec(chunk)?.[1]
      if (!event || data === undefined) continue
      const parsed = JSON.parse(data)
      if (event === 'status') h.onStatus?.(parsed.text)
      else if (event === 'message') h.onMessage(parsed)
      else if (event === 'options') h.onOptions?.(parsed)
      else if (event === 'error') throw new ApiError(503, parsed.error ?? 'The AI could not complete this reply.')
    }
  }
}

export const TYPE_LABELS: Record<MaterialType, string> = {
  slides: 'Slides',
  past_paper: 'Past papers',
  quiz: 'Quizzes',
  exercise: 'Exercises',
  lab: 'Labs',
  notes: 'Notes',
  other: 'Other',
}

export const TYPE_SINGULAR: Record<MaterialType, string> = {
  slides: 'Slides',
  past_paper: 'Past paper',
  quiz: 'Quiz',
  exercise: 'Exercise',
  lab: 'Lab',
  notes: 'Notes',
  other: 'Other',
}

export const TYPE_ORDER: MaterialType[] = ['slides', 'past_paper', 'quiz', 'exercise', 'lab', 'notes', 'other']

const plural = new Intl.PluralRules('en')
export function count(n: number, one: string, many = one + 's') {
  return `${n} ${plural.select(n) === 'one' ? one : many}`
}

export function formatBytes(n: number) {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${Math.round(n / 1024)} KB`
  return `${(n / 1024 / 1024).toFixed(1)} MB`
}

/** Card frame colours rotate like the design (blue, pink, green, yellow). */
export const CARD_COLORS = ['#2F6BFF', '#FF8CC2', '#2BC48A', '#FFD34D']
export const onDark = (color: string) => color.toUpperCase() === '#2F6BFF'
