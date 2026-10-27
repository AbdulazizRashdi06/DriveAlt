#!/usr/bin/env bash
# Runs ON THE VPS (piped over ssh by .github/workflows/deploy.yml and caddy-guard.yml).
#
# DriveAlt is served by the Zarqa stack's Caddy (/opt/zarqa/deploy/Caddyfile). Zarqa's deploy uploads its own
# deploy/Caddyfile over that file, which has dropped DriveAlt's site before (Cloudflare 525, 2026-10-26). This script
# puts DriveAlt's blocks back when they're missing, and changes nothing when they're there.
#
# Safety, because the same Caddy serves Zarqa, Uni Schedule and Zoor:
#   - edits a copy and validates it with the running Caddy's own image before touching the live file;
#   - keeps a timestamped backup and writes in place (same inode), so Caddy's single-file bind mount sees the change;
#   - reloads gracefully (no restart); on any failure puts the backup back and reloads again.
set -euo pipefail

ZARQA_DIR="${ZARQA_DIR:-/opt/zarqa}"
HOST="${DRIVEALT_HOST:-drivealt.tryzarqa.com}"
UPSTREAM="${DRIVEALT_UPSTREAM:-drivealt:8080}"
LIVE="$ZARQA_DIR/deploy/Caddyfile"

if [ ! -f "$LIVE" ]; then
  echo "::warning::$LIVE not found; not checking Caddy."
  exit 0
fi

caddy_id=$(docker ps -q --filter "label=com.docker.compose.service=caddy" --filter "label=com.docker.compose.project.working_dir=$ZARQA_DIR/deploy" | head -1)
[ -n "$caddy_id" ] || caddy_id=$(docker ps -q --filter "label=com.docker.compose.service=caddy" | head -1)
if [ -z "$caddy_id" ]; then
  echo "::error::No running Caddy container found."
  exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
cand="$work/Caddyfile"
cp "$LIVE" "$cand"
changed=""

# Global options: trust Cloudflare's ranges and take the visitor's IP from CF-Connecting-IP (DriveAlt is proxied by
# Cloudflare; the sign-in rate limit is keyed by client IP). Only added when the file has no global block of its own.
first_line=$(grep -vE '^[[:space:]]*(#|$)' "$cand" | head -1 | tr -d '[:space:]')
if [ "$first_line" != "{" ]; then
  {
    cat <<'EOF'
# Added by DriveAlt's deploy (deploy/ensure-caddy.sh): Cloudflare proxies drivealt.tryzarqa.com.
# Ranges from https://www.cloudflare.com/ips-v4 and /ips-v6 (checked 2026-10-26).
{
	servers {
		trusted_proxies static 173.245.48.0/20 103.21.244.0/22 103.22.200.0/22 103.31.4.0/22 141.101.64.0/18 108.162.192.0/18 190.93.240.0/20 188.114.96.0/20 197.234.240.0/22 198.41.128.0/17 162.158.0.0/15 104.16.0.0/13 104.24.0.0/14 172.64.0.0/13 131.0.72.0/22 2400:cb00::/32 2606:4700::/32 2803:f800::/32 2405:b500::/32 2405:8100::/32 2a06:98c0::/29 2c0f:f248::/32
		client_ip_headers CF-Connecting-IP
	}
}

EOF
    cat "$LIVE"
  } > "$cand"
  changed="global Cloudflare block"
fi

# DriveAlt's site.
host_re=$(printf '%s' "$HOST" | sed 's/\./\\./g')
if ! grep -qE "^[[:space:]]*([^#]*[ ,])?${host_re}([ ,{]|$)" "$cand"; then
  cat >> "$cand" <<EOF

# >>> drivealt
# Added by DriveAlt's deploy (deploy/ensure-caddy.sh). DriveAlt runs as container "drivealt" on zarqa_default.
# Cloudflare proxies this host with SSL "Full (strict)", so Caddy must keep serving a valid certificate for it.
$HOST {
	encode zstd gzip
	reverse_proxy $UPSTREAM {
		header_up X-Forwarded-For {client_ip}
		flush_interval -1
	}
}
# <<< drivealt
EOF
  changed="${changed:+$changed and }$HOST site"
fi

if [ -z "$changed" ]; then
  echo "Caddy already routes $HOST."
  exit 0
fi
echo "Caddy is missing the $changed; restoring."

# Validate the candidate with the running Caddy's image and environment (ZARQA_DOMAIN is used by the file).
image=$(docker inspect --format '{{.Config.Image}}' "$caddy_id")
domain=$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$caddy_id" | sed -n 's/^ZARQA_DOMAIN=//p' | head -1)
chmod 644 "$cand"
if ! docker run --rm -e "ZARQA_DOMAIN=$domain" -v "$cand:/etc/caddy/Caddyfile:ro" "$image" \
     caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >"$work/validate.log" 2>&1; then
  echo "::error::The restored Caddyfile did not validate; the live file was not touched."
  tail -20 "$work/validate.log"
  exit 1
fi

if [ ! -w "$LIVE" ] || [ ! -w "$(dirname "$LIVE")" ]; then
  echo "::error::$(id -un) can't write $LIVE; restore DriveAlt's Caddy block by hand (see HANDOFF.md)."
  exit 1
fi
backup="$LIVE.bak-drivealt-$(date -u +%Y%m%dT%H%M%SZ)"
cp -p "$LIVE" "$backup"
cat "$cand" > "$LIVE"   # in place: keeps the inode the container's bind mount points at

reload() { docker exec "$caddy_id" caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile; }

if ! docker exec "$caddy_id" grep -qF "$HOST" /etc/caddy/Caddyfile; then
  # The container is on an older copy of the file (it was replaced, not rewritten): recreate only Caddy.
  echo "Caddy's mount is stale; recreating the caddy container."
  (cd "$ZARQA_DIR" && docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --no-deps --force-recreate caddy)
elif ! reload; then
  echo "::error::Caddy rejected the reload; putting the previous file back."
  cat "$backup" > "$LIVE"
  reload || true
  exit 1
fi

echo "Restored the $changed (backup: $backup)."
