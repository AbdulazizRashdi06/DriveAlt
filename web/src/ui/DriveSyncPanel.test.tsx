import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
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
  { id: 'sem1', name: 'semester 1', segments: ['semester 1'], enabled: false, available: true }, ...state.folders]
const get = (folders = tree, files: Record<string, unknown[]> = {}) => vi.mocked(api.get).mockImplementation(async (url: string) =>
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
  it('starts paused and changes only the chosen folder', async () => {
    get()
    vi.mocked(api.put).mockResolvedValue({})
    mount()
    expect(await screen.findByRole('switch', { name: 'Sync semester 1' })).toBeTruthy()
    expect(screen.queryByRole('switch', { name: 'Sync semester 1 / Data Structures' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /semester 1/ }))
    const ds = await screen.findByRole('switch', { name: 'Sync semester 1 / Data Structures' })
    expect((screen.getByRole('switch', { name: 'Automatic Drive sync' }) as HTMLInputElement).checked).toBe(false)
    fireEvent.click(ds)
    await waitFor(() => expect(api.put).toHaveBeenCalledWith('/api/admin/drive-sync/folders/ds', { enabled: true }))
    fireEvent.click(screen.getByRole('button', { name: 'Year 2' }))
    expect((await screen.findByRole('switch', { name: 'Sync semester 2' }) as HTMLInputElement).checked).toBe(false)
  })
  it('walks Year 2 → semester → course and marks each file uploaded or not', async () => {
    get(tree, { ds: [
      { id: 'a', name: 'Quiz 1.pdf', importable: true, fileId: 'f-1' },
      { id: 'b', name: 'Quiz 2.pdf', importable: true, fileId: null },
      { id: 'c', name: 'lecture.mp4', importable: false, fileId: null },
    ] })
    mount()
    fireEvent.click(await screen.findByRole('button', { name: /semester 1/ }))
    fireEvent.click(await screen.findByRole('button', { name: /Data Structures/ }))
    expect(await screen.findByText('1 of 3 uploaded')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Uploaded' }).getAttribute('href')).toBe('/files/f-1')
    expect(screen.getByText('Not uploaded')).toBeTruthy()
    expect(screen.getByText('Skipped type')).toBeTruthy()
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
