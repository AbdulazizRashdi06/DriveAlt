import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { api, type AppConfig, type Me, type ResultCard as Card } from '../lib/api'
import { Login } from './Login'
import { Saved } from './Saved'
import { Layout } from '../ui/Layout'
import { ReportButton } from '../ui/ReportButton'
import { ResultCard } from '../ui/ResultCard'

// Owner, 2026-11-08: "i wanna make login optional in home screen you can continue as a guest".

const { state, toast } = vi.hoisted(() => ({
  state: { me: null as Me | null, config: null as AppConfig | null },
  toast: vi.fn(),
}))
vi.mock('../lib/app', () => ({
  useApp: () => ({ me: state.me, config: state.config, loading: false, toast, openViewer: vi.fn(), signOut: vi.fn(), refreshMe: vi.fn() }),
}))
vi.mock('../lib/api', async importOriginal => ({
  ...(await importOriginal<typeof import('../lib/api')>()),
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn(), del: vi.fn() },
}))

const config = (over: Partial<AppConfig> = {}): AppConfig =>
  ({ name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: true, guestAccess: true, guestChat: true, ...over })
const guest: Me = { id: 'g1', uniId: 'Guest', email: '', role: 'guest', initials: 'G' }

const question: Card = {
  ref: 'r:region-id', kind: 'question', fileId: 'file-id', pageId: 'page-id', regionId: 'region-id',
  fileTitle: 'DS Midterm', courseName: 'Data Structures', materialType: 'past_paper',
  imageUrl: '/api/media/crops/region-id', thumbUrl: '/api/media/thumbs/page-id',
  pageNumber: 2, pageCount: 7, year: 2024, tags: [], saved: false,
  pageImageUrl: '/api/media/pages/page-id', fileName: 'midterm.pdf', sourceKind: 'pdf',
  courseSlug: 'data-structures', courseColor: '#2F6BFF', semester: 1, assessmentLabel: null,
  questionNumber: '3', box: { x: 0, y: 0, w: 1, h: 1 }, width: 600, height: 800, answerRef: null, answerImageUrl: null,
}

function renderAt(path: string, routes: React.ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}><MemoryRouter initialEntries={[path]}><Routes>{routes}</Routes></MemoryRouter></QueryClientProvider>)
}

beforeEach(() => { state.me = null; state.config = config(); vi.mocked(api.post).mockReset(); vi.mocked(api.get).mockReset(); toast.mockReset() })
afterEach(cleanup)

describe('Continue as a guest', () => {
  const loginRoutes = <>
    <Route path="/login" element={<Login />} />
    <Route path="/chat" element={<p>chat screen</p>} />
    <Route path="/browse" element={<p>browse screen</p>} />
  </>

  it('starts a guest session from the sign-in screen and lands on chat', async () => {
    vi.mocked(api.post).mockResolvedValue(guest)
    renderAt('/login', loginRoutes)
    expect(screen.getByRole('button', { name: 'Email me a code' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Continue as a guest' }))
    await waitFor(() => expect(screen.getByText('chat screen')).toBeTruthy())
    expect(api.post).toHaveBeenCalledWith('/api/auth/guest')
  })

  it('lands guests on Browse when guest chat is off', async () => {
    state.config = config({ guestChat: false })
    vi.mocked(api.post).mockResolvedValue(guest)
    renderAt('/login', loginRoutes)
    expect(screen.getByText(/Guests can browse and open files\./)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Continue as a guest' }))
    await waitFor(() => expect(screen.getByText('browse screen')).toBeTruthy())
  })

  it('offers no guest button when the server has guest access off', () => {
    state.config = config({ guestAccess: false })
    renderAt('/login', loginRoutes)
    expect(screen.queryByRole('button', { name: /as a guest/ })).toBeNull()
  })

  it('lets a guest who opens sign-in stay there to sign in', () => {
    state.me = guest
    renderAt('/login', loginRoutes)
    expect(screen.getByLabelText('Uni ID')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Keep going as a guest' })).toBeTruthy()
  })
})

describe('What a guest sees', () => {
  it('shows a Sign in button and hides Chat when guest chat is off', () => {
    state.me = guest
    state.config = config({ guestChat: false })
    renderAt('/browse', <Route element={<Layout />}><Route path="/browse" element={<p>browse screen</p>} /></Route>)
    expect(screen.getByText('browse screen')).toBeTruthy()
    expect(screen.queryAllByRole('link', { name: /Chat/ })).toHaveLength(0)
    expect(screen.queryAllByRole('link', { name: 'Upload' })).toHaveLength(0)
    expect(screen.getByRole('link', { name: 'Sign in' }).getAttribute('href')).toBe('/login')
  })

  it('keeps Chat for guests when guest chat is on', () => {
    state.me = guest
    renderAt('/browse', <Route element={<Layout />}><Route path="/browse" element={<p>browse screen</p>} /></Route>)
    expect(screen.getAllByRole('link', { name: /Chat/ }).length).toBeGreaterThan(0)
  })

  it('asks a guest to sign in on Saved instead of loading bookmarks', () => {
    state.me = guest
    renderAt('/saved', <Route path="/saved" element={<Saved />} />)
    expect(screen.getByText(/Saving is for signed-in students/)).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Sign in with uni ID' }).getAttribute('href')).toBe('/login')
    expect(api.get).not.toHaveBeenCalled()
  })

  it('tells a guest to sign in when they press Save, without calling the server', () => {
    state.me = guest
    renderAt('/', <Route path="/" element={<ResultCard card={question} index={0} all={[question]} />} />)
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(toast).toHaveBeenCalledWith('Sign in with your uni ID to save pages and questions.')
    expect(api.post).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Save' }).getAttribute('aria-pressed')).toBe('false')
  })

  it('tells a guest to sign in when they press Report a problem', () => {
    state.me = guest
    renderAt('/', <Route path="/" element={<ReportButton target={{ ref: 'p:page-id', fileTitle: 'DS Midterm', pageNumber: 2 }} />} />)
    fireEvent.click(screen.getByRole('button', { name: 'Report a problem' }))
    expect(toast).toHaveBeenCalledWith('Sign in with your uni ID to report a problem.')
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})
