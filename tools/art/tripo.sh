#!/usr/bin/env bash
# Tripo v3 API (https://developers.tripo3d.ai). Key: ~/.vibe/tripo_curl_key.conf, a curl config line
#   header = "Authorization: Bearer tsk_..."
# usage:
#   tripo.sh balance
#   tripo.sh upload <image or model>                      -> prints file_token
#   tripo.sh task <endpoint> <request.json> <out dir> <name>
#     POSTs the request to /v3/<endpoint>, polls until done, saves <name>.task.json and downloads
#     every *_url in the output as <name>.<ext> / <name>.<key>.<ext>. Prints the task id.
set -euo pipefail
API=https://openapi.tripo3d.ai/v3
CONF="$HOME/.vibe/tripo_curl_key.conf"
api() { curl -s --config "$CONF" "$@"; }
json() { node -e "const r=JSON.parse(require('fs').readFileSync(0,'utf8'));if(r.code!==0){console.error(JSON.stringify(r));process.exit(1)}console.log($1)"; }

case "$1" in
  balance)
    api "$API/account/balance" | json 'JSON.stringify(r.data)' ;;
  upload)
    api -F "file=@$2" "$API/files" | json 'r.data.file_token' ;;
  task)
    ENDPOINT="$2"; REQ="$3"; OUT="$4"; NAME="$5"
    mkdir -p "$OUT"
    TID=$(api -H "Content-Type: application/json" --data @"$REQ" "$API/$ENDPOINT" | json 'r.data.task_id')
    echo "task $TID" >&2
    while :; do
      api "$API/tasks/$TID" > "$OUT/$NAME.task.json"
      ST=$(json 'r.data.status+" "+(r.data.progress??"")' < "$OUT/$NAME.task.json")
      case "$ST" in
        success*) break ;;
        failed*|cancelled*) cat "$OUT/$NAME.task.json" >&2; exit 1 ;;
      esac
      echo "  $ST" >&2
      sleep 10
    done
    # Download every URL in output; the main model keeps <name>, others get <name>.<key>.
    json 'Object.entries(r.data.output||{}).filter(([k,v])=>typeof v==="string"&&v.startsWith("http")).map(([k,v])=>k+" "+v).join("\n")' \
      < "$OUT/$NAME.task.json" | while read -r KEY URL; do
        [ -n "$URL" ] || continue
        EXT=$(sed -E 's/\?.*//; s/.*\.//' <<<"$URL")
        if [ "$KEY" = model_url ]; then F="$OUT/$NAME.$EXT"; else F="$OUT/$NAME.${KEY%_url}.$EXT"; fi
        curl -s -o "$F" "$URL"
        ls -l "$F" >&2
      done
    json '"credits "+r.data.credits_consumed' < "$OUT/$NAME.task.json" >&2
    echo "$TID" ;;
  *) echo "usage: tripo.sh balance | upload <file> | task <endpoint> <req.json> <out dir> <name>" >&2; exit 2 ;;
esac
