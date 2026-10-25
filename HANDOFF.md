# HANDOFF: DriveAlt

> Every agent: read this first and run the **Resume check** (section 9). Update the page in the same branch as every change. The rules are in [`AGENTS.md`](AGENTS.md).

## 1. Header

| | |
|---|---|
| Project | DriveAlt: the GUtech Year 2 batch drive (CS / CYS / AI) as a web app. Live at https://drivealt.tryzarqa.com |
| Repo | `AbdulazizRashdi06/DriveAlt` (**public**), local `C:\Projects\DriveAlt` |
| Branch / base | `claude/handoff-reread`, from main `d73edeb` |
| Updated | 2026-10-25 ~00:05 UTC, by Claude Code (Opus 5.5) |
| Purpose of this update | Codex: record the question-only re-read and claim the full import and usage-limit monitoring. **Claude Code:** fix the answers rule (answers may be SHOWN in crops, never WRITTEN by any AI), and record the import's real state at 2026-10-24 11:48 UTC (stuck: lost ChatGPT login, Gemini quota). **Then, on the owner's order, take over the import from Codex** and add `import-local`, which fills pages from DriveAlt-local results without the page-reading model, plus a Gemini-quota fallback. |

**In one line:** the app is live (image `5c7641a`). Chat runs on DeepSeek, and Sol reads pages through the Codex gateway, which is **logged in again**. 457 pages are queued to be re-read so their crops include answers and typed exams get per-question boxes. **The ChatGPT plan's usage limit hit again at 2026-10-24 23:36 UTC**, so files are paused until it resets. **Semester 1 without ITS goes first; Semester 2 and ITS start at 2026-10-25 08:00 UTC.**

## 2. State of the world

**Git** (verified after the merges):
```
6852f1d Merge pull request #5 from AbdulazizRashdi06/codex/question-only-crops
7228c22 Merge pull request #6 from AbdulazizRashdi06/docs/agents-handoff
```

**PRs:**

| PR | State | Branch | What |
|---|---|---|---|
| #1 | merged | `claude/elegant-keller-1n8kkd` | First release |
| #2 | merged | `fix/uni-id-dash` | Uni ID dash appears right after the year |
| #3 | merged | `feat/deepseek` | DeepSeek for chat and page reading; ghcr.io deploys |
| #4 | merged, **deployed** | `feat/codex-pages` | Pages read by Codex `gpt-6.1-sol`; pause on usage limits |
| **#5** | **merged and deployed** | `codex/question-only-crops` (by the Codex agent) | Question crops only, no answer crops or "Show answer"; question searches no longer treated as answer requests |
| **#6** | **merged** 15:12:09 UTC, deployed 15:14 | `docs/agents-handoff` (by Claude Code) | Adds `AGENTS.md` and `CLAUDE.md`; this page. Docs only. |
| **#7** | **merged** | `codex/handoff-question-only-deployment` (Codex) | This page after PR #5. Docs only. |
| #8 | **closed** unmerged (superseded by #9) | `docs/handoff-after-6` (Claude Code) | Same facts as #7, written in parallel; conflicted with #7. |
| **#9** | **merged** | `docs/handoff-merge-proof` (Claude Code) | Built on #7 and merged as `5a95318` with `[skip ci]`; live image remains `6852f1d`. |
| #10 | merged `6b7a6fe` with `[skip ci]` (by Claude Code, owner's order) | `codex/drive-import-monitoring` (Codex) | Test-file validation, import launch and monitoring notes. Docs only. |
| #11 | merged `5c7641a`, **deployed 2026-10-24 19:17–19:21 UTC** | `claude/answers-visible-in-crops` (Claude Code) | Crops include answers; 1.5% box padding; `import-local`; keyword-only fallback on Gemini 429 or network error. |
| #12 | merged `d73edeb` with `[skip ci]` | `claude/handoff-local-import` (Claude Code) | This page after the local import. Docs only. |
| **#13** | **open** | `claude/handoff-reread` (Claude Code) | This page after the gateway fix and the re-read. Docs only; merge with `[skip ci]`. |

**Other repos:**
- **Zarqa repo** (`AbdulazizRashdi06/Zarqa`, local `C:\Projects\LostraAi`): branch `codex-model-choice` (`f403756`) is pushed and **deployed to the VPS**, but has **no PR and isn't merged**. The owner's local LostraAi `main` is ahead of origin by 3 commits (Caddy changes for Zoor and Schedule). That's the owner's own work; leave it alone.

**Tests:**
- **PR #4:** CI passed (run `39960420833`, `test pass 1m37s`).
- **Unit tests on `main`:** 28/28 passed locally on `feat/codex-pages`, before PR #5.
- **PR #5:** 39 backend unit tests, 8 frontend tests, TypeScript and frontend build passed locally. Full CI run `39975981256` passed on head `95822c8`: `Passed! - Failed: 0, Passed: 43, Skipped: 0, Total: 43`, including database integration tests. Frontend tests and build passed. Main deployment run: `39976312738`.

**Running on the VPS** (verified by Codex, 2026-10-23 15:19:19 UTC):
```
docker inspect --format '{{.Config.Image}} {{.State.Status}}' drivealt-app-1
→ ghcr.io/abdulazizrashdi06/drivealt:6852f1d012beceb6ffcdc875bb01917b3439f157 running
Codex__BaseUrl and Codex__Model exact-match counts in ~/drivealt/.env → 1, 1
https://drivealt.tryzarqa.com/api/health → {"ok":true}
gateway /health → {"signedIn":true}
gateway.mjs SHA256 → 5da0c724e36f45626f438728030eb357e02c7a45163425c8ad9562bbcc3f607f
GitHub main run 39976312738 → test success, build success, deploy success
```
The codex gateway was not rebuilt again. No DNS, Caddy, secrets or database configuration was changed by PR #5.

**Question-only test-file validation passed.** At 18:55:40 UTC, read-only SQL returned `ready | 7 | 7`, every page `extracted=t, offline=f`, and only question regions. The file updated at 15:21:20 UTC after the PR #5 deployment. Per-page question counts: 7, 5, 2, 6, 1, 5, 3 (29 total); answer/other counts are zero. Pages 2 and 6 each have five questions. Visual inspection of crop placement has not been performed by Codex because browser access was declined.

**Full import started at 2026-10-23T18:58:30Z (22:58:30 Muscat).** Codex owns it on `codex/drive-import-monitoring`, draft PR #10; do not launch it again. Detached `dotnet DriveAlt.Api.dll import-drive` was confirmed running over a second SSH session. Start marker: `~/drivealt/import.started.utc`; launcher PID: 2284190. Output is in `import.log`; the wrapper writes `import.download-finished.utc` and `import.exit-code` when downloading/queuing ends. File/page processing continues in the app afterwards. HTTP client informational logging is suppressed for this importer invocation to keep API-key URLs out of its log. No source, secrets or service configuration was changed.

Pre-import baseline was one ready file, seven AI-read pages, zero offline pages, zero paused jobs. `import-drive --dry-run` found **407 files**, with videos and ZIPs skipped. Its log is `~/drivealt/import-dryrun.log`.

**Monitoring sample, 2026-10-23 19:15:21 UTC:** 343 registered files (330 queued, 7 reading, 5 ready, 1 tagging); 249 pages read successfully, zero offline pages. Download/queuing is still running. Five files paused between 19:07:56 and 19:08:54 UTC because the gateway returned **503 (login check)**, not 429/plan usage limits. A later gateway health check returned signedIn=true, and other pages continue progressing. Leave paused jobs to retry normally. Two importer failures are HTTP/socket connection errors, not HTTP status/quota errors; plan a controlled idempotent retry after the current importer exits, preserving its existing log and exit markers. No actual usage-limit event observed yet. The gateway is CPU-capped at about 100%; **hypothesis:** login-status checks can time out under load. No service/configuration change was made.

**Usage-limit accounting:** observed pause events above are gateway login checks. Do not count them as ChatGPT plan quota delays. Keep the count and duration of true plan-limit intervals separate.

**Import state at 2026-10-24 11:48 UTC** (checked by Claude Code from the VPS; logs `--since 24h`):
- **Importer:** finished queuing at 19:17 UTC with `Done: 404 queued, 1 skipped, 2 failed` (`~/drivealt/import.log`, exit code 1).
- **Files:**

  | Status | Files | Pages |
  |---|---|---|
  | `reading` | 332 | 8,501 |
  | `failed` | 51 | 702 |
  | `ready` | 17 | 426 |

- **Pages:** 1,444 read by the AI and 0 offline; 413 `question` regions.
- **The real plan limit:** **2,614** `AI paused … (429): Codex usage limit reached`, the first at **2026-10-23 23:37:42 UTC**.
- **Login lost:** after that, **9,390** `AI paused … (503): Codex needs ChatGPT login`, the last at 2026-10-24 11:48:53 UTC. Gateway `/health` returns `{"signedIn":false}`, and all **332** queued process jobs show `Paused: Codex unavailable (503)`. **Nothing will progress until the owner logs the gateway in again** (command in section 9, Rollback).
- **Failures:**
  - **49 files: `Gemini API error 429: You exceeded your current quota`**. The free embedding quota ran out. An embedding error fails the whole file, even after Sol has read its pages (`ProcessingService.EmbedAsync`).
  - **1 file:** `Network is unreachable (generativelanguage.googleapis.com:443)`. **Hypothesis:** an IPv6 attempt; the VPS has no IPv6 route.
  - **1 file:** `pdftoppm … Document stream is empty`.
- **Old boxes:** the 1,444 pages read so far used the PR #5 prompt, which **kept answers out of question boxes**. After this branch deploys, those files need Re-run AI to get crops that include answers.

**Import state at 2026-10-24 19:42 UTC, after `import-local`** (Claude Code):
- **The run:** `import-local /data/import-local --only "semester 1/" --skip "semester 1/ai/its"` at 2026-10-24 ~19:23 UTC, exit 0. It filled **3,310 pages from PDF text** and **357 scanned pages from reviewed boxes** (24 question boxes) in 126 files. 109 files were fully read and queued to finish; 17 still have pages for Sol; 1 wasn't split. Log: `~/drivealt/import-local.log`. Data: `/data/import-local` inside the app's `files` volume (23 MB: `manifest.json`, `results/`, `crops/`).
- **Requeued:** 50 files that had failed on `Gemini API error 429` (49) or `Network is unreachable` (1), with all pages already read. Their failed `process` jobs were reset to queued in one SQL transaction.
- **Then:** the queue emptied by 19:42 UTC with **0 job errors**. Files: `ready 176, reading 223, failed 1` (`pdftoppm … Document stream is empty`). **105 files finished without embeddings** (`Embeddings skipped` log lines: the Gemini quota is still used up), so they have keyword search only.
- **Semester 1 per course:**

  | Course | Files ready | Pages read |
  |---|---|---|
  | Data Structures | 44/44 | 1,280/1,280 |
  | Organisation & HR | 10/10 | 123/123 |
  | Representation & Problem Solving | 13/15 | 838/843 |
  | Probability & Statistics | 26/39 | 1,367/1,415 |
  | Database Systems | 44/57 | 498/551 |
  | Accounting & Controlling | 12/25 | 91/337 (mostly photos) |

  ITS: 21/31 ready, from the earlier Sol run. Semester 2: 6/179 ready.
- **Checked visually:** crops made from the reviewed boxes (*Accounting final exam* p3 Q2, *cpp4 solutions* p2 Q2) are complete, with stem, requirements and answer space or solution code.

**Gateway fix and re-read, 2026-10-24 23:00 – 2026-10-25 00:00 UTC** (Claude Code):
- **The "not logged in" cause:** the owner had logged in, but `/health` still said `{"signedIn":false}`. The `zarqa-codex-1` container was at **249 of 256 processes**, holding **45 `codex` processes stuck for 23–25 h**. When the gateway kills a run, only the wrapper dies and the native `codex` child keeps running. So `codex login status` couldn't start, and the gateway reported a lost login. Seen with `docker top zarqa-codex-1` and `docker stats`.
- **The restart:** `docker restart zarqa-codex-1` (that container only) gave `Logged in using ChatGPT` and `{"signedIn":true}`, with 8 processes. The login lives in the `zarqa_codex_auth` volume and survived.
- **The cap:** each Codex run uses about 45 threads, so 4 parallel runs use about 200 of 256. Raised live with `docker update --pids-limit 1024 zarqa-codex-1`, at 245 processes. **Not persistent:** `/opt/zarqa/deploy/docker-compose.yml:63` still says `pids_limit: 256`, so a recreate brings back the cap.
- **The re-read**, in one SQL transaction: **457 pages** (152 older Sol pages read before #11, whose boxes leave answers out, and 305 typed exam pages with one whole-page box) set to `extracted = false`. 81 `process` jobs inserted, 5 woken, 86 files `queued`. Pages edited in Inspect: 0, so none were affected.
- **Then:** Sol made **478 successful calls** in 30 minutes (about 19 s each, all HTTP 200, 0 offline, 0 job errors). Then **`429 Codex usage limit reached` from 23:36 UTC**; 161 pauses by 00:00.
- **At 00:00 UTC:** files `ready 120, reading 279, failed 1`; pages read 5,132, unread 4,497, of which 435 are re-reads not reached yet. Ready dropped from 176 because the re-read files are back in the queue.
- **Queue order:** jobs run by `id` (`JobQueue.ClaimAsync`). The **88 Semester 1 jobs** (paused files, then re-reads) are due now and retry every 20 min. The **189 Semester 2 and ITS jobs** have `run_after` = 2026-10-25 08:00 UTC, so the next limit windows go to Semester 1. Undo with `update jobs set run_after = now() where state = 'queued'`.
- **Not verified yet:** whether re-read crops include answers. No re-read file had finished before the limit. Check page 6 of "midterm 1 (photos)" (`6a3b9e40-0912-4c5b-b927-29b8f0a6d43e`) once its job, `461`, is `done`.

**Broken on purpose:** nothing known. **Drift** (live, but not in git):
- The `drivealt.tryzarqa.com` block exists only in the VPS's `/opt/zarqa/deploy/Caddyfile`, not in the Zarqa repo.
- The VPS gateway runs an unmerged branch.
- `README.md` still says Gemini does page reading and chat.

## 3. What was done

**2026-10-22/23, Claude Code** (unless noted):
- **First deploy:**
  - GitHub secrets and variables set; deploy key `drivealt-deploy` installed on the VPS.
  - The app joins Zarqa's Docker network as `drivealt:8080` (`deploy/docker-compose.yml:43`), because Zarqa's Caddy runs in a container and can't reach the host's `127.0.0.1`.
  - Caddy block added and reloaded (backup `Caddyfile.bak-before-drivealt`); Cloudflare A record `drivealt` → VPS, DNS only.
  - `main` created and PR #1 merged.
- **PR #2:** the dash in the uni ID input appears after the 2-digit year, and Backspace still removes it (`web/src/screens/Login.tsx`).
- **PR #3:**
  - DeepSeek runs the chat tool loop (`src/DriveAlt.Api/Chat/ChatService.cs:67`), and Gemini keeps embeddings.
  - Deploys: the image is built in parallel with the tests, with a GitHub Actions cache (`.github/workflows/deploy.yml:88`), and pushed to `ghcr.io`. The VPS pulls only changed layers, so deploys take about 2–3 min instead of about 10.
- **Box test:** four models compared on midterm pages 2 and 6 with the app's real prompt (results in section 4). Images are in `.box-compare/`, which isn't committed.
- **PR #4:**
  - The Codex page reader (`src/DriveAlt.Api/Ai/CodexClient.cs`) asks for pixel boxes (`src/DriveAlt.Api/Ai/MaterialAi.cs:115`).
  - A 429, 503, 400 or 404 from the gateway pauses the file for 20 min without using up an attempt (`src/DriveAlt.Api/Ai/CodexClient.cs:67`, `src/DriveAlt.Api/Processing/Jobs.cs:92`). Before, any AI failure quietly read the page offline.
- **Zarqa `codex-model-choice`:** the gateway accepts `CODEX_MODELS` (`gpt-6-luna,gpt-6.1-sol`), allows 240 s per run (was 105 s) and answers 429 on usage limits; 9/9 gateway tests passed.
- **2026-10-23, another session (probably the Codex agent):** deployed the gateway (13:33 UTC), merged and deployed PR #4, re-ran AI on the test file (jobs 14:33 and 14:47 UTC), opened PR #5. All of this was checked here against the VPS and GitHub. The agent's own notes weren't seen.
- **2026-10-23 15:04, Claude Code:** added `AGENTS.md` (with `CLAUDE.md` importing it) and rewrote this page.
- **2026-10-23 15:10, Claude Code:** committed them on `docs/agents-handoff` (`66779e0`) and opened **PR #6** into `main`. The owner chose to commit the server details.
- **2026-10-23 15:12, Claude Code:** merged **PR #6** after its CI passed, with the owner's OK ("Yes, merge PR #6 once its tests pass"). It deployed at 15:12–15:14 UTC (run `39976236075`).

- **2026-10-23, Codex:** merged PR #5 as `6852f1d` after full CI passed. New extraction accepts question boxes only. Chat no longer substitutes answer crops, the Show answer toggle is removed, and legacy answer refs open their question or original page. The reported false-refusal query has a regression test. CI run `39972107374` stalled for over 20 minutes; cancelled logs showed repeated `Ign: ... azure.archive.ubuntu.com` entries. CI now uses Ubuntu's official HTTPS mirror, 20-second network timeouts, a five-minute installer limit and cancellation of superseded PR runs. Deployment verification is recorded in section 2. Full import has not started.

- **2026-10-23 15:20–15:35, Claude Code:** opened PR #8 with a parallel handoff update (it conflicts with #7). It was then rebuilt as **PR #9**, on top of #7's commit `930905b`, so that both agents' facts are kept. #9 adds three things:
  - the merge-proof resume check (section 9);
  - the count of leftover answer regions (section 2);
  - a rule in `AGENTS.md`: write the handoff for the state after your PR merges, and fix leftover staleness in a docs PR, never by pushing to `main`.

  **A lesson for the process:** two agents updated this page at the same minute. Check `gh pr list` for an open handoff PR before starting another.

- **2026-10-23, Codex:** resumed after the uploader re-read. SQL sampled at 18:55:40 UTC verified all seven pages extracted without errors and only question crops (29 total). Confirmed no importer was running and only the test file existed. Started the already-approved import once at 18:58:30 UTC, confirmed its detached process running, and set follow-up monitoring. Preflight found no other open handoff PR or importer. Draft PR #10 records this claim and progress.

- **2026-10-24, Claude Code: took over the import from Codex** at the owner's order. Added to #11:
- **2026-10-24 19:10–19:50, Claude Code:**
  - merged #10 (`[skip ci]`) and #11 (deployed `5c7641a`);
  - uploaded the DriveAlt-local results and crops to `/data/import-local`;
  - ran `import-local` (dry run first: same totals);
  - requeued 50 Gemini- or network-failed files, and checked crops visually.
  
  Result: 176 files ready. Details in section 2.
  - **`import-local`** (`src/DriveAlt.Api/Import/LocalImporter.cs`): for files already in the app (matched by Drive ID and page number), it fills unread pages:
    - **Scanned pages:** the reviewed boxes of the other agent (`crops/*.json`). Its pixel boxes are divided by each page's own render size from `crops/worklist.json`, then clamped and padded.
    - **Typed PDF pages:** the PDF text, read like the offline fallback. Assessment pages get one whole-page question box until Sol splits them.
    - **Finishing:** pages already read, or edited in Inspect, are left alone. Files whose pages are all read are queued to embed, crop and become ready.
  - **Embedding fallback:** `ProcessingService.EmbedAsync` no longer fails a file on a Gemini 429 or network error; it keeps keyword search.
  - **Tests:** 38/38 unit tests pass locally.
- **2026-10-24, Claude Code** (`claude/answers-visible-in-crops`, built on #10):
  - **Answers rule fixed.** The owner said, with emphasis: *"answers can be shown just cant be written by the ai"*. So:
    - **Page-reading prompt** (`src/DriveAlt.Api/Ai/MaterialAi.cs`, `ExtractInstructions`): each question box now covers the question as it is on the paper, answers and working included, with a generous margin. The question's `text` and the page `text` still never contain answers.
    - **Padding:** `FromJson` pads every box by `BoxPadding` = 1.5% of the page per side, clamped to the page.
    - **Bot prompt** (`src/DriveAlt.Api/Chat/ChatService.cs` rule 1): showing answers in crops is fine; only typing is forbidden.
    - **Docs and tests:** `AGENTS.md`, `README.md` and the tests were updated; 35/35 unit tests pass locally.
  - **Haiku prompt corrected:** the prompt Claude Code had handed the owner for Haiku had the same mistake, and the owner was given a corrected version in chat.
  - **Import diagnosed:** stuck since 23:37 UTC (section 2).

- **2026-10-23 19:15, Codex:** read-only monitoring confirmed 249 successful page reads with zero offline pages. Diagnosed five paused files as gateway 503 login checks; signed-in health subsequently returned true. Two importer failures were connection errors. Import still runs; retries are deferred until it exits. No restarts or VPS changes were made. Detailed safe observations are stored in the chat workspace `work/import-monitor/observations.jsonl`.

## 4. User decisions

- **Domain:** `drivealt.tryzarqa.com`. The owner added the Cloudflare record themselves. Using an `*.sslip.io` address was raised and not adopted.
- **Keys:** the owner made a new Resend key `drivealt` and pasted it into GitHub. They first said to take keys from the other projects, then reversed that: "I will make new ones for this". Later they asked for DeepSeek "from zoors repo for now", so `DEEPSEEK_API_KEY` is Zoor's key.
- **Repo public:** "make the repo public it is ok", so ghcr.io storage and pulls are free. The history was scanned first and no secrets were found.
- **Builds:** "build in github", not on the VPS. The VPS already holds about 36 GB of build cache from the apps built there.
- **Import ownership (2026-10-24):** the owner said *"no no override codex for now do whateer it takes to upload everything"*. **Claude Code now owns the import**, which overrides Codex's claim in PR #10. Codex: don't relaunch or requeue it.
  - **Scope:** files with local results, i.e. Semester 1 PDFs without ITS.
  - **Everything else** (photos, Office files, Semester 2, ITS, and pages without local text) still waits for Sol, which needs the ChatGPT gateway logged in again.
- **Answers: SHOWN yes, WRITTEN never.** On 2026-10-24 the owner said, with emphasis: *"answers can be shown just cant bew written by the ai ffs i told you million times"*. This **replaces** the earlier "question-only crops; find answers on the original sheet" reading from PR #5. That came from the earlier quote *"dont black the answers out … dont output them as text but leave them in paper"*, which meant: don't hide answers, and don't type them.
  - **Crops and pages:** they show the paper as it is, answers included. Nothing is blacked out or blurred.
  - **No AI writes an answer:** not the bot, not summaries, not extracted or region text.
  - **Never tell a model to keep answers out of a box.**
- **Page-reading model:**
  - **Gemini 3.8 Flash** on the free tier took 80–136 s a call, so it was dropped.
  - **DeepSeek:** its boxes drift.
  - **Claude via subscription:** asked for "opus 5.5 using auth" (the Claude subscription). Not allowed for a server app; it needs an API key.
  - **Codex on the ChatGPT plan:** the owner said "chat gpt cost nothing", and added "zarqa isnt live", so sharing Zarqa's gateway is fine.
  - **Final choice: `gpt-6.1-sol`.** The owner said the first test harness was bad; it was rerun fairly. Then: "use sol then let me know if limits had limited".
- **Owner's standing request:** report whether the ChatGPT plan's usage limits slowed the import, and by how much.
- **Merges:** PR #5 was explicitly approved with “Merge and deploy after CI passes.” The owner also approved the original full import; it remains gated on successful question-only test-file validation. New PR merges still need the owner's OK.

## 5. Judgment calls (made without asking)

- **Database:** DriveAlt has its **own Postgres** container, not Zarqa's, so the apps don't share data or migrations.
- **Creating `main`:** `main` was started at the first commit, so PR #1 had something to merge into.
- **Caddy:** the block has no CSP, unlike Zarqa's, because the SPA's needs weren't audited. Response streaming is on.
- **Pausing on 400/404:** these count as "pause", not "read offline". A rejected request is a setup problem, and pausing keeps a misconfiguration from quietly stripping boxes.
- **DeepSeek model:** `deepseek-chat`, the non-thinking mode. Forced tool calls and JSON mode fail in thinking mode (an HTTP 400 seen in testing).
- **Gateway update:** only the `codex` container is rebuilt, with a backup of the old file. Zarqa's `deploy.sh` would have shipped the owner's unpushed LostraAi commits.
- **`CLAUDE.md`:** there was no `CLAUDE.md` to rename. `AGENTS.md` holds the rules, and `CLAUDE.md` is one line (`@AGENTS.md`), so Claude Code loads the same rules.
- **One box per question, answers inside (2026-10-24, Claude Code):** separate answer boxes weren't brought back. Answers sit inside the question box, as they do on the paper, which keeps the model's job simple and the crop faithful to the page. The 1.5% padding in code is a safety net, so a slightly tight box can't clip a line, whatever the model does.
- **Server details in a public repo:** this page names the VPS IP, user and paths. They're not secrets (DNS already exposes the IP, and SSH takes keys only). The owner was offered to strip them and chose to commit them as they are ("Commit AGENTS.md, CLAUDE.md and HANDOFF.md … and open a PR into main").

## 6. File inventory

| Path | Role | Status |
|---|---|---|
| `AGENTS.md`, `CLAUDE.md` | Rules for every agent; `CLAUDE.md` imports `AGENTS.md` | created (this update) |
| `HANDOFF.md` | This page | import progress tracked in draft PR #10 |
| VPS `~/drivealt/import.{log,started.utc,launcher.pid,download-finished.utc,exit-code}` | Import output and lifecycle markers; never overwrite or blindly relaunch | started 18:58:30 UTC |
| `README.md` | Features, local setup, import | **needs work**: its AI section is stale |
| `.github/workflows/deploy.yml` | CI, build, ghcr.io push, VPS deploy; writes the server `.env` | current |
| `deploy/docker-compose.yml`, `deploy/Dockerfile`, `deploy/.env.example` | Containers and their config; the app joins `zarqa_default` | current |
| `src/DriveAlt.Api/Ai/{CodexClient,DeepSeekClient,GeminiClient,MaterialAi}.cs` | AI clients; page reading precedence is Codex → DeepSeek → Gemini → offline | updated by PR #5 (merged) |
| `src/DriveAlt.Api/Chat/{ChatService,Guardrails}.cs` | Bot tool loop; text guardrails (`Guardrails.cs:64`) | updated by PR #5 (merged) |
| `src/DriveAlt.Api/Processing/{Jobs,ProcessingService}.cs` | Job queue, pipeline, pause on limits | current |
| `src/DriveAlt.Api/Import/DriveImporter.cs` | `import-drive` command | current |
| `src/DriveAlt.Api/Search/ResultCards.cs`, `web/src/ui/ResultCard.tsx` | Result cards | updated by PR #5 (merged) |
| VPS `~/drivealt/{.env,generated.env,docker-compose.yml}` | Server config. **`generated.env` (DB password, OTP secret) must never be regenerated** | live |
| VPS `/opt/zarqa/deploy/codex/gateway.mjs` (+ `.bak-before-sol`) | Codex gateway | live, from an unmerged branch |
| VPS `/opt/zarqa/deploy/Caddyfile` (+ `.bak-before-drivealt`) | Zarqa's Caddy, including the `# >>> drivealt` block | live; the block isn't in git |
| `.box-compare/` | Model box-test images | not committed (excluded in `.git/info/exclude`) |

**Secrets** (names only, in GitHub → Settings → Secrets):
- `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`
- `RESEND_API_KEY`
- `GEMINI_API_KEY` (free tier)
- `DEEPSEEK_API_KEY` (shared with Zoor)

**Variables:** `UPLOADERS=24-0161`, `PUBLIC_URL`, `CODEX_BASE_URL=http://codex:8090/v1`. The ChatGPT login lives in the VPS volume `zarqa_codex_auth`.

## 7. Open threads

0. **Risk from `import-local` (hypothesis):** typed exam pages filled from the PDF text use the whole page text, which on solution sheets contains typed answers. That text is indexed, and search snippets are private context for the chat model. The server guardrails (`Chat/Guardrails.cs`) still stop the bot from writing them. This is the same as the app's built-in offline mode. Re-running those pages through Sol (which leaves answers out of its text) removes the risk.
0. **Blockers found 2026-10-24 (Claude Code):**
   - **(e) Codex process leak (2026-10-25):** killed runs leave the native `codex` process running. Fix it in the Zarqa repo's `deploy/codex/gateway.mjs`: spawn detached and kill the whole process group, or reap `codex` processes older than the timeout. Also set `pids_limit` in `deploy/docker-compose.yml` to 1024 to match the live value. Until then, if the gateway says `signedIn:false`, check `docker stats` PIDs before asking the owner to log in again.
   - **(a) ChatGPT login:** the gateway is logged out. Only the owner can log it in again (section 9, Rollback).
   - **(b) Gemini quota:** the free embedding quota fails whole files (49). Options: switch on billing for the key, or make a Gemini 429 pause the file the way Codex limits do (code change, not done).
   - **(c) Wrong rule in another checkout:** `C:\Users\abdul\DriveAlt` (branch `feat/luna-sol-review`, **another agent's uncommitted work**) has a prompt saying answers are *"never inside a question box"* (`src/DriveAlt.Api/Ai/MaterialAi.cs:147` there). That contradicts the owner's rule and must be fixed before it's committed. Not edited here, because it belongs to another agent.
   - **(d) Re-run AI:** pages read before this branch have crops without answers, so re-run AI on those files once this branch deploys.
1. **Question-only test-file validation:** complete by SQL: seven AI-read pages, zero offline pages, 29 questions and no answer/other regions. The owner can still inspect box placement in the UI; Codex has not inspected it visually.
2. **Full Drive import:** running since 18:58:30 UTC; Codex owns startup and monitoring on `codex/drive-import-monitoring`, draft PR #10. Check markers/processes before attempting any relaunch.
3. **Usage-limit report:** Codex is tracking this through the app follow-up `monitor-drivealt-import-and-codex-usage-limits`, initially every 15 minutes then hourly once steady. Remain quiet while unchanged. Report ready/failed files, total/offline pages, actual usage-limit pauses and estimated blocking time, and wall clock. Do not add overlapping per-file retries as independent wall-clock delays.
4. **Zarqa repo:** open a PR for `codex-model-choice`, and add the `drivealt.tryzarqa.com` block to `deploy/Caddyfile` there. Until then, a Zarqa redeploy that rewrites the Caddyfile or the gateway would undo both.
5. **README:** the AI section needs updating to match section 2.
6. **Codex's limit wording is unverified:** the gateway matches `usage limit|rate limit|quota|too many requests`. If a limit arrives as a plain 502, pages are read offline instead of pausing. Watch `read_offline` during the import.
7. **VPS disk:** about 36 GB of unused Docker build cache from the other apps. Prune it only with the owner's OK.
8. **Optional:** skip re-running the tests on `main` after a PR has already passed them; it would save about 1.5 min per deploy.

## 8. Continuation plan

| # | Step | Owner | Verify |
|---|---|---|---|
| 0 | **Done:** PR #6 merged; read AGENTS.md and this handoff | all agents | Main contains `7228c22` |
| 1 | **Done:** PR #5 merged and deployed with owner approval; evidence in section 2 | Codex, `codex/question-only-crops` | VPS image tag must equal `6852f1d012beceb6ffcdc875bb01917b3439f157` |
| 2 | **Done:** test file re-read under PR #5 and verified by SQL | Codex | Seven pages, no offline failures, 29 question regions, zero answer/other regions |
| 3 | **Started 18:58:30 UTC:** full import command (do not launch again): `cd ~/drivealt && nohup docker compose --env-file generated.env --env-file .env exec -T app dotnet DriveAlt.Api.dll import-drive > ~/drivealt/import.log 2>&1 &` | agent | `tail -1 ~/drivealt/import.log` shows `Done: N queued…`; file count rises toward 407 |
| 4 | **Claude Code owns the import.** Semester 1 local data is done (section 2). The 223 files left need Sol: photos, Office files, pages with too little text, Semester 2 and some ITS. | agent | `select status, count(*) from files group by status` |
| 4a | **Done 2026-10-24:** owner logged in, plus a container restart and the cap raised to 1024 (section 2) | owner, Claude Code | `/health` returns `{"signedIn":true}` |
| 4b | **Owner decides:** Gemini billing, so the 105 keyword-only files get embeddings (queue `reembed` jobs for them afterwards). | **owner**, then agent | pages with `embedding is not null` rise |
| 4c | **Queued 2026-10-24 23:07 UTC:** 457 pages to re-read (older Sol pages and typed exam pages). They run as usage limits allow; Semester 1 first. | Claude Code | Page 6 of `6a3b9e40…`: crops include its answers; typed exam pages have more than one box |
| 5 | Report to the owner: files ready and failed, total pages, pages read offline, **how many times limits paused the import and for how long in total**, and start-to-finish time | agent | Numbers taken from the step 4 queries |
| 6 | Zarqa repo: PR `codex-model-choice` and add the Caddy block (open thread 4) | agent; **owner** merges | Diff of the repo against the VPS files is empty |
| 7 | Update the README's AI section | agent | PR |

**Test re-read is complete.** The owner can inspect its question boxes; no further click is needed for the completed re-read. **Pending approval:** merging this handoff follow-up, future PRs, and pruning build cache.

## 9. Resume check

Run these. If the output differs, this page is stale: find out why and update it first. The local gh CLI is not authenticated in the Codex runtime; use GitHub MCP as AbdulazizRashdi06 for PR operations. Codex used an isolated clone under its chat workspace, leaving C:/Projects/DriveAlt and LostraAi unchanged.

```bash
cd C:/Projects/DriveAlt && git fetch -q && git merge-base --is-ancestor 6852f1d origin/main && echo "main has PR #5" && git log --oneline 6852f1d..origin/main
# expect: "main has PR #5", then only commits that sections 2–3 explain (handoff PRs #7/#9). Anything else = stale page.
gh pr list -R AbdulazizRashdi06/DriveAlt --state open --json number,headRefName --jq '.[] | "#\(.number) \(.headRefName)"'
# PRs #7 and #9 are merged. Open: #10 (Codex, import monitoring) and #11 (Claude Code, answers in crops; built on #10) until merged.
ssh -i ~/.ssh/zarqa_vps deploy@173.249.40.122 'cd ~/drivealt && grep -o "^DRIVEALT_IMAGE=.*" .env && curl -s https://drivealt.tryzarqa.com/api/health && docker exec zarqa-codex-1 node -e "fetch(\"http://127.0.0.1:8090/health\").then(r=>r.text()).then(console.log)" && sha256sum /opt/zarqa/deploy/codex/gateway.mjs | cut -c1-16'
# expect: DRIVEALT_IMAGE=…drivealt:<last main commit that changed code>, i.e.
#   git log -1 --first-parent --format=%H origin/main -- . ':(exclude)*.md'   (now 5c7641a2ce4ba4ff2a53d3e294c90b0f623118e3)
#   Docs-only PRs (#7, #9) are merged with [skip ci], so they don't build or deploy and the image stays put.
#         {"ok":true}{"signedIn":true}
#         5da0c724e36f4562
```
Port 22 is blocked on campus Wi-Fi; use another network if SSH times out.

**Monitoring and test-file SQL** (on the VPS, in `~/drivealt`):
```bash
D="docker compose --env-file generated.env --env-file .env"
$D exec -T db psql -U drivealt -d drivealt -c "select status, count(*), sum(page_count) pages from files group by status" \
  -c "select count(*) filter (where error is null and extracted) read_ok, count(*) filter (where error is not null) read_offline from pages" \
  -c "select count(*) paused_now from jobs where state='queued' and error like 'Paused:%'"
$D exec -T db psql -U drivealt -d drivealt -c "select label, count(*) from regions group by label order by 1"
$D logs --since 24h --no-log-prefix app 2>&1 | grep -c "AI paused"    # exits 1 when the count is 0; that's normal
# expect at 2026-10-25 00:00 UTC: files ready 120, reading 279, failed 1; pages read 5,132, unread 4,497. Paused jobs (429) are expected until the plan resets.
# The reading count only falls after the owner logs the gateway in (step 4a). Higher ready / lower reading = progress since.
```

**Rollback:**
- **Gateway:** `cd /opt/zarqa && cp -p deploy/codex/gateway.mjs.bak-before-sol deploy/codex/gateway.mjs && docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build --no-deps codex`
- **Page reading back to DeepSeek:** `gh variable delete CODEX_BASE_URL -R AbdulazizRashdi06/DriveAlt`, then `gh workflow run deploy.yml -R AbdulazizRashdi06/DriveAlt --ref main`
- **Caddy:** restore `Caddyfile.bak-before-drivealt`, then `docker exec zarqa-caddy-1 caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile`
- **Re-logging into the ChatGPT plan** (owner only): `cd /opt/zarqa && docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec codex codex login --device-auth`
