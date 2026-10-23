# HANDOFF: DriveAlt

> Every agent: read this first and run the **Resume check** (section 9). Update the page in the same branch as every change. The rules are in [`AGENTS.md`](AGENTS.md).

## 1. Header

| | |
|---|---|
| Project | DriveAlt: the GUtech Year 2 batch drive (CS / CYS / AI) as a web app. Live at https://drivealt.tryzarqa.com |
| Repo | `AbdulazizRashdi06/DriveAlt` (**public**), local `C:\Projects\DriveAlt` |
| Branch / base | Written on `docs/agents-handoff` (PR #6), from `origin/main` at `e02ca2d` (merge of PR #4) |
| Updated | 2026-10-23 15:10 UTC, by Claude Code (Opus 5.5) |
| Purpose of this update | Move to multi-agent work: add `AGENTS.md` and rewrite this page in the handover skill's format, reflecting everything up to PR #5 |

**In one line:** the app is deployed and works. Chat runs on DeepSeek, and pages are read by Codex `gpt-6.1-sol`. Next: review PR #5 (question-only crops), then run the full Drive import (407 files) and report on the ChatGPT plan's usage limits.

## 2. State of the world

**Git** (checked 2026-10-23 15:10 UTC):
```
$ git status --short            # on docs/agents-handoff: clean once this update is committed
$ git log --oneline -1 origin/docs/agents-handoff
66779e0 Add AGENTS.md and a shared HANDOFF.md for multi-agent work   (+ this page's PR-state update)
$ git log --oneline -3 origin/main
e02ca2d Merge pull request #4 from AbdulazizRashdi06/feat/codex-pages
140780f Read pages through the Codex gateway (gpt-6.1-sol) and pause on usage limits
fda5822 Merge pull request #2 from AbdulazizRashdi06/fix/uni-id-dash
```

**PRs:**

| PR | State | Branch | What |
|---|---|---|---|
| #1 | merged | `claude/elegant-keller-1n8kkd` | First release |
| #2 | merged | `fix/uni-id-dash` | Uni ID dash appears right after the year |
| #3 | merged | `feat/deepseek` | DeepSeek for chat and page reading; ghcr.io deploys |
| #4 | merged, **deployed** | `feat/codex-pages` | Pages read by Codex `gpt-6.1-sol`; pause on usage limits |
| **#5** | **open**, CI `test` **pending** at 15:04 UTC | `codex/question-only-crops` (by the Codex agent) | Question crops only, no answer crops or "Show answer"; question searches no longer treated as answer requests |
| **#6** | **open** | `docs/agents-handoff` (by Claude Code) | Adds `AGENTS.md` and `CLAUDE.md`; this page. Docs only. |

**Other repos:**
- **Zarqa repo** (`AbdulazizRashdi06/Zarqa`, local `C:\Projects\LostraAi`): branch `codex-model-choice` (`f403756`) is pushed and **deployed to the VPS**, but has **no PR and isn't merged**. The owner's local LostraAi `main` is ahead of origin by 3 commits (Caddy changes for Zoor and Schedule). That's the owner's own work; leave it alone.

**Tests:**
- **PR #4:** CI passed (run `39960420833`, `test pass 1m37s`).
- **Unit tests on `main`:** 28/28 passed locally on `feat/codex-pages`, before PR #5.
- **PR #5:** its description says 39 backend unit tests, 8 frontend tests, `tsc` and the build passed locally. Its CI is pending and **unverified here**.

**Running on the VPS** (`deploy@173.249.40.122`, checked 15:00 UTC):
```
drivealt-app-1 Up 42 minutes          image ghcr.io/abdulazizrashdi06/drivealt:e02ca2d…
drivealt-db-1  Up 4 hours (healthy)
zarqa-codex-1  Up About an hour        gateway.mjs sha256 5da0c724… = branch codex-model-choice
zarqa-caddy-1  Up 4 days               serves drivealt.tryzarqa.com
https://drivealt.tryzarqa.com/api/health → {"ok":true}
DB: 1 files, 7 pages     ~/drivealt/.env has Codex__BaseUrl set (count 1)     45 GB disk free
```

**Data:** the only file imported is **"midterm 1 (photos)"** (`6a3b9e40-0912-4c5b-b927-29b8f0a6d43e`, Data Structures past paper, 7 phone photos). Sol re-read it at 14:49 UTC: status `ready`, and all 7 pages were read by the AI (`pages.error` is null on every page).

| Page | Questions | Answers |
|---|---|---|
| 1 | 8 | 9 |
| 2 | 5 | 1 |
| 3 | 2 | 1 |
| 4 | 6 | 4 |
| 5 | 1 | 0 |
| 6 | 5 | 5 |
| 7 | 4 | 2 |

**The full Drive import has not started.** `import-drive --dry-run` found **407 files**, with videos and ZIPs skipped. Its log is `~/drivealt/import-dryrun.log`.

**Usage limits:** no `AI paused` log lines in the 3 hours before 15:00 UTC.

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

## 4. User decisions

- **Domain:** `drivealt.tryzarqa.com`. The owner added the Cloudflare record themselves. Using an `*.sslip.io` address was raised and not adopted.
- **Keys:** the owner made a new Resend key `drivealt` and pasted it into GitHub. They first said to take keys from the other projects, then reversed that: "I will make new ones for this". Later they asked for DeepSeek "from zoors repo for now", so `DEEPSEEK_API_KEY` is Zoor's key.
- **Repo public:** "make the repo public it is ok", so ghcr.io storage and pulls are free. The history was scanned first and no secrets were found.
- **Builds:** "build in github", not on the VPS. The VPS already holds about 36 GB of build cache from the apps built there.
- **Answers:** in the owner's words, *"dont black the answers out … the rule is dont output them as text but leave them in paper"*. PR #5 goes further and removes answer crops altogether. **Hypothesis:** the owner asked the Codex agent for that; the request wasn't seen in this session.
- **Page-reading model:**
  - **Gemini 3.8 Flash** on the free tier took 80–136 s a call, so it was dropped.
  - **DeepSeek:** its boxes drift.
  - **Claude via subscription:** asked for "opus 5.5 using auth" (the Claude subscription). Not allowed for a server app; it needs an API key.
  - **Codex on the ChatGPT plan:** the owner said "chat gpt cost nothing", and added "zarqa isnt live", so sharing Zarqa's gateway is fine.
  - **Final choice: `gpt-6.1-sol`.** The owner said the first test harness was bad; it was rerun fairly. Then: "use sol then let me know if limits had limited".
- **Owner's standing request:** report whether the ChatGPT plan's usage limits slowed the import, and by how much.
- **Merges:** the owner approved PRs #1–#4 in chat. Merging needs their OK every time.

## 5. Judgment calls (made without asking)

- **Database:** DriveAlt has its **own Postgres** container, not Zarqa's, so the apps don't share data or migrations.
- **Creating `main`:** `main` was started at the first commit, so PR #1 had something to merge into.
- **Caddy:** the block has no CSP, unlike Zarqa's, because the SPA's needs weren't audited. Response streaming is on.
- **Pausing on 400/404:** these count as "pause", not "read offline". A rejected request is a setup problem, and pausing keeps a misconfiguration from quietly stripping boxes.
- **DeepSeek model:** `deepseek-chat`, the non-thinking mode. Forced tool calls and JSON mode fail in thinking mode (an HTTP 400 seen in testing).
- **Gateway update:** only the `codex` container is rebuilt, with a backup of the old file. Zarqa's `deploy.sh` would have shipped the owner's unpushed LostraAi commits.
- **`CLAUDE.md`:** there was no `CLAUDE.md` to rename. `AGENTS.md` holds the rules, and `CLAUDE.md` is one line (`@AGENTS.md`), so Claude Code loads the same rules.
- **Server details in a public repo:** this page names the VPS IP, user and paths. They're not secrets (DNS already exposes the IP, and SSH takes keys only). The owner was offered to strip them and chose to commit them as they are ("Commit AGENTS.md, CLAUDE.md and HANDOFF.md … and open a PR into main").

## 6. File inventory

| Path | Role | Status |
|---|---|---|
| `AGENTS.md`, `CLAUDE.md` | Rules for every agent; `CLAUDE.md` imports `AGENTS.md` | created (this update) |
| `HANDOFF.md` | This page | rewritten (this update) |
| `README.md` | Features, local setup, import | **needs work**: its AI section is stale |
| `.github/workflows/deploy.yml` | CI, build, ghcr.io push, VPS deploy; writes the server `.env` | current |
| `deploy/docker-compose.yml`, `deploy/Dockerfile`, `deploy/.env.example` | Containers and their config; the app joins `zarqa_default` | current |
| `src/DriveAlt.Api/Ai/{CodexClient,DeepSeekClient,GeminiClient,MaterialAi}.cs` | AI clients; page reading precedence is Codex → DeepSeek → Gemini → offline | modified by PR #5 (open) |
| `src/DriveAlt.Api/Chat/{ChatService,Guardrails}.cs` | Bot tool loop; text guardrails (`Guardrails.cs:64`) | modified by PR #5 (open) |
| `src/DriveAlt.Api/Processing/{Jobs,ProcessingService}.cs` | Job queue, pipeline, pause on limits | current |
| `src/DriveAlt.Api/Import/DriveImporter.cs` | `import-drive` command | current |
| `src/DriveAlt.Api/Search/ResultCards.cs`, `web/src/ui/ResultCard.tsx` | Result cards | modified by PR #5 (open) |
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

1. **PR #5:** awaiting CI and the owner's review. It changes the product (no answer crops), so the owner should confirm that's what they want.
2. **Full Drive import:** not started. It's waiting on PR #5, per PR #5's description.
3. **Usage-limit report:** owed to the owner after the import.
4. **Zarqa repo:** open a PR for `codex-model-choice`, and add the `drivealt.tryzarqa.com` block to `deploy/Caddyfile` there. Until then, a Zarqa redeploy that rewrites the Caddyfile or the gateway would undo both.
5. **README:** the AI section needs updating to match section 2.
6. **Codex's limit wording is unverified:** the gateway matches `usage limit|rate limit|quota|too many requests`. If a limit arrives as a plain 502, pages are read offline instead of pausing. Watch `read_offline` during the import.
7. **VPS disk:** about 36 GB of unused Docker build cache from the other apps. Prune it only with the owner's OK.
8. **Optional:** skip re-running the tests on `main` after a PR has already passed them; it would save about 1.5 min per deploy.

## 8. Continuation plan

| # | Step | Owner | Verify |
|---|---|---|---|
| 0 | Owner merges **PR #6** (docs only), so every agent's checkout has `AGENTS.md` and this page | **owner** | `git log --oneline -1 origin/main` shows the PR #6 merge |
| 1 | Get PR #5's CI green, then the owner reviews and merges it. That deploys it. | agent (CI), **owner** (merge) | `gh pr checks 5 -R AbdulazizRashdi06/DriveAlt`; after the merge, the VPS image tag equals the merge SHA |
| 2 | Re-run AI on the test file under PR #5 (Inspect → "midterm 1 (photos)" → **Re-run AI**) | **owner** clicks; agent checks | SQL in section 9: 7 pages, `offline=f`, only question regions |
| 3 | Start the full import (owner's OK): `cd ~/drivealt && nohup docker compose --env-file generated.env --env-file .env exec -T app dotnet DriveAlt.Api.dll import-drive > ~/drivealt/import.log 2>&1 &` | agent | `tail -1 ~/drivealt/import.log` shows `Done: N queued…`; file count rises toward 407 |
| 4 | Monitor about hourly until every file is `ready` or `failed`. At about 6 pages a minute, expect 10–20 h. | agent | Monitoring SQL and the `AI paused` count (section 9) |
| 5 | Report to the owner: files ready and failed, total pages, pages read offline, **how many times limits paused the import and for how long in total**, and start-to-finish time | agent | Numbers taken from the step 4 queries |
| 6 | Zarqa repo: PR `codex-model-choice` and add the Caddy block (open thread 4) | agent; **owner** merges | Diff of the repo against the VPS files is empty |
| 7 | Update the README's AI section | agent | PR |

**Pending decisions for the owner:** PR #5's behaviour, when to start the import, and pruning the build cache.

## 9. Resume check

Run these. If the output differs, this page is stale: find out why and update it first.

```bash
cd C:/Projects/DriveAlt && git fetch -q && git log --oneline -1 origin/main
# expect: e02ca2d Merge pull request #4 …   (newer = something was merged since)
gh pr list -R AbdulazizRashdi06/DriveAlt --state open --json number,headRefName --jq '.[] | "#\(.number) \(.headRefName)"'
# expect: #5 codex/question-only-crops and #6 docs/agents-handoff (while unmerged)
ssh -i ~/.ssh/zarqa_vps deploy@173.249.40.122 'cd ~/drivealt && grep -o "^DRIVEALT_IMAGE=.*" .env && curl -s https://drivealt.tryzarqa.com/api/health && docker exec zarqa-codex-1 node -e "fetch(\"http://127.0.0.1:8090/health\").then(r=>r.text()).then(console.log)" && sha256sum /opt/zarqa/deploy/codex/gateway.mjs | cut -c1-16'
# expect: DRIVEALT_IMAGE=ghcr.io/abdulazizrashdi06/drivealt:e02ca2d4501985276482cc38dda9029fbcb095fa
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
$D logs --since 24h --no-log-prefix app 2>&1 | grep -c "AI paused"
# expect now: ready | 1 | 7 ; read_ok 7, read_offline 0 ; paused_now 0 ; 0
```

**Rollback:**
- **Gateway:** `cd /opt/zarqa && cp -p deploy/codex/gateway.mjs.bak-before-sol deploy/codex/gateway.mjs && docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build --no-deps codex`
- **Page reading back to DeepSeek:** `gh variable delete CODEX_BASE_URL -R AbdulazizRashdi06/DriveAlt`, then `gh workflow run deploy.yml -R AbdulazizRashdi06/DriveAlt --ref main`
- **Caddy:** restore `Caddyfile.bak-before-drivealt`, then `docker exec zarqa-caddy-1 caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile`
- **Re-logging into the ChatGPT plan** (owner only): `cd /opt/zarqa && docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec codex codex login --device-auth`
