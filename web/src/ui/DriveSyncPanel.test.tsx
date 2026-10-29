import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { DriveSyncPanel } from './DriveSyncPanel'
import { api } from '../lib/api'
vi.mock('../lib/app', () => ({ useApp: () => ({ toast: vi.fn() }) }))
vi.mock('../lib/api', () => ({ api: { get: vi.fn(), put: vi.fn(), post: vi.fn() } }))
afterEach(() => { cleanup(); vi.clearAllMocks() })
const state = { enabled: false, scanRequested: false, startedAt: null, finishedAt: null, error: null, imported: 0,
  folders: [{ id: 'ds', name: 'Data Structures', segments: ['semester 1', 'Data Structures'], enabled: false, available: true },
    { id: 'sem2', name: 'semester 2', segments: ['semester 2'], enabled: false, available: true }] }
function mount() { return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><DriveSyncPanel /></QueryClientProvider>) }
describe('Drive folder controls', () => {
  it('shows progress from this scan rather than a permanent empty completion message', async () => {
    vi.mocked(api.get).mockResolvedValue({ ...state, startedAt: new Date().toISOString(), folders: [
      { ...state.folders[0], checkedAt: new Date(Date.now() + 1000).toISOString() },
      { ...state.folders[1], checkedAt: '2026-01-01T00:00:00Z' },
    ] })
    mount()
    expect(await screen.findByText('1 folders checked · 2 known · 0 file groups imported')).toBeTruthy()
    expect(screen.queryByText('No completed scan yet')).toBeNull()
  })
  it('starts paused and changes only the chosen folder', async () => {
    vi.mocked(api.get).mockResolvedValue(state)
    vi.mocked(api.put).mockResolvedValue({})
    mount()
    const ds = await screen.findByRole('switch', { name: 'Sync semester 1 / Data Structures' })
    expect((screen.getByRole('switch', { name: 'Automatic Drive sync' }) as HTMLInputElement).checked).toBe(false)
    fireEvent.click(ds)
    await waitFor(() => expect(api.put).toHaveBeenCalledWith('/api/admin/drive-sync/folders/ds', { enabled: true }))
    expect((screen.getByRole('switch', { name: 'Sync semester 2' }) as HTMLInputElement).checked).toBe(false)
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
