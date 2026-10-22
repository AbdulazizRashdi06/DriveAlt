# DriveAlt

The Year 2 batch drive (CS / CYS / AI, GUtech) as a web app. Students ask a bot for slides, past papers, quizzes, exercises and labs, and get back **screenshots of the real pages**: never typed-out content, never answers.

- **Chat:** ask in plain words ("past paper questions on queue rear counting in DS", "Quiz 3 from last year", "where did we cover AVL trees?").
  - The bot figures out course, type and year itself. It only asks when something is truly ambiguous.
  - Results are question crops or full slides, each with its source (course, file, year, page, question number) and tags.
  - Ask it for an answer, hint or check and it refuses with a rotating "blame Abdulaziz" line.
- **Viewer:** zoom, flip through results with ← / →, open the full page or the original file at that page, save.
- **Browse:** the Drive's Year 2 tree:
  - Semester 1 is grouped into AI / ALL / CS & CYS, then course → type → folders → files → pages.
  - It also has keyword search and tag filters.
- **Saved:** bookmarked crops grouped by course, downloadable one by one or as a ZIP.
- **Upload** (uploaders):
  - PDF / PPTX / PPT / DOCX / photos, several at once, with an option to combine photos of one paper.
  - The course and type are pre-filled (from the file name, then an AI look at page 1). Uploaders confirm them, then a live 5-step pipeline runs.
- **Inspect** (uploaders): the original page next to what the AI understood (summary, text, tags, labelled regions). Uploaders can:
  - redraw, add or delete regions and relabel them;
  - edit text and tags.

  There is no approval step: everything is searchable once processed.

Design: the "Candy pop" handoff (Lilita One + Nunito, candy buttons, mascot, Emil Kowalski motion rules), on both desktop and phone.

## How answers stay hidden

1. Gemini labels every region on a page as **question**, **answer** or **other**.
2. Answer regions are **burned out** (opaque hatching) of the page images students see. Question crops are cut from that masked page.
3. Answer text is stored but **never indexed, embedded or sent to the bot**. The bot's tools only see question/slide snippets.
4. The bot picks results by reference, and the server renders them from the database. It only accepts references its own searches returned.
5. The bot's one-line comment is filtered on the server:
   - It is capped at 2 sentences.
   - It is rejected if it contains answer-like phrases or quotes 5+ words from any page involved.
6. Refusal lines are written by the server, not the model (`Chat/RefusalLines`).
7. **"Open original"** shows the untouched file. The spec allows the file to contain answers; the bot just never says them.

## Stack

| | |
|---|---|
| API | ASP.NET Core 10, EF Core + Npgsql, background job workers (Postgres `SKIP LOCKED` queue) |
| DB | PostgreSQL 17 + pgvector: full-text search + vector embeddings, merged with reciprocal-rank fusion |
| Files | Originals on disk (`/data`, never modified), LibreOffice (Office → PDF), poppler (PDF → page images, text layer), libheif (HEIC), SkiaSharp (crops/masks) |
| AI | Gemini (`Gemini:Model`, default `gemini-3.8-flash`) for page extraction (structured output, `box_2d` regions) and the tool-calling bot; `gemini-embedding-001` (768-d) for search |
| Email | Resend: `{uniId}@student.gutech.edu.om` gets a 6-digit code |
| Web | React 19 + Vite + TanStack Query + React Router, pdf.js, react-zoom-pan-pinch; served by the API from `wwwroot` |

**Without a Gemini key the app still runs in offline mode:**
- text comes from the PDF text layer;
- each assessment page counts as one question region;
- answers are **not** masked;
- search is keyword-only;
- the bot is rule-based.

Useful for development; set the key for real use.

## Run locally

Needs .NET 10 SDK, Node 22, PostgreSQL with pgvector, `libreoffice`, `poppler-utils`, and (for HEIC) `libheif-examples`.

```bash
createdb drivealt && psql -d drivealt -c 'CREATE EXTENSION vector'
cd web && npm ci && npm run build          # builds into src/DriveAlt.Api/wwwroot
cd ../src/DriveAlt.Api && dotnet run       # http://localhost:5257 (migrates + seeds courses)
```

In Development:
- the sign-in code is printed in the API log, and also readable at `GET /api/dev/otp/{uniId}`;
- `24-0161` is an uploader (`appsettings.Development.json`).

For frontend work, run `npm run dev` in `web/` (proxies `/api` to :5257).

## Deploy on the Zarqa VPS

```bash
cd deploy
cp .env.example .env        # set POSTGRES_PASSWORD, Auth__OtpSecret, Resend__ApiKey, Gemini__ApiKey, uploaders
docker compose up -d --build
```

The app listens on `127.0.0.1:8080`. Put it behind the VPS's existing reverse proxy, e.g. nginx:

```nginx
server {
  server_name drive.tryzarqa.com;
  client_max_body_size 1g;
  location / {
    proxy_pass http://127.0.0.1:8080;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_buffering off;          # chat replies stream (Server-Sent Events)
  }
}
```

Production refuses to start without `Auth__OtpSecret`. Originals, page images and crops live in the `files` volume, and the database lives in `pgdata`. Back up both.

## Import the Year 2 Google Drive

The importer walks the public Year 2 folder and maps folder paths to course / type / year / subfolder. For example, `semester 1/ALL/data structures/past papers/midterm/midterm 1` becomes:
- **course:** Data Structures
- **type:** Past paper
- **folder:** `midterm/midterm 1`

Loose photos in one folder become one multi-page file. Videos, ZIPs and GoodNotes files are skipped. It's safe to re-run: files already imported are skipped.

```bash
docker compose exec app dotnet DriveAlt.Api.dll import-drive --dry-run           # preview the mapping
docker compose exec app dotnet DriveAlt.Api.dll import-drive                     # everything (~410 files)
docker compose exec app dotnet DriveAlt.Api.dll import-drive --only "semester 1/ALL/data structures"
```

Without `Google__ApiKey` it reads the public folder view. The running app's workers then process the queue; watch progress in Upload → "Your uploads" (`/api/uploads?all=true`).

## Tests

```bash
dotnet test                      # unit + integration (needs local Postgres with pgvector; DRIVEALT_TEST_PG to override)
cd web && npm test               # Vitest
cd web && node e2e/screens.mjs   # Playwright walkthrough + desktop/phone screenshots against a running dev API
```

## Layout

```
src/DriveAlt.Api/   Auth, Files, Processing (pipeline, renderer), Ai (Gemini), Search, Chat (bot + guardrails),
                    Browse, Inspect, Bookmarks, Admin, Import (Drive), Data (EF, migrations, course seed)
tests/              xUnit tests
web/                React app (screens/, ui/, lib/, styles/app.css design tokens)
deploy/             Dockerfile, docker-compose.yml, .env.example
```
