import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { DriveSyncPanel } from './DriveSyncPanel'
import { MemoryRouter } from 'react-router'
import { api } from '../lib/api'
vi.mock('../lib/app', () => ({ useApp: () => ({ toast: vi.fn() }) }))
vi.mock('../lib/api', () => ({ api: { get: vi.fn(), put: vi.fn(), post: vi.fn() } }))
afterEach(() => { cleanup(); vi.clearAllMocks() })
const state = { enabled: false, scanRequested: false, startedAt: null, finishedAt: null, error: null, imported: 0,
  folders: [{ id: 'ds', name: 'Data Structures', segments: ['semester 1', 'Data Structures'], enabled: false, available: true },
    { id: 'sem2', name: 'semester 2', segments: ['semester 2'], enabled: false, available: true }] }
const tree = [{ id: 'root', name: 'Year 2 Drive', segments: [], enabled: false, available: true },
  { id: 'sem1', parentId: 'root', name: 'semester 1', segments: ['semester 1'], enabled: false, available: true },
  { ...state.folders[0], parentId: 'sem1' }, { ...state.folders[1], parentId: 'root' }]
const get = (folders: unknown[] = tree, files: Record<string, unknown[]> = {}) => vi.mocked(api.get).mockImplementation(async (url: string) =>
  url.includes('/files') ? files[decodeURIComponent(url.split('/folders/')[1].split('/files')[0])] ?? [] : { ...state, folders })
function mount() { return render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><DriveSyncPanel /></QueryClientProvider></MemoryRouter>) }
describe('Drive folder controls', () => {
  it('shows progress from this scan rather than a permanent empty completion message', async () => {
    vi.mocked(api.get).mockResolvedValue({ ...state, startedAt: new Date().toISOString(), folders: [
      { ...state.folders[0], checkedAt: new Date(Date.now() + 1000).toISOString() },
      { ...state.folders[1], checkedAt: '2026-01-01T00:00:00Z' },
    ] })
    mount()
    expect(await screen.findByText('1 of 2 known folders checked · 0 file groups imported')).toBeTruthy()
    const progress = screen.getByRole('progressbar', { name: 'Drive folder scan progress' }) as HTMLProgressElement
    expect(progress.value).toBe(1)
    expect(progress.max).toBe(2)
    expect(screen.queryByText('No completed scan yet')).toBeNull()
  })
  it('shows an indeterminate bar while the first scan is queued', async () => {
    vi.mocked(api.get).mockResolvedValue({ ...state, scanRequested: true })
    mount()
    expect(await screen.findByText('Scan queued…')).toBeTruthy()
    expect(screen.getByRole('progressbar').hasAttribute('value')).toBe(false)
  })
  it('starts paused and sets one folder to off, this folder or with subfolders', async () => {
    get()
    vi.mocked(api.put).mockResolvedValue({})
    mount()
    const sem1 = await screen.findByRole('group', { name: 'Sync semester 1' })
    expect(within(sem1).getByRole('button', { name: 'Off' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.queryByRole('group', { name: 'Sync semester 1 / Data Structures' })).toBeNull()
    expect((screen.getByRole('switch', { name: 'Automatic Drive sync' }) as HTMLInputElement).checked).toBe(false)
    fireEvent.click(within(sem1).getByRole('button', { name: '+ Subfolders' }))
    await waitFor(() => expect(api.put).toHaveBeenCalledWith('/api/admin/drive-sync/folders/sem1', { mode: 'all' }))
    fireEvent.click(screen.getByRole('button', { name: /semester 1/ }))
    const ds = await screen.findByRole('group', { name: 'Sync semester 1 / Data Structures' })
    fireEvent.click(within(ds).getByRole('button', { name: 'This folder' }))
    await waitFor(() => expect(api.put).toHaveBeenCalledWith('/api/admin/drive-sync/folders/ds', { mode: 'only' }))
    expect(api.put).toHaveBeenCalledTimes(2)
  })
  it('colours folders by what is missing below them and shows when a parent covers them', async () => {
    const status = (state: string, extra = {}) => ({ state, files: 4, missing: 0, working: 0, failed: 0, ...extra })
    get([tree[0], { ...tree[1], enabled: true, recursive: true, status: status('missing', { missing: 2 }) },
      { ...tree[2], status: status('working', { working: 1, failed: 1 }) }, { ...tree[3], status: status('done') }])
    mount()
    expect(await screen.findByText('2 not uploaded')).toBeTruthy()
    expect(screen.getByText('All 4 uploaded')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /semester 1/ }))
    expect(await screen.findByText('1 processing, 1 failed')).toBeTruthy()
    expect(screen.getByText('On through semester 1')).toBeTruthy()
  })
  it('walks Year 2 → semester → course and marks each file uploaded or not', async () => {
    get(tree, { ds: [
      { id: 'a', name: 'Quiz 1.pdf', importable: true, fileId: 'f-1', fileStatus: 'ready' },
      { id: 'd', name: 'Quiz 3.pdf', importable: true, fileId: 'f-3', fileStatus: 'reading' },
      { id: 'b', name: 'Quiz 2.pdf', importable: true, fileId: null },
      { id: 'c', name: 'lecture.mp4', importable: false, fileId: null },
    ] })
    mount()
    fireEvent.click(await screen.findByRole('button', { name: /semester 1/ }))
    fireEvent.click(await screen.findByRole('button', { name: /Data Structures/ }))
    expect(await screen.findByText('2 of 4 uploaded')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Processing' }).getAttribute('href')).toBe('/files/f-3')
    expect(screen.getByRole('link', { name: 'Uploaded' }).getAttribute('href')).toBe('/files/f-1')
    expect(screen.getByText('Not uploaded', { selector: 'span' })).toBeTruthy()
    expect(screen.getByText('Skipped type')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Not uploaded' }))
    expect(screen.queryByText('Quiz 1.pdf')).toBeNull()
    expect(screen.queryByText('lecture.mp4')).toBeNull()
    expect(screen.getByText('Quiz 2.pdf')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'All' }))
    expect(screen.getByText('Quiz 1.pdf')).toBeTruthy()
    const crumbs = screen.getByRole('navigation', { name: 'Drive folder path' })
    expect(crumbs.textContent).toBe('Year 2 › semester 1 › Data Structures')
  })
  it('allows folder discovery while paused without enabling imports', async () => {
    vi.mocked(api.get).mockResolvedValue({ ...state, folders: [] })
    vi.mocked(api.post).mockResolvedValue({})
    mount()
    const discover = await screen.findByRole('button', { name: 'Discover folders' })
    await waitFor(() => expect((discover as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(discover)
    await waitFor(() => expect(api.post).toHaveBeenCalledWith('/api/admin/drive-sync/scan', {}))
    expect(api.put).not.toHaveBeenCalled()
  })
})
