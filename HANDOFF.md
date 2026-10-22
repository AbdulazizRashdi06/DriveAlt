# Handoff: deploy DriveAlt with GitHub Secrets

For a **local Claude Code session** that has the owner's GitHub (`gh` logged in) and SSH access to the Zarqa VPS. A cloud session built the app; it couldn't set GitHub secrets or reach the VPS, so the steps below are left for you.

## Where things are

- **Repo:** `AbdulazizRashdi06/DriveAlt`.
- **Branch:** all work is on `claude/elegant-keller-1n8kkd`. There is **no `main` branch yet**.
- **What it is:** ASP.NET Core 10 API + React SPA + Postgres/pgvector, shipped as one Docker image. See `README.md`.
- **Deploy pipeline:** `.github/workflows/deploy.yml`.
  - Tests run on every push and pull request.
  - On a push to `main` (or a manual run) it:
    1. builds the image;
    2. copies it to the VPS over SSH;
    3. writes `.env` on the server from GitHub Secrets;
    4. runs `docker compose up`;
    5. waits for `/api/health`.
- **Generated values:** the database password and sign-in secret are created on the VPS on the first deploy (`generated.env`). They are not GitHub secrets.

## Rules

- Never print, log, commit or paste secret values (no `echo $KEY`, no values in chat). Pipe them straight into `gh secret set`.
- Ask the owner before anything hard to undo: merging, changing DNS, or editing the VPS's existing web server config.
- The owner adds **`GEMINI_API_KEY` themselves**. Don't ask for it; you set everything else.

## Steps

**1. Get the code**
```bash
git clone https://github.com/AbdulazizRashdi06/DriveAlt && cd DriveAlt
git checkout claude/elegant-keller-1n8kkd
```

**2. Check the VPS.** Ask the owner for its host and SSH user if they aren't already in `~/.ssh/config`.
- It needs `docker` with the compose plugin, plus `openssl` and `curl`.
- The SSH user must be able to run `docker` without sudo (it should be in the `docker` group).
- Check that port 8080 on 127.0.0.1 is free. If it isn't, pick another port and set it as `APP_PORT` in step 5.

**3. Create a deploy key and set the VPS secrets**
```bash
ssh-keygen -t ed25519 -N '' -C drivealt-deploy -f ./drivealt_deploy
ssh <user>@<host> 'mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys' < ./drivealt_deploy.pub
gh secret set VPS_SSH_KEY  -R AbdulazizRashdi06/DriveAlt < ./drivealt_deploy
gh secret set VPS_HOST     -R AbdulazizRashdi06/DriveAlt --body '<host>'
gh secret set VPS_USER     -R AbdulazizRashdi06/DriveAlt --body '<user>'
# only if SSH isn't on 22:
gh secret set VPS_PORT     -R AbdulazizRashdi06/DriveAlt --body '<port>'
rm -f ./drivealt_deploy ./drivealt_deploy.pub
```

**4. Resend key** (sends the sign-in codes; `tryzarqa.com` is already verified in Resend):
- Create a **sending-only** API key restricted to `tryzarqa.com`, named `drivealt`. Use the Resend dashboard, or the Resend API/MCP if you have it.
- Then pipe it in without echoing it: `gh secret set RESEND_API_KEY -R AbdulazizRashdi06/DriveAlt`, and paste at the prompt.

**5. Variables** (not secret):
```bash
gh variable set UPLOADERS  -R AbdulazizRashdi06/DriveAlt --body '24-0161'
gh variable set PUBLIC_URL -R AbdulazizRashdi06/DriveAlt --body 'https://<subdomain>'   # confirm the subdomain with the owner, e.g. drive.tryzarqa.com
# optional: APP_PORT (default 8080), DEPLOY_DIR (default ~/drivealt), GEMINI_MODEL (default gemini-3.8-flash)
```

**6. Domain and reverse proxy** (ask the owner first):
- Point the subdomain's DNS at the VPS.
- Add a site to the VPS's existing nginx/Caddy that proxies to `127.0.0.1:8080`. The nginx example is in `README.md`; keep `proxy_buffering off` so chat replies stream.
- Use the proxy's usual way of getting HTTPS certificates.

**7. Gemini key.** Remind the owner to add `GEMINI_API_KEY` at https://github.com/AbdulazizRashdi06/DriveAlt/settings/secrets/actions. Also ask them to confirm the model ID: `gemini-3.8-flash` is the default and was never tested. If it's wrong, set the `GEMINI_MODEL` variable.

**8. Deploy.** With the owner's OK:
1. Open a PR from `claude/elegant-keller-1n8kkd` into a new `main` branch and make `main` the default branch.
2. Merge it. That push to `main` deploys.
3. Watch it with `gh run watch -R AbdulazizRashdi06/DriveAlt`.
4. If it fails, the last step prints the app logs. Fix, push, repeat.

**9. Check it works**
- Open the site and sign in with uni ID `24-0161`. The code arrives at `24-0161@student.gutech.edu.om`.
- Import the batch Drive (about 410 files; each page is one Gemini call). Run these on the VPS, in the deploy dir:
  ```bash
  docker compose --env-file generated.env --env-file .env exec app dotnet DriveAlt.Api.dll import-drive --dry-run
  docker compose --env-file generated.env --env-file .env exec app dotnet DriveAlt.Api.dll import-drive
  ```
- Watch progress in the app under Upload → Your uploads.

## Never verified (expect to fix things here)

- The Docker image has never been built: the cloud session had no Docker. The Release publish and the SPA build were checked separately.
- The workflow has never run on GitHub.
- Nothing has run against real Gemini; everything was tested in offline mode.
