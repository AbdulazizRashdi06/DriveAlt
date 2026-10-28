# DriveAlt

The Year 2 batch drive (CS / CYS / AI, GUtech) as a web app. Students ask a bot for slides, past papers, quizzes, exercises and labs, and get back **screenshots of the real pages**: never typed-out content, and never typed-out answers (answers come as screenshots from the file).

- **Chat:** ask in plain words ("past paper questions on queue rear counting in DS", "Quiz 3 from last year", "where did we cover AVL trees?").
  - The bot figures out course, type and year itself. It only asks when something is truly ambiguous.
  - Results are question crops or full slides, each with its source (course, file, year, page, question number) and tags. A question crop shows the question as it is on the paper, including any answer written there.
  - Ask it for an answer, hint or check and it won't type anything: it blames Abdulaziz and shows the question crop or page from the file instead.
- **Viewer:** zoom, flip through results with ← / →, open the full page or the original file at that page, save.
- **Browse:** the Drive's Year 2 tree:
  - Semester 1 is grouped into AI / ALL / CS & CYS, then course → type → folders → files → pages.
  - It also has keyword search and tag filters.
- **Saved:** bookmarked crops grouped by course, downloadable one by one or as a ZIP.
- **Report a problem:** students can flag a clipped crop, wrong course/label, missing or unreadable page, or irrelevant result from the viewer or original page, with an optional note. Uploaders review open/resolved reports in Account → People & reports and jump straight to Inspect to fix them. Reports stay separate from searchable material and are never sent to AI.
- **Upload** (uploaders):
  - PDF / PPTX / PPT / DOCX / photos, several at once, with an option to combine photos of one paper.
  - The course and type are pre-filled (from the file name, then an AI look at page 1). Uploaders confirm them, then a live 5-step pipeline runs.
- **Inspect** (uploaders): the original page next to what the AI understood (summary, text, tags, labelled regions). Uploaders can:
  - redraw, add or delete regions and relabel them;
  - edit text and tags.

  There is no approval step: everything is searchable once processed.

Design: the "Candy pop" handoff (Lilita One + Nunito, candy buttons, mascot, Emil Kowalski motion rules), on both desktop and phone.

## Answers: screenshots yes, typing no

**The rule: answers can be shown, never written.**

1. The page-reading model boxes each question on a page. The box covers everything that belongs to the question on the paper: stem, sub-parts, figures, code, **and any answer, solution or handwriting with it**. Each box is padded by 1.5% of the page on every side (`MaterialAi.BoxPadding`), so a tight box never clips anything. Pages themselves are shown exactly as they are.
2. Crops and pages are never edited: nothing is blacked out, blurred or hidden.
3. When a student asks the bot for an answer, a hint or a check, the bot replies with a rotating "blame Abdulaziz" line and shows the question crop or page, answers included as they appear on the paper. It never types the answer, explains it or checks work.
4. No AI writes answers anywhere. The page `text` and each question's text leave answers out, so answer text is never indexed, embedded or sent to the chat model.
5. The bot picks results by reference, and the server renders them from the database. It only accepts references its own searches (or earlier messages) returned.
6. The bot's one-line comment is filtered on the server:
   - It is capped at 2 sentences.
   - It is rejected if it contains answer-like phrases or quotes 5+ words from any page involved, answers included.
7. Refusal lines are written by the server, not the model (`Chat/RefusalLines`).

## Stack

| | |
|---|---|
| API | ASP.NET Core 10, EF Core + Npgsql, background job workers (Postgres `SKIP LOCKED` queue) |
| DB | PostgreSQL 17 + pgvector: full-text search + vector embeddings, merged with reciprocal-rank fusion |
| Files | Originals on disk (`/data`, never modified), LibreOffice (Office → PDF), poppler (PDF → page images, text layer), libheif (HEIC), SkiaSharp (crops, thumbnails) |
| AI | Page reading: Codex `gpt-6.1-sol` through the Zarqa gateway (`Codex:BaseUrl`), else DeepSeek, else Gemini. Chat bot: DeepSeek (`deepseek-chat`), else Gemini. Search: `gemini-embedding-001` (768-d). |
| Email | Resend: `{uniId}@student.gutech.edu.om` gets a 6-digit code |
| Web | React 19 + Vite + TanStack Query + React Router, pdf.js, react-zoom-pan-pinch; served by the API from `wwwroot` |

**Without a Gemini key the app still runs in offline mode:**
- text comes from the PDF text layer;
- each assessment page counts as one question region;
- "give me the answer" gets the refusal line and the whole page;
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

### With GitHub Actions (recommended)

`.github/workflows/deploy.yml` runs the tests on every push and pull request. On a push to `main` (or **Run workflow** in the Actions tab) it:
1. builds the Docker image;
2. copies the image to the VPS over SSH;
3. writes the server's `.env` from GitHub Secrets;
4. runs `docker compose up`;
5. waits for `/api/health`.

No keys are stored in the repo or typed on the server.

Add these at **Settings → Secrets and variables → Actions** (https://github.com/AbdulazizRashdi06/DriveAlt/settings/secrets/actions):

| Secret | What |
|---|---|
| `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY` | SSH access to the VPS (private key; the user must be able to run `docker`). `VPS_PORT` if not 22 |
| `RESEND_API_KEY` | Sends the sign-in codes |
| `GEMINI_API_KEY` | Page reading + bot |
| `GOOGLE_API_KEY` | Optional, for the Drive import |

Optional **Variables** (same page, Variables tab):
- `UPLOADERS`: comma-separated uni IDs
- `PUBLIC_URL`
- `APP_PORT` (default 8080)
- `DEPLOY_DIR` (default `~/drivealt`)
- `GEMINI_MODEL`

The database password and the sign-in secret are not secrets you set: the first deploy generates them on the VPS (`generated.env`) and keeps them. The VPS needs Docker with the compose plugin.

### By hand

```bash
cd deploy
cp .env.example .env        # set Resend__ApiKey, Gemini__ApiKey, uploaders
[ -f generated.env ] || (umask 077; printf 'POSTGRES_PASSWORD=%s\nAuth__OtpSecret=%s\n' "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" > generated.env)  # once: Postgres keeps its first password
docker compose --env-file generated.env --env-file .env up -d --build
```

The app listens on `127.0.0.1:8080` and joins the Zarqa stack's `zarqa_default` Docker network as `drivealt`. On the Zarqa VPS, add a block to `/opt/zarqa/deploy/Caddyfile`:

```caddy
drivealt.tryzarqa.com {
	encode zstd gzip
	reverse_proxy drivealt:8080 {
		flush_interval -1    # chat replies stream (Server-Sent Events)
	}
}
```

Behind nginx on the host instead:

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
cd web && node e2e/reports.mjs   # report flow against Vite on :5177 with a stand-in API; desktop/phone (no DB or AI calls)
```

## Layout

```
src/DriveAlt.Api/   Auth, Files, Processing (pipeline, renderer), Ai (Gemini), Search, Chat (bot + guardrails),
                    Browse, Inspect, Bookmarks, Admin, Import (Drive), Data (EF, migrations, course seed)
tests/              xUnit tests
web/                React app (screens/, ui/, lib/, styles/app.css design tokens)
deploy/             Dockerfile, docker-compose.yml, .env.example
```
