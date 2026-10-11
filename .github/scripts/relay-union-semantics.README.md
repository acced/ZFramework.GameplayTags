# Existing semantic evidence relay

This artifact-only workflow republishes exact ZIP bytes from the existing
[source run](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38170243660)
at snapshot `de367d4ee1af6915c87934abd8482c2dc53c465b`. It does not compile or
execute runtime code, generate replacement measurements, rerun the source
experiment, sample evidence, alter its conclusions, or change its original artifacts.

The fixed .NET source is artifact `11714915720`, exactly `1241700111` bytes,
SHA256 `8f77fb38060ac2c3dae3ca3a36d800bc3a5ca15fcfad97a9a6f9b1370377de5f`.
The fixed Mono source is artifact `11714772430`, exactly `1209692778` bytes,
SHA256 `a253942696d7668f8f01946757f224e6a846d03c69b42791ee8422dfff8c2072`.
Both names must be `union-repair-semantics-BACKEND-de367d4ee1af6915c87934abd8482c2dc53c465b`
for their respective `net` or `mono` backend on that exact run, attempt,
repository, branch and head. Both source artifacts already exist, so there is
no polling or artifact discovery. All source identities appear in every manifest.

## Integrity and limits

- Each archive is downloaded once and its complete streamed SHA256 and byte count
  must match GitHub API metadata and its respective fixed artifact pin.
  Run and artifact identities are checked again after the download.
- Exact original ZIP bytes are split into consecutive parts of at most 256 MiB.
  There can be at most eight parts and the original archive cannot exceed 2 GiB.
  The complete split byte stream is independently hashed against the original
  ZIP digest. Every part has a SHA256, exact size and zero-based byte offset.
- Each part and its small JSON manifest are uploaded as a separate Actions
  artifact. The upload wrapper is a new ZIP with its own digest; it is not the
  original source ZIP. The original bytes are the `source.zip.partNN` member
  inside each wrapper.
- A separate summary artifact includes `manifest.json` and only these exact
  original root members when present: `report.json`, `completion.json`,
  `preflight-failure.json`, `upload-allowlist.json`. Their bytes are unchanged.
  The limits are 8/2/2/4 MiB respectively and 16 MiB combined. Other ZIP members
  are not extracted; all remain preserved in the raw ZIP parts.
- Every ZIP member path and file type is checked before extraction. Absolute,
  traversal, duplicate, control-character, backslash and drive-qualified names,
  symlinks, special files, and encrypted members are rejected. The central
  directory is capped at 16 MiB and 10,000 entries before loading it.
- All publication paths name individual files. No build directories, host
  reports, environment dumps or unrelated diagnostics are collected.

To recover the original archive, download all part artifacts and the summary,
unwrap their ZIP containers, verify each part's SHA256 and size, and concatenate
`source.zip.part01`, `source.zip.part02`, and so on in manifest order. Verify that
each offset equals the running size, then verify the resulting original ZIP's
total size and SHA256 against `manifest.json`. Only that verified reassembly
represents the original evidence. A relay success establishes transport integrity,
not a passing semantic or performance result; inspect the original reports.

## Network and execution scope

Python uses only the standard library. The workflow uses official checkout and
upload actions. Its built-in, ephemeral `GITHUB_TOKEN` has only `contents: read`
and `actions: read`; checkout does not persist credentials. The token is passed
only to the Python relay step, never read or expanded by its shell command, and
is attached only to requests at `https://api.github.com`.

Automatic redirects and environment proxies are disabled. The artifact endpoint
must return one 302 redirect to HTTPS GitHub artifact storage under
`*.blob.core.windows.net` or `*.actions.githubusercontent.com`. A fresh request
fetches the signed URL without authorization or cookies and does not follow
further redirects. Tokens, signed URLs, response bodies and exception details
are never logged. Access errors stop the relay; there is no alternate access route
or credential creation. This addresses an artifact transport size limit.

Network calls have 30-second socket timeouts; the one archive download has a
15-minute deadline, the script a 25-minute deadline, and each job a 90-minute
limit. There are no download retries. Independent NET/Mono jobs do not cancel
each other. Uploads only start after all relay checks pass. Interrupted uploads
can leave an incomplete set: consumers must require every manifest-listed part.

The relay's push filter matches only its own workflow, script, README and
synthetic tests. Publishing those files does not trigger the existing Evolution
or repaired-snapshot experiment workflows. The repository's unchanged generic
push CI can still run its usual validation and timings as a side effect of this
commit. Those incidental results are excluded from the focused snapshot evidence;
this relay never uses them or reruns the pinned source experiment.

`test-relay-union-semantics.py` exercises tiny synthetic ZIPs and mocked HTTP
responses only. It does not download artifacts, read credentials, or execute
software under review.

GitHub documents the API's redirect and Actions-read requirements in its
[artifact REST API](https://docs.github.com/en/rest/actions/artifacts#download-an-artifact).
