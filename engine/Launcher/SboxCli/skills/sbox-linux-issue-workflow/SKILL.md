---
name: sbox-linux-issue-workflow
description: Triage sub-skill for suspected Linux-specific s&box engine bugs. Search Facepunch/sbox-public issues, PRs, newer upstream commits, and forks for an existing fix, validate or create a local fix, and prepare an upstream report for the user to submit. Use when a Linux failure appears during s&box development; not for general Linux administration or project-owned game bugs.
---

# s&box Linux issue workflow

Fix the local problem and hand the user an evidence-backed upstream report. If the `triage` skill is installed, follow its order of work (read, check existing fixes, trace, reproduce twice, test, fix, prove) and use this skill for the s&box Linux parts.

## Upstream boundary

Research, reproduce, fix, and validate locally without asking for permission at each step. Follow the branch conventions and editor resource rules.

Everything upstream is read-only or an unsubmitted draft. Never submit an issue, comment, reaction, or PR (draft PRs too), never push a branch or publish a patch, and never contact fork authors. You may open and fill an issue form. The user submits it. Approval of the research or the fix is not permission to publish.

## Establish the failure

- Record a minimal repro, expected and actual behavior, focused logs, the engine revision and build, and the Linux environment details that matter.
- Get a failing baseline before edits. If the repro is intermittent or unavailable, say so and keep the evidence.
- Decide the owner: engine, project, or local configuration. A failure seen on Linux is not proof that it is Linux-only. Say which platforms you tested.

## Search for an existing fix, in this order

Track two separate questions: is there a matching report, and is there a usable fix? A matching issue without a fix does not end the search. Reproduce and diagnose in parallel with the search, but do not write your own fix before the search is done.

1. **Upstream issues and PRs.** Search [Facepunch/sbox-public](https://github.com/Facepunch/sbox-public) open and closed issues and open, merged, and closed-unmerged PRs. Use symptoms, exact errors, affected files and symbols, and other terms. Read the discussions, linked commits, and patches, not only titles. A closed issue does not prove a fix landed. An unmerged PR can still hold a usable fix.
2. **Newer Facepunch/master commits,** including commits with no issue or PR. Find the local revision, refresh the upstream tip, and inspect the commits missing locally by ancestry and merge-base, not by date. Screen messages and files, and read plausible patches, including related changes outside the failing file. Record the local SHA, the upstream tip SHA, the range, and findings. A stale tracking ref or an empty issue search does not count.
3. **Validate the upstream candidates** against the local repro. Prefer bringing in or adapting an upstream fix. Do not upgrade the whole branch to test one. If it works, skip the fork search. If it is partial, carry that evidence forward.
4. **Forks**, only when upstream has no usable fix (see below).
5. **Your own fix**, when nothing above solves it. Use the partial fixes and clues you found.

An access failure is not a negative result. If an upstream check stays blocked, report that gate as incomplete and continue local work. Do not start the fork search on top of an incomplete upstream gate.

## Fork search

Spawn one cheap read-only research subagent (`gpt-6-luna` with `fork_turns="none"` when available, otherwise the cheapest suitable model). Give it the symptom, repro and environment evidence, local revision, affected code and errors, upstream findings, and these rules. No builds, editor launches, remote writes, or further delegation. Continue local work while it searches, but read its report before you conclude.

**Coverage rules:**

- Enumerate all accessible forks, with full pagination, and every accessible branch head, not only default branches.
- Skip a fork only when no branch contains a Facepunch/master commit from the last three calendar months, judged by upstream ancestry and the upstream commit date, not the fork's push date. Fast-forwards and rebases count. If freshness is unknown, investigate instead of skipping. Report the cutoff and the excluded forks with evidence.
- For each kept fork, list every commit reachable from any branch or tag that upstream history (all branches and tags) does not contain. Detect truncated API comparisons and use read-only fetches into a scratch repo when needed. Deduplicate SHAs across forks and keep their sources.
- Screen every unique commit: message and files, and the patch whenever relevance is plausible. Look for full fixes, partial fixes, workarounds, diagnostics, and tests. Do not stop at the first candidate. The three-month rule filters forks, not commits.
- Stay economical with cached inventories, shared Git objects, batched reads, and deduplication, not with caps on forks or commits.

**Access:** first find a working route for fork enumeration and commit inspection. An unsupported connector endpoint is a tool limit, not proof that GitHub is down. On failure, the subagent reports the exact operation, error category (unsupported route, auth, rate limit, timeout, DNS), and context, and does not repeat failed requests. The main agent owns recovery: check connector capabilities and local Git remotes, and whether the failure is sandbox-specific. Use the supported approval path for a bounded host-side check where permitted. Never bypass a denial through another transport or credentials, expose tokens, or change network or security settings. When the main agent has access the subagent lacks, gather the fork and commit inventory into local files and resume the same subagent on them.

If access stays blocked, report **fork screening blocked before enumeration** (zero screened) or **partial fork screening**, with the routes tried, the blockers, and a resumable checkpoint. That is never a completed search or a negative finding.

**Subagent report:** search date, cutoff, upstream refs used, fork and ref counts, excluded and unknown forks, unique and screened commit counts, incomplete pages; ranked candidates with links, SHAs, authors, files, and the evidence that links each one to the failure, labeled full fix, partial fix, workaround, or clue; dependencies and checks for the main agent; and a coverage ledger that can resume the search.

The main agent reads the promising patches in context and validates applicable ones locally. The report is research, not permission to cherry-pick blindly. "No candidate in the screened commits" does not prove that no fix exists.

## Fix and validate locally

Make a focused fix at the demonstrated cause, on the working branch. Keep unrelated work and attribution. Keep the patch easy for a maintainer to read. Rerun the original repro and the regression checks, and add a focused regression test when it is practical. Report what you tested, what improved, and what is uncertain. Source reading and compilation do not prove runtime success. If the evidence cannot support a fix, name the next discriminating check. If the failure is project or configuration-owned, fix it locally and explain why no upstream report is needed.

## Prepare the handoff

- **No matching issue:** open the repo's issue form with the current template and fill it, unsubmitted, and check that the content is there. If the browser is not available, give the full draft and the creation link and say the form was not prepared. Lead with the failure and evidence: symptom title, environment and revision, repro steps, expected and actual behavior, focused logs and research links, confirmed findings apart from hypotheses, the suspected cause, then the candidate fix with before and after validation and limits. Present the fix as a proposal. Include a focused diff or an already-public commit link. A local commit link is not usable. Remove secrets and private project details.
- **Matching issue exists:** give the link and suggest a thumbs-up. Do not react yourself. Draft an unsubmitted comment only when it adds new repro details, environment differences, evidence, validation, or a fix. Never "same here". Never a duplicate issue.
- **Weak evidence:** do not build a report from an unverified suspicion. Name the gap and the local check that closes it. An unknown cause does not block a report when the failure is reproducible and clear.

## Completion

Summarize the local change or open result, validation and limits, issue, PR, and fork links, and the draft location or text. Name the checks and the submit or reaction steps left for the user. State that nothing was submitted.
