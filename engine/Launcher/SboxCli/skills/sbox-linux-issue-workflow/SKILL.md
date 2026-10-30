---
name: sbox-linux-issue-workflow
description: Investigate suspected Linux-specific s&box bugs, search Facepunch/sbox-public and forks for fixes, implement and validate local fixes, and prepare upstream reports for the user to submit. Use when Linux failures arise during s&box development; not for general Linux administration or unrelated game bugs.
---

# s&box Linux Issue Workflow

Resolve the local problem and give the user an evidence-backed upstream handoff. Local user validation protects Facepunch from unverified or redundant reports; automated tests alone do not replace that validation.

## Local autonomy and upstream boundary

Proceed with research, reproduction, focused local fixes, and validation within the requested scope. Follow the project's branch conventions and existing s&box editor/resource-guard rules. Reuse established debugging and editor workflows rather than introducing another launch or build procedure.

Treat upstream interactions as read-only research and unsubmitted drafts. Do not submit issues, comments, reactions, PRs (including draft PRs), or other remote changes. Do not push a branch, publish a patch, or contact fork authors as part of this workflow. Opening and filling an issue/comment form is allowed; leave its submission to the user. A suggested thumbs-up is a user action too.

Do not ask for permission to continue ordinary authorized local investigation. Prepare a concrete result, then explain what the user should validate before deciding whether to submit it. Do not interpret approval of the research or fix as permission to publish.

## Establish the failure

- Record a minimal reproduction, expected and actual behavior, relevant logs, and the engine revision/build and Linux environment details needed to interpret the failure.
- Establish the failing baseline before edits where practical. If reproduction is unavailable or intermittent, state that and preserve the evidence that is available.
- Determine whether the failure belongs to the engine, project code, or local configuration. A failure observed on Linux is not proof it is Linux-exclusive; distinguish tested platforms from assumptions.
- Keep observations, inferred causes, and unresolved questions distinct throughout the investigation.

## Search upstream, then forks

Follow this order: (1) search upstream issues and PRs, (2) check newer Facepunch/master commits, (3) validate promising upstream fixes, (4) search forks and investigate/validate their candidates if still unresolved, and (5) create our own fix if no suitable existing fix resolves the issue. Reproduction and diagnosis can proceed alongside research; do not jump to implementing a bespoke fix before reviewing the available upstream and fork evidence.

Complete these upstream checks before starting expensive fork enumeration or spawning the fork-search subagent:

1. Search current [Facepunch/sbox-public](https://github.com/Facepunch/sbox-public) **open and closed issues, open PRs, merged PRs, and closed-unmerged PRs** using symptoms, exact errors, affected files/symbols, and alternative terminology. Inspect promising discussions, linked commits, and patches rather than relying on titles or status. A closed issue does not prove a fix landed, and an unmerged PR may still contain a usable complete or partial fix. Respect repository reporting instructions/templates when preparing a report.
2. Independently check **newer commits on Facepunch/master**, including commits with no linked issue or PR. Establish the local engine's actual revision and refresh/verify the upstream master tip. Inspect upstream commits missing locally, using ancestry/merge-base rather than dates alone when the local branch has diverged. Screen commit messages and affected files and inspect plausible or uncertain patches, including related changes outside the immediately failing file. Record the local SHA, upstream tip SHA, range examined, and relevant findings. A stale tracking ref or empty issue search does not satisfy this check.
3. Evaluate promising upstream fixes against the local reproduction before expanding the search. Prefer bringing in or adapting an existing upstream fix where appropriate, preserving project branch conventions. Do not perform a broad branch upgrade merely to test a candidate. If the fix resolves the issue, skip the fork search. If it is only partial or fails validation, carry that evidence into the fork search.

An access failure is not a negative upstream result. Use the access-recovery procedure below for these checks too; if they remain blocked, report the upstream gate as incomplete and continue independent local investigation rather than automatically launching the expensive fork search.

Track two independent questions: is there a matching report, and is there a usable fix? Finding a matching issue without a fix does not end the search.

If upstream has no usable fix, spawn one cheap research subagent to perform the fork search below. Use `gpt-6-luna` with `fork_turns="none"` when available, or the lowest-cost available suitable model. Give it the symptom, reproduction/environment evidence, local revision, affected code and errors, upstream findings, and these search/report requirements. Keep it read-only with no builds, editor launches, remote mutations, or further delegation. The main agent can continue independent local investigation while it searches, but must examine its report before concluding the investigation.

### Establish access and recover from research blockers

Before bulk screening, establish a working route for live fork enumeration and commit/ancestry inspection. Discover available GitHub connector capabilities first; do not assume a generic fetch tool supports arbitrary API endpoints. An unsupported endpoint rejection is a tool capability failure, not proof that GitHub is inaccessible. Use an appropriate supported connector operation or, where necessary and permitted, GitHub API/Git access through local tools. Cached web pages and keyword searches can supply leads but cannot substitute for fork enumeration.

If access fails, the subagent must promptly send the main agent the exact operation, error category, execution context, and missing capability. Distinguish unsupported tool routes, authentication/authorization failures, rate limits, timeouts, and DNS/network failures. Do not spend the remaining search budget repeating equivalent keyword searches or identical failed requests.

The main agent owns recovery: inspect available connector capabilities and existing local Git remotes/caches, and determine whether the failure is specific to the subagent or sandbox. If a read-only network command fails because the sandbox restricts network access, use the supported approval/escalation mechanism for a bounded host-side API/Git check where permitted. Do not label sandbox DNS failure as a host outage without checking. Respect explicit access denials and automatic approval rejections; never bypass them through another transport or credentials. Do not expose tokens or modify DNS, networking, or security settings.

When the main agent has working access that the subagent lacks, gather the paginated fork/ref inventory and required commit metadata/patches into shared local artifacts, then resume the same cheap subagent against those artifacts. Include the upstream snapshot, provenance, coverage, and pagination state. The main agent may supply access and data while the subagent performs the exhaustive screening; lack of direct subagent network access does not end the task.

If appropriate permitted routes remain unavailable, report **fork screening blocked before enumeration** when zero commits were screened, or **partial fork screening** when some were screened. Include attempted routes and concrete blockers, not only “retry when access returns.” Continue independent authorized local work, preserve a resumable checkpoint, and identify the specific access or user action needed. Do not present this as a completed fork-search step or a negative finding. Do not retry indefinitely when the blocker is unchanged.

### Exhaustive commit screening in current forks

The subagent must enumerate all accessible forks of Facepunch/sbox-public, completing pagination and inspecting all accessible branch heads rather than just default branches. Apply this one age exclusion: skip a fork if no branch incorporates a Facepunch/master commit from within the preceding three calendar months, relative to the search date. Determine incorporation using upstream commit ancestry; do not require a literal merge commit, since fast-forwards and rebases also incorporate upstream. Use the upstream commit's date rather than the fork's last push date. A recent unrelated fork commit does not establish synchronization. If freshness cannot be established, record it as unknown and investigate rather than silently treating it as stale. A cached page's update timestamp is neither an ancestry check nor sufficient evidence for exclusion. Report the cutoff and excluded forks with evidence. A known directly relevant patch in an excluded fork may still be inspected as an explicitly reported exception.

For every qualifying fork, enumerate **all commits reachable from every accessible branch and tag that are absent from the upstream repository's accessible branch/tag history**. Compare against upstream as a whole, not only master or the fork's default branch. Complete commit-list pagination, detect truncated API comparisons, and use read-only Git fetching in a scratch repository where needed to establish complete ancestry. Deleted or inaccessible refs cannot be claimed as covered. Deduplicate commit SHAs across forks while preserving their source locations; identify equivalent patches where useful without discarding unique context.

Screen every unique upstream-absent commit for possible relevance, including partial fixes, workarounds, diagnostics, tests, infrastructure changes, and clues about the underlying cause. Inspect commit messages and changed files; inspect patches whenever relevance is plausible or uncertain. Do not rely on titles or exact symptom keywords, skip old commits within qualifying forks, or stop after finding the first promising candidate. The three-month rule filters forks, not commits within them.

Keep the work economical through cached inventories, shared Git objects, batched reads, and deduplication, not arbitrary limits on forks or commits. Respect resource guards for expensive operations. If rate limits, inaccessible repositories, tool limits, or resource constraints prevent completion, return a partial report with exact gaps and a resumable cursor/checkpoint; never describe that as an exhaustive search or infer that no fix exists.

### Subagent report and main-agent follow-through

Return a concise report containing:

- Search date, three-month cutoff, upstream refs/revision used, fork and ref counts, excluded/unknown forks, unique upstream-absent commit count, screened count, and any incomplete pages or refs.
- Ranked candidate commits with source links, SHAs, authorship, affected files, and the evidence connecting each to the failure. Label complete-fix candidates, partial fixes, workarounds, and diagnostic clues; do not claim validation from inspection.
- Dependencies or related commit sequences, applicability risks, and concrete checks for the main agent to perform.
- A compact coverage ledger or local artifact sufficient to resume and account for screened commits, including those judged irrelevant.

The main agent must inspect promising patches and their context, assess partial fixes as useful leads even when none solves the whole issue, and validate applicable changes locally. The subagent's report is research evidence, not authority to cherry-pick blindly. Preserve attribution and report coverage honestly: “no candidate found in the screened commits” is not proof that no fix exists anywhere.

Check whether a candidate applies to the local revision and addresses the same cause. Do not treat an open PR, fork patch, or similar symptom as proof of correctness. If an upstream fix already landed, assess whether bringing that fix into the local branch is sufficient.

## Fix and validate locally

Apply and validate suitable discovered fixes during the upstream and fork stages. **Step 5: create our own fix** when those stages have not produced a suitable solution. Use their findings, including partial fixes and diagnostic clues, to implement a focused candidate addressing the demonstrated cause on the working branch according to project conventions. Preserve unrelated work and attribution; avoid unrelated cleanup. Keep the patch easy for a maintainer to inspect or reuse. If research is blocked, disclose the missing coverage and recover access as described above; do not quietly treat it as an exhausted search. Follow an explicit user direction to proceed with a local fix despite that gap.

Re-run the original reproduction and relevant regression checks. Add a focused regression test when it meaningfully demonstrates the defect and is practical in the project. Use guarded heavy work and existing editor reuse rules. Report exactly what was tested, what improved, and what remains uncertain. Do not claim runtime success from source inspection or compilation alone.

If the evidence cannot support a fix, preserve the investigation and identify the next discriminating check rather than inventing a solution. If the failure is project/configuration-owned, resolve it locally where authorized and explain why an upstream report is not warranted.

## Prepare the appropriate handoff

### No matching issue

When evidence supports an upstream report, open the browser to the repository's issue creation flow and prepare the title/body for the user to inspect and submit. Use the current template and leave the form unsubmitted. Verify that the draft content is present. If browser access or prefilling is unavailable, provide the complete copyable draft and creation link, explicitly stating that the browser draft was not prepared.

Lead with the failure and evidence, followed by the candidate fix. Include the relevant subset of:

- Concise symptom-based title, environment and revision.
- Reproduction steps or minimal reproducer; expected versus actual behavior.
- Focused logs and research links, with confirmed findings separated from hypotheses.
- Suspected underlying cause and why the evidence points to it.
- Our candidate fix, why it appears to help, before/after validation, and remaining limitations.

Present the patch as a proposal, not an established definitive solution or an instruction that Facepunch must adopt it. Include a focused diff/code excerpt or an already-public, verified accessible commit link. A local/private commit link is not a usable upstream handoff; do not publish it automatically. Exclude secrets and irrelevant private project details from draft evidence.

### Matching issue exists

Provide the issue link and suggest that the user add a thumbs-up; do not react automatically. Read the discussion before deciding whether a comment adds value.

Prepare an unsubmitted comment only when it contributes new reproduction details, environment distinctions, investigation evidence, validation, or a candidate fix. Include the accessible fix and its limitations when useful. Avoid comments that merely restate the issue or say “same here.” If the discussion already covers our findings, explain that no additional comment is needed. Do not create a duplicate issue to share a fix.

### Insufficient evidence

Do not manufacture a submission-ready report from a weak or unverified suspicion. Summarize the evidence gap and what local validation would resolve it. An unresolved cause does not itself prevent a useful report when the failure is reproducible and clearly described.

## Completion

Summarize the local change or unresolved result, validation and limitations, relevant issue/PR/fork links, and the exact draft location or copyable text. Identify any remaining user verification and suggested submission/reaction actions. Make clear that nothing was submitted remotely. If the user later provides validation results, revise the draft accordingly; the user remains the submitter.
