#!/usr/bin/env bash
# Run on the GitHub runner after SSH setup. Only the shared codex service is rebuilt.
set -euo pipefail
host="${1:-vps}"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
before=5da0c724e36f45626f438728030eb357e02c7a45163425c8ad9562bbcc3f607f
after=ef0e322e17be2943b78e1fe65abb919b6d64bbb7dbf698d6fb379d884a49aef3
scratch="$(mktemp -d /tmp/drivealt-terra.XXXXXX)"
mkdir -p "$scratch/deploy/codex"
scp "$host:/opt/zarqa/deploy/codex/gateway.mjs" "$scratch/deploy/codex/gateway.mjs"
current="$(sha256sum "$scratch/deploy/codex/gateway.mjs" | cut -d' ' -f1)"
if [[ "$current" != "$before" && "$current" != "$after" ]]; then
  echo 'Gateway source differs from the reviewed baseline; refusing to overwrite it.' >&2
  exit 1
fi
if [[ "$current" == "$before" ]]; then
  git -C "$scratch" init -q
  git -C "$scratch" config core.autocrlf false
  git -C "$scratch" apply --check "$script_dir/codex-terra-batches.patch"
  git -C "$scratch" apply "$script_dir/codex-terra-batches.patch"
fi
patched_hash="$(sha256sum "$scratch/deploy/codex/gateway.mjs" | cut -d' ' -f1)"
if [[ "$patched_hash" != "$after" ]]; then
  echo "Patched gateway checksum mismatch: expected $after, got $patched_hash" >&2
  exit 1
fi
CODEX_GATEWAY_MODULE="$scratch/deploy/codex/gateway.mjs" CODEX_MODELS=gpt-6-luna,gpt-6.1-sol,gpt-6.1-terra \
  node --test "$script_dir/codex-terra-gateway.test.mjs"
scp "$scratch/deploy/codex/gateway.mjs" "$host:drivealt/gateway.terra-staged.mjs"
ssh "$host" bash -s -- "$current" "$after" <<'REMOTE'
set -euo pipefail
expected="$1"; target="$2"
cd /opt/zarqa
source=deploy/codex/gateway.mjs
test "$(sha256sum "$source" | cut -d' ' -f1)" = "$expected"
running="$(docker exec zarqa-codex-1 sha256sum /gateway/gateway.mjs | cut -d' ' -f1)"
models="$(docker exec zarqa-codex-1 node -e 'console.log(process.env.CODEX_MODELS || "gpt-6-luna,gpt-6.1-sol,gpt-6.1-terra")')"
if [[ "$expected" == "$target" && "$running" == "$target" && ",$models," == *,gpt-6.1-terra,* ]]; then
  echo 'Terra batch gateway is already installed; no restart needed.'
  exit 0
fi
previous_image="$(docker inspect --format '{{.Image}}' zarqa-codex-1)"
pids="$(docker inspect --format '{{.HostConfig.PidsLimit}}' zarqa-codex-1)"
[[ "$pids" =~ ^-?[0-9]+$ ]]
backup="$source.bak-terra-$(date -u +%Y%m%dT%H%M%SZ)"
cp -p "$source" "$backup"
# JSON is valid Compose YAML. Read only the non-secret model allow-list; never inspect auth files or volumes.
docker exec -e DRIVEALT_GATEWAY_PIDS="$pids" zarqa-codex-1 node -e '
  const models = new Set((process.env.CODEX_MODELS || "gpt-6-luna,gpt-6.1-sol").split(",").map(m=>m.trim()).filter(Boolean));
  models.add("gpt-6.1-terra");
  console.log(JSON.stringify({services:{codex:{pids_limit:Number(process.env.DRIVEALT_GATEWAY_PIDS),environment:{CODEX_MODELS:[...models].join(",")}}}}));
' > deploy/drivealt-terra.override.yml
rollback() {
  trap - ERR
  set +e
  cp -p "$backup" "$source"
  printf '{"services":{"codex":{"image":"%s"}}}\n' "$previous_image" > deploy/drivealt-terra.rollback.yml
  docker compose -f deploy/docker-compose.yml -f deploy/drivealt-terra.override.yml -f deploy/drivealt-terra.rollback.yml \
    --env-file deploy/.env up -d --no-build --no-deps codex
  echo 'Terra gateway verification failed; restored its prior source and image.' >&2
  exit 1
}
trap rollback ERR
cp -p "$HOME/drivealt/gateway.terra-staged.mjs" "$source"
docker compose -f deploy/docker-compose.yml -f deploy/drivealt-terra.override.yml --env-file deploy/.env up -d --build --no-deps codex
for attempt in $(seq 1 30); do
  if docker exec zarqa-codex-1 node -e 'fetch("http://127.0.0.1:8090/health").then(r=>r.json()).then(j=>{if(!j.signedIn)process.exit(1)}).catch(()=>process.exit(1))'; then break; fi
  sleep 2
done
# A tiny subscription-backed readiness check, with no paper content and no API key.
docker exec -i zarqa-codex-1 node --input-type=module <<'CANARY'
const body={model:'gpt-6.1-terra',reasoning_effort:'low',messages:[{role:'system',content:'Return the requested JSON only.'},
  {role:'user',content:[{type:'text',text:'Return {"ready":true}.'}]}],response_format:{type:'json_schema',json_schema:{name:'readiness',schema:{type:'object',additionalProperties:false,required:['ready'],properties:{ready:{type:'boolean'}}}}}};
const r=await fetch('http://127.0.0.1:8090/v1/chat/completions',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(300000)});
if(!r.ok)throw new Error(`Terra readiness HTTP ${r.status}`);
const j=await r.json();if(JSON.parse(j.choices?.[0]?.message?.content??'{}').ready!==true)throw new Error('Terra readiness failed');
console.log('Terra low readiness passed.');
CANARY
trap - ERR
echo 'Terra batch gateway installed; existing models and process limit preserved.'
REMOTE
