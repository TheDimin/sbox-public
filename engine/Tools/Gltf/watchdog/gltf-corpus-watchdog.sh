#!/bin/sh
# External hang watchdog for the in-editor glTF corpus run (gltf_corpus_run).
# The editor starts this process. It only observes: it never kills or restarts the editor.
#
# Usage: gltf-corpus-watchdog.sh <editor-pid> <output-dir>
# Reads  <output-dir>/heartbeat.txt  line 1: deadline (unix seconds), line 2: active case ids,
#                                    line 3: "done" when the run has finished.
# Writes <output-dir>/watchdog.json  when the editor exits or a deadline passes with no new heartbeat.
# The next gltf_corpus_run reads watchdog.json, records the case(s) as hung, and skips them.
set -u
pid="$1"; out="$2"
beat="$out/heartbeat.txt"; report="$out/watchdog.json"
grace=60
rm -f "$report"

json_escape() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g' | tr '\n' ' '; }

write_report() {
	cases=$(sed -n 2p "$beat" 2>/dev/null)
	tmp="$report.tmp"
	printf '{"status":"%s","editorPid":%s,"cases":"%s","observedAt":%s,"deadline":%s}\n' \
		"$1" "$pid" "$(json_escape "$cases")" "$(date +%s)" "${2:-0}" > "$tmp" && mv -f "$tmp" "$report"
}

while :; do
	if ! kill -0 "$pid" 2>/dev/null; then
		[ "$(sed -n 3p "$beat" 2>/dev/null)" = "done" ] || write_report editor-exited
		exit 0
	fi
	[ "$(sed -n 3p "$beat" 2>/dev/null)" = "done" ] && exit 0
	deadline=$(sed -n 1p "$beat" 2>/dev/null)
	case "$deadline" in ''|*[!0-9]*) deadline=0 ;; esac
	if [ "$deadline" -gt 0 ] && [ "$(date +%s)" -gt $((deadline + grace)) ]; then
		write_report hung "$deadline"
		# Keep watching: if the editor recovers, the run clears this report with a newer heartbeat.
		while kill -0 "$pid" 2>/dev/null; do
			now_deadline=$(sed -n 1p "$beat" 2>/dev/null)
			[ "$now_deadline" != "$deadline" ] && { rm -f "$report"; break; }
			[ "$(sed -n 3p "$beat" 2>/dev/null)" = "done" ] && exit 0
			sleep 5
		done
	fi
	sleep 5
done
