# AGENTS.md

DriveAlt is the GUtech Year 2 batch drive as a web app: ASP.NET Core 10 + React + Postgres/pgvector, live at https://drivealt.tryzarqa.com.

Several agents work in this repo (Claude Code, Codex and others), often one after another and sometimes at the same time. This file holds the rules for all of them. `CLAUDE.md` only imports it.

## Handoff: read it first, update it after every change

[`HANDOFF.md`](HANDOFF.md) is the shared memory between agents. It describes what is true **now**: what's deployed, what's open, what was decided, and what's next.

### When you start
1. Read `HANDOFF.md` in full before you read or change anything else.
2. Run its **Resume check** commands. If the output doesn't match, the page is stale. Find out what changed (git log, `gh pr list`, the VPS) and fix the page before you do anything else.
3. Check **Continuation plan** and **Open threads** for work another agent has claimed. Don't take a step that is claimed by someone else and still in progress.

### After every change
Every commit, PR, merge, deploy, VPS change, secret or variable change, or decision from the owner counts. Update `HANDOFF.md` **in the same branch or commit as the change**, before you stop:
- **State of the world:** what's committed, deployed and running, and the test results with their real output.
- **What was done:** add a dated entry (`YYYY-MM-DD, <agent>`) with the outcome, not a transcript.
- **User decisions** and **Judgment calls:** add any new ones.
- **File inventory**, **Open threads** and **Continuation plan:** keep them current. Remove what's finished and claim what you're doing (`agent, branch`).
- **Resume check:** fix it so its expected output matches the new state.
- **Header:** set the date, branch, base commit and who updated it.

If your session ends mid-task, say so in the handoff, along with your working hypotheses. Never leave it looking tidier than it is.

**Merges and deploys:**
- **Write for after the merge:** in your PR, write the handoff for the state *after* your PR merges and deploys, because the merge itself is the owner's step.
- **Merge-proof checks:** write Resume checks that survive later merges ("`main` contains `<sha>`", "the image tag equals `origin/main`"), not "`main` is exactly `<sha>`".
- **No direct pushes:** if a merge or deploy still leaves the page stale, fix it in a small docs PR. Never push to `main` directly.

### How the handoff is written
Based on the `handover` skill, adapted for a Markdown file kept in the repo:
- **Reader:** someone with zero context. No "as discussed", no shorthand from your chat, no pronoun whose meaning only existed in your session.
- **Evidence:** any claim about *why* cites a `file:line`, a command and its output, or a log line. Without one, label it a **hypothesis**.
- **Test status:** pass, fail or **unknown**, with the command and its last output. Unknown is fine; a guess isn't.
- **Broken on purpose:** flag anything disabled, stubbed or temporary loudly, with what undoes it.
- **No secrets:** name the variable and where it lives, never its value. The repo is **public**.
- **Same file, rewritten:** keep one `HANDOFF.md` and update it. Don't add parallel handoff files.
- **Sections, in this order:**
  1. Header
  2. State of the world
  3. What was done
  4. User decisions
  5. Judgment calls
  6. File inventory
  7. Open threads
  8. Continuation plan
  9. Resume check

## Working together

- **One task, one branch, one PR.** Name branches after the agent and task (`codex/…`, `claude/…`, `feat/…`). Before starting, check `gh pr list` and the handoff for open work that touches the same files.
- **Conflicts:** don't push to another agent's branch or rewrite its commits. If two branches change the same files, coordinate through the handoff's Open threads.
- **`HANDOFF.md` conflicts:** keep both sides' facts and re-check them against reality. Don't pick a side blindly.
- **Open handoff PRs:** if one is already open, build your branch on top of its commit instead of starting a parallel one, and say in your PR that it must be merged after that one.
- **Deploying:** merging to `main` deploys (`.github/workflows/deploy.yml`, about 2–3 min). Don't merge without the owner's OK.

## Rules

- **Secrets:** never print, log, commit or paste secret values. No `cat .env` and no `echo $KEY`. To check a key is set, count it (`grep -c '^Name=.' .env`). Pipe new keys straight into `gh secret set`. Never open the Codex login (`auth.json` or the `zarqa_codex_auth` volume).
- **Ask the owner first** before merging, before any VPS change that isn't a read, before deleting anything, before DNS or Caddy changes, and before anything that costs money.
- **Shared VPS:** the VPS also runs Zarqa, Uni Schedule and Zoor. Rebuild only the container you mean to. **Never run Zarqa's `deploy/deploy.sh`**: it uploads the owner's whole local LostraAi tree.
- **Answers:** never type out an answer, solution or hint, whether as the bot, in summaries or in crops' text. Answers stay on the original page. Don't black out, blur or hide anything in images.
- **Commits:** keep them small, with a message that explains *why*.

## Commands

```bash
dotnet build                                   # backend
dotnet test --filter "FullyQualifiedName~CodexTests|FullyQualifiedName~DeepSeekTests|FullyQualifiedName~MappingTests|FullyQualifiedName~GuardrailTests"   # unit tests, no DB
dotnet test                                    # everything; API tests need Postgres + pgvector (DRIVEALT_TEST_PG)
cd web && npm ci && npm test && npx tsc -b     # frontend
```

CI (`.github/workflows/deploy.yml`) runs all of these on every PR. `README.md` covers features, local setup and the import. `HANDOFF.md` covers hosting, the AI setup and current state.
