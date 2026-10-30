import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import type { ResultCard as Card } from '../lib/api'
import { ResultCard } from './ResultCard'

const { openViewer, toast } = vi.hoisted(() => ({ openViewer: vi.fn(), toast: vi.fn() }))
vi.mock('../lib/app', () => ({ useApp: () => ({ openViewer, toast }) }))
vi.mock('../lib/saved', () => ({ useSaved: () => [false, vi.fn()] }))
afterEach(() => { cleanup(); openViewer.mockClear(); toast.mockClear() })

const file: Card = {
  ref: 'f:file-id', kind: 'file', fileId: 'file-id', pageId: 'page-id', regionId: null,
  fileTitle: 'DS Midterm', courseName: 'Data Structures', materialType: 'past_paper',
  imageUrl: '/api/media/pages/page-id', thumbUrl: '/api/media/thumbs/page-id',
  pageNumber: 1, pageCount: 7, year: 2024, tags: [], saved: false,
  pageImageUrl: '/api/media/pages/page-id', fileName: 'midterm.pdf', sourceKind: 'pdf',
  courseSlug: 'data-structures', courseColor: '#2F6BFF', semester: 1, assessmentLabel: null,
  questionNumber: null, box: null, width: 600, height: 800, answerRef: null, answerImageUrl: null,
}

describe('Result cards respect the requested material', () => {
  it('opens and downloads the whole file rather than a question/page viewer', () => {
    render(<MemoryRouter><ResultCard card={file} index={0} all={[file]} /></MemoryRouter>)
    expect(screen.getByText('Full file')).toBeTruthy()
    expect(screen.getByText('7 pages')).toBeTruthy()
    expect(screen.getByRole('link', { name: /^Open file$/ }).getAttribute('href')).toBe('/files/file-id?page=1')
    expect(screen.getByRole('link', { name: 'Download full file DS Midterm' }).getAttribute('href')).toBe('/api/files/file-id/download')
    expect(screen.queryByRole('button', { name: 'Open full page' })).toBeNull()
    fireEvent.click(screen.getByRole('link', { name: 'Open full file DS Midterm' }))
    expect(openViewer).not.toHaveBeenCalled()
  })

  it('opens a complete slide deck at its best matching slide', () => {
    const deck = { ...file, materialType: 'slides', fileTitle: 'Queues lecture', pageNumber: 5 } as Card
    render(<MemoryRouter><ResultCard card={deck} index={0} all={[deck]} /></MemoryRouter>)
    expect(screen.getByRole('link', { name: /^Open file$/ }).getAttribute('href')).toBe('/files/file-id?page=5')
    expect(screen.getByRole('link', { name: 'Open full file Queues lecture' }).getAttribute('href')).toBe('/files/file-id?page=5')
    expect(screen.getByRole('img').getAttribute('alt')).toBe('Page 5 preview of Queues lecture')
    expect(screen.getByRole('link', { name: 'Download full file Queues lecture' }).getAttribute('href')).toBe('/api/files/file-id/download')
  })

  it('keeps question results in the crop/page viewer, excluding any file cards', () => {
    const question = { ...file, ref: 'r:region-id', kind: 'question', regionId: 'region-id', questionNumber: '2', imageUrl: '/api/media/crops/region-id' } as Card
    render(<MemoryRouter><ResultCard card={question} index={1} all={[file, question]} /></MemoryRouter>)
    expect(screen.getByText('Q2')).toBeTruthy()
    expect((screen.getByRole('img') as HTMLImageElement).getAttribute('src')).toBe('/api/media/crops/region-id')
    fireEvent.click(screen.getByRole('button', { name: 'Open full page' }))
    expect(openViewer).toHaveBeenCalledWith([question], 0)
  })

  it('uses lightweight thumbnails for slide/page cards and copies shareable deep links', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, { clipboard: { writeText } })
    const slide = { ...file, ref: 'p:page-id', kind: 'slide', pageNumber: 4 } as Card
    render(<MemoryRouter><ResultCard card={slide} index={0} all={[slide]} /></MemoryRouter>)
    expect((screen.getByRole('img') as HTMLImageElement).getAttribute('src')).toBe('/api/media/thumbs/page-id')
    fireEvent.click(screen.getByRole('button', { name: 'Copy link to Slide 4' }))
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/browse/data-structures?ref=p%3Apage-id`))
    expect(toast).toHaveBeenCalledWith('Link copied! Share it with your batch.')
  })
})
