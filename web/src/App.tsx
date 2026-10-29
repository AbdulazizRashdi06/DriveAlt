import { QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ApiError } from './lib/api'
import { createBrowserRouter, Navigate, RouterProvider, Outlet } from 'react-router'
import { AppProvider } from './lib/app'
import { Layout, RequireUploader } from './ui/Layout'
import { Login } from './screens/Login'
import { Chat } from './screens/Chat'
import { Browse } from './screens/Browse'
import { Saved } from './screens/Saved'
import { Upload } from './screens/Upload'
import { Inspect } from './screens/Inspect'
import { Admin } from './screens/Admin'
import { FileView } from './screens/FileView'

function RouteError() {
  return <main className="page-main"><h1 className="disp">This page couldn’t load</h1>
    <p>Reload the app to try again. Your saved files and chats will stay available.</p>
    <button className="candy" type="button" onClick={() => window.location.reload()}>Reload app</button></main>
}

const queryClient: QueryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
  // An expired session anywhere sends the user back to sign-in.
  queryCache: new QueryCache({
    onError: e => { if (e instanceof ApiError && e.status === 401) queryClient.setQueryData(['me'], null) },
  }),
})

function Root() {
  return (
    <AppProvider>
      <Outlet />
    </AppProvider>
  )
}

const router = createBrowserRouter([
  {
    element: <Root />,
    errorElement: <RouteError />,
    children: [
      { path: '/login', element: <Login /> },
      {
        element: <Layout />,
        children: [
          { index: true, element: <Navigate to="/chat" replace /> },
          { path: '/chat', element: <Chat /> },
          { path: '/chat/:id', element: <Chat /> },
          { path: '/browse', element: <Browse /> },
          { path: '/browse/:slug', element: <Browse /> },
          { path: '/files/:id', element: <FileView /> },
          { path: '/saved', element: <Saved /> },
          { path: '/upload', element: <RequireUploader><Upload /></RequireUploader> },
          { path: '/inspect', element: <RequireUploader><Inspect /></RequireUploader> },
          { path: '/inspect/:id', element: <RequireUploader><Inspect /></RequireUploader> },
          { path: '/admin', element: <RequireUploader><Admin /></RequireUploader> },
          { path: '*', element: <Navigate to="/chat" replace /> },
        ],
      },
    ],
  },
])

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}
