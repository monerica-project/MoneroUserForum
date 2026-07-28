#!/usr/bin/env bash
# deploy.sh — publish the web app, sync to the deploy host, restart the service.
# One-time infra (Postgres DB, /etc/monerouserforum/db.env, systemd unit, nginx
# + Let's Encrypt cert) is already provisioned; this handles ongoing deploys.
# EF migrations are applied automatically on app startup (Program.cs MigrateAsync).
#
# Set the SSH host/alias via the DEPLOY_HOST env var, or put it in an untracked
# CI/deploy.env (gitignored):  DEPLOY_HOST=your-ssh-host
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"

# Optional local, untracked overrides (e.g. DEPLOY_HOST).
[ -f "$SCRIPT_DIR/deploy.env" ] && . "$SCRIPT_DIR/deploy.env"
VPS="${DEPLOY_HOST:?Set DEPLOY_HOST (SSH host/alias) via env or CI/deploy.env}"
APP="monerouserforum"
PORT="5230"
DOMAIN="monerouserforum.com"
WEB_PROJECT="$ROOT/src/MoneroUserForum.Web/MoneroUserForum.Web.csproj"
OUT="$ROOT/publish/web"

echo "==> Publishing $APP (linux-x64, framework-dependent)"
rm -rf "$OUT"   # clean so deleted assets (e.g. old logos) don't linger and get re-synced
dotnet publish "$WEB_PROJECT" -c Release -r linux-x64 --self-contained false -o "$OUT" --nologo -v q

echo "==> Raising the \"Updating\" splash page"
ssh "$VPS" "sudo touch /var/www/$APP-maint/maintenance.on"

echo "==> Syncing to $VPS:/opt/$APP"
rsync -az --delete --exclude 'wwwroot/uploads/' --rsync-path="sudo rsync" "$OUT/" "$VPS:/opt/$APP/"
ssh "$VPS" "sudo chown -R webapp:webapp /opt/$APP \
  && sudo cp /opt/$APP/wwwroot/img/monerouserforum.png /var/www/$APP-maint/maint-logo.png 2>/dev/null || true \
  && sudo systemctl restart $APP"

echo "==> Waiting for the app to respond on :$PORT"
ok=""
for _ in $(seq 1 25); do
  code=$(ssh "$VPS" "curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$PORT/" 2>/dev/null || true)
  if [ "$code" = "200" ]; then ok="1"; echo "    OK: app is responding"; break; fi
  sleep 2
done
if [ -z "$ok" ]; then
  echo "    ERROR: app did not come up — the Updating page is left showing."
  echo "    Check: ssh $VPS sudo journalctl -u $APP -n 50"
  exit 1
fi

echo "==> Clearing the \"Updating\" splash page"
ssh "$VPS" "sudo rm -f /var/www/$APP-maint/maintenance.on"

echo "==> Smoke test https://$DOMAIN/"
echo "    HTTP $(curl -s -o /dev/null -w '%{http_code}' "https://$DOMAIN/")"
echo "==> Deploy complete."
