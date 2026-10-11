#!/usr/bin/env python3
"""Relay two existing semantic artifacts without running the software under test."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import stat
import struct
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile


REPOSITORY = "acced/ZFramework.GameplayTags"
BRANCH = "perf/compact-runtime-rewrite"
SOURCE_RUN = 38170243660
SOURCE_HEAD = "de367d4ee1af6915c87934abd8482c2dc53c465b"
SOURCE_ATTEMPT = 1
SOURCE_WORKFLOW = ".github/workflows/union-repaired-strategies.yml"
NET_ID = 11714915720
NET_SIZE = 1241700111
NET_SHA256 = "8f77fb38060ac2c3dae3ca3a36d800bc3a5ca15fcfad97a9a6f9b1370377de5f"
MONO_ID = 11714772430
MONO_SIZE = 1209692778
MONO_SHA256 = "a253942696d7668f8f01946757f224e6a846d03c69b42791ee8422dfff8c2072"
API_BASE = "https://api.github.com"
CHUNK_BYTES = 256 * 1024 * 1024
MAX_PARTS = 8
MAX_ARCHIVE_BYTES = CHUNK_BYTES * MAX_PARTS
BUFFER_BYTES = 1024 * 1024
MAX_API_BYTES = 2 * 1024 * 1024
MAX_MEMBERS = 10000
MAX_CENTRAL_BYTES = 16 * 1024 * 1024
MAX_SUMMARY_BYTES = 16 * 1024 * 1024
SUMMARY_LIMITS = {
    "report.json": 8 * 1024 * 1024,
    "completion.json": 2 * 1024 * 1024,
    "preflight-failure.json": 2 * 1024 * 1024,
    "upload-allowlist.json": 4 * 1024 * 1024,
}
SOCKET_SECONDS = 30
DOWNLOAD_SECONDS = 15 * 60
SCRIPT_SECONDS = 25 * 60
OUTPUT = Path("evidence-relay-output")


class RelayError(Exception):
    """Only fixed, public error codes may be put in this exception."""


def require(condition, code):
    if not condition:
        raise RelayError(code)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


def opener():
    # No environment-provided proxy, cookie, credential or redirect handlers.
    return urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())


def api_request(path, token):
    require(path.startswith("/repos/" + REPOSITORY + "/actions/"), "api_path")
    url = API_BASE + path
    parsed = urllib.parse.urlsplit(url)
    require(parsed.scheme == "https" and parsed.netloc == "api.github.com", "api_origin")
    return urllib.request.Request(url, headers={
        "Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "fixed-semantic-artifact-relay",
    }, method="GET")


def read_json_api(path, token):
    try:
        with opener().open(api_request(path, token), timeout=SOCKET_SECONDS) as response:
            require(response.status == 200, "api_status")
            raw = response.read(MAX_API_BYTES + 1)
    except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, OSError):
        # Do not include exception text: it can contain an authenticated URL.
        raise RelayError("api_request_failed") from None
    require(len(raw) <= MAX_API_BYTES, "api_response_size")
    result = json.loads(raw)
    require(isinstance(result, dict), "api_response_shape")
    return result


def verified_run(token):
    run = read_json_api(f"/repos/{REPOSITORY}/actions/runs/{SOURCE_RUN}", token)
    require(run.get("id") == SOURCE_RUN and run.get("head_sha") == SOURCE_HEAD,
            "source_run_identity")
    require(run.get("head_branch") == BRANCH and run.get("run_attempt") == SOURCE_ATTEMPT,
            "source_run_attempt")
    require(run.get("event") == "push" and run.get("path") == SOURCE_WORKFLOW,
            "source_workflow")
    repository = run.get("repository", {})
    head_repository = run.get("head_repository", {})
    require(repository.get("full_name") == REPOSITORY and
            head_repository.get("full_name") == REPOSITORY and
            type(repository.get("id")) is int and
            repository["id"] == head_repository.get("id"), "source_repository")
    return run


def verified_artifact(item, backend, repository_id):
    require(isinstance(item, dict), "artifact_shape")
    name = f"union-repair-semantics-{backend}-{SOURCE_HEAD}"
    require(item.get("name") == name and item.get("expired") is False, "artifact_name_or_expiry")
    artifact_id = item.get("id")
    size = item.get("size_in_bytes")
    digest = item.get("digest")
    require(type(artifact_id) is int and artifact_id > 0, "artifact_id")
    require(type(size) is int and 0 < size <= MAX_ARCHIVE_BYTES, "artifact_size")
    require(isinstance(digest, str) and re.fullmatch(r"sha256:[0-9a-f]{64}", digest),
            "artifact_digest")
    source = item.get("workflow_run", {})
    require(source.get("id") == SOURCE_RUN and source.get("head_sha") == SOURCE_HEAD and
            source.get("head_branch") == BRANCH and
            source.get("repository_id") == repository_id and
            source.get("head_repository_id") == repository_id, "artifact_source")
    expected = {"net": (NET_ID, NET_SIZE, NET_SHA256),
                "mono": (MONO_ID, MONO_SIZE, MONO_SHA256)}.get(backend)
    require(expected is not None, "backend")
    require((artifact_id, size, digest) == (expected[0], expected[1], "sha256:" + expected[2]),
            "artifact_pin")
    # Only allowlisted public metadata is retained, never URLs or arbitrary API fields.
    return {"artifact_id": artifact_id, "artifact_name": name,
            "size_bytes": size, "sha256": digest.removeprefix("sha256:")}


def locate_artifact(backend, token):
    run = verified_run(token)
    repository_id = run["repository"]["id"]
    artifact_id = {"net": NET_ID, "mono": MONO_ID}[backend]
    item = read_json_api(f"/repos/{REPOSITORY}/actions/artifacts/{artifact_id}", token)
    return verified_artifact(item, backend, repository_id), repository_id


def storage_request(url):
    parsed = urllib.parse.urlsplit(url)
    host = parsed.hostname or ""
    require(parsed.scheme == "https" and parsed.port in {None, 443} and
            parsed.username is None and parsed.password is None and not parsed.fragment and
            (host.endswith(".blob.core.windows.net") or
             host.endswith(".actions.githubusercontent.com")), "storage_origin")
    # A fresh request has NO GitHub authorization, cookies or inherited API headers.
    return urllib.request.Request(url, headers={
        "User-Agent": "fixed-semantic-artifact-relay",
        "Accept-Encoding": "identity",
    }, method="GET")


def download_url(artifact_id, token):
    request = api_request(f"/repos/{REPOSITORY}/actions/artifacts/{artifact_id}/zip", token)
    try:
        with opener().open(request, timeout=SOCKET_SECONDS):
            raise RelayError("download_redirect_required")
    except urllib.error.HTTPError as error:
        try:
            require(error.code == 302, "download_redirect_status")
            location = error.headers.get("Location")
            require(isinstance(location, str) and 0 < len(location) <= 16384, "download_location")
            storage_request(location)
            return location
        finally:
            error.close()
    except (urllib.error.URLError, TimeoutError, OSError):
        raise RelayError("download_redirect_failed") from None


def streamed_copy(source, destination, expected_size, expected_sha256, deadline):
    digest = hashlib.sha256()
    size = 0
    with destination.open("xb") as output:
        while True:
            require(time.monotonic() < deadline, "download_timeout")
            block = source.read(min(BUFFER_BYTES, expected_size - size + 1))
            if not block:
                break
            size += len(block)
            require(size <= expected_size and size <= MAX_ARCHIVE_BYTES, "download_size_overflow")
            digest.update(block)
            output.write(block)
    require(size == expected_size, "download_size_mismatch")
    require(digest.hexdigest() == expected_sha256, "download_digest_mismatch")


def download_archive(source, token, destination):
    # Exactly one archive GET. There are no network retries or alternate download routes.
    request = storage_request(download_url(source["artifact_id"], token))
    deadline = time.monotonic() + DOWNLOAD_SECONDS
    try:
        with opener().open(request, timeout=SOCKET_SECONDS) as response:
            require(response.status == 200, "storage_status")
            length = response.headers.get("Content-Length")
            if length is not None:
                require(length.isdecimal() and int(length) == source["size_bytes"], "storage_length")
            require(response.headers.get("Content-Encoding", "identity") == "identity", "storage_encoding")
            streamed_copy(response, destination, source["size_bytes"], source["sha256"], deadline)
    except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, OSError):
        raise RelayError("archive_download_failed") from None


def split_archive(archive, parts_dir, source, chunk_bytes=CHUNK_BYTES):
    require(0 < chunk_bytes <= CHUNK_BYTES, "chunk_size")
    require(0 < source["size_bytes"] <= chunk_bytes * MAX_PARTS, "part_limit")
    parts_dir.mkdir()
    parts = []
    whole = hashlib.sha256()
    offset = 0
    with archive.open("rb") as original:
        while offset < source["size_bytes"]:
            number = len(parts) + 1
            name = f"source.zip.part{number:02d}"
            digest = hashlib.sha256()
            part_size = 0
            with (parts_dir / name).open("xb") as part:
                while part_size < min(chunk_bytes, source["size_bytes"] - offset):
                    block = original.read(min(BUFFER_BYTES, chunk_bytes - part_size,
                                              source["size_bytes"] - offset - part_size))
                    require(bool(block), "split_truncated")
                    part.write(block)
                    digest.update(block)
                    whole.update(block)
                    part_size += len(block)
            item = {"number": number, "file": name, "offset_bytes": offset,
                    "size_bytes": part_size, "sha256": digest.hexdigest()}
            write_json(parts_dir / f"part{number:02d}.json", {"schema": 1, "source": source, "part": item})
            parts.append(item)
            offset += part_size
        require(not original.read(1), "split_extra_bytes")
    require(whole.hexdigest() == source["sha256"], "split_digest_mismatch")
    return parts


def bounded_zip_directory(archive):
    # Check directory allocation bounds before ZipFile reads the central directory.
    size = archive.stat().st_size
    with archive.open("rb") as stream:
        stream.seek(max(0, size - 65557))
        tail = stream.read(65557)
        index = tail.rfind(b"PK\x05\x06")
        require(index >= 0 and len(tail) - index >= 22, "zip_end_record")
        end = struct.unpack_from("<4s4H2LH", tail, index)
        require(index + 22 + end[7] == len(tail), "zip_trailing_bytes")
        require(end[1] == 0 and end[2] == 0, "zip_multidisk")
        entries, central_size, central_offset = end[4], end[5], end[6]
        end_offset = size - len(tail) + index
        if end_offset >= 20:
            stream.seek(end_offset - 20)
            locator = stream.read(20)
            if locator[:4] == b"PK\x06\x07":
                _, disk, record_offset, disks = struct.unpack("<4sLQL", locator)
                require(disk == 0 and disks == 1 and record_offset + 56 <= end_offset - 20,
                        "zip64_locator")
                stream.seek(record_offset)
                record = stream.read(56)
                fields = struct.unpack("<4sQ2H2L4Q", record)
                require(fields[0] == b"PK\x06\x06" and 44 <= fields[1] <= MAX_CENTRAL_BYTES and
                        record_offset + fields[1] + 12 == end_offset - 20 and
                        fields[4] == 0 and fields[5] == 0 and fields[6] == fields[7], "zip64_record")
                entries, central_size, central_offset = fields[7], fields[8], fields[9]
        require(0 < entries <= MAX_MEMBERS and 0 < central_size <= MAX_CENTRAL_BYTES and
                0 <= central_offset <= size and central_offset + central_size <= end_offset,
                "zip_directory_bounds")


def safe_member(info):
    name = info.filename
    require(name == info.orig_filename and 0 < len(name) <= 512 and
            not any(ord(char) < 32 or ord(char) == 127 for char in name) and
            "\\" not in name and ":" not in name and not name.startswith("/"), "zip_member_path")
    components = (name[:-1] if name.endswith("/") else name).split("/")
    require(all(component not in {"", ".", ".."} for component in components), "zip_member_path")
    mode = info.external_attr >> 16
    kind = stat.S_IFMT(mode)
    require(kind in {0, stat.S_IFREG, stat.S_IFDIR} and
            (kind != stat.S_IFDIR or info.is_dir()) and
            not (info.flag_bits & 1), "zip_member_type")
    require(info.file_size >= 0 and info.compress_size >= 0, "zip_member_size")


def extract_summaries(archive, summary_dir):
    bounded_zip_directory(archive)
    summaries = []
    total = 0
    with zipfile.ZipFile(archive, "r") as bundle:
        members = bundle.infolist()
        require(len(members) <= MAX_MEMBERS, "zip_member_count")
        seen = set()
        for info in members:
            safe_member(info)
            require(info.filename not in seen, "zip_duplicate_member")
            seen.add(info.filename)
        require("report.json" in seen or "preflight-failure.json" in seen, "zip_missing_report")
        for info in members:
            if info.filename not in SUMMARY_LIMITS:
                continue
            limit = SUMMARY_LIMITS[info.filename]
            require(not info.is_dir() and info.file_size <= limit, "summary_size")
            with bundle.open(info, "r") as member:
                raw = member.read(limit + 1)
            require(len(raw) == info.file_size and len(raw) <= limit, "summary_read_size")
            total += len(raw)
            require(total <= MAX_SUMMARY_BYTES, "summary_total_size")
            require(isinstance(json.loads(raw), (dict, list)), "summary_json")
            # Preserve original bytes. Never extract other members, logs or binaries.
            (summary_dir / info.filename).write_bytes(raw)
            summaries.append({"file": info.filename, "size_bytes": len(raw),
                              "sha256": hashlib.sha256(raw).hexdigest()})
    return summaries


def write_json(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, sort_keys=True, indent=2)
        stream.write("\n")


def script_timeout(_signal, _frame):
    raise RelayError("script_timeout")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backend", choices=("net", "mono"), required=True)
    args = parser.parse_args()
    require(os.environ.get("GITHUB_ACTIONS") == "true" and
            os.environ.get("GITHUB_REPOSITORY") == REPOSITORY and
            os.environ.get("GITHUB_REF") == "refs/heads/" + BRANCH, "execution_context")
    token = os.environ.get("GITHUB_TOKEN")
    require(bool(token), "missing_ephemeral_token")
    signal.signal(signal.SIGALRM, script_timeout)
    signal.alarm(SCRIPT_SECONDS)
    source, repository_id = locate_artifact(args.backend, token)
    source.update({"repository": REPOSITORY, "run_id": SOURCE_RUN, "run_attempt": SOURCE_ATTEMPT,
                   "head_sha": SOURCE_HEAD, "head_branch": BRANCH, "backend": args.backend})
    OUTPUT.mkdir()
    archive = OUTPUT / "original.zip"
    download_archive(source, token, archive)
    verified_run(token)
    final = read_json_api(f"/repos/{REPOSITORY}/actions/artifacts/{source['artifact_id']}", token)
    checked = verified_artifact(final, args.backend, repository_id)
    require(all(source[key] == value for key, value in checked.items()), "artifact_metadata_changed")
    summary_dir = OUTPUT / "summary"
    summary_dir.mkdir()
    summaries = extract_summaries(archive, summary_dir)
    parts = split_archive(archive, OUTPUT / "parts", source)
    manifest = {"schema": 1, "purpose": "byte-preserving relay of existing evidence",
                "source": source, "chunk_limit_bytes": CHUNK_BYTES, "part_count": len(parts),
                "parts": parts, "original_public_summaries": summaries,
                "verification": {"api_metadata_before_and_after": True, "complete_zip_sha256": True,
                                 "complete_split_sha256": True, "all_zip_member_paths_checked": True},
                "reran_tests_or_benchmarks": False}
    write_json(summary_dir / "manifest.json", manifest)
    # Outputs are published only after every integrity, path and extraction check passes.
    output_path = os.environ.get("GITHUB_OUTPUT")
    require(bool(output_path), "missing_actions_output")
    with open(output_path, "a", encoding="utf-8") as output:
        output.write(f"part_count={len(parts)}\n")
    print(f"Verified {args.backend} original ZIP and {len(parts)} exact-byte parts; no tests rerun.")
    signal.alarm(0)


if __name__ == "__main__":
    try:
        main()
    except RelayError as error:
        print("Artifact relay failed: " + str(error), file=sys.stderr)
        sys.exit(1)
    except Exception:
        # Never expose API data, storage URLs, local paths or environment diagnostics.
        print("Artifact relay failed: unexpected_error", file=sys.stderr)
        sys.exit(1)
