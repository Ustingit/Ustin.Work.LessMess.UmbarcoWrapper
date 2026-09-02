#!/usr/bin/env bash
# End-to-end check against a running `docker compose up` stack.
# Drives the wrapper UI (register -> login -> list -> logout) and then asserts
# the wrapper audit trail recorded every step.
set -euo pipefail

WRAP="${WRAP_URL:-http://localhost:8090}"
UMBRACO="${UMBRACO_URL:-http://localhost:8080}"
USER="itest_$(date +%s)"
EMAIL="${USER}@wrapper.local"
PASS='Wrapper-Test-1!'
JAR="$(mktemp)"
trap 'rm -f "$JAR" "$JAR".body' EXIT

say() { printf '\n=== %s\n' "$1"; }
fail() { printf '\nFAIL: %s\n' "$1" >&2; exit 1; }

token() {
  # pull the antiforgery token out of a freshly fetched form
  curl -sS -c "$JAR" -b "$JAR" "$1" \
    | grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' \
    | sed -E 's/.*value="([^"]*)".*/\1/' | head -n1
}

status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }

say "wait for services"
for i in $(seq 1 60); do
  if curl -fsS "$WRAP/health" >/dev/null 2>&1 && curl -fsS "$UMBRACO/umbraco/" >/dev/null 2>&1; then
    echo "wrapper + umbraco are up"; break
  fi
  [ "$i" = 60 ] && fail "services did not become healthy in time"
  sleep 5
done

say "register $USER via the wrapper"
T="$(token "$WRAP/Register")"
[ -n "$T" ] || fail "no antiforgery token on /Register"
CODE="$(curl -sS -c "$JAR" -b "$JAR" -o "$JAR".body -w '%{http_code}' \
  --data-urlencode "Input.Username=$USER" \
  --data-urlencode "Input.Email=$EMAIL" \
  --data-urlencode "Input.Password=$PASS" \
  --data-urlencode "Input.ConfirmPassword=$PASS" \
  --data-urlencode "__RequestVerificationToken=$T" \
  "$WRAP/Register")"
echo "  -> HTTP $CODE"
[ "$CODE" = 302 ] || { cat "$JAR".body; fail "register did not redirect (got $CODE)"; }

say "login"
T="$(token "$WRAP/Login")"
CODE="$(curl -sS -c "$JAR" -b "$JAR" -o "$JAR".body -w '%{http_code}' \
  --data-urlencode "Input.Username=$USER" \
  --data-urlencode "Input.Password=$PASS" \
  --data-urlencode "__RequestVerificationToken=$T" \
  "$WRAP/Login")"
echo "  -> HTTP $CODE"
[ "$CODE" = 302 ] || { cat "$JAR".body; fail "login did not redirect (got $CODE)"; }
grep -q "wrapper_sid" "$JAR" || fail "no wrapper_sid session cookie after login"

say "list translations"
BODY="$(curl -sS -c "$JAR" -b "$JAR" "$WRAP/Translations")"
echo "$BODY" | grep -q "General.Save"   || fail "translations page missing General.Save"
echo "$BODY" | grep -q "Speichern"      || fail "translations page missing the de-DE value 'Speichern'"
echo "$BODY" | grep -qE "[0-9]+ entries from Umbraco" || fail "translations page missing the count line"
echo "  -> $(echo "$BODY" | grep -oE '[0-9]+ entries from Umbraco' | head -n1)"

say "logout"
T="$(token "$WRAP/Login")"   # any wrapper page carries a token; /Login is always reachable
# the logout form lives in the layout header of an authenticated page:
T="$(curl -sS -c "$JAR" -b "$JAR" "$WRAP/Translations" \
  | grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' \
  | sed -E 's/.*value="([^"]*)".*/\1/' | head -n1)"
CODE="$(curl -sS -c "$JAR" -b "$JAR" -o /dev/null -w '%{http_code}' \
  --data-urlencode "__RequestVerificationToken=$T" "$WRAP/Logout")"
echo "  -> HTTP $CODE"
[ "$CODE" = 302 ] || fail "logout did not redirect (got $CODE)"

say "translations after logout must bounce to /Login"
CODE="$(curl -sS -c "$JAR" -b "$JAR" -o /dev/null -w '%{http_code}' "$WRAP/Translations")"
LOC="$(curl -sS -c "$JAR" -b "$JAR" -o /dev/null -D - "$WRAP/Translations" | tr -d '\r' | awk 'tolower($1)=="location:"{print $2}')"
echo "  -> HTTP $CODE Location: $LOC"
[ "$CODE" = 302 ] || fail "expected redirect after logout (got $CODE)"
case "$LOC" in */Login*) ;; *) fail "expected redirect to /Login, got '$LOC'";; esac

say "audit trail"
AUDIT="$(docker compose exec -T wrapper cat /data/audit.log)"
echo "$AUDIT" | grep "\"username\":\"$USER\"" | grep -q '"event":"register"'          || fail "audit missing register"
echo "$AUDIT" | grep "\"username\":\"$USER\"" | grep -q '"event":"login"'             || fail "audit missing login"
echo "$AUDIT" | grep "\"username\":\"$USER\"" | grep -q '"event":"translations.list"' || fail "audit missing translations.list"
echo "$AUDIT" | grep "\"username\":\"$USER\"" | grep -q '"event":"logout"'            || fail "audit missing logout"
echo "$AUDIT" | grep "\"username\":\"$USER\"" | grep -qE '"ip":"[0-9a-f.:]+"'         || fail "audit lines have no client IP"
echo "$AUDIT" | grep "\"username\":\"$USER\""

printf '\nPASS: full register -> login -> list -> logout flow is traceable\n'
