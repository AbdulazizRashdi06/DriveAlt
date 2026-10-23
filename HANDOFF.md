# HANDOFF: DriveAlt

> Every agent: read this first and run the **Resume check** (section 9). Update the page in the same branch as every change. The rules are in [`AGENTS.md`](AGENTS.md).

## 1. Header

| | |
|---|---|
| Project | DriveAlt: the GUtech Year 2 batch drive (CS / CYS / AI) as a web app. Live at https://drivealt.tryzarqa.com |
| Repo | `AbdulazizRashdi06/DriveAlt` (**public**), local `C:\Projects\DriveAlt` |
| Branch / base | Updated on `docs/handoff-merge-proof`, which is built **on top of** PR #7 (`codex/handoff-question-only-deployment` at `930905b`, base `main` at `6852f1d`) |
| Updated | 2026-10-23 15:35 UTC, by Claude Code (Opus 5.5), on top of Codex's 15:19:19 UTC update |
| Purpose of this update | Codex: record PR #5, its CI mirror fix, deployment verification and the pending question-only test-file re-read. Claude Code: the merge-proof resume check, the leftover answer regions, and the `AGENTS.md` rule for handoffs around merges. |

**In one line:** the app is deployed and works. Chat runs on DeepSeek, and pages are read by Codex `gpt-6.1-sol`. Next: re-read the test file with question-only crops, then run the already-approved full Drive import (407 files) and report on the ChatGPT plan's usage limits.

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
| **#7** | **open** | `codex/handoff-question-only-deployment` (Codex) | This page after PR #5. Docs only. |
| #8 | **closed** unmerged (superseded by #9) | `docs/handoff-after-6` (Claude Code) | Same facts as #7, written in parallel; conflicted with #7. |
| **#9** | **open** | `docs/handoff-merge-proof` (Claude Code) | Built on #7's commit. Adds only the items listed under "Purpose" above. Merge **#7 first**, then #9, which is then conflict-free. |

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

**Data before the post-PR #5 re-read (still pending):** the only file imported is **"midterm 1 (photos)"** (`6a3b9e40-0912-4c5b-b927-29b8f0a6d43e`, Data Structures past paper, 7 phone photos). Sol re-read it at 14:49 UTC: status `ready`, and all 7 pages were read by the AI (`pages.error` is null on every page). Regions in the DB at 15:19 UTC: `question 31, answer 22, other 5`. **The 22 answer regions are left over from that pre-PR #5 read.** PR #5 no longer creates them and resolves old answer refs to their question; the pending re-run replaces them.

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
- **2026-10-23 15:12, Claude Code:** merged **PR #6** after its CI passed, with the owner's OK ("Yes, merge PR #6 once its tests pass"). It deployed at 15:12–15:14 UTC (run `39976236075`).

- **2026-10-23, Codex:** merged PR #5 as `6852f1d` after full CI passed. New extraction accepts question boxes only. Chat no longer substitutes answer crops, the Show answer toggle is removed, and legacy answer refs open their question or original page. The reported false-refusal query has a regression test. CI run `39972107374` stalled for over 20 minutes; cancelled logs showed repeated `Ign: ... azure.archive.ubuntu.com` entries. CI now uses Ubuntu's official HTTPS mirror, 20-second network timeouts, a five-minute installer limit and cancellation of superseded PR runs. Deployment verification is recorded in section 2. Full import has not started.

- **2026-10-23 15:20–15:35, Claude Code:** opened PR #8 with a parallel handoff update (it conflicts with #7). It was then rebuilt as **PR #9**, on top of #7's commit `930905b`, so that both agents' facts are kept. #9 adds three things:
  - the merge-proof resume check (section 9);
  - the count of leftover answer regions (section 2);
  - a rule in `AGENTS.md`: write the handoff for the state after your PR merges, and fix leftover staleness in a docs PR, never by pushing to `main`.

  **A lesson for the process:** two agents updated this page at the same minute. Check `gh pr list` for an open handoff PR before starting another.

## 4. User decisions

- **Domain:** `drivealt.tryzarqa.com`. The owner added the Cloudflare record themselves. Using an `*.sslip.io` address was raised and not adopted.
- **Keys:** the owner made a new Resend key `drivealt` and pasted it into GitHub. They first said to take keys from the other projects, then reversed that: "I will make new ones for this". Later they asked for DeepSeek "from zoors repo for now", so `DEEPSEEK_API_KEY` is Zoor's key.
- **Repo public:** "make the repo public it is ok", so ghcr.io storage and pulls are free. The history was scanned first and no secrets were found.
- **Builds:** "build in github", not on the VPS. The VPS already holds about 36 GB of build cache from the apps built there.
- **Answers:** in the owner's words, *"dont black the answers out … the rule is dont output them as text but leave them in paper"*. PR #5 removes answer crops altogether. **Confirmed by the owner in the Codex chat:** crop only questions; students can find any answers on the original sheet. No original image is changed.
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
- **Server details in a public repo:** this page names the VPS IP, user and paths. They're not secrets (DNS already exposes the IP, and SSH takes keys only). The owner was offered to strip them and chose to commit them as they are ("Commit AGENTS.md, CLAUDE.md and HANDOFF.md … and open a PR into main").

## 6. File inventory

| Path | Role | Status |
|---|---|---|
| `AGENTS.md`, `CLAUDE.md` | Rules for every agent; `CLAUDE.md` imports `AGENTS.md` | created (this update) |
| `HANDOFF.md` | This page | rewritten (this update) |
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

1. **Question-only test-file validation:** PR #5 is merged and approved. Codex verified deployment. The owner must click Inspect → midterm 1 (photos) → Re-run AI, then Codex checks seven AI-read pages and question-only regions. Browser access was declined; do not bypass it.
2. **Full Drive import:** approved but not started; waiting for the post-PR #5 test-file validation. Codex will start and monitor it after validation.
3. **Usage-limit report:** owed to the owner after the import.
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
| 2 | Re-run AI on the test file under PR #5 (Inspect → "midterm 1 (photos)" → **Re-run AI**) | **owner** clicks; agent checks | SQL in section 9: 7 pages, `offline=f`, only question regions |
| 3 | Start the full import (already approved, after step 2 passes): `cd ~/drivealt && nohup docker compose --env-file generated.env --env-file .env exec -T app dotnet DriveAlt.Api.dll import-drive > ~/drivealt/import.log 2>&1 &` | agent | `tail -1 ~/drivealt/import.log` shows `Done: N queued…`; file count rises toward 407 |
| 4 | Monitor about hourly until every file is `ready` or `failed`. At about 6 pages a minute, expect 10–20 h. | agent | Monitoring SQL and the `AI paused` count (section 9) |
| 5 | Report to the owner: files ready and failed, total pages, pages read offline, **how many times limits paused the import and for how long in total**, and start-to-finish time | agent | Numbers taken from the step 4 queries |
| 6 | Zarqa repo: PR `codex-model-choice` and add the Caddy block (open thread 4) | agent; **owner** merges | Diff of the repo against the VPS files is empty |
| 7 | Update the README's AI section | agent | PR |

**Required owner action:** re-run the test file after deployment and inspect its question boxes. **Pending approval:** merging this handoff follow-up, future PRs, and pruning build cache.

## 9. Resume check

Run these. If the output differs, this page is stale: find out why and update it first. The local gh CLI is not authenticated in the Codex runtime; use GitHub MCP as AbdulazizRashdi06 for PR operations. Codex used an isolated clone under its chat workspace, leaving C:/Projects/DriveAlt and LostraAi unchanged.

```bash
cd C:/Projects/DriveAlt && git fetch -q && git merge-base --is-ancestor 6852f1d origin/main && echo "main has PR #5" && git log --oneline 6852f1d..origin/main
# expect: "main has PR #5", then only commits that sections 2–3 explain (handoff PRs #7/#9). Anything else = stale page.
gh pr list -R AbdulazizRashdi06/DriveAlt --state open --json number,headRefName --jq '.[] | "#\(.number) \(.headRefName)"'
# expect: only PRs listed as open in section 2 (#7 and #9 until merged)
ssh -i ~/.ssh/zarqa_vps deploy@173.249.40.122 'cd ~/drivealt && grep -o "^DRIVEALT_IMAGE=.*" .env && curl -s https://drivealt.tryzarqa.com/api/health && docker exec zarqa-codex-1 node -e "fetch(\"http://127.0.0.1:8090/health\").then(r=>r.text()).then(console.log)" && sha256sum /opt/zarqa/deploy/codex/gateway.mjs | cut -c1-16'
# expect: DRIVEALT_IMAGE=…drivealt:<SHA of origin/main> (git rev-parse origin/main; now 6852f1d012beceb6ffcdc875bb01917b3439f157). A docs-only merge redeploys too.
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
# expect now: ready | 1 | 7 ; read_ok 7, read_offline 0 ; paused_now 0 ; answer 22, other 5, question 31 (no answer rows after the re-run) ; 0
```

**Rollback:**
- **Gateway:** `cd /opt/zarqa && cp -p deploy/codex/gateway.mjs.bak-before-sol deploy/codex/gateway.mjs && docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build --no-deps codex`
- **Page reading back to DeepSeek:** `gh variable delete CODEX_BASE_URL -R AbdulazizRashdi06/DriveAlt`, then `gh workflow run deploy.yml -R AbdulazizRashdi06/DriveAlt --ref main`
- **Caddy:** restore `Caddyfile.bak-before-drivealt`, then `docker exec zarqa-caddy-1 caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile`
- **Re-logging into the ChatGPT plan** (owner only): `cd /opt/zarqa && docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec codex codex login --device-auth`
