import { StrictMode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Link, MemoryRouter, Route, Routes } from 'react-router'
import { api, type Course, type FileDto, type Me, type PageThumb, type ResultCard } from '../lib/api'
import { AppProvider, useApp } from '../lib/app'
import { FileView } from './FileView'
import { Upload } from './Upload'

vi.mock('react-zoom-pan-pinch', () => ({
  TransformWrapper: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  TransformComponent: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}))

const sampleFile: FileDto = {
  id: 'file-1',
  title: 'Numerical Computation Midterm',
  originalName: 'MIDTERM Numerical computation SS2024.pdf',
  sourceKind: 'pdf',
  sizeBytes: 152000,
  pageCount: 2,
  status: 'ready',
  step: 5,
  progress: 2,
  error: null,
  courseId: 5,
  courseSlug: 'numerical-computation',
  courseName: 'Numerical Computation',
  courseColor: '#2F6BFF',
  materialType: 'past_paper',
  year: 2024,
  assessmentLabel: 'Midterm',
  folder: null,
  guessed: [],
  createdAt: '2026-10-28T00:00:00Z',
  thumbUrl: '/api/media/files/file-1/thumb?v=1',
  parts: 1,
}

const samplePages: PageThumb[] = [
  {
    id: 'page-1',
    number: 1,
    kind: 'assessment',
    thumbUrl: '/api/media/thumbs/page-1?v=abc',
    pageUrl: '/api/media/pages/page-1?v=abc',
    tags: ['cramers rule', 'matrices'],
    questions: 1,
    saved: true,
    width: 1000,
    height: 1414,
    regions: [{ id: 'reg-1', questionNumber: '1', x: 0.1, y: 0.2, w: 0.8, h: 0.3, saved: true }],
  },
  {
    id: 'page-2',
    number: 2,
    kind: 'assessment',
    thumbUrl: '/api/media/thumbs/page-2?v=abc',
    pageUrl: '/api/media/pages/page-2?v=abc',
    tags: ['gaussian elimination'],
    questions: 0,
    width: 1000,
    height: 1414,
    regions: [],
  },
]

const sampleCard: ResultCard = {
  ref: 'r:reg-1',
  kind: 'question',
  imageUrl: '/api/media/crops/reg-1?v=abc',
  pageImageUrl: '/api/media/pages/page-1?v=abc',
  thumbUrl: '/api/media/thumbs/page-1?v=abc',
  fileId: 'file-1',
  pageId: 'page-1',
  regionId: 'reg-1',
  fileTitle: 'Numerical Computation Midterm',
  fileName: 'MIDTERM Numerical computation SS2024.pdf',
  sourceKind: 'pdf',
  courseSlug: 'numerical-computation',
  courseName: 'Numerical Computation',
  courseColor: '#2F6BFF',
  semester: 2,
  materialType: 'past_paper',
  year: 2024,
  assessmentLabel: 'Midterm',
  pageNumber: 1,
  pageCount: 2,
  questionNumber: '1',
  tags: ['cramers rule'],
  box: { x: 0.1, y: 0.2, w: 0.8, h: 0.3 },
  width: 1000,
  height: 1414,
  saved: false,
  answerRef: null,
  answerImageUrl: null,
}

const studentMe: Me = {
  id: 'u-1',
  uniId: '24-0161',
  email: '24-0161@student.gutech.edu.om',
  role: 'uploader',
  initials: '24',
}

beforeEach(() => {
  Element.prototype.scrollIntoView = vi.fn()
  Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } })
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('Features #5, #6, and #7', () => {
  it('lets uploaders inspect a flagged file and retry without discarding completed pages', async () => {
    vi.spyOn(api, 'get').mockImplementation(async (path: string) => {
      if (path === '/api/auth/me') return studentMe
      if (path === '/api/config') return { name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: true }
      if (path.startsWith('/api/uploads')) return [{ ...sampleFile, status: 'failed', error: 'Terra review required on page 1' }]
      return []
    })
    const post = vi.spyOn(api, 'post').mockResolvedValue({})
    render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AppProvider><Upload /></AppProvider></QueryClientProvider></MemoryRouter>)
    const retry = await screen.findByRole('button', { name: 'Retry' })
    expect(screen.getByRole('link', { name: 'Inspect' }).getAttribute('href')).toBe('/inspect/file-1')
    fireEvent.click(retry)
    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/files/file-1/process'))
    expect(post).not.toHaveBeenCalledWith('/api/files/file-1/reprocess')
  })
  it('renders pre-rendered page images, topic filters, and interactive question boxes in FileView (#5)', async () => {
    vi.spyOn(api, 'get').mockImplementation(async (path: string) => {
      if (path === '/api/auth/me') return studentMe
      if (path === '/api/config') return { name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: true }
      if (path === '/api/files/file-1') return { file: sampleFile, pages: samplePages }
      if (path === '/api/files/file-2') return { file: { ...sampleFile, id: 'file-2', title: 'Second file' }, pages: samplePages.map(p => ({ ...p, tags: [] })) }
      return []
    })
    const deleteSpy = vi.spyOn(api, 'del').mockResolvedValue({ saved: false })

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <MemoryRouter initialEntries={['/files/file-1']}>
        <QueryClientProvider client={qc}>
          <AppProvider>
            <Link to="/files/file-2">Next file</Link>
            <Routes>
              <Route path="/files/:id" element={<FileView />} />
            </Routes>
          </AppProvider>
        </QueryClientProvider>
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByAltText('Page 1 of Numerical Computation Midterm')).toBeTruthy())
    expect(screen.getByAltText('Page 2 of Numerical Computation Midterm')).toBeTruthy()

    // Question overlay button opens the Viewer modal on click
    const qBtn = screen.getByRole('button', { name: 'Open Q1 on page 1' })
    fireEvent.click(qBtn)
    await waitFor(() => expect(screen.getByRole('dialog', { name: /Viewing Numerical Computation Midterm page 1/i })).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: 'Saved' }))
    await waitFor(() => expect(deleteSpy).toHaveBeenCalledWith('/api/bookmarks/r%3Areg-1'))
    fireEvent.click(screen.getByRole('button', { name: 'Close viewer' }))
    fireEvent.click(screen.getByRole('button', { name: 'Zoom page 1' }))
    expect(screen.getByRole('button', { name: 'Saved' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Close viewer' }))

    // Topic chip filters the displayed pages
    fireEvent.click(screen.getByRole('button', { name: 'gaussian elimination' }))
    expect(screen.queryByAltText('Page 1 of Numerical Computation Midterm')).toBeNull()
    expect(screen.getByAltText('Page 2 of Numerical Computation Midterm')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Hide questions (1)' }))
    fireEvent.click(screen.getByRole('link', { name: 'Next file' }))
    await waitFor(() => expect(screen.getByAltText('Page 1 of Second file')).toBeTruthy())
    expect(screen.getByAltText('Page 2 of Second file')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Hide questions (1)' })).toBeTruthy()
  })

  it('hydrates and opens shared ?ref= deep links under StrictMode and after login refresh (#6)', async () => {
    let signedIn = false
    const getSpy = vi.spyOn(api, 'get').mockImplementation(async (path: string) => {
      if (path === '/api/auth/me') return signedIn ? studentMe : null
      if (path === '/api/config') return { name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: true }
      if (path.startsWith('/api/cards?refs=')) return [sampleCard]
      return []
    })

    function LoginTrigger() {
      const { refreshMe } = useApp()
      return <button type="button" onClick={() => { signedIn = true; void refreshMe() }}>Sign in now</button>
    }

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <StrictMode>
        <MemoryRouter initialEntries={['/browse/numerical-computation?ref=r%3Areg-1']}>
          <QueryClientProvider client={qc}>
            <AppProvider>
              <LoginTrigger />
            </AppProvider>
          </QueryClientProvider>
        </MemoryRouter>
      </StrictMode>,
    )

    await waitFor(() => expect(getSpy).toHaveBeenCalledWith('/api/auth/me'))
    expect(screen.queryByRole('dialog')).toBeNull()

    // Simulate completing login/session refresh; ?ref= should now hydrate and open Viewer even under StrictMode
    fireEvent.click(screen.getByRole('button', { name: 'Sign in now' }))
    await waitFor(() => expect(screen.getByRole('dialog', { name: /Viewing Numerical Computation Midterm page 1/i })).toBeTruthy())
    expect(getSpy).toHaveBeenCalledWith('/api/cards?refs=r%3Areg-1')

    // Copy link inside Viewer
    fireEvent.click(screen.getByRole('button', { name: 'Copy link' }))
    await waitFor(() => expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
      `${window.location.origin}/browse/numerical-computation?ref=r%3Areg-1`,
    ))
  })

  it('skips exact SHA-256 duplicates and warns on fuzzy title matches with Upload anyway in Upload (#7)', async () => {
    vi.spyOn(api, 'get').mockImplementation(async (path: string) => {
      if (path === '/api/auth/me') return studentMe
      if (path === '/api/config') return { name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: true }
      if (path === '/api/courses') return [{ id: 5, slug: 'numerical-computation', name: 'Numerical Computation', semester: 2, group: '', color: '#2F6BFF', aliases: [], files: 1 } satisfies Course]
      if (path === '/api/uploads') return []
      return []
    })

    const postSpy = vi.spyOn(api, 'post').mockResolvedValue([
      { index: 0, kind: 'exact', existing: sampleFile },
      { index: 1, kind: 'similar', existing: sampleFile },
    ])

    let xhrSent = 0
    const OrigXHR = globalThis.XMLHttpRequest
    class MockXHR {
      status = 200
      responseText = JSON.stringify([{ duplicate: false, file: sampleFile }])
      upload = { onprogress: null as ((e: ProgressEvent) => void) | null }
      onload: (() => void) | null = null
      onerror: (() => void) | null = null
      open() {}
      send() {
        xhrSent++
        this.onload?.()
      }
    }
    globalThis.XMLHttpRequest = MockXHR as unknown as typeof XMLHttpRequest

    try {
      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      const { container } = render(
        <MemoryRouter initialEntries={['/upload']}>
          <QueryClientProvider client={qc}>
            <AppProvider>
              <Upload />
            </AppProvider>
          </QueryClientProvider>
        </MemoryRouter>,
      )

      const fileInput = container.querySelector('input[type="file"][multiple]') as HTMLInputElement
      const exactFile = new File(['exact bytes'], 'MIDTERM Numerical computation SS2024.pdf', { type: 'application/pdf' })
      const similarFile = new File(['modified bytes'], 'MIDTERM Numerical computation SS2024 (1).pdf', { type: 'application/pdf' })

      fireEvent.change(fileInput, { target: { files: [exactFile, similarFile] } })

      await waitFor(() => expect(postSpy).toHaveBeenCalledWith('/api/uploads/check', expect.anything()))
      // Neither file should have been uploaded yet!
      expect(xhrSent).toBe(0)

      // Both exact and similar warning cards are visible
      expect(screen.getByRole('region', { name: 'Already in drive: MIDTERM Numerical computation SS2024.pdf' })).toBeTruthy()
      expect(screen.getByRole('region', { name: 'Possible duplicate: MIDTERM Numerical computation SS2024 (1).pdf' })).toBeTruthy()

      // Clicking "Upload anyway" on the similar match uploads that file
      fireEvent.click(screen.getByRole('button', { name: 'Upload anyway' }))
      await waitFor(() => expect(xhrSent).toBe(1))
    } finally {
      globalThis.XMLHttpRequest = OrigXHR
    }
  })
})
