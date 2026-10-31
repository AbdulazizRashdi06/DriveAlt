# HANDOFF: DriveAlt

> Every agent: read this first and run the **Resume check** (section 9). Update the page in the same branch as every change. The rules are in [`AGENTS.md`](AGENTS.md).

## 1. Header

**Current update: 2026-10-30, Codex, `fix/chat-asked-material-kind`; base PR #45 `0c77a80` on main code `083b6d1`.** Asked-for contract implemented; owner now requires AI-only interpretation with no keyword overrides. AI-only code `94046e1` passed full GitHub CI; this final update records the results. Root checkout's unrelated dirty AI/deployment files remain untouched. PR #46 is open for GitHub CI; merge/deployment are not authorized. Older dated state claims below are historical.

| | |
|---|---|
| Project | DriveAlt: the GUtech Year 2 batch drive (CS / CYS / AI) as a web app. Live at https://drivealt.tryzarqa.com |
| Repo | `AbdulazizRashdi06/DriveAlt` (**public**), local `C:\Projects\DriveAlt` |
| Branch / base | `fix/chat-asked-material-kind`, based on PR #45 `0c77a80`; main code base `083b6d1`. Merge #45 before this feature PR. |
| Updated | 2026-10-30, Codex; asked-for material contract and online CI validation |

| Purpose of this update | Enforce requested material kinds in search/replies/options/Show more; open a PR for full GitHub CI. |

**2026-10-26/27 outage, fixed and deployed (PR #22, run 40590404639, 2026-10-27 ~21:58 UTC):** https://drivealt.tryzarqa.com returned **Cloudflare 525** after a Zarqa deploy replaced the shared Caddyfile without DriveAlt's route. Every DriveAlt deploy now restores the block (`deploy/ensure-caddy.sh`), and `caddy-guard.yml` re-checks it every 15 minutes. Details in section 3, 2026-10-27. **The permanent fix (Zarqa PR #6, `abebc56`) is merged and present in the owner's local checkout.** Keep the guard until a later authorized Zarqa deploy is verified to preserve the route (open thread 2).

**PR #32 merged and deployed:** `main` contains `18a6b605ec768a54c83fde6583e90db0f7afb2f5` and merge `56bd76b585e6540bdd25645760b2a640dc37efa0` (reader, sharing, duplicate checks and reader regression fixes). **PR #21 remains open** and belongs to Claude Code; its typed-pick and fixed-code login changes are not deployed.

**In one line:** Verified production image is `083b6d1ef7cb1269b05a8d1e6081ec2a64be70fc` with healthy public/gateway endpoints. Asked-for contract is prepared on this branch; AI-only code `94046e1` passed online CI: 115 backend/API tests, 31 frontend tests and 3 gateway checks, plus TypeScript/build. No production change is authorized by this task.
## 2. State of the world

**Resume held files (Claude Code, 2026-10-31, branch `claude/resume-held`; written for after it merges and deploys):** owner: *"how do i unhold it"*. Files whose process job was held by SQL on 2026-10-25 (`run_after` 2100, `Held: owner wants Semester 1 without ITS only`, see "Semester 2 and ITS held") show "On hold" on the Upload page and now get a **Resume** button: `POST /api/files/{id}/resume` (uploaders only) sets that file's held queued jobs to `run_after = now`, `error = null`, so they join the normal queue. Paused (usage-limit) jobs are not touched. This releases one file at a time, by the owner's choice; it does not release the other held Semester 2 / ITS jobs.

**Uploads say why they're waiting (Claude Code, 2026-10-31, branch `claude/upload-wait-reason`; written for after it merges and deploys):** owner, on files stuck at "Step 2 of 5 · Reading 0/5": *"i think the app should tell me the reason"*. `/api/uploads` adds `wait` to each unfinished file (`FileEndpoints.WaitsAsync`, from its latest process job): `reading` (a worker has it), `queued` (N due jobs ahead, or a start time), `paused` (AI usage limit or AI unavailable, with the resume time), `retrying` (after an error, attempt n of 3, with the error), `held` (job on hold) or `stuck` (in progress but no job). The Upload page shows it under the progress bar (`Upload.tsx`, `waitText`); a stuck file gets a Retry button, and `/process` now re-queues an in-progress file that has no live job. Tests: backend 107/107 (new `Unfinished_uploads_say_why_they_are_waiting`), frontend 32/32 (new `UploadWait.test.tsx`).

**The AI writes its own searches (Claude Code, 2026-10-31, branch `claude/ai-semantic-search`; written for after it merges and deploys):** owner: *"for the semantic search the ai should be the one prompting"*. `search_material` and `ask` options take `meaning` (a sentence embedded as written) and `keywords` (exact words/phrases for full-text, `SearchService.KeywordTsQuery`) instead of `query`. For these searches `SearchService` applies no stop words, no file-word stripping, no Q-number boost; `ChatService.SearchWantedAsync` no longer retries without year/label (the `broadened` flag is gone; the model searches again if it wants). Fallbacks: embeddings down → `meaning` words as keywords; no meaning and no keywords → filter-only listing, newest first. Browse search and old saved options with only `query` keep the older processing. Tests: backend 106/106 locally (new `Model_keywords_are_used_as_written`, `A_year_that_matches_nothing_returns_nothing_instead_of_a_silent_retry`). Real DeepSeek search quality is unverified until live use.

**The AI picks, the server shows (Claude Code, 2026-10-31, branch `claude/ai-picks-no-filter`; written for after it merges and deploys):** owner: *"why does the program filter things out ?? … nothing hardcoded ai have a library he gets from the library … imagine i ask for slides and questions"*. Removed every server-side override of the model's picks: the kind filter (`ResultCards.FilterAsync`/`KindsAsync`), `reply.asked_for`, the typed-pick re-search, the "nothing of that kind" rewrite, and the want/type conflict errors. What the model picks is shown as is; the only check left is that each ref came from a search this conversation (`AllowedRefs`). Prompt rules 1 and 4–7 rewritten as general rules (show what was asked, search each kind wanted, ask when unclear, offer but don't show), dropping the example patches. Tests: backend 104/104 twice locally, frontend 31/31. Deployed with #44 (run for merge `ac22688`).

**Material-kind follow-ups (Claude Code, 2026-10-30, branch `claude/material-kind-followups`, stacked on Codex's PR #46; written for after both merge and deploy):** fixes from a review of #46. (1) A typed pick or "more" that comes with `asked_for="none"` used to filter every result away; it now keeps the option's / shown kind, and a different declared kind re-searches the option (`ChatService.PickAsync`, `ShowMoreAsync`; prompt rule 7 says what to set). (2) When every picked ref is dropped, or "more" has nothing of that kind left, the reply writer writes a fresh line instead of the model's "here are …". (3) `f:id` without its `?page=` hint maps to the searched file (`AllowedRefs`). (4) Impossible want/type pairs return a tool error (`KindConflict`). (6) A stale page hint falls back to the cover (`ResultCards.HydrateAsync`). No keyword rules anywhere: the model's `asked_for` is the only interpretation, the server only checks real kinds. Tests: backend 124/124 twice locally, 7 new in `MaterialKindApiTests.cs`. Not fixed (minor, from the same review): year fallback for slides/files searches, Show-more line order after filtering, whole non-slide files opening at the best page instead of the cover, `recentRegions` in Browse search with no usable words.

**Current: AI-only intent, 2026-10-30 (Codex, PR #46):** owner explicitly requires no hardcoded interpretation, including Arabic and negated requests. Removed `Heuristics.AskedKind`, its regexes and `MaterialWants.Effective`. Replies now use only AI `asked_for`; option clicks use AI-authored `want`, typed picks/follow-ups use current `asked_for`, and Show more buttons continue the stored selected kind using metadata only. Server still validates tool kinds, search provenance and real database ref kinds. Prompt instructs interpreting full meaning, language, context and negation. Replaced keyword tests with scripted-model regressions proving the app honors AI decisions for Arabic, negation, lecture notes and question-PDF requests, even when literal words conflict. This supersedes the earlier vocabulary corrections below. Full GitHub run `41072080669`, job `124118521792`, passed on `94046e1`: `Passed! - Failed: 0, Passed: 115, Skipped: 0, Total: 115`; gateway `3 passed / 0 failed`; frontend `9 passed` files / `31 passed` tests; TypeScript and Vite build passed. Image/deploy jobs were skipped. This documentation-only result record changes no tested code; its normal CI rerun is watched. No local tests or production changes.

**Vocabulary correction, 2026-10-30 (Codex, PR #46):** owner identified overbroad exercise/lecture keyword overrides. `AskedKind` now treats exercise(s) as questions only without PDF/sheet/file/download words (including plurals), and excludes lecture notes from slide vocabulary. Explicit questions/Q-number vocabulary still wins. Added nine vocabulary cases and three scripted-model API cases proving exercise files and notes pages remain visible. Full online run `41070599306`, job `124113429123`, passed on corrected code head `43d0ce0`: `Passed! - Failed: 0, Passed: 128, Skipped: 0, Total: 128`; gateway `3 passed / 0 failed`; frontend `9 passed` files / `31 passed` tests; TypeScript and production build passed. This documentation-only commit records the verified code results and triggers ordinary CI; it changes no tested code. Do not run local tests. No merge/deploy occurred.

**Asked-for contract, 2026-10-30 (Codex, [PR #46](https://github.com/AbdulazizRashdi06/DriveAlt/pull/46), not deployed):** searches require `want` (`questions`, `slides`, `slide_pages`, `files`, `notes_pages`); replies require `asked_for` (those plus `none`). `SearchService` filters every page/region candidate path, including vector/filter-only search, and excludes slide/notes content from questions. Question page fallback is limited to assessment pages without question boxes. `ResultCards.KindsAsync` uses database metadata to validate refs before batching, on option picks and on Show more. Only the AI interprets request intent; there is no keyword override. Specific-slide requests use AI-declared `slide_pages`. Slide decks open as one full file at the best-matching page. Ref hints `f:<id>?page=N` preserve that page through history/options without a schema migration; legacy bare file refs still open the cover. Old options `files:true` map to `files`, otherwise missing `want` maps to `questions`. AI-authored text remains unchanged if every ref is dropped.

**Online validation:** PR #46 opened on implementation commit `26b0401`. GitHub run `41069331169`, job `124109403349`, tested head `9a23253`: `Failed: 1, Passed: 115, Skipped: 0, Total: 116`. The new contract regressions passed. The existing whole-paper test expected one held result but got nine because the new fixtures shared its Data Structures/2026 inventory (`ApiTests.cs:484`; `MaterialKindApiTests.cs:28`). New fixtures now use 2028, keeping the existing 2026 inventory isolated. The corrective code head `b622787` passed full online run `41069786816` (job `124110547778`): `Passed! - Failed: 0, Passed: 116, Skipped: 0, Total: 116`; gateway contract `tests 3 / pass 3 / fail 0`; frontend `9 passed` files / `31 passed` tests; TypeScript and Vite production build passed. Image build/deploy jobs were skipped as intended for a PR. This final documentation commit records those results without changing the tested code; its ordinary CI rerun is tracked by T3. No merge or deployment occurred.

**Validation:** prior to the owner's online-only correction, `dotnet build` passed with 0 warnings/errors, `dotnet test --filter 'FullyQualifiedName!~ApiTests'` passed 83/83, `npx tsc -b` passed and `npx vitest run` passed 9 files / 31 tests. Initial full `dotnet test --no-build` failed at fixture initialization: 32 failures / 83 passed, `Failed to connect to 127.0.0.1:5432` (Postgres absent), not API behavior failures. Docker reports `hasNoVirtualization:true`. Native test-tool downloads/extraction under `%TEMP%/drivealt-kind-test-tools` were stopped at the owner's instruction; no local database/server was initialized. Do not continue local test setup or run local tests. Full backend/API, frontend and build validation must run in the PR's GitHub Actions workflow (same method as PR #27/#29). Latest regression addition is not locally rerun. Full code validation now passed online as recorded above. `git diff --check` passed. No merge/deploy/live model check has occurred.

**Resume verification, 2026-10-30 (Codex):** fetched main contains `1e13a87` (exit 0); latest first-parent code commit and live `DRIVEALT_IMAGE` both equal `083b6d1ef7cb1269b05a8d1e6081ec2a64be70fc`. Read-only SSH: public health `{"ok":true}`, gateway `{"signedIn":true}`, gateway hash prefix `ef0e322e17be2943`. SQL: 400 ready / 2 reading / 1 failed files; 9,666 extracted pages; 484 done / 2 held queued / 1 failed jobs; zero paused jobs; zero unread Semester 1 pages excluding ITS; 2,684 question regions; messages has `more_commentary`. Latest pause-log count unknown (not returned). Local Zarqa ancestor check passed. GitHub connector open-PR search returned #21 (overlapping chat, separate ownership), #44 (Drive UI), #45 (AI docs). `gh` remains unauthenticated. This branch includes #45's docs commit; merge #45 first. Implementation is prepared for online CI; production is unchanged.

**Inspect as a folder tree; unfinished uploads first (Claude Code, 2026-10-30, branch `claude/inspect-tree`; written for after it merges and deploys):** owner: *"inspect page should use the same searching style"* and *"this upload thing need to put the files yet to done up"*. Inspect's file list now opens at **Year 2** with breadcrumbs: Semester › (Semester 1 group: AI / ALL / CS & CYS) › course › type › the file's own folders (`web/src/screens/Inspect.tsx`, `treePath`). The path is kept in `?path=` so Back works. Each folder shows its file count and how many have problems; searching lists matching files from anywhere with their path. `/api/uploads?all=true` now returns up to 10,000 files (was the latest 200, which hid older files from Inspect). Upload's list shows files that aren't `ready` (waiting, processing, failed) above finished ones. Tests: `src/screens/InspectTree.test.tsx` (3 new), frontend 30/30, backend 86/86.

**Drive sync panel as a folder tree (Claude Code, 2026-10-30, branch `claude/drive-sync-tree`; written for after it merges and deploys):** owner: *"the way i navigate between them is stupid … it should be year 2 -> (sem1->etc, sem2->etc) and every folders have the files with a status of uploaded and not uploaded"*. Admin → Google Drive sync now opens at **Year 2** with breadcrumbs; each folder shows its own switch, its subfolders (each with a switch) and its files listed live from Drive, each marked **Uploaded** (links to the app file), **Not uploaded** or **Skipped type** (videos, ZIPs, GoodNotes), with "N of M uploaded" and an **All / Not uploaded** filter (owner: *"make a no ulpaded filter in the resync section"*, branch `claude/sync-not-uploaded-filter`; it stays on while you move between folders and hides uploaded and skipped-type files). The search box still lists matching folders anywhere; clicking one opens it. New endpoint `GET /api/admin/drive-sync/folders/{id}/files` (uploaders only, 20 s Drive listing timeout, 502 with a safe message on failure): uploaded = the Drive ID is in `drive_sync_items` or in a file's `source_ref` (`gdrive:id1+id2`), **or** (owner: *"uploadd should be show if there is a file that match it in our drive"*, branch `claude/drive-sync-match`) the app has a file whose title or original name matches by `Heuristics.CanonicalTitle` (extension, "Copy of", "(1)" removed) in the course `Heuristics.FromPath` gets from the folder path (any course if none). Phone-photo names (`LooksLikePhoto`) never match by name. A file renamed completely still shows as Not uploaded. Tests: backend 86/86 (new `Folder_files_are_listed_with_their_upload_status`), frontend 27/27, `CHROME=/opt/pw-browsers/chromium node e2e/drive-sync.mjs` passes desktop and phone with a stand-in API.

**Review fixes (Claude Code, 2026-10-30, branch `claude/review-fixes`; written for after it merges and deploys):** a code review of everything merged in PRs #25–#38 found 10 issues; all are fixed here.
- **Terra gateway lives in Zarqa's repo now:** Zarqa PR #7 (`claude/gateway-terra`) puts DriveAlt's Terra patch into Zarqa's `deploy/codex/gateway.mjs`, byte-identical (sha256 `ef0e322e…`, the "after" hash in `deploy/ensure-terra-gateway.sh`), and sets `pids_limit: 1024` to match live. Before it, a Zarqa `deploy.sh` would have restored the 4-image gateway, every 8-page Terra batch would have got 400, and every DriveAlt deploy would then stop at "Gateway source differs from the reviewed baseline". **The owner merges Zarqa PR #7 and pulls it into local `C:/Projects/LostraAi` before any Zarqa deploy** (deploy.sh uploads the local tree). `ensure-terra-gateway.sh` stays as a no-op check.
- **Drive sync (`Import/DriveSync.cs`):** a failing item now fails on its own; the rest of the folder still imports, and the folder shows "Some files in this folder couldn't be imported…". HttpClient timeouts no longer end the whole scan. New photos are grouped 40 per file (`PhotosPerFile`), so the joined `source_ref` stays under Postgres's ~2.7 KB index row limit.
- **Chat (`Chat/ChatService.cs`, `ChatEndpoints.cs`):** missing pre-drafted "show more"/option lines no longer fail the turn; the missing line is written by the model when clicked. Blank option labels are dropped. A model HTTP timeout now streams the "try again" error instead of breaking the stream. Still no server text filter (owner's 2026-10-28 decision).
- **Terra review flags (`Processing/ProcessingService.cs`):** a flagged page now keeps Terra's boxes/text and the file finishes. The page carries `ProcessingService.ReviewFlag`, shown in Inspect ("AI hiccup: Flagged for review…"); saving the page clears it. This **replaces** "review flags leave pages unread"; `MaterialReviewRequiredException` is gone. Pages flagged under the old code are still unread and get re-read on Retry.
- **AI routing (`Ai/MaterialAi.cs`):** upload suggestions use `Codex:Model` (`gpt-6.1-sol`) again, as `Options.cs` says, not the Terra batch model. The unreachable Codex single-page extraction branch, `CodexExtractSchema` and `PixelBoxInstructions` are removed; the answers-rule prompt test now checks `ExtractInstructions` and `BatchInstructions`.
- **Upload duplicate check:** the browser hashes one file at a time and skips files over 64 MB (`Upload.tsx`, the server still dedupes exact copies); `/api/uploads/check` loads titles only and canonicalises once per file (`FileEndpoints.cs`).
- **Removed dead UI:** `FileView.tsx`'s PDF.js fallback report button, which could never render.
- **Tests (2026-10-30, cloud session, local Postgres 16 + pgvector):** `dotnet test` → `Passed! - Failed: 0, Passed: 85, Skipped: 0, Total: 85` (3 runs in a row), including new `One_failing_drive_file_does_not_hide_the_rest_of_its_folder` (confirmed failing on the old `DriveSync.cs`), `Incomplete_clarification_drafts_degrade_instead_of_failing_the_turn` and `Flagged_Terra_pages_keep_their_boxes_and_do_not_stop_the_file`. `cd web && npx tsc -b` OK, `npx vitest run` → `8 passed` files / `26 passed` tests, Vite build OK. Zarqa: `node --test deploy/codex/gateway.test.mjs deploy/codex/gateway.terra.test.mjs` → `pass 12, fail 0`. **Not tested:** real DeepSeek/Terra behaviour (no keys in the cloud session).

**Measured fast-pass prototypes, not import readiness (2026-10-29):** non-Calculus measured 105 pages in 60.08 seconds, one Terra-low call, 47 flagged / 58 unreviewed candidates (`workers/year1_other_native/fast-benchmark/benchmark.json`). Calculus convenience sample measured 101 pages in 61.53 seconds, 63 flagged / 38 candidates. A distributed Calculus sample then measured 104 pages in 52.19 seconds: 1.86 preparation, 47.20 one model call, 3.12 assembly/render/structural validation; 13 flagged / 91 unreviewed candidates. These imply roughly 1.5–2 hours for a similar automatic PDF pass across the two uneven partitions, not finished QA/upload time. Parent identified a mixed question/inline-answer line selected in a Calculus prototype (source hash `04cbf45f7645e8abab234a2b22f2b0a93fd1346a8f36e99eaee9a70e6897b753`, page 1, question II; answer content intentionally not reproduced). Whole-line selection cannot safely split mixed lines. Worker reports quarantining generated text from both benchmark sets, pilots and 173 earlier native worker pages, preserving originals/renders/metadata; span-based extraction correction is required. No output from those prototypes is ready for import, no final random sample has run, and a full completion ETA is still unknown. Other-native worker's five sample checks also found incomplete text on a flagged dense notes page. Do not use unverified candidate throughput as reviewed completion throughput.

**Year 2 workflow comparison and ETA retraction (2026-10-29):** owner challenged the overnight estimate against the earlier 12-minute Year 2 run. Parent read `C:/Users/abdul/DriveAlt-local/runs/2026-10-28-year2-remainder/scripts/segment-tl.mjs`: typed pages used deterministic PDF text/geometry segmentation without a model; the Terra import packager covered 576 visual results across three shards. The new Year 1 prototype unnecessarily adds two CLI model calls per at-most-20-page text batch. The 6–8-hour projection therefore describes that inefficient prototype and is withdrawn as the completion estimate. All workers were instructed to reuse the established native geometry/extraction path, retain slide text, and use larger bounded single-pass Terra source-line-ID batches for necessary question/answer separation or ambiguity, plus eight-image visual batches. Do not copy the old segmenter's unsafe assumption that answers always have explicit headings, and do not repeat the old empty-slide-text defect. Benchmark the corrected complete workflow before publishing another ETA; no 12-minute Year 1 completion promise is established.

**Native pipeline correction in progress (2026-10-29):** parent inspected `native-driver.py`: the prototype explicitly excluded all question wording and automatically marked question pages for review. At the measured snapshot Calculus had 133 outputs/132 flagged, other courses 51/45 flagged; driver creation-to-checkpoint rates were approximately 30 and 16.5 pages/minute. Extrapolating those short samples gives roughly 6–8 hours for the selection pass alone, not complete extraction or website readiness. Workers were directed to pause that flag-heavy pass and implement actual visible question-stem grouping with PDF geometry/crops, retaining all original choices/answers in the crop images, and restricting review to genuine uncertainty. Calculus acknowledged a safe pause and is preparing a representative pilot; coordinator is coordinating the same correction for the other partition. A reliable end-to-end ETA remains unknown until that corrected workflow is tested. Previously saved outputs remain preserved; no random final sample or import has occurred.

**Year 1 parallel workers (2026-10-29):** owner requested five additional subagents. The session permits four contexts including the parent, so only three subagents can operate simultaneously. `/root/terra_year1_resume` now owns standalone photos, legacy visual-result repair, sparse/scanned-page queues and final canonical reconciliation. `/root/year1_calculus_native` owns PDF/conversion pages with any original source path containing `/calculus/`, writing only `workers/year1_calculus_native/`. An attempted second new spawn hit `agent thread limit reached` because the old `/root/terra_batch8` was pending initialization; its old test was interrupted and that Terra worker was reassigned to all other PDF/conversion pages, writing only `workers/year1_other_native/`. No standalone images belong to either native partition. Native workers use separate queue/output checkpoints and sequential Terra-low CLI requests; no paid fallback. Calculus reports 11,783 content/page items and an active native driver. Phase-one selection/flags are not full extraction completion. This replaces the earlier single-worker arrangement.

**Owner's current completion/import gate (2026-10-29):** finish all Year 1 preparation, then independently inspect **15 randomly selected pages**, then upload only if all 15 pass. This supersedes earlier prompt-only/local-only authorization for the parent: the Terra worker remains local-only, while the parent may perform the website import after the gate passes and correct Year 1 mapping is prepared. Do not select the sample before the finished corpus is frozen, substitute hand-picked pages, interpret inspected/flagged pages as finished, or rerun a failed draw until convenient pages pass. Fix the failure and its affected category before a fresh recorded random check. Ordinary structural/completeness checks cover all artifacts before sampling; original answers stay visible, and no answer text is written. No import has occurred and the 15-page gate has not run.

**Year 1 restart and workload correction (2026-10-29):** `collaboration.list_agents` returned only the parent; the former `/root/terra_year1` worker was no longer active. The owner requested restarting Terra and a time estimate. Spawned `/root/terra_year1_resume` explicitly with `model=gpt-6.1-terra`, `reasoning_effort=low`, `fork_turns=none`; the worker acknowledged resuming the existing run. This supersedes the older active-agent/session claims below. Local `worklist.json` has 20,175 source-page/image entries; `deduplicated-worklist.json` has 19,602 content/page entries, 117 inspected (87 flagged) and 19,485 pending. These are **not all scanned images**. Worklist representations contain 178 standalone photos/images: 131 source images, 13 sniffed images and 34 decoded HEIC representations. Joining full Drive ID/page to saved `batch-*.json` gives 34 inspected photos (all flagged), 144 unreviewed. These are worklist entries before photo-content deduplication. Parent previously mislabeled the PDF-inclusive total as images requiring visual review; that statement was incorrect. The resumed worker is auditing native text versus scans and prioritizing pending photos in batches of up to eight. Native assessment/solution text remains an aid, never blindly indexed. No finished-all-material ETA is verified yet.

**Year 1 audit follow-up:** Calculus accounts for 11,806 source pages across 155 files, including 11,469 under `Slides & ex with solution` (2023/2024 collections). File-hash deduplication leaves 11,783 Calculus pages. Worker independently verified PDF page counts of 808, 563, 547, 539 and two 519-page files; `calculus-lecture-audit.json` records extraction comparisons, which do not establish visual duplication. `workload-audit.json` is now saved. A 24-PDF sample found extractable text on 69 of 70 sampled pages; this does not prove answer safety. Initial resumed photo batches were mostly marked incomplete; parent instructed the worker to finish legible question wording/crops rather than equate viewing or blanket flags with extraction. Timed view/flag batches are unsuitable for a finished-material ETA.

**Resume verification for this restart:** feature ancestor and local Zarqa Caddy ancestor checks returned exit 0; last main code SHA remains `69211bd0ad695df8806da3ba512319fd81c01252`; GitHub connector open-PR search returned only #21. Public health HTTP 200 `{"ok":true}`. Read-only SSH succeeded: live image has that SHA and gateway hash prefix is `ef0e322e17be2943`. No VPS writes or Year 1 import occurred. App year/course mapping is still required: `Data/Seeder.cs` loads `year2.json`, and `Import/DriveImporter.cs:133-161` maps into existing courses.

**Deployment verified (Codex, 2026-10-29):** live image `69211bd0ad695df8806da3ba512319fd81c01252` is PR #37's corrective merge, containing PR #34's combined feature/fix rollout. PR #37 CI run `40878587154` passed, including the byte-exact gateway patch regression. Main run `40878875382` passed test (`123464728673`), image build (`123464728796`) and deploy (`123465524352`). Deploy logs: `Terra low readiness passed` at 13:10:45 UTC; `Terra batch gateway installed; existing models and process limit preserved`; `DriveAlt is up` at 13:11:50; Caddy already routes the host; public URL is up. Independent public checks: health HTTP 200 `{"ok":true}`, home HTTP 200 with `Cache-Control: no-cache`, `/assets/index-CYLOzs77.js` and `/assets/index-BJ3aP7vm.css`; new JS returned 200 and contains both `Drive folder scan progress` and the one-time recovery marker. Full tests: 84 backend / 26 frontend; TypeScript/Vite/Bash validation and 3 real-patch mocked HTTP/parser contracts pass. Prior failed run `40842751426` stopped before server writes because of a Windows CRLF checksum; PR #37 corrects LF handling and verifies exact patch bytes in CI.

**Live behavior:** Codex-enabled uploads and Drive imports use up to eight `gpt-6.1-terra` images per low-effort request, with PDF text as an aid, page-order/count/dimension validation, generous pixel crops and batch checkpoints. Readable slide text is retained even without regions. Review flags keep Terra's result and mark the page for Inspect (since `claude/review-fixes`; before that they left pages unread); failed-file Retry resumes `/process` and preserves completed/corrected pages. Gemini embeddings/chat stay unchanged. Folder listing concurrency is six, with a 20-second timeout per listing; live progress displays checked/known folders and imported groups. FileView loads eagerly, HTML revalidates, and other dynamic-import failures get one reload per build and a readable error screen. Existing old browser tabs may need one hard refresh to load this recovery code.

**Gateway now updated:** reviewed source hash is `ef0e322e17be2943b78e1fe65abb919b6d64bbb7dbf698d6fb379d884a49aef3`. The installer backs up source, preserves existing model allowances plus Terra and the live PID limit in `/opt/zarqa/deploy/drivealt-terra.override.yml`, rebuilds only codex and checks a tiny Terra-low request through the existing subscription. Future matching DriveAlt deploys skip its restart. No auth files/volume were opened, and no new API key/billing configuration was added. A later Zarqa deployment must retain this patched source and override; otherwise it can restore the old gateway/limits. The sibling local Zarqa checkout was not changed in this task.

**Year 1 external preparation is active:** the owner explicitly requested a Terra-low subagent after receiving a stopped inventory report. Agent `/root/terra_year1` is continuing `C:/Users/abdul/DriveAlt-Year1-local` from the provided checkpoints under `C:/Users/abdul/Documents/Codex/2026-10-29/files-pasted-by-the-user-terra/outputs/`. Owner's checkpoint identifies Year 1 folder `15a6VJ-R81lU8Ko8zv2ejS_SvegL1LtsQ`; 1,005 entries / 211 folders remain provisional, with listing completeness and exact historical folder names unverified. Agent reported a repaired three-worker downloader with retries, integrity checks, local hashes and atomic checkpoints. Reported snapshots: 63 verified downloads at an earlier checkpoint; the agent later reached ten inspected pages across six atomic batches with typed-assessment text/crops and explicit handwriting/cross-page review flags. Counts continue changing. Work is ongoing, not complete. Run directory: `C:/Users/abdul/DriveAlt-Year1-local/runs/2026-10-29-terra-continuation`; `download-manifest.json` is the live atomic download checkpoint, `worklist.json` / `validation-report.json` are timestamped snapshots, batches are `batch-*.json`, render/crop directories are `images/` / `crops/`. Downloader script `download-validated.py` is running in exec session `92837` (owned by the Terra subagent). The local prompt is `C:/Users/abdul/DriveAlt-local/YEAR1-TERRA-PROMPT.md`. No Year 1 import, course remapping, VPS operation or historical held-job release is authorized by this preparation. Keep the laptop awake for this local worker; the server deployment itself no longer needs it.

**Limits and older data:** full live document-processing throughput/quality, authenticated post-deploy UI flows and refreshed production SQL counts are still unknown. Last pre-PR #32 SQL: 400 ready / 2 reading / 1 failed files, 9,666 extracted / 7 unread pages, jobs 484 done / 2 queued / 1 failed, 2 enabled / 118 Drive folders. The owner's latest screenshot shows master sync on; Codex did not alter switches or release held jobs. The 322 historical empty-text Terra slide pages and 42 producer flags remain separate remediation work. This rollout does not reprocess those papers.
<details>
<summary>Earlier snapshots and implementation evidence (historical; superseded by the current rollout status above)</summary>


**Retry UI correction before rollout (Codex, 2026-10-29):** failed-file Retry previously called destructive `/reprocess`, which would discard completed batches and uploader fixes. It now calls resumable `/process`, and failed files with pages expose Inspect. A new frontend regression covers both. TypeScript and frontend 8 suites / 26 tests pass. Combined CI also passed on `1e13a87` (run `40834631479`, test job `123316419310`); this final UI change needs its own CI pass before the already-authorized merge. No production changes yet.

**Final local verification before authorized rollout (Codex, 2026-10-29):** combined TypeScript/Vite build passed; frontend 8 suites / 25 tests passed. `BASE_URL=http://127.0.0.1:5184 node e2e/drive-sync.mjs` passed desktop/phone with the live 1-of-3 progress bar; screenshots inspected (`drivealt-sync-NRdBMg` in local temp). Its wait spans the five-second polling boundary rather than racing it. `e2e/features-567.mjs` also passed both viewports (`drivealt-567-4syYAg` in local temp). Original independent CI already passed for all three feature heads; final combined CI must pass before PR #34 merges. PRs #35/#36 are now merged into the parent feature branch and closed; only PR #34 targets main. No production mutation has happened yet.

**Combined owner-approved rollout (2026-10-29, Codex, PR #34):** integrated PR #35 Terra-low processing and PR #36 retired-script recovery into `codex/drive-scan-speed`, then added the owner's accessible live progress bar. All individual PR CI runs passed; final combined CI is next. Deployment now prepares/verifies the shared gateway before starting the new app, using exact before/after source hashes, existing-model/PID-limit preservation, source/image rollback and a tiny subscription-backed Terra-low readiness check. It rebuilds only codex and the DriveAlt app. A gateway mismatch or failed readiness aborts before app deployment; no auth file/volume is opened. Existing source material and held jobs are untouched. The current live image remains `56bd76b` until this rollout succeeds. Gateway override lives at `/opt/zarqa/deploy/drivealt-terra.override.yml`; later Zarqa deployments must retain its model/PID settings and the patched source (the old gateway drift thread remains relevant).

**Visible Drive progress (Codex, 2026-10-29):** owner explicitly requested a progress line/bar. The panel now shows an accessible progress bar with “N of M known folders checked · K file groups imported”, polling every five seconds. Queued scans use an indeterminate bar; disabled/unavailable historic folders are excluded from the denominator. Folder failures count as attempted checks while keeping their safe retryable error. TypeScript and frontend 7 suites / 22 tests pass. Earlier PR #34 CI passed (`40830064132`); new progress CI pending. Owner said “deepploy” for all prepared fixes, including the necessary shared-codex gateway update; consolidate the tested branches for one production rollout while preserving original files, secrets, unrelated apps and held jobs.

**2026-10-29 deployment authorized (Codex):** owner said “deepploy” after all three fixes were prepared. PR #34 full CI passed (`40830064132`); Terra PR #35 full CI passed (`40831970386`); browser PR #36 full CI passed (`40832014678`). Added an idempotent gateway preparation step to the existing GitHub deployment because campus SSH is refused. It verifies the known before/after hashes, tests the patch contract on the runner, backs up the current gateway, preserves the existing model allow-list and live PID limit in a Compose override, rebuilds only codex, and runs a tiny Terra-low readiness check through the existing subscription. On verification failure it restores the previous source/image. Later deployments skip the restart when the code/model allowance already match. Local Bash syntax and 3 mocked HTTP/parser contract tests pass. No VPS mutation has run yet; a new CI pass is pending for this deployment support. Deployment is authorized, including the required gateway update; no need to request that authorization again.

**Terra workflow implementation (Codex, 2026-10-29, not deployed):** `codex/terra-upload-batches` adds up-to-eight-image Terra-low batches to the Codex-enabled processing path, using existing PDF text as a transcription aid, pixel coordinates validated against each actual image, page-order/count validation, compact nonduplicated model text, model/effort/dimension provenance and per-batch checkpoints. All page results are structurally validated before any batch is applied. Slide/notes text is retained without requiring question regions. Quota/login/configuration errors pause; other errors do not trigger offline extraction in the batch path. Producer review flags leave pages unread, expose them in Inspect, and stop retries until an uploader edits them; retry then reuses corrected/completed pages. No schema/vector model change or historical reprocessing. Focused backend tests pass 44/44; two new scripted-DB lifecycle tests await CI. A reviewable `deploy/codex-terra-batches.patch` upgrades the shared gateway from four to eight images, enables validated request-specific reasoning, and adds Terra to its default allow-list. Patch applies to a temporary copy of the unchanged gateway SHA256 `5da0c724e36f45626f438728030eb357e02c7a45163425c8ad9562bbcc3f607f`; 11 gateway tests pass there with the explicit model allow-list including Terra. Actual VPS Terra access and latency remain unknown; no model call was made. Do not deploy the app before the gateway patch/configuration is approved and applied to only the codex service.

**Drive scan fix PR #34:** full CI run `40830064132` passed (test job `123301975566`); not merged/deployed. Root workspace retains unrelated untracked `external-prep/` untouched. A separate managed worktree `stale-chunk-recovery` holds the urgent browser-script recovery fix, based on the same PR #34 so its handoff must merge after that PR and preserve both sets of facts.

**Retired script recovery fix (Codex, 2026-10-29, not deployed):** owner screenshot requests `/assets/FileView-D_WLom67.js`; independent HEAD request returns HTTP 404 while `/` and health are 200. The browser still has an older bundle referring to a retired lazy module. Immediate workaround is Ctrl+Shift+R. `codex/stale-chunk-recovery` loads the now-small FileView component eagerly (PDF.js remains dynamic), revalidates HTML with Cache-Control no-cache, handles Vite preload errors with a once-per-build reload, and supplies a friendly route error/reload screen for persistent failures. Automatic reload is bounded using sessionStorage; blocked storage leaves the error visible instead of looping. Frontend TypeScript, 8 suites / 24 tests and Vite production build pass; 33 selected backend unit tests pass after package restore. Desktop/phone `e2e/features-567.mjs` passes on localhost 5183. Build emits no separate FileView chunk (main JS 496.19 kB / 150.10 kB gzip). No deploy or VPS change. Full CI pending. PR #34 scan fix already passed full CI (`40830064132`); merge this fix after #34 and retain the parallel Terra facts when resolving handoff conflicts.

**Drive scan performance fix, 2026-10-29 (Codex, `codex/drive-scan-speed`, not deployed):** scanner previously listed all folders serially using the import HTTP client's ten-minute timeout (`DriveSync.cs` and `DriveImporter.ListFolderAsync`). It now lists at most six folders concurrently with a 20-second timeout per listing, keeping all EF operations, downloads, current-switch checks and import commits serial. One slow folder produces its existing safe retryable error and does not prevent siblings being checked. The panel reports checked/known folders and imported groups while running. Unit tests verify overlap/bound/order, slow-folder isolation and scan cancellation (3/3 pass); frontend suite 7 files / 21 tests and TypeScript pass. Full DB CI pending. No live scan or import was triggered; production remains PR #32. PR #33 docs reconciliation is already merged (`60090bf`), contrary to the older pending language below.

**PR #32 deployed, 2026-10-29 (Codex):** owner-authorized merge `56bd76b585e6540bdd25645760b2a640dc37efa0` contains feature/fix commit `18a6b605ec768a54c83fde6583e90db0f7afb2f5`. PR CI run `40826739134` passed; `dotnet test` output: `Failed: 0, Passed: 67, Skipped: 0, Total: 67`; frontend `7 passed` suites / `20 passed` tests, TypeScript and production build passed. Main run `40826986882` passed test (`123292178528`), image build (`123292178200`) and deploy (`123292920754`). Independent public requests returned `health HTTP 200 {"ok":true}`, `home HTTP 200` and `/assets/index-CvtCAUQw.js` (new tested bundle). Local desktop/phone mocked-API smoke and screenshot inspection passed. Direct SSH is refused from the current network; post-deploy authenticated production interactions, SQL counts and an independent `docker inspect` remain **unknown**. No new migration was needed, no source material was imported or requeued, no paid AI call was made in testing. This docs-only reconciliation must merge with `[skip ci]`; live image stays `56bd76b`.

**Historical preparation evidence before PR #32 merge (superseded by deployment result above):** owner requested “so you ficed every thinf ? then deploy”. The initial review had not fixed code; both reader regressions are now fixed on `codex/reader-fixes-deploy`, carrying Antigravity's uncommitted feature work without pushing to its branch. `/api/files/{id}` projects user-scoped bookmark status for pages and question regions in the existing detail request; reader cards use that state. The reader is keyed by route file ID so topic/PDF/toggle/select state resets across file navigation. No schema migration or extra model call is needed. `npx tsc -b` and Vite build passed; `npm test -- --run` passed 7 suites / 20 tests, including saved-question unsave, saved-page zoom, and switching to an untagged file. Backend unit filter (Codex/DeepSeek/Mapping/Sniff) passed 36/36 with a successful rebuild. Desktop and phone `e2e/features-567.mjs` passed including saved-state checks; screenshots were inspected. New database integration test verifies per-user bookmark isolation and independent page/question saved state; execution awaits PR CI (local DB unavailable). `git diff --check` passed. Public health returned HTTP 200; SSH resume reads were refused by the network, so the older production SQL/image snapshot below was not refreshed. Deployment is pending, not yet claimed successful.

**Features #5, #6 and #7 completed on `feat/reader-share-dupcheck`, 2026-10-29 (Antigravity):**
- **#5 Instant Page-Strip Reader (`web/src/screens/FileView.tsx`, `src/DriveAlt.Api/Files/FileEndpoints.cs`):** `GET /api/files/{id}` includes question region boxes (`PageRegionDto`) on each page thumb. `FileView.tsx` renders pre-rendered page images (`/api/media/pages/{pageId}?v=...`) with a sticky Jump-to-page selector, `Show/Hide questions (N)` toggle, topic filter chips, clickable `.doc-qbox` overlays that open `Viewer`, and per-page Zoom / Copy link / Report buttons. `pdfjs-dist` is dynamically imported only inside `PdfDoc` when `pages.length === 0`, reducing `FileView.js` to `9.87 kB` (`3.57 kB` gzip). `FileEndpoints.ServeImage` (`MediaCacheControl`) sets `Cache-Control: private, max-age=604800, immutable` on completed versioned media (`?v=`) and `Cache-Control: private, no-cache` whenever a temporary fallback image is served so fallbacks always revalidate once the crop/thumb is generated.
- **#6 Deep-Link Sharing (`src/DriveAlt.Api/Browse/BrowseEndpoints.cs`, `web/src/lib/app.tsx`, `web/src/ui/Viewer.tsx`, `web/src/ui/ResultCard.tsx`):** `GET /api/cards?refs=...` hydrates up to 24 `r:` / `p:` refs. `AppProvider` watches `?ref=` and hydrates/opens `Viewer` once authenticated (`openedRef` is marked only after the async request resolves, surviving React `StrictMode` double-invocation and login/session refresh), stripping `?ref=` on close. `Viewer` and `ResultCard` include `Copy link` buttons, and slide/page cards use `thumbUrl` for card previews.
- **#7 Pre-Upload Duplicate Detection (`src/DriveAlt.Api/Files/Heuristics.cs`, `src/DriveAlt.Api/Files/FileEndpoints.cs`, `web/src/screens/Upload.tsx`):** `POST /api/uploads/check` checks client-computed `SHA-256` hashes (`exact`) and `Heuristics.CanonicalTitle` (`similar`). `CanonicalTitle` strips only known file extensions (preserving decimal chapter numbers like `Chapter 2.1 - Introduction to Trees` on extensionless titles) and strips trailing `(N)` copy suffixes only when not preceded by an assessment/lecture/part noun (`DS Quiz (1)` and `DS Quiz (2)` stay distinct). Exact matches are skipped before uploading bytes; fuzzy matches show a non-blocking warning card with `View existing`, `Upload anyway`, and `Skip`.
- **Validation (2026-10-29, Antigravity):**
  - `dotnet build DriveAlt.slnx --no-restore` → `Build succeeded. 0 Warning(s), 0 Error(s)`.
  - `dotnet test tests/DriveAlt.Api.Tests --no-build --filter "FullyQualifiedName~CodexTests|FullyQualifiedName~DeepSeekTests|FullyQualifiedName~MappingTests|FullyQualifiedName~SniffTests"` → `Passed! - Failed: 0, Passed: 36, Skipped: 0, Total: 36`.
  - `cd web && npx tsc -b` → passed (0 errors).
  - `cd web && npx vitest run --pool=threads` → `7 passed (7)` suites, `20 passed (20)` tests (including `Features567.test.tsx` covering `StrictMode` + login refresh for `?ref=`, `FileView` reader/question boxes, and `Upload` exact/similar preflight).
  - `cd web && npx vite build --outDir ../.ci-web` → passed (`FileView` chunk `9.87 kB` / `3.57 kB` gzip).
  - Playwright desktop (`1440x1000`) and phone (`390x844`) stand-in API checks (`node e2e/features-567.mjs` and `node e2e/reports.mjs`) → passed with zero page errors and zero horizontal overflow.

**Current reconciliation, 2026-10-29 (Codex, read-only review):** the live image is `bd3366c97c9740719509ca9fa69ec7ecf8bed48b` (PR #31), and public `/api/health` returned HTTP 200. Read-only SQL returned `9666 extracted / 7 unread`, `400 ready / 2 reading / 1 failed`, jobs `484 done / 2 queued / 1 failed`, and `2 enabled / 118` Drive sync folders. These supersede older 2026-10-28 status counts below. On 2026-10-28, following the owner's instruction to check five and deploy, the external Terra run imported 576 crop pages across 63 files; 12 fully read files were queued to finish and 42 producer-flagged pages were included. Current `/data/terra-low-20261028` holds the imported package. The five inspected samples are not a comprehensive quality review. The import supplies empty page text for pages with no question regions: 322 of its 479 slide results have no regions, so their slide content was not transcribed. Not every page has an embedding.

**Drive sync deployed (2026-10-28, Codex):** owner explicitly requested deployment. PR #29 merged as `0e44df7a71001d7c02553303672c2349d687ab9a`; run `40662469965` passed test (`122728624891`), build (`122728624606`) and deploy (`122729967049`). Normal public `curl /api/health` returned `{`"ok`":true}`, `HTTP=200 ip=173.249.40.122`. In the authenticated production uploader page `/admin`, Google Drive sync loaded successfully with master switch off, Discover folders available, no discovered folders, and no completed scan. Admin stats still show 212 ready, 190 processing, one failed, 9,673 pages and 1,105 question crops. No discovery, import, paid AI call or held-job release was triggered by Codex. Direct post-deploy SQL/image inspection remains unknown because SSH is refused from the campus network. The successful live settings response verifies the new sync schema is accessible.

**DNS-only, 2026-10-28 (Codex, owner approved):** repeated campus failures returned curl exit 7 against Cloudflare IPs while origin HTTPS returned 200. Owner accepted slower photos and authorized DNS-only; the Cloudflare UI now shows only `drivealt.tryzarqa.com` set to DNS only, A `173.249.40.122`. After propagation, `Resolve-DnsName` returned that IP and normal `/api/health` returned `HTTP=200 ip=173.249.40.122`. No Caddy/server change was needed. Screenshot: local temp `drivealt-dns-only-confirmed.png`. Historical notes about proxying below are superseded by this state.

**Deployed (2026-10-28, Codex):** Owner authorized making the reports and chat changes live. PR #25 merged as `99739d0` with `[skip ci]`, PR #26 as `fbeeaeb`, and PR #27 as `068f0b9f7936984d64a19c73ea11063e5ea0dd6d`. GitHub run `40657842044` passed test, build and deploy; deploy logs at 08:39 UTC report `DriveAlt is up`, `Caddy already routes drivealt.tryzarqa.com`, and `https://drivealt.tryzarqa.com is up`. Its image tag is the merge SHA. An origin health request using `curl --resolve drivealt.tryzarqa.com:443:173.249.40.122 https://drivealt.tryzarqa.com/api/health` returned HTTP 200 and `{`"ok`":true}`. On the university network, Cloudflare IPs `188.114.96.6` and `188.114.97.6` refused TCP 443; the owner confirmed the university network was refusing it. SSH port 22 was also refused from this network, so direct post-deploy schema, running-image and held-job checks are **unknown**; verify later from an unrestricted network. The earlier PR #26 run `40652632065` was cancelled before deploy after discovery that its workflow deleted old images and pruned Docker images despite the owner-approval rule. PR #27 removed that cleanup and `--remove-orphans`. PR #21 remains open and belongs to Claude Code.

**Chat feature, deployed in PR #27:** DeepSeek chooses whole-file (`f:`), question-crop (`r:`), or page (`p:`) results; a full-file card opens/downloads the original file. File searches can use already-available originals from held courses without releasing processing jobs. The model writes all conversational replies; the server no longer substitutes canned refusal/search/option lines or rewrites the model's wording. The model prompt still forbids writing, explaining, hinting at or grading answers and permits showing original images intact. The owner explicitly chose to trust DeepSeek instead of server text filtering; this makes compliance depend on the model, rather than the removed `Guardrails.cs` filter. The historical imported-text risk remains open below.

For new replies, the model prepares follow-up text for held-back batches and clarification options in its initial tool response; `messages.more_commentary` and the options JSON store those lines. Clicking an option or “Show more” uses the prepared text without a new model call. If the model omits needed drafts, they are generated before the initial message is shown; older messages without drafts may need one model call on the first click. AI failures are streamed as an error to the UI, not presented as a canned bot answer. Migration `20261028072124_PredraftChatReplies` adds only the new text-array column. GitHub deployment passed; direct post-deploy database verification remains unknown because the university network refuses SSH.

**Chat validation (2026-10-28, Codex):** `dotnet build --no-restore` → `Build succeeded. 0 Warning(s), 0 Error(s)`; backend unit filter plus `FileSearchTests` → `Passed: 19, Failed: 0, Skipped: 0`; `cd web; npm test` → `5 passed` files / `14 passed` tests; `npx tsc -b` and Vite production build passed. `dotnet ef migrations has-pending-model-changes --project src/DriveAlt.Api --no-build` → `No changes have been made to the model since the last migration.` `node e2e/reports.mjs` and `node e2e/chat-files.mjs` passed desktop/phone checks using stand-in APIs; the file-card layout was visually checked. Local database/API tests are **unknown** because the local Docker daemon is unavailable. Full CI passed on `8c0f4ee` (run `40650875313`, job `122690387785`): `dotnet test` → `Failed: 0, Passed: 48, Skipped: 0, Total: 48`, including scripted-model/database cases; frontend `14 passed`, TypeScript and Vite build passed. Later documentation-only commits do not change the tested code. No paid AI call was made; actual live DeepSeek behavior/latency remains unverified.

**Report feature, deployed in PR #27:** `material_reports` stores student feedback separately from page text/search/AI. Viewer crops and original pages have a Report action; uploaders review open/resolved reports under Account → People & reports. Migration `20261028062003_MaterialReports` adds the report table/indexes/FKs only. No model change or re-embedding is needed. The owner authorized the merge and deploy; the migration has not been independently checked over SSH.

**Report validation (2026-10-28, Codex):** `dotnet build --no-restore` → `Build succeeded. 0 Warning(s), 0 Error(s)`; the prescribed unit-test filter → `Passed: 45, Failed: 0, Skipped: 0`; `cd web; npm test` → `3 passed` files / `11 passed` tests; `npx tsc -b` and `npx vite build --outDir ../.ci-web` passed. `dotnet ef migrations has-pending-model-changes --project src/DriveAlt.Api --no-build` → `No changes have been made to the model since the last migration.` `node e2e/reports.mjs` passed crop reporting, uploader review/resolution/history, original-page reporting and viewport overflow checks on desktop and phone against a stand-in API, with no page errors. Screenshots were visually inspected. A focus-restoration issue found by the frontend tests was fixed. Local database/API tests were not run because the local Docker daemon is unavailable. **Full CI passed** on report code head `8b8c553` (run `40645271789`, job `122672563097`): `Passed! - Failed: 0, Passed: 68, Skipped: 0, Total: 68`, including all three new report integration tests, and frontend `11 passed`; TypeScript/build also passed. Later documentation-only commits do not change that tested code.

**Pre-deployment verification, 2026-10-28 05:57 UTC** (Codex; read-only SSH and GitHub connector):
- **Full Year 2 inventory checked again at 06:03 UTC, on the owner's clarification:** SQL grouped by `courses.semester` returns Semester 1 `224 added / 210 ready / 13 reading / 1 failed / 6085 pages`; Semester 2 `179 added / 2 ready / 177 reading / 0 failed / 3588 pages`. `select count(*), count(distinct course_id), sum(page_count) from files` returns `403 / 14 / 9673`. All 403 files have a source reference. File kinds: `pdf 291`, `office 89`, `images 23`. This confirms material from both semesters has been added; exact completeness against the original Drive needs a source-manifest comparison and is not established by these counts alone.
- `git merge-base --is-ancestor b1461cc origin/main` exits 0; fetched main is `7c7fe49`. GitHub `search_prs`, query `repo:AbdulazizRashdi06/DriveAlt is:open`, returns only **#21**.
- `grep -o '^DRIVEALT_IMAGE=.*' ~/drivealt/.env` returns `ghcr.io/abdulazizrashdi06/drivealt:b1461cc1a18da2c1ce210a167d1f50d809dae821`, matching `git log -1 --first-parent --format=%H origin/main -- . ':(exclude)*.md'`.
- Public `/api/health` returns `{"ok":true}`; gateway `/health` returns `{"signedIn":true}`; gateway source SHA256 prefix remains `5da0c724e36f4562`.
- SQL: files `ready 212 / 5,501 pages`, `reading 190 / 4,172 pages`, `failed 1 / 0 pages`; pages `read_ok 5665`, `read_offline 0`; regions `question 1105` only.
- SQL: jobs `done 296`, `failed 1`, `queued 190`; all 190 queued jobs have `Held: owner wants Semester 1 without ITS only`; `paused_now 0`. `docker compose ... logs --since 24h --no-log-prefix app | grep -c 'AI paused'` returns `0` (grep exit 1 is expected for no matches).
- Semester 1 excluding `its`: Accounting 26 ready, Database Systems 56 ready + 1 failed, Data Structures 44 ready, Organisation & HR 10 ready, Probability & Statistics 41 ready, RPS 15 ready. SQL grouped by course finds **zero unread pages in every in-scope course**. The 4,008 unread pages are all in held courses.
- Import lifecycle markers still show start `2026-10-23T18:58:30Z`, download/queuing end `2026-10-23T19:17:03Z`, exit `1`; `tail -1 import.log` says `Done: 404 queued, 1 skipped, 2 failed. The server's workers will process them.` Download completion is separate from later processing completion. The exact time the last in-scope file finished and cumulative usage-limit blocking time remain **unknown**.
- `git -C C:/Projects/LostraAi merge-base --is-ancestor abebc56 HEAD` exits 0: the owner's local Zarqa checkout now contains the permanent Caddy fix. A subsequent Zarqa deploy preserving the route has **not been verified** here.
- No application tests were rerun for this Markdown-only correction. Historical test results below retain their original dates and scope. No VPS, DNS, Caddy, secrets or job state was changed.

**Historical evidence below is dated; the snapshot above is the current state.**

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
| #13 | merged `406db5a` with `[skip ci]` | `claude/handoff-reread` (Claude Code) | Gateway fix, re-read queued, usage limit. Docs only. |
| #14 | merged with `[skip ci]` | `claude/hold-sem2` (Claude Code) | Semester 2 and ITS held. Docs only. |
| #15 | merged with `[skip ci]` | `claude/cloudflare-proxy` (Claude Code) | DriveAlt behind the Cloudflare proxy. Docs only. |
| #16 | merged `2673a3d`, **deployed 2026-10-25 04:24–04:28 UTC** | `claude/searchable-while-rereading` (Claude Code) | Searchable while re-read; image fallbacks; questions for question requests; `import-drive --from-local`. |
| #17 | merged with `[skip ci]` | `claude/handoff-missing-files` (Claude Code) | Handoff after #16. Docs only. |
| #18 | merged `dfb1877`, **deployed**; the owner confirmed chat works | `claude/show-sent-message` (Claude Code) | Chat: the sent message shows at once in a new chat. |
| #19 | merged `6e212e0` with `[skip ci]` | `claude/handoff-after-18` (Claude Code) | This page after #18. Docs only. |
| #20 | merged `a920112`, deployed | `claude/conversational-bot` (Claude Code) | Bot asks instead of substituting; options and Show more; migration confirmed on the VPS. |
| **#21** | **open** | `claude/pick-fix-and-owner-login` (Claude Code) | Typed picks follow the named course; owner's fixed-code student login; login subtitle removed. Owner approval required to merge; code PR, so merge normally. |
| #22 | merged `b1461cc`, deployed | `claude/deploy-fixes-caddy` | Caddy recovery and deployment checks; current live image. |
| #23 | merged `6c74000`, docs only | `claude/handoff-zarqa-caddy` | Records deployed Caddy recovery. |
| #24 | merged `7c7fe49`, docs only | `claude/handoff-zarqa-caddy-done` | Records Zarqa PR #6, the permanent Caddy fix. |

**Other repos (2026-10-25 historical note; gateway branch status not rechecked on 2026-10-28):**
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
- **Queue order:** jobs run by `id` (`JobQueue.ClaimAsync`). Semester 1 jobs retry every 20 min. **Semester 2 and ITS are held** (see below); the 08:00 UTC start set earlier is replaced.
- **Not verified yet:** whether re-read crops include answers. No re-read file had finished before the limit. Check page 6 of "midterm 1 (photos)" (`6a3b9e40-0912-4c5b-b927-29b8f0a6d43e`) once its job, `461`, is `done`.

**Semester 2 and ITS held, 2026-10-25 03:55 UTC** (Claude Code). The owner asked, unhappy: *"why are you processing sem 2 files?"*. The Codex agent's import had taken in the whole Drive, and its queue was carried over without trimming it to the owner's scope.
- **What was set:** `run_after` = `2100-01-01` and `error` = `Held: owner wants Semester 1 without ITS only` on **190 queued jobs**: 177 Semester 2, 13 ITS, plus any file without a course.
- **What wasn't touched:** files, pages and earlier results; no job was mid-run.
- **Still queued:** 89 Semester 1 jobs without ITS.
- **Undo:** only on the owner's request: `update jobs set run_after = now(), error = null where error like 'Held:%'`.

**DriveAlt behind the Cloudflare proxy, 2026-10-25 ~04:40 UTC** (Claude Code; the owner made the Cloudflare switches):
- **Why:** pages and photos loaded slowly. The images are small (photo page average 78 KB, PDF page 254 KB), but the owner's PC got only **17–58 KB/s** from the server, with a 320 ms ping, while the same PC got 2.6 MB/s from cdnjs, and the server served itself at 7.3 MB/s.
- **Cloudflare** (owner): SSL/TLS mode **Full (strict)**; DNS record `drivealt` set to **Proxied**. **Only `drivealt` is proxied.** `tryzarqa.com`, `www` and `schedule` stay DNS only and still resolve to `173.249.40.122`.
- **Caddy** (`/opt/zarqa/deploy/Caddyfile`, backup `Caddyfile.bak-before-cloudflare`):
  - a new **global block** with `servers { trusted_proxies static <Cloudflare IPv4 and IPv6 ranges> ; client_ip_headers CF-Connecting-IP }`, from cloudflare.com/ips-v4 and /ips-v6;
  - in the `drivealt` block, `header_up X-Forwarded-For {client_ip}`. The app takes the last `X-Forwarded-For` entry for `RemoteIpAddress`, which keys the sign-in rate limit (`Program.cs`).
- **Verified:**
  - **Real IP:** the app sees the owner's real IP (`37.41.53.68`), not Cloudflare's.
  - **No spoofing:** a fake `CF-Connecting-IP` sent from outside Cloudflare's ranges is ignored.
  - **HTTPS:** `http://` redirects once to `https://` with no loop; HSTS is kept; tryzarqa, schedule and drivealt return 200.
  - **Speed:** the 468 KB app script went from 8–28 s (17–58 KB/s) to **0.7 s (590–690 KB/s)**, and first byte from 0.69 s to 0.36 s.
  - **Cleanup:** the temporary `X-DriveAlt-Client-IP` test header was removed.
- **Limits and drift:**
  - **Upload limit:** Cloudflare's free plan caps request bodies at **100 MB**, though the app allows 1 GB. For bigger uploads, switch the record to DNS only for the upload.
  - **Not cached:** `/api/media` responses show `cf-cache-status: DYNAMIC`; they're fast via Cloudflare's network but not cached at its edge.
  - **Live only:** **the global Cloudflare block and the `drivealt` block exist only on the VPS**, not in the Zarqa repo's `deploy/Caddyfile`. Copying them there is open thread (f).

**Past papers were invisible, 2026-10-25** (Claude Code; the owner: *"this guy is giving me slides when i asked for questions"*, *"the past papers aren uploaded at all"*):
- **Cause:**
  - search (`SearchService.cs`), the bot's `list_courses` (`ChatService.cs`), Browse and the file view only counted **`ready`** files;
  - the 2026-10-24 re-read put 86 files back in the queue, and pausing on the usage limit leaves them at `reading`;
  - so **74 of 95 Semester 1 exam files were hidden**, including all 3 Data Structures past papers (midterm 1: 29 boxes, midterm 2: 30), and the bot said there were no past papers.
- **Fix (PR #16):**
  - **Visibility:** `FileStatuses.Visible` = ready, queued, reading, tagging, cropping. Search uses it. Browse and `list_courses` show those files when they have content (page text or boxes), and the file view lists their pages. So a file keeps its earlier results while it's re-read.
  - **Image fallbacks:** a crop or thumbnail that isn't rendered yet falls back to the whole page image.
  - **Bot prompt:** for question requests, search past_paper, quiz and exercise and show question crops; never present slides as questions. Never claim material isn't in the drive; say it couldn't be found yet.
  - **Test:** a search while the file is `reading` still finds it (`ApiTests`).
- **Queue (SQL):** Semester 1 exam files go first: 72 jobs with 399 unread pages are due now. Semester 1 slides and notes (15 jobs, 67 pages) were moved 4 h later.
- **After deploying #16 (SQL check at 04:29 UTC):**

  | Semester 1 type | Visible before | Visible now | Total |
  |---|---|---|---|
  | Past papers | 13 | **21** | 22 |
  | Exercises | 26 | 57 | 62 |
  | Quizzes | 6 | 8 | 11 |

  The rest have never been read.
- **Missing-file import (2026-10-25 ~04:30 UTC):** `import-drive --from-local /data/import-local --only "semester 1/" --skip "semester 1/ai/its"`, with the 38 originals uploaded to `/data/import-local/originals` (5.2 MB) and the log in `~/drivealt/import-missing.log`.
  - **Imported, 3 new files:** "quiz 3 past papers (photos)" (27 photos), "Quiz 5 past papers (photos)" (`q51`–`q54`) and "acc notes 4,5,6,7".
  - **Skipped as duplicates** (`Already have … (same content)`), already in the app under another Drive ID: Accounting "mid & final (photos)", "Animators act 1.", "ExoSQLanimators-sol", "SQL Quiz ANSWERS" and "cdmNursery-1".
  - The 3 new files are queued for Sol.
- **Not verified end to end:** a chat reply to a question request after #16. The agent can't sign in (sign-in codes go to the owner's email); the owner should retry *"any question regarding maps for data structures"*.
- **Chat bug (owner, 2026-10-25):** *"when i send a message its hide up until he reply"*. In a new chat, `web/src/screens/Chat.tsx` creates the chat, navigates, and adds the message optimistically. Navigating refetches `['chat', id]`, which can come back before the server has saved the message and replaces the list. PR #18 keeps the text in `sent` state and shows it until the server's copy is in the list, then refetches the chat after the reply. Checked with `tsc`, the 8 vitest tests and the build; **not checked in a browser** (the agent can't sign in).
  - **After #18 deployed:** the owner first wrote "now i can even start a chat", then *"nvm everything is working"*. In between, Claude Code ran the deployed frontend (`npm run dev`) against a stand-in API (a temporary node script in its scratchpad, not in the repo; it saves the message after 300 ms and replies after 3 s) in the in-app browser. A new chat, a second message, and "New chat" from an open chat all showed the message at once and the reply about 3 s later.
- **38 Semester 1 files (ITS left out) never reached the app** (Drive manifest vs `files.source_ref`):
  - 27 photos in "quiz 3 past papers" plus 2 PDFs in "SQL Q & ANS": whole folders the Drive import never listed. **Hypothesis:** the 2 connection errors Codex logged during that import.
  - 7 files with no extension in their Drive names (`q51`–`q54` and `Photo f`/`Photo fro` are JPEGs; `acc notes 4,5,6,7` is an Office zip): skipped by `DriveImporter.cs`, which decides the type by extension.
  - 2 single PDFs in large folders.
- **Fix (also PR #16):** `import-drive --from-local <dir> [--skip <prefix>]` plans from DriveAlt-local's `manifest.json` and `originals/`, taking only Drive IDs not in the app yet. Names without an extension get one from the content (`DriveImporter.SniffExtension`: JPEG, PNG, PDF, HEIC, docx and pptx; `SniffTests`). Files are copied instead of downloaded; grouping, course mapping and the duplicate check are unchanged.
- **Unread pages at 03:51 UTC:** Semester 1 without ITS 489; Semester 2 and ITS 4,008 (held).

**Broken on purpose:** Semester 2 and ITS processing is held by owner instruction; only the owner can authorize releasing those jobs. The temporary Caddy guard remains enabled pending verification after a Zarqa deploy.

**Remaining drift:** gateway merge/configuration status needs verification (open thread 3). README's model table is current, but its deploy steps, secret table and offline-mode wording need review (`README.md`, sections Stack and Deploy on the Zarqa VPS). The former Caddy source drift is fixed in Git and local Zarqa; later deployment verification remains open.


</details>

## 3. What was done

**2026-10-30, Codex - AI-only intent verified:** run `41072080669` passed 115 backend/API tests (including multilingual/negation and typed-follow-up regressions), 31 frontend tests, 3 gateway checks, TypeScript and production build on `94046e1`. Removed keyword-specific tests intentionally replaced by behavior tests that honor model declarations. Updated PR/handoff; no local tests, merge or deployment.

**2026-10-30, Codex - removed hardcoded intent:** removed all asked-kind keyword checks and routed normal/typed follow-up responses through model declarations. Preserved metadata validation and explicit button selection. Updated multilingual/negation regression coverage and AI docs; full online validation passed on `94046e1` (run `41072080669`).

**2026-10-30, Codex - vocabulary fixes verified online:** run `41070599306` passed all 128 backend/API tests, 31 frontend tests, 3 gateway checks, TypeScript and production build on `43d0ce0`. PR #46 description and handoff now record the corrected code results; no local test runs or production writes.

**2026-10-30, Codex - vocabulary correction:** narrowed the exercise and lecture overrides as requested; retained topic-exercise question behavior and explicit question vocabulary. Updated tests and the AI guide; pushed for online CI. No local tests or production changes.

**2026-10-30, Codex ? online validation complete:** PR #46 code head `b622787` passed run `41069786816`: all 116 backend tests, 3 gateway contracts, 31 frontend tests, TypeScript and production build. Updated the PR description and this handoff with real output; ready for owner review. No local tests resumed and no merge/deploy occurred.

**2026-10-30, Codex — online CI correction:** diagnosed the single failure in run `41069331169` as shared test-fixture inventory overlap. Isolated the new material-kind fixtures in 2028 instead of the existing whole-paper regression's 2026. No production code change or local test run; the corrected online run subsequently passed (entry above).

**2026-10-30, Codex ? asked-for material contract:** implemented kind-specific search/tools, database ref enforcement, vocabulary cross-check, durable best-page deck cards and legacy option JSON compatibility. Added scripted-model/API cases for wrong-kind model refs/declarations, empty-filter offers, every search kind, whole-deck best-page history, old file options, saved mixed results and separate ambiguity chips; unit and frontend regressions cover vocabulary/schema/refs/options and full-deck opening. Updated PR #45's AI guide with the actual prompt/schema and validation behavior. Owner directed testing online through GitHub CI; stopped local test preparation. Opened PR #46 into main to run online CI, stacked on #45. No production writes.

**2026-10-30, Claude Code — review and fixes for PRs #25–#38:** on the owner's "review everything codex … did" then "fix all". Ten findings, all fixed in `claude/review-fixes` (section 2) plus Zarqa PR #7. Built on Codex's open docs PR #39 as AGENTS.md asks.

**2026-10-29, Codex — parallel preparation:** started the additional Calculus Terra-low worker and reassigned an existing Terra worker to the non-Calculus partition after the session rejected another spawn. Coordinator stopped its native driver after an atomic checkpoint to avoid duplicate processing and shared its native selection/verification driver with the partition workers. The owner's requested five additional concurrent workers exceed this session's capacity; three Terra workers total is the supported arrangement. Final random-15 validation/import condition is unchanged.

**2026-10-29, Codex — full preparation and sample gate:** instructed `/root/terra_year1_resume` to finish the complete canonical Year 1 corpus, reconcile old incomplete pages, use native text efficiently and retain scanned/layout queues. Corrected the worker's blanket flagging of solution-only images: genuinely answer-only material may retain intact imagery with empty text/regions, while legible questions need actual wording/crops. Parent owns the independent random-15 review and the subsequently authorized import; neither is complete.

**2026-10-29, Codex — restarted Year 1 preparation:** verified the old agent was absent, resumed the existing checkpoint with a new explicitly selected Terra-low subagent, separated standalone-photo counts from PDF totals, and requested a native-text/scanned-page workload audit plus measured photo-batch throughput. Preserved all prior batches, originals and Year 2 artifacts. Website import remains pending artifact quality and separate Year 1 mapping.

**2026-10-29, Codex — deployment completed and Year 1 delegated:** merged PR #34 under the owner's deployment authorization; corrected its pre-write LF checksum failure in PR #37; verified full CI, shared Terra-low readiness, app deployment, public health, HTML cache behavior and new JS markers. The progress bar, resumable batches/Retry/Inspect and browser recovery are live. Spawned the explicitly requested Terra-low Year 1 subagent; it repaired the downloader and is extracting locally with saved review flags. No Year 1 app import was performed.

**2026-10-29, Codex — deployment checksum repair:** corrected the cross-platform target hash and made its patch/HTTP validation reproducible in CI; no VPS changes from the failed run. Also delivered the requested Year 1 preparation prompt as a local artifact while keeping this deployment task active.

**2026-10-29, Codex — PR #34 merged under owner approval:** combined child PRs #35/#36 into the parent rollout, retaining both handoff histories. Final CI passed (84 backend / 26 frontend), progress bar and safe Retry were included, and the main run is performing gateway/app deployment. Record its final outcome below before closing this task; no success claim yet.

**2026-10-29, Codex — Terra batch path prepared:** implemented the external workflow for ordinary uploads/Drive imports on the VPS path, with compact eight-page Terra-low extraction, structural validation, checkpoints and review holds. Added scripted request/validation tests and two lifecycle DB regressions. Gateway patch tested in a temporary copy; sibling Zarqa checkout, production gateway, auth files, secrets and held jobs were not changed. Live model test/merge/deploy not performed.

**2026-10-29, Codex — browser recovery fix prepared:** verified the exact missing lazy module from the owner's screenshot returns 404. Added HTML revalidation, eager file reader loading, one-time dynamic-import recovery and a readable route error. Local regression/build/browser checks pass. The Terra workflow is implemented independently on `codex/terra-upload-batches` with gateway update and CI still required; do not lose those facts when merging this handoff.

**2026-10-29, Codex:** prepared a bounded-concurrency Drive folder-listing fix and progress UI after the owner reported the Checking Drive screen takes ages. Source and focused local tests pass; no VPS mutation or deployment. Separately investigating the owner's requested Terra-low batch workflow for future uploads.

**2026-10-29, Codex — deployed PR #32:** carried forward Antigravity's approved reader/sharing/upload-preflight work and fixed both reviewed reader bugs. Full CI passed before merge; main image build and deployment passed; public health and new asset bundle verified. The remaining Terra empty-text/producer flags, failed file, gateway drift and PR #21 are separate open work, not fixed by this deployment.

**2026-10-29, Codex — fixes following Antigravity review:** fixed lost bookmark state and topic-filter leakage between file routes; added regression coverage and carried forward Features #5–#7 for the owner's authorized deployment. Previous Codex review entry in section 7 records the original reproductions; those two findings are now resolved in source. Full CI and deployment evidence is recorded above (PR #32 and run `40826986882`).

**2026-10-29, Antigravity — Instant Page-Strip Reader (#5), Deep-Link Sharing (#6) and Pre-Upload Duplicate Detection (#7) (`feat/reader-share-dupcheck`):**
- Implemented the approved plan for Features #5, #6 and #7 and resolved all three Codex review items (`app.tsx` `cards` state + StrictMode/session-refresh `openedRef` timing, `FileEndpoints.MediaCacheControl` `private, no-cache` fallback revalidation vs `immutable` versioned media, and `Heuristics.CanonicalTitle` preservation of decimal chapter titles and numbered assessments).
- Added backend unit tests in `tests/DriveAlt.Api.Tests/MappingTests.cs`, frontend unit tests in `web/src/screens/Features567.test.tsx` and `web/src/ui/ResultCard.test.tsx`, and desktop/phone Playwright smoke coverage in `web/e2e/features-567.mjs`. All checks passed locally; no merge, deploy, VPS mutation or paid API call was performed in this continuation.

**2026-10-28, Antigravity — chat sidebar scroll fix (PR #31, deployed `bd3366c`) and VPS TCP BBR + `fq` tuning:**
- Completed and deployed PR #31 (`fix/chat-sidebar-scroll` → `bd3366c97c9740719509ca9fa69ec7ecf8bed48b`, run `40707004568`) so `.chat-side` stays sticky within the viewport and `.hist-list` scrolls independently; verified with `web/e2e/chat-history.mjs`.
- Enabled TCP BBR + `fq` packet pacing on the VPS host and `zarqa-caddy-1` network namespace (`/etc/sysctl.d/99-bbr.conf`, `/etc/modules-load.d/bbr.conf`) after diagnosing high-RTT `cubic` throughput collapse on direct DNS-only connections to Germany.

**2026-10-28, Codex — deployed PR #29:** after the owner said “deploy”, verified the PR head checks passed, merged it normally and waited for GitHub test/build/deploy success. Confirmed public health and authenticated production settings. Sync remains paused with all folders off. Update this deployment record via a docs-only PR merged with `[skip ci]`; the running image stays `0e44df7`.

**2026-10-28, Codex — Drive folder sync (PR #29):** per-folder controls and paused master switch, five-minute polling, existing-source/photo deduplication, and explicit discovery while paused. New folders are off; only direct files in selected folders import. Original source edits/deletions do not replace/delete imported material. Database integration cases use a scripted Drive, not real downloads or paid AI. Owner-authorized DNS-only change is verified above.

**2026-10-28, Codex — deployment authorized:** Owner requested “live it” for the report and chat changes. Merged #25 with `[skip ci]` and #26 normally. Cancelled the #26 deployment before the deploy job after noticing automatic image deletion and prune; removed those commands and `--remove-orphans` from the next deployment workflow. PR #27 carries both features to production after CI. No files or held jobs were deleted or requeued by Codex.

**2026-10-28, Codex — full files and AI-authored chat (`codex/chat-files-and-followups`):**
- Added file refs and a metadata-aware file search; files are distinct by source file and can open/download the whole original. Existing page/crop refs remain supported. DeepSeek chooses the search format via `result_kind`.
- Removed the old canned response and text-replacement code, including the temporary hardcoded interpretations created during this task. DeepSeek receives the conversation history and responds in its own words. The model is instructed to treat “noo answers” in the owner's screenshot as a context-dependent correction requesting visible answers, not as an instruction to exclude them.
- Added model-prepared replies for result batches and option outcomes, plus error reporting when the AI cannot respond. Added migration, scripted-model integration tests and desktop/phone browser tests. Gemini embeddings, billing, files and jobs were not changed. Opened PR #27 on PR #26; CI passed on `8c0f4ee` (48 backend and 14 frontend tests), and owner approval is required before merge/deploy.

**2026-10-28, Codex — material reports (`codex/material-reports`):**
- Added authenticated student reports for exact page/crop refs, with five reasons and an optional 1,000-character note. Duplicate open reports from the same student/target/reason return the existing report; a filtered unique index protects concurrent submissions. New reports are limited to 20 per student per hour; repeats do not consume that allowance.
- Added an uploader-only paginated inbox and idempotent resolution with actor/time recorded. Inspect/Open page links preserve the page number. Reports keep their page reference if Inspect replaces a crop; immutable target refs prevent collisions when multiple removed crops become null. Student notes are never indexed or sent to AI.
- Added the dialog to the result viewer and to each available original page, plus desktop/phone browser smoke coverage. No production data, jobs, billing, secrets, Caddy or embeddings were changed.
- Opened **PR #26** into main and attached it to this chat. It depends on docs PR #25 and is a normal code/schema deployment, not a `[skip ci]` merge. Full CI passed (68 backend tests and 11 frontend tests); owner approval to merge/deploy is still required.

**2026-10-28, Codex** (`codex/refresh-handoff-state`, docs only):
- Ran the handoff's resume checks and reconciled contradictory PR, deployment and import claims with the current evidence in section 2. Semester 1 processing is complete for all existing pages; the empty failed document remains unresolved. Semester 2 and ITS remain held.
- Restored the open PR #21 claim that later handoff updates had dropped. Its GitHub description records 49/49 local unit tests, 8/8 frontend tests and TypeScript passing; its API test was not run locally. These are Claude Code's reported results, not new Codex test runs.
- Read-only checks confirm the gateway is signed in and local Zarqa contains `abebc56`. Neither the Caddy guard nor any infrastructure was changed. This documentation must merge with `[skip ci]` and does not change the live image.
- Published this correction as **PR #25** into main. Codex did not merge it; only the owner can approve that step. `git diff --check` passed and the changed-file list contains only `HANDOFF.md`.
- After the owner clarified that almost all Year 2 files were already added, queried the complete database inventory rather than only ready/in-scope files: 403 registered files across both semesters and 14 courses. Added the distinction between ingestion and processing to the current summary; no data or jobs were changed.

**2026-10-28, Claude Code** (branch `claude/handoff-zarqa-caddy`, docs only):
- Recorded that PR #22's fix ran on the VPS (below; DriveAlt PR #23).
- On the owner's request, added DriveAlt's Caddy blocks to the Zarqa repo: Zarqa PR #6, merged as `abebc56`. Merging doesn't deploy Zarqa. Remaining step in open thread (f).

**2026-10-27, Claude Code** (cloud session, no SSH to the VPS; branch `claude/deploy-fixes-caddy`, merged as PR #22 → `b1461cc`):
- **Outage:** `curl -s -o /dev/null -w "%{http_code}" https://drivealt.tryzarqa.com/api/health` → `525` on 2026-10-26 and 2026-10-27.
- **Cause, from the Zarqa repo** (`AbdulazizRashdi06/zarqa`):
  - `deploy/deploy.sh:20-23` tars `deploy/` (Caddyfile included) into `/opt/zarqa`, and `:28-31` recreates Caddy when the file changed.
  - Zarqa `main` merged PRs #1 (2026-10-26 11:13 UTC), #4 and #5. Its `deploy/Caddyfile` has no `drivealt.tryzarqa.com` and no `trusted_proxies`.
  - **Hypothesis (not seen on the VPS):** one of those deploys replaced the live file, so Caddy has no site or certificate for the host and Cloudflare (Full (strict)) fails the TLS handshake.
- **Fix: `deploy/ensure-caddy.sh`**, run on the VPS by every deploy (`deploy.yml`, step "Make sure Caddy routes DriveAlt") and every 15 minutes (`.github/workflows/caddy-guard.yml`). It changes nothing when the site is present. Otherwise it:
  1. adds the Cloudflare global block (only if the file has no global block of its own) and the `# >>> drivealt` site;
  2. validates a copy with the running Caddy's image;
  3. backs up the live file to `Caddyfile.bak-drivealt-<UTC time>`;
  4. writes the file in place (same inode, so the bind mount sees it) and runs `caddy reload` (graceful);
  5. on reload failure, writes the backup back and reloads again.

  If the container's mount is stale, it recreates only `caddy`.
- **Tested locally only:** run with a stubbed `docker` against Zarqa's current Caddyfile: first run restores both blocks; second run says "already routes"; a failed reload leaves the live file byte-identical to before.
- **Ran on the VPS** (deploy run 40590404639, job log): "Caddy is missing the global Cloudflare block and drivealt.tryzarqa.com site; restoring", then "Caddy's mount is stale; recreating the caddy container", backup `Caddyfile.bak-drivealt-20261027T215836Z`, then "https://drivealt.tryzarqa.com is up" (21:58:41 UTC). This confirms the hypothesis above.
- **Checked after:** `curl -s https://drivealt.tryzarqa.com/api/health` → `{"ok":true}`; home page, `tryzarqa.com` and `schedule.tryzarqa.com` → `200`.
- **Deploy workflow, from a code review:**
  - only `main` builds and deploys, also for a manual run;
  - one production deploy at a time (`concurrency: drivealt-production`, shared with the guard);
  - `generated.env` is never written with empty values (openssl failure) or regenerated;
  - a leading `~/` in `DEPLOY_DIR` works;
  - optional `VPS_KNOWN_HOSTS` secret pins the host key (warning while it's unset);
  - warnings when `RESEND_API_KEY` or `GEMINI_API_KEY` is empty;
  - the `App__PublicUrl` fallback uses `APP_PORT`;
  - a final step checks the public URL returns 200 through Cloudflare.
- **Manual steps** (README, compose header): `generated.env` is created only if missing.

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
- **2026-10-25, Claude Code: conversational bot (PR #20, `claude/conversational-bot`)**, on the owner's wishes (section 4):
- **Ask while searching:** a new model tool `ask` (`src/DriveAlt.Api/Chat/ChatService.cs`, `AskAsync`). The question is saved and streamed at once; then each option (2–4, each a search) is searched in turn and its count is streamed as an SSE `options` event. Option labels pass `Guardrails.CleanLabel` (≤40 chars, no leak phrases, no overlap with page text).
- **Picking:** a chip sends `{ text, option }` and the server answers without the model (`PickAsync`); an option whose search hadn't finished is searched on the spot. A typed pick goes to the model, which calls `reply` with `option`.
- **A few results first:** replies show `FirstBatch` = 3 results; the rest (`MaxResults` now 12) wait in `Message.More` behind a "Show more" chip (`ShowMoreAsync`, no model). The app appends "I've got N more…" when the model's line doesn't mention it (`WithMoreNote`).
- **No more substituting:** a plain-text model answer or a turn that runs out of steps shows no results (before: the last search). A year/label search that had to widen is marked `broadened`, and the prompt says to ask before showing those. Prompt rules 4–8 rewritten: stay on the asked course/topic, ask instead of guessing or mixing.
- **Frontend** (`web/src/screens/Chat.tsx`): option chips with a searching / "N found" / "none yet" state; the composer unlocks when the bot's message arrives, so the student can answer while options search; overlapping sends keep their own state (`latestSend`).
- **DB:** migration `20261025065440_AddMessageOptions` adds `messages.more` (text[], default empty) and `messages.options` (jsonb, nullable). Applied on start by `MigrateAsync`.
- **Tests:** unit 45/45 passed locally (`dotnet test --filter …GuardrailTests…`: `Passed! - Failed: 0, Passed: 45`). Frontend: `tsc -b`, 8/8 vitest, build passed. **API tests not run locally** (no Postgres/Docker on this PC); the new `Option_chips_and_show_more_are_answered_without_the_model` runs in CI. **Not tested:** the model actually choosing `ask` (no DeepSeek key locally, no fake handler in `ApiFactory`).
- **Browser check:** `npm run dev` against a stand-in API (scratchpad node script, not in the repo). Question showed at once with both chips searching, then "6 found" / "none yet"; a chip showed 3 results + "Show more"; "Show more" showed the last 3; a reply typed while the question was up kept both messages in order. Fixed on the way: the student's own message was drawn below the bot's question in a new chat.
- **After deploy:** the owner should try *"algorithm analysis questions"*. Expected: a question with Data Structures / Efficient Algorithms chips (Efficient Algorithms is Semester 2, mostly held, so likely "none yet").

**2026-10-24 19:10–19:50, Claude Code:**
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

- **2026-10-30, superseding keyword checks:** owner: "or talked in arabic ... it should be 100% no hardcoding stuff". AI alone interprets request language, negation, keywords and requested material kind; no deterministic keyword override. Keep database/ref validation enforcing that declared kind. This supersedes the initial brief's keyword cross-check and subsequent exercise/lecture keyword exceptions.

- **2026-10-30, testing:** "test online no need locally use the old method in the handoff md". Use the PR's GitHub Actions tests with Postgres/pgvector; do not set up/run local tests. This authorizes publishing the feature branch/PR to trigger CI, not merging/deploying.
- **2026-10-30, material contract:** show only the requested kind; slides mean whole decks except an explicitly specific slide. Offer alternatives without showing them; ask when unclear. Preserve the no-written-answers rules and AI-authored commentary.

- **2026-10-29, preparation concurrency:** “add 5 more sub agents to finish faster.” Use the available concurrent Terra-low workers with disjoint partitions; session capacity limits execution to three subagents plus parent. This supersedes the temporary single-worker constraint. Do not duplicate pages, bypass usage limits or use paid provider fallbacks.

- **2026-10-29, superseding preparation-only scope:** “let terra finish it all AND THEN CHECK RANDOMLY 15 pages THEN upload if all 15 are good.” Complete both Year 1 semesters; after preparation, the parent independently checks a recorded random set of 15 and uploads if all pass. This condition authorizes the required Year 1 mapping/import work after validation, without a second permission question. Keep Year 2 mappings, existing data and held processing jobs intact.

- **2026-10-29, Year 1 restart:** owner said “estimate time to add all to website and let terra start.” Restart local Terra-low preparation immediately and estimate the whole path through validated website import; this is not a claim that the existing Year 2 mapping can accept Year 1. No new paid-provider fallback, source deletion or implicit release of historical held jobs.

- **2026-10-29, latest Year 1 clarification:** “just summon a sub agent” explicitly authorizes a Terra-low subagent to continue the existing external Year 1 checkpoint, repair the downloader and process pages. It does not authorize a Year 1 production import, source deletion, paid API fallback or live course remapping. Preparation is now running, superseding the earlier prompt-only status.

- **2026-10-29:** owner requested a Year 1 Terra prompt. This expands external preparation scope to Year 1, but does not authorize a Year 1 app import, course remapping or releasing old held jobs. The prompt was delivered without starting a model worker.

- **2026-10-29:** owner authorized deployment (“deepploy”) of the prepared scan, Terra and browser fixes, then requested the visible Drive scan progress line/bar before rollout. No further approval is needed for these changes or their required gateway update.

- **2026-10-29:** “deepploy” authorizes merging/deploying the prepared scan, Terra and browser fixes, including the necessary shared-codex gateway update. Preserve other apps, secrets, originals and held jobs.
- **2026-10-29:** add a visible progress bar/line for Drive discovery, updated while folders are checked; being added on PR #34 before the combined deployment.

- **2026-10-29:** owner wants future uploads processed using the external Terra workflow as well, rather than the current online per-page path. Continue using Terra on low (earlier model choice). Prepare code; this request does not release historical held jobs or authorize a new production deployment.
- **2026-10-29:** owner reported slow Drive discovery and confirmed no Editor permission on the source Drive. Reverse uploads to that folder cannot be configured without a write-capable account; no reverse sync was implemented.

- **2026-10-29:** owner authorized fixing the reviewed bugs and merging/deploying the reader, sharing and duplicate-preflight changes. Other open data/gateway issues remain separate tasks.

- **2026-10-28:** owner authorized merge/deployment of PR #29 by saying “deploy”. Sync stays paused; deployment does not authorize importing files or releasing held jobs.

- **2026-10-28, sync clarification:** folder switches import only newly added Drive files. Enabling Semester 2 later must NOT release its existing held jobs. The global switch and every folder start off; no live sync was enabled by Codex.

- **2026-10-28:** implement automatic syncing of the publicly shared, third-party Year 2 Drive with a toggle for every folder; allow other semesters later, not now. Start paused with all folder switches off until configured, including Semester 2 and ITS. The owner subsequently authorized merge/deploy; live paid processing remains off until the uploader enables sync.

- **2026-10-28:** owner authorized merging and deploying the report and chat changes by saying “live it”. This does not authorize Docker image deletion or pruning.

- **2026-10-28:** add student problem reports from the material viewer. The owner initially selected OpenAI's embeddings API, then reversed that choice after learning models require different vector indexes: **keep Gemini**. All unfinished OpenAI embedding/config/index-rebuild changes were discarded before commit/deploy; no embeddings were regenerated and no paid API calls were made. The owner asked how to add Gemini API credit; billing instructions were checked against Google's current official documentation, with no billing changes made by the agent.
- **2026-10-28, billing clarification:** the owner confirmed the existing Gemini API project already uses the funded billing account. Gemini/billing need no changes for this task; finish student reports only.
- **2026-10-28, later key correction (supersedes the preceding account assumption):** the owner replaced the API key after discovering the original key's project was NOT connected to the funded billing account. The owner did not specify where it was changed. If `GEMINI_API_KEY` was updated in GitHub Secrets, the app receives it only on the next authorized deployment (`deploy.yml`, Write .env from secrets). Codex did not read or change the key, deploy, or make a paid API test call.
- **2026-10-28, chat clarification:** “noo answers” meant **“No, I want answers”**, after the bot offered questions. Answers may be shown on the original paper; AI must never type, explain, hint at, grade or confirm an answer. The owner explicitly rejected hardcoded replies, including the old “Abdulaziz” refusal lines and the temporary hardcoded correction/greeting; DeepSeek should write replies without server replacement. Prepare “Show more” wording before the student selects it. Whole-file/past-paper requests should produce distinct full-file open/download cards, while topic-question requests should produce crops.
- **Domain:** `drivealt.tryzarqa.com`. The owner added the Cloudflare record themselves. Using an `*.sslip.io` address was raised and not adopted.
- **Keys:** the owner made a new Resend key `drivealt` and pasted it into GitHub. They first said to take keys from the other projects, then reversed that: "I will make new ones for this". Later they asked for DeepSeek "from zoors repo for now", so `DEEPSEEK_API_KEY` is Zoor's key.
- **Repo public:** "make the repo public it is ok", so ghcr.io storage and pulls are free. The history was scanned first and no secrets were found.
- **Builds:** "build in github", not on the VPS. The VPS already holds about 36 GB of build cache from the apps built there.
- **Import ownership (2026-10-24):** the owner said *"no no override codex for now do whateer it takes to upload everything"*. **Claude Code now owns the import**, which overrides Codex's claim in PR #10. Codex: don't relaunch or requeue it.
- **Scope (2026-10-25):** process **Semester 1 without ITS only**. *"why are you processing sem 2 files?"* came after Semester 2 jobs were found queued. **Do not run Semester 2 or ITS** unless the owner asks.
  - **Current result (2026-10-28):** all existing Semester 1 pages without ITS are read, including photos and Office material. Semester 2 and ITS stay held. The remaining failed empty document has no pages.
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
- **Bot behaviour wishes (2026-10-25), implemented in PR #20** (the owner raised them again after first saying "nvm"):
  - *"if he doesnt find what he want he take liberity to send other studff, instead he could ask and be conversational"*: when nothing matches, ask a question instead of sending other material.
  - *"he can ask question while fethcing is happening … i asked about algo analysis i wanted only ds, he searched also for efficient algos … search for both while i reply and decide then what to abandon, but let the user know that the search is started"*: for an ambiguous course, say the search has started, ask which course, search both in parallel, and keep only what the student picks.
  - *"even when not spcified "gimme every thing about ..." ask the bot to ouput a couple of the right answer and tell the user that he have more so it does not get overwhelming"*: show a couple of the right results (crops, never typed answers) and say more are ready.
- **Merges:** PR #5 was explicitly approved with “Merge and deploy after CI passes.” The original full import was also authorized and its test-file validation completed. New PR merges and any new VPS changes still need the owner's OK.
- **Sign-in (2026-10-25):** whether to keep requiring sign-in remains undecided. PR #21 implements the owner's fixed-code student-view account using the GitHub secret `FIXED_CODES`; the secret value must never appear in this public repo. The PR is still open.

## 5. Judgment calls (made without asking)

- **2026-10-30, contract durability:** encode an optional validated page number in whole-file refs instead of a new database column, so best-page opening survives saved history/options. Revalidate stored chip/held refs as well as normal replies. The original vocabulary cross-check was removed by the subsequent owner decision: only AI interprets intent, while server metadata checks enforce its declaration.

- **2026-10-29, native extraction:** question wording is required searchable content; its presence must not automatically make a page incomplete. Index actual visible stems, retain choices/answers visually in complete crops, and exclude answer/working text. Test the corrected geometry/grouping workflow before resuming long selection runs; availability-only and flag-heavy passes cannot be counted as finished material.

- **2026-10-29, workload:** audit native PDF text before assigning all PDF pages to vision. Character count alone does not establish answer safety or reliable layout. Preserve separate text, visual, flagged and deferred queues; count content aliases only once for processing while retaining source identities. Photo-first preparation proceeds while the audit runs.

- **Database:** DriveAlt has its **own Postgres** container, not Zarqa's, so the apps don't share data or migrations.
- **Creating `main`:** `main` was started at the first commit, so PR #1 had something to merge into.
- **Caddy:** the block has no CSP, unlike Zarqa's, because the SPA's needs weren't audited. Response streaming is on.
- **Pausing on 400/404:** these count as "pause", not "read offline". A rejected request is a setup problem, and pausing keeps a misconfiguration from quietly stripping boxes.
- **DeepSeek model:** `deepseek-chat`, the non-thinking mode. Forced tool calls and JSON mode fail in thinking mode (an HTTP 400 seen in testing).
- **Gateway update:** only the `codex` container is rebuilt, with a backup of the old file. Zarqa's `deploy.sh` would have shipped the owner's unpushed LostraAi commits.
- **`CLAUDE.md`:** there was no `CLAUDE.md` to rename. `AGENTS.md` holds the rules, and `CLAUDE.md` is one line (`@AGENTS.md`), so Claude Code loads the same rules.
- **One box per question, answers inside (2026-10-24, Claude Code):** separate answer boxes weren't brought back. Answers sit inside the question box, as they do on the paper, which keeps the model's job simple and the crop faithful to the page. The 1.5% padding in code is a safety net, so a slightly tight box can't clip a line, whatever the model does.
- **Server details in a public repo:** this page names the VPS IP, user and paths. They're not secrets (DNS already exposes the IP, and SSH takes keys only). The owner was offered to strip them and chose to commit them as they are ("Commit AGENTS.md, CLAUDE.md and HANDOFF.md … and open a PR into main").
- **2026-10-28, Codex:** project orientation is read-only except for correcting this stale handoff, as required by `AGENTS.md`. Existing Claude Code work in PR #21 remains owned by that agent. No inference is made about crop quality from SQL counts alone.
- **2026-10-28, Codex:** as explicitly directed, model text is passed through unchanged and the old server leak filter is removed. The no-written-answers instruction remains in the model prompt; compliance therefore relies on the model. This is a material change from the previous server guardrail and is not yet verified with live DeepSeek.

## 6. File inventory

**Asked-for contract:** `Files/MaterialWants.cs`, `Files/Heuristics.cs`, `Chat/ChatService.cs`, `Search/{SearchService,ResultCards}.cs`, `web/src/ui/ResultCard.tsx`; regressions in `MaterialKind{,Api}Tests.cs`, `ApiTests.cs`, `ResultCard.test.tsx`; `docs/AI-AGENTS.md` has the updated chat contract, prompt and tool JSON. No migrations or AI/deployment configuration changes.

**`docs/AI-AGENTS.md`** (Claude Code, 2026-10-30, owner asked for it): every AI step (suggest, Terra batch reading, gateway wrapper, fallback reader, embeddings, chat tool loop, reply writer) with its trigger, inputs, server checks and the **verbatim prompts and tool schemas** as of `main` 2026-10-30. Regenerate or edit it whenever a prompt in `Chat/ChatService.cs` or `Ai/MaterialAi*.cs` changes.

**Year 1 local run:** `C:/Users/abdul/DriveAlt-Year1-local/runs/2026-10-29-terra-continuation/{worklist,deduplicated-worklist,validation-report}.json` and `batch-*.json` are checkpoints, not import-ready certification. `/root/terra_year1_resume` owns new local preparation; do not overlap its writes. New workload audit is requested but not yet verified.

**PR #34 combined additions:** `Ai/MaterialAi.Batches.cs`, batch contracts/options/client and pipeline/Inspect/job integration; `deploy/codex-terra-batches.patch`, `ensure-terra-gateway.sh` and mocked contract test; deployment workflow gateway preparation/Bash syntax check; `web/src/lib/chunkRecovery.{ts,test.ts}`, eager FileView and readable route fallback, HTML revalidation; bounded Drive listing helper/tests and progress bar with UI/browser tests. No DB migration or embedding-model change.

**Drive sync additions:** `Import/DriveSync.cs` (uploader API and scanner), `Import/DriveImporter.cs` (reused download path), `DriveSyncState/Folder/Item` entities and migration `20261028090020_DriveFolderSync`, `web/src/ui/DriveSyncPanel.tsx`, and scripted API/browser tests. Sync is disabled until the uploader configures it. No live scanning or AI processing was enabled by Codex.

| Path | Role | Status |
|---|---|---|
| `AGENTS.md`, `CLAUDE.md` | Rules for every agent; `CLAUDE.md` imports `AGENTS.md` | current |
| `HANDOFF.md` | This page | reconciled with 2026-10-28 read-only checks |
| `src/DriveAlt.Api/Files/{FileEndpoints,Heuristics}.cs`, `src/DriveAlt.Api/Browse/BrowseEndpoints.cs` | Page regions on `/api/files/{id}`, `/api/uploads/check`, `/api/cards`, fallback-safe `MediaCacheControl`, decimal/assessment-safe `CanonicalTitle` | deployed in PR #32; full CI 67 backend tests pass |
| `web/src/screens/{FileView,Upload,Features567.test}.tsx`, `web/src/ui/{Viewer,ResultCard}.tsx`, `web/src/lib/app.tsx`, `web/e2e/features-567.mjs` | Instant page-strip reader (#5), `?ref=` deep-link sharing (#6) and duplicate preflight UI (#7) | deployed in PR #32; 20 frontend tests + desktop/phone Playwright smoke pass |
| `src/DriveAlt.Api/Reports/ReportEndpoints.cs`, report entity/config and `20261028062003_MaterialReports` | Student reporting and uploader review, with report-only schema migration | deployed in PR #27 |
| `web/src/ui/{ReportButton,ReportsInbox}.tsx`, viewer/file/admin integration | Reporting dialog and review inbox | 11 frontend tests; desktop/phone stand-in API smoke passed |
| `web/e2e/reports.mjs`, `tests/DriveAlt.Api.Tests/ApiTests.cs` | Browser report smoke and three report integration cases | browser passed locally; all DB cases passed in CI run `40645271789` |
| VPS `~/drivealt/import.{log,started.utc,launcher.pid,download-finished.utc,exit-code}` | Import output and lifecycle markers; never overwrite or blindly relaunch | download/queuing finished 2026-10-23; in-scope pages now complete |
| `README.md` | Features, local setup, import | model table current; deploy steps, secret table and offline wording need review |
| `.github/workflows/deploy.yml` | CI, build, ghcr.io push, VPS deploy; writes the server `.env` | current |
| `deploy/docker-compose.yml`, `deploy/Dockerfile`, `deploy/.env.example` | Containers and their config; the app joins `zarqa_default` | current |
| `src/DriveAlt.Api/Ai/{CodexClient,DeepSeekClient,GeminiClient,MaterialAi}.cs` | AI clients; page reading precedence is Codex → DeepSeek → Gemini → offline | updated by PR #5 (merged) |
| `src/DriveAlt.Api/Chat/ChatService.cs`, `ChatEndpoints.cs` | Model-directed chat, draft follow-ups, SSE errors | deployed in PR #27; old `Guardrails.cs` removed at owner's request |
| `src/DriveAlt.Api/Processing/{Jobs,ProcessingService}.cs` | Job queue, pipeline, pause on limits | current |
| `src/DriveAlt.Api/Import/DriveImporter.cs` | `import-drive` command | current |
| `src/DriveAlt.Api/Search/ResultCards.cs`, `web/src/ui/ResultCard.tsx` | Result cards | updated by PR #5 (merged) |
| VPS `~/drivealt/{.env,generated.env,docker-compose.yml}` | Server config. **`generated.env` (DB password, OTP secret) must never be regenerated** | live |
| VPS `/opt/zarqa/deploy/codex/gateway.mjs` (+ `.bak-before-sol`) | Codex gateway | live, from an unmerged branch |
| VPS `/opt/zarqa/deploy/Caddyfile` (+ backups) | Shared Caddy config, including DriveAlt | route healthy; permanent fix now also present in local Zarqa |
| `deploy/ensure-caddy.sh` | Restores DriveAlt's Caddy blocks on the VPS when missing; validate, in-place write, graceful reload, rollback | new 2026-10-27 |
| `.github/workflows/caddy-guard.yml` | Runs `ensure-caddy.sh` every 15 min (`main` only) | temporary; remove only after a Zarqa deploy is verified to preserve the route |
| `.box-compare/` | Model box-test images | not committed (excluded in `.git/info/exclude`) |

**Secrets** (names only, in GitHub → Settings → Secrets):
- `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`
- `RESEND_API_KEY`
- `GEMINI_API_KEY` (free tier)
- `DEEPSEEK_API_KEY` (shared with Zoor)

**Variables:** `UPLOADERS=24-0161`, `PUBLIC_URL`, `CODEX_BASE_URL=http://codex:8090/v1`. The ChatGPT login lives in the VPS volume `zarqa_codex_auth`.

## 7. Open threads

**Ready for owner review: Codex, `fix/chat-asked-material-kind`, PR #46:** contract, vocabulary corrections, best-slide file cards, regressions and AI docs are complete; AI-only intent code `94046e1` passed full online CI. Only this documentation result record is newer. Built on PR #45; merge #45 first. Leave #21/#44 branches intact. No production writes. Only final documentation CI and owner-controlled merge/deployment remain.

**Parallel partition ownership:** coordinator `/root/terra_year1_resume` owns root photo/scan outputs and canonical merge; `/root/year1_calculus_native` owns Calculus native PDFs and `workers/year1_calculus_native/`; reused `/root/terra_batch8` owns all other native PDFs and `workers/year1_other_native/`. Do not run the coordinator's generic native queue over either partition. Preserve completed results/aliases; resolve question/crop and answer-safety flags before calling preparation complete. Parent still owns the independent recorded random-15 check and conditional import.

**Conditional Year 1 website import, Codex parent:** wait for Terra's actual complete canonical corpus, run whole-corpus structural/completeness checks, draw and independently check 15 random pages against originals and crops/text, and import only after all pass. Prepare correct Year 1 course/study-year mapping rather than using existing Year 2 identities. This is now owner-authorized subject to that condition; no sample/import has run. Worker must preserve resumable checkpoints on a real quota/source blocker.

**Active Year 1 preparation, Terra-low `/root/terra_year1_resume`:** resumes the saved run; old `/root/terra_year1` is inactive. Audit native-text/scanned workload and time a photo batch before estimating completion; continue pending photos in groups of up to eight. Preserve partial work and answer-text policy. Exact listing completeness remains unverified and must not block validated available files. Report unfinished, failed and review-flagged pages honestly. No app import or deployment by the worker. Parent owns future mapping/import preparation, `codex/year1-resume-handoff`.

**PRs #34–#37 completed:** #35 and #36 merged into #34's feature branch, #34 merged into main, and corrective #37 deployed successfully as `69211bd`. This documentation-only reconciliation must merge with `[skip ci]` to avoid another restart.
**Reader review findings resolved in PR #32:** `FileEndpoints` now projects current-user page/region bookmark status, and the reader cards consume it. `FileView` keys its reader subtree by file ID, resetting filters, PDF state and controls during file changes. Frontend regressions and a user-isolation DB regression pass in full CI. The following review entry is the original pre-fix evidence.

**Potential follow-up requested by owner, 2026-10-29:** app uploads could also be copied to Google Drive. This is a feasibility discussion; destination folder and a connected Google account with write permission are not configured. Existing public-folder import credentials are read-only. Proposed design: background copy with retry/status, separate destination or source-ID tracking to avoid re-import loops. No reverse-sync code or Google writes were performed. Google API reference: https://developers.google.com/workspace/drive/api/guides/manage-uploads and https://developers.google.com/workspace/drive/api/guides/ref-roles.

**2026-10-29, Codex review of completed Antigravity work:** independent `npx tsc -b` passed; frontend tests passed 7 suites / 20 tests; backend Codex/DeepSeek/Mapping/Sniff filter passed 36 tests; `BASE_URL=http://127.0.0.1:5182 node e2e/features-567.mjs` passed desktop and phone. Two additional browser reproductions exposed remaining issues: (1) `FileView.tsx:120,133` constructs cards with `saved:false`, so an already-bookmarked question shows Save rather than Saved in the reader's Viewer; backend hydration can return saved=true, but reader cards bypass it. (2) `FileView.tsx:146,153` retains the selected topic on direct client-side file-route changes; switching to a file without that topic rendered zero images and “0 OF 2 PAGES” with no matching topic chip to clear. Reset per-file filter state and hydrate bookmark status before merging. Reproduction scripts were executed in memory against mocked APIs; no source test or application code was changed in this review. Vite test server was local only. The earlier undefined-items and fallback-immutable-cache findings are fixed in the reviewed source. No merge/deploy/VPS change was performed.

**PR #29 deployed:** the five-minute scanner and per-folder controls are live, with master switch off and no selected folders. Enabling folders later imports new files only; existing held jobs stay held. No real discovery/import was run by Codex.

0. **Historical PR #32 deployment, superseded by PR #37:** Features #5–#7 plus bookmark hydration and per-file reader-state fixes passed full CI (67 backend / 20 frontend tests). Merge/image `56bd76b`; deploy run `40826986882` passed. Public health/home HTTP 200 and the new bundle are independently verified. No data import or held-job release was performed in this task.
0b. **Terra crop-only import empty slide text & producer flags (2026-10-28/29):** the external Terra import (`/data/terra-low-20261028`, 576 crop pages across 63 files) marked 322 slide pages that had no question regions as `extracted` while leaving their `pages.text` empty, omitting that slide content from text/keyword search. Forty-two producer-flagged pages also remain for review. Needs a separate targeted remediation task (do not conflate with `feat/reader-share-dupcheck`).
0c. **Reports, chat, Drive sync and sidebar remain deployed:** now included in PR #32 image `56bd76b585e6540bdd25645760b2a640dc37efa0`. Last SQL check before deployment: Drive sync 2 of 118 folders enabled.
0a. **PR #27:** full-file results and AI-authored chat with prewritten button follow-ups are deployed. PR #21 overlaps `ChatService.cs` and contains canned pick text rejected by the owner; preserve typed-course selection when reconciling. No other agent branch was modified.
1. **PR #21 — Claude Code, `claude/pick-fix-and-owner-login`:** the typed-pick fix, fixed-code student login and login subtitle removal remain open. It modifies `ChatService.cs` and writes a canned pick line, which conflicts with the owner's newer AI-written-replies decision. Coordinate/reconcile with the chat feature before either overlapping change is merged; do not edit or push to Claude's branch. Owner approval is required to merge.
2. **Caddy guard retirement:** Zarqa PR #6 (`abebc56`) is merged and present in local `C:/Projects/LostraAi` (ancestor check passed 2026-10-28). A later Zarqa deploy retaining the route has not been verified. After an owner-authorized Zarqa deploy, verify health remains good and the guard reports “Caddy already routes drivealt.tryzarqa.com”; only then propose removal of the temporary guard in a separate PR. Never run Zarqa's `deploy/deploy.sh`.
3. **Gateway process leak and configuration drift** (the PID-cap half is fixed by Zarqa PR #7, `pids_limit: 1024`; the leak itself is still open)**:** the last reviewed gateway could leave native Codex children running after timeout, and its live PID cap was raised to 1024 while the compose file still used 256 (2026-10-25 evidence above). Gateway source hash is unchanged; no fix is confirmed here. If health says signed out, inspect process pressure before asking for another login. The deployed `codex-model-choice` branch's merge status has not been rechecked in this orientation. Its limit matcher (`usage limit|rate limit|quota|too many requests`) may miss a plain 502; offline fallback handling needs review if such a response occurs.
4. **Failed document:** SQL still reports one Database Systems file with zero pages and failed status. Earlier diagnosis was `pdftoppm … Document stream is empty`. Repair/replacement needs a separate task; nothing was deleted or requeued.
5. **Answer text in local imports (hypothesis):** historical `import-local` copied PDF text from solution sheets. All in-scope pages are now read, but this does not prove all indexed answer text was removed or that every crop is visually correct. No answer content was retrieved in these checks. The rule remains: show intact paper/crops; no AI writes answers in any text field.
6. **Usage-limit report:** actual 429 pauses occurred during the import (dated evidence in section 2). The exact cumulative wall-clock delay and last in-scope completion time are unknown. Current queued jobs are held by owner instruction, not AI pauses; do not relaunch or requeue them. Do not add overlapping per-file retry intervals as independent elapsed time. Earlier notes named the automation `monitor-drivealt-import-and-codex-usage-limits`; its present configuration was not checked here.
7. **README and embeddings:** README's model table is current, but deploy instructions and the Gemini secret/offline descriptions need review. The 2026-10-24 import finished 105 files without embeddings using keyword search; present embedding coverage was not queried. Paid Gemini usage remains an owner decision.
8. **Other-checkout prompt (unverified):** the 2026-10-24 note about `C:/Users/abdul/DriveAlt` had a prompt excluding answers from boxes. That checkout belongs to another agent and was not inspected or changed here.
9. **Owner decisions pending:** sign-in requirement; whether to enable paid Gemini usage; any Docker cache pruning. No approval is implied by this orientation.

## 8. Continuation plan

**Current task:** PR #46 AI-only intent code `94046e1` passed full online CI; only this final documentation result record is newer. Once its routine CI rerun passes, stop monitoring and hand back for owner review without another documentation commit. No local testing; no merge/deploy authorization. Merge #45 first. Unscripted live checks in English/Arabic, including negation and file requests, remain for an authorized deployment.

**Current:** server rollout is complete (run `40878875382`); this new docs-only restart record needs a PR and an owner-approved `[skip ci]` merge. Continue `/root/terra_year1_resume` locally with the workload audit, timed photo batches and checkpoints. Before Year 1 website import, validate finished artifacts/flags and design separate study-year/course mapping; never shoehorn it into Year 2 courses. Verify the next Zarqa deployment retains the gateway source and model/PID override. Do not rerun old papers or release held jobs implicitly.

**Completed documentation, Codex (PR #33 merged `60090bf` with `[skip ci]`):** PR #32 passed full CI (run `40826739134`, test job `123291392748`: 67 backend / 20 frontend) and merged under owner authorization. Main deploy run `40826986882` passed test/build/deploy. Public health and home returned HTTP 200 and served the new bundle. Merge this docs-only reconciliation with `[skip ci]` so it does not restart processing. Preserve PR #21 and unrelated `external-prep/` files.

**Historical PR #29 validation (deployment complete):** local backend build passed with 0 warnings/errors; 20 backend unit tests passed; 16 frontend tests, TypeScript and production build passed. EF reports no pending model changes. Full CI run `40661496361` on `83e7b26` passed: `Failed: 0, Passed: 51, Skipped: 0, Total: 51`, including two scripted-Drive/database cases, and frontend `16 passed`. `node e2e/drive-sync.mjs` passed on desktop and phone using a stand-in API; both screenshots were visually inspected after controls were styled. The final code head `d2be664` also passed full CI run `40661879269` (test job `122726648712`); this includes the disabled-folder assertion, original-name preservation and styled controls. Subsequent commits only record results/owner decisions. Docker is unavailable locally. No live import or paid AI call was made. Owner-authorized deployment is complete (run `40662469965`). Next: the owner can discover folders while paused, select desired folders and enable sync; Semester 2/ITS should stay off until explicitly selected.

**Follow-up:** From an unrestricted network, verify the running image, report table, `messages.more_commentary`, and held jobs. Confirm normal Cloudflare access from outside the university network and test DeepSeek behavior when authorized. Keep PR #21 coordinated with Claude Code.

| # | Step | Owner | Verify |
|---|---|---|---|
| 1 | **Done:** orient and reconcile stale handoff from GitHub/VPS evidence; docs-only correction | Codex, `codex/refresh-handoff-state` | `git diff --check`; Markdown-only diff; owner's eventual merge uses `[skip ci]` |
| 2 | **Owner decision:** PR #21 | Claude Code owns implementation; owner merges | Review CI on its current head; after authorized merge, verify typed course selection and student-view login |
| 3 | **Done for existing in-scope pages:** Semester 1 without ITS; preserve held work | Claude Code retains import ownership | 192 ready files, one empty failed document; zero unread in-scope pages; 190 jobs held |
| 4 | Verify the next authorized Zarqa deploy preserves Caddy, then consider retiring guard | owner, then agent | Local Zarqa contains `abebc56`; public health good; guard reports no repair needed |
| 5 | Diagnose gateway leak/config drift in the Zarqa repo, without deploying implicitly | unclaimed separate task | Tests plus comparison with live gateway and compose; owner approves VPS changes |
| 6 | Recover usage-limit timing if retained logs allow it; otherwise keep duration unknown | unclaimed separate task | Non-overlapping wall-clock intervals, with log timestamps |
| 7 | Review README, failed document and remaining text/crop risks | unclaimed separate tasks | Follow the answers rule; do not print solution content |

## 9. Resume check

Read the whole handoff first. Run independent checks against GitHub and the VPS; dated counts may legitimately improve, but explain discrepancies before further work. Never read secrets or the Codex auth volume.

Local Git (PowerShell; metadata checks only, no local test setup):

```powershell
git fetch -q
git status --short --branch
git merge-base --is-ancestor 1e13a87ec9b5833e14ee893f8db7b5df90c4349c origin/main
# Expect exit 0: main contains the PR #34 features and PR #37 corrective rollout.
git log -1 --first-parent --format=%H origin/main -- . ':(exclude)*.md'
# Latest verified code/image: 083b6d1ef7cb1269b05a8d1e6081ec2a64be70fc (2026-10-30).
# Later docs-only merges must not change the running image.
git -C C:/Projects/LostraAi merge-base --is-ancestor abebc56 HEAD
# Expect exit 0: local Zarqa contains the permanent Caddy fix.
gh pr list -R AbdulazizRashdi06/DriveAlt --state open --json number,headRefName
# #21 claude/pick-fix-and-owner-login remains open unless owner merges/closes it.
# Latest open PRs: #21, #44, #45, #46 (this feature). Re-check rather than assuming this list is fixed.
# PRs #25–#29 are merged; PR #21 remains open. Later deployment handoff PRs are docs only.
# gh is unauthenticated in this Codex runtime: use GitHub search_prs with
# query repo:AbdulazizRashdi06/DriveAlt is:open instead. Do not read auth files.
```

Read-only SSH (run the following on the VPS in `~/drivealt`; Windows callers should send LF lines or strip CR from stdin):

```sh
grep -o '^DRIVEALT_IMAGE=.*' .env
curl --max-time 15 -s https://drivealt.tryzarqa.com/api/health
docker exec zarqa-codex-1 node -e 'fetch("http://127.0.0.1:8090/health").then(r=>r.text()).then(console.log)'
sha256sum /opt/zarqa/deploy/codex/gateway.mjs | cut -c1-16
# Expect image tag equal to the last first-parent code commit above,
# {"ok":true}, {"signedIn":true}, and ef0e322e17be2943.
docker compose --env-file generated.env --env-file .env exec -T db psql -U drivealt -d drivealt \
  -c '\d messages' \
  -c "select status, count(*), sum(page_count) pages from files group by status" \
  -c "select count(*) filter (where error is null and extracted) read_ok, count(*) filter (where error is not null) read_offline from pages" \
  -c "select state, count(*), count(*) filter (where error like 'Held:%') held, count(*) filter (where error like 'Paused:%') paused from jobs group by state order by state" \
  -c "select c.slug, count(*) filter (where not p.extracted) unread from pages p join files f on f.id=p.file_id join courses c on c.id=f.course_id where c.semester=1 and c.slug <> 'its' group by c.slug order by c.slug" \
  -c "select label, count(*) from regions group by label order by 1"
# Latest verified image: 083b6d1; read-only laptop SSH succeeded 2026-10-30.
# Last SQL verified before PR #32: files ready 400, reading 2, failed 1; pages extracted 9666, unread 7
# (note: 322 Terra-imported slide pages without regions have empty text, and not every page has an embedding);
# jobs: done 484, queued 2, failed 1; Drive sync folders: 2 enabled of 118.
# After the owner merges/deploys the chat feature, messages also has more_commentary;
# after the report feature, to_regclass('public.material_reports') returns material_reports.
# After PR #29, drive_sync_states has id=1, enabled=false, scan_requested=false (until the owner changes it).
docker compose --env-file generated.env --env-file .env logs --since 24h --no-log-prefix app 2>&1 | grep -c 'AI paused'
# Last verified: 0. grep exits 1 when there are no matches; that is expected.
```

SSH identity: `~/.ssh/zarqa_vps`, account `deploy@173.249.40.122`. Port 22 can be blocked on campus Wi-Fi. These checks are reads; rollback commands below mutate the VPS and require owner approval.

**Rollback:**
- **Gateway:** `cd /opt/zarqa && cp -p deploy/codex/gateway.mjs.bak-before-sol deploy/codex/gateway.mjs && docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build --no-deps codex`
- **Page reading back to DeepSeek:** `gh variable delete CODEX_BASE_URL -R AbdulazizRashdi06/DriveAlt`, then `gh workflow run deploy.yml -R AbdulazizRashdi06/DriveAlt --ref main`
- **Caddy:** restore the newest `Caddyfile.bak-drivealt-*` (or `Caddyfile.bak-before-drivealt`), then `docker exec zarqa-caddy-1 caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile`
- **Re-logging into the ChatGPT plan** (owner only): `cd /opt/zarqa && docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec codex codex login --device-auth`
