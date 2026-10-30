import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { api, type Course, type FileDto, type Me } from '../lib/api'
import { AppProvider } from '../lib/app'
import { Inspect } from './Inspect'
import { Upload } from './Upload'

const me: Me = { id: 'u', uniId: '24-0161', role: 'uploader', email: 'test@example.invalid', initials: 'AA' } as Me
const course = (id: number, name: string, semester: number, group: string): Course =>
  ({ id, slug: name.toLowerCase().replace(/ /g, '-'), name, semester, group, color: '#2F6BFF', aliases: [], files: 0 })
const courses = [course(1, 'Data Structures', 1, 'ALL'), course(2, 'Efficient Algorithms', 2, '')]
const file = (id: string, title: string, courseId: number | null, extra: Partial<FileDto> = {}): FileDto => ({
  id, title, originalName: `${title}.pdf`, sourceKind: 'pdf', sizeBytes: 1000, pageCount: 2, status: 'ready', step: 5, progress: 2,
  error: null, courseId, courseSlug: null, courseName: courses.find(c => c.id === courseId)?.name ?? null, courseColor: null,
  materialType: 'past_paper', year: 2024, assessmentLabel: null, folder: null, guessed: [], createdAt: '2026-10-28T00:00:00Z',
  thumbUrl: null, parts: 1, ...extra,
})

function mount(files: FileDto[], ui: React.ReactNode, at = '/') {
  vi.spyOn(api, 'get').mockImplementation(async (path: string) => {
    if (path === '/api/auth/me') return me
    if (path === '/api/config') return { name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: true }
    if (path === '/api/courses') return courses
    if (path.startsWith('/api/uploads')) return files
    return []
  })
  render(<MemoryRouter initialEntries={[at]}><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <AppProvider><Routes><Route path="*" element={ui} /></Routes></AppProvider></QueryClientProvider></MemoryRouter>)
}

afterEach(() => { cleanup(); vi.restoreAllMocks() })

describe('Inspect folder tree', () => {
  it('walks Year 2 → semester → group → course → type → folder like the Drive', async () => {
    mount([file('a', 'Midterm 1', 1, { folder: 'midterm' }), file('b', 'Final', 2)], <Inspect />, '/inspect')
    fireEvent.click(await screen.findByRole('button', { name: /Semester 1/ }))
    expect(screen.queryByText('Final')).toBeNull()
    fireEvent.click(await screen.findByRole('button', { name: /ALL/ }))
    fireEvent.click(await screen.findByRole('button', { name: /Data Structures/ }))
    fireEvent.click(await screen.findByRole('button', { name: /Past papers/i }))
    fireEvent.click(await screen.findByRole('button', { name: /midterm/ }))
    expect((await screen.findByRole('link', { name: /Midterm 1/ })).getAttribute('href')).toBe('/inspect/a')
    const crumbs = screen.getByRole('navigation', { name: 'Folder path' })
    fireEvent.click(within(crumbs).getByRole('button', { name: 'Year 2' }))
    expect(await screen.findByRole('button', { name: /Semester 2/ })).toBeTruthy()
  })
  it('searches every file wherever it sits', async () => {
    mount([file('a', 'Midterm 1', 1), file('b', 'Final', 2)], <Inspect />, '/inspect')
    await screen.findByRole('button', { name: /Semester 1/ })
    fireEvent.change(screen.getByRole('searchbox', { name: 'Filter files' }), { target: { value: 'final' } })
    expect(await screen.findByRole('link', { name: /Final/ })).toBeTruthy()
    expect(screen.queryByRole('link', { name: /Midterm 1/ })).toBeNull()
  })
})

describe('Upload list', () => {
  it('puts files that are not done above finished ones', async () => {
    mount([file('r1', 'Done first', 1), file('w1', 'Still reading', 1, { status: 'reading', step: 2 }),
      file('r2', 'Done second', 1), file('f1', 'Broken one', 1, { status: 'failed', error: 'x' })], <Upload />)
    await screen.findByText('Done first')
    const order = ['Still reading', 'Broken one', 'Done first', 'Done second'].map(t => screen.getByText(t))
    for (let i = 1; i < order.length; i++)
      expect(order[i - 1].compareDocumentPosition(order[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
