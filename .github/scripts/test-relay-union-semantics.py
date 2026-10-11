#!/usr/bin/env python3
"""Small, offline transport/security checks. No runtime or benchmark execution."""

import hashlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import struct
import sys
import tempfile
import unittest
from unittest import mock
import urllib.error
import zipfile


sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("relay", Path(__file__).with_name("relay-union-semantics.py"))
relay = importlib.util.module_from_spec(spec)
spec.loader.exec_module(relay)


def archive_bytes(extra=()):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("report.json", b'{"ok": true}\n')
        archive.writestr("completion.json", b'{"done": true}\n')
        for name, data in extra:
            archive.writestr(name, data)
    return stream.getvalue()


def identity(raw):
    return {"artifact_id": 123, "size_bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


class RelayTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def save_zip(self, raw):
        path = self.root / "original.zip"
        path.write_bytes(raw)
        return path

    def extract(self, raw):
        directory = self.root / "summary"
        directory.mkdir()
        return relay.extract_summaries(self.save_zip(raw), directory)

    def test_streamed_exact_bytes_and_hash(self):
        raw = archive_bytes()
        source = identity(raw)
        destination = self.root / "copy.zip"
        relay.streamed_copy(io.BytesIO(raw), destination, len(raw), source["sha256"], float("inf"))
        self.assertEqual(destination.read_bytes(), raw)

    def test_stream_rejects_short_long_wrong_digest_and_expired_deadline(self):
        raw = archive_bytes()
        for index, (data, size, digest, deadline) in enumerate([
            (raw[:-1], len(raw), hashlib.sha256(raw).hexdigest(), float("inf")),
            (raw + b"x", len(raw), hashlib.sha256(raw).hexdigest(), float("inf")),
            (raw, len(raw), "0" * 64, float("inf")),
            (raw, len(raw), hashlib.sha256(raw).hexdigest(), -1),
        ]):
            with self.subTest(index=index), self.assertRaises(relay.RelayError):
                relay.streamed_copy(io.BytesIO(data), self.root / f"bad{index}", size, digest, deadline)

    def test_split_exact_reassembly_offsets_sizes_hashes(self):
        raw = archive_bytes((("logs/run-1/Stdout.gz", bytes(range(256))),))
        source = identity(raw)
        chunk = (len(raw) + 6) // 7
        directory = self.root / "parts"
        parts = relay.split_archive(self.save_zip(raw), directory, source, chunk_bytes=chunk)
        self.assertEqual(len(parts), 7)
        result = bytearray()
        for part in parts:
            data = (directory / part["file"]).read_bytes()
            self.assertEqual(part["offset_bytes"], len(result))
            self.assertEqual(part["size_bytes"], len(data))
            self.assertLessEqual(len(data), chunk)
            self.assertEqual(part["sha256"], hashlib.sha256(data).hexdigest())
            manifest = json.loads((directory / f"part{part['number']:02d}.json").read_text())
            self.assertEqual(manifest, {"schema": 1, "source": source, "part": part})
            result.extend(data)
        self.assertEqual(bytes(result), raw)
        self.assertEqual(hashlib.sha256(result).hexdigest(), source["sha256"])

    def test_split_enforces_eight_part_limit(self):
        raw = b"123456789"
        with self.assertRaises(relay.RelayError):
            relay.split_archive(self.save_zip(raw), self.root / "parts", identity(raw), chunk_bytes=1)

    def test_split_rejects_wrong_complete_digest(self):
        raw = archive_bytes()
        source = identity(raw)
        source["sha256"] = "0" * 64
        with self.assertRaises(relay.RelayError):
            relay.split_archive(self.save_zip(raw), self.root / "parts", source)

    def test_summary_allowlist_and_exact_original_bytes(self):
        raw = archive_bytes((("logs/run-1/Stdout.gz", b"opaque raw evidence"),
                             ("nested/report.json", b'{"not": "extracted"}')))
        entries = self.extract(raw)
        self.assertEqual({entry["file"] for entry in entries}, {"report.json", "completion.json"})
        self.assertEqual({path.name for path in (self.root / "summary").iterdir()},
                         {"report.json", "completion.json"})
        self.assertEqual((self.root / "summary/report.json").read_bytes(), b'{"ok": true}\n')
        self.assertEqual((self.root / "original.zip").read_bytes(), raw)

    def test_reject_unsafe_paths_even_for_unextracted_members(self):
        names = ("../outside", "/absolute", "nested/../../outside", "C:/drive", "a\\b",
                 "a//b", "a/./b", "a\nb", "a//", "./a")
        for index, name in enumerate(names):
            with self.subTest(name=name), self.assertRaises(relay.RelayError):
                target = self.root / f"unsafe{index}.zip"
                target.write_bytes(archive_bytes(((name, b"x"),)))
                relay.extract_summaries(target, self.root)

    def test_reject_symlinks_and_special_files(self):
        for index, mode in enumerate((stat.S_IFLNK, stat.S_IFIFO, stat.S_IFCHR)):
            member = zipfile.ZipInfo("unextracted-link")
            member.create_system = 3
            member.external_attr = (mode | 0o777) << 16
            target = self.root / f"special{index}.zip"
            target.write_bytes(archive_bytes(((member, b"target"),)))
            with self.subTest(mode=mode), self.assertRaises(relay.RelayError):
                relay.extract_summaries(target, self.root)

    def test_reject_duplicate_names(self):
        with mock.patch("warnings.warn"):
            raw = archive_bytes((("report.json", b'{}'),))
        with self.assertRaises(relay.RelayError):
            self.extract(raw)

    def test_reject_oversized_summary_without_extracting_it(self):
        with mock.patch.dict(relay.SUMMARY_LIMITS, {"report.json": 1}):
            with self.assertRaises(relay.RelayError):
                self.extract(archive_bytes())
        self.assertFalse((self.root / "summary/report.json").exists())

    def test_reject_oversized_central_directory_before_zipfile(self):
        raw = bytearray(archive_bytes())
        offset = raw.rfind(b"PK\x05\x06")
        struct.pack_into("<L", raw, offset + 12, relay.MAX_CENTRAL_BYTES + 1)
        with mock.patch.object(relay.zipfile, "ZipFile") as reader:
            with self.assertRaises(relay.RelayError):
                self.extract(raw)
            reader.assert_not_called()

    def test_zip64_end_record(self):
        raw = archive_bytes()
        offset = raw.rfind(b"PK\x05\x06")
        fields = struct.unpack("<4s4H2LH", raw[offset:])
        record = struct.pack("<4sQ2H2L4Q", b"PK\x06\x06", 44, 45, 45, 0, 0,
                             fields[3], fields[4], fields[5], fields[6])
        locator = struct.pack("<4sLQL", b"PK\x06\x07", 0, offset, 1)
        end = struct.pack("<4s4H2LH", b"PK\x05\x06", 0, 0, 65535, 65535,
                          0xffffffff, 0xffffffff, 0)
        entries = self.extract(raw[:offset] + record + locator + end)
        self.assertEqual(len(entries), 2)

    def test_net_metadata_pins_all_original_identity_fields(self):
        artifact = {
            "id": relay.NET_ID, "name": f"union-repair-semantics-net-{relay.SOURCE_HEAD}",
            "size_in_bytes": relay.NET_SIZE, "digest": "sha256:" + relay.NET_SHA256,
            "expired": False,
            "workflow_run": {"id": relay.SOURCE_RUN, "head_sha": relay.SOURCE_HEAD,
                             "head_branch": relay.BRANCH, "repository_id": 42, "head_repository_id": 42},
        }
        source = relay.verified_artifact(artifact, "net", 42)
        self.assertEqual(source["sha256"], relay.NET_SHA256)
        for key, value in (("id", relay.NET_ID + 1), ("size_in_bytes", 1),
                           ("digest", "sha256:" + "0" * 64), ("expired", True),
                           ("name", "wrong")):
            with self.subTest(key=key), self.assertRaises(relay.RelayError):
                relay.verified_artifact(dict(artifact, **{key: value}), "net", 42)
        for key, value in (("id", relay.SOURCE_RUN + 1), ("head_sha", "0" * 40),
                           ("head_branch", "wrong"), ("repository_id", 43), ("head_repository_id", 43)):
            with self.subTest(key=key), self.assertRaises(relay.RelayError):
                relay.verified_artifact(dict(artifact, workflow_run=dict(artifact["workflow_run"], **{key: value})), "net", 42)

    def test_mono_metadata_pins_all_original_identity_fields(self):
        artifact = {
            "id": relay.MONO_ID, "name": f"union-repair-semantics-mono-{relay.SOURCE_HEAD}",
            "size_in_bytes": relay.MONO_SIZE, "digest": "sha256:" + relay.MONO_SHA256, "expired": False,
            "workflow_run": {"id": relay.SOURCE_RUN, "head_sha": relay.SOURCE_HEAD,
                             "head_branch": relay.BRANCH, "repository_id": 42, "head_repository_id": 42},
        }
        self.assertEqual(relay.verified_artifact(artifact, "mono", 42)["artifact_id"], relay.MONO_ID)
        for key, value in (("id", relay.MONO_ID + 1), ("size_in_bytes", 1),
                           ("digest", "sha256:" + "0" * 64), ("expired", True),
                           ("digest", None), ("digest", "md5:" + "1" * 32),
                           ("size_in_bytes", relay.MAX_ARCHIVE_BYTES + 1), ("name", "wrong")):
            with self.subTest(key=key), self.assertRaises(relay.RelayError):
                relay.verified_artifact(dict(artifact, **{key: value}), "mono", 42)

    def test_authorization_is_never_forwarded_to_storage(self):
        raw = archive_bytes()
        signed = "https://example.blob.core.windows.net/artifacts/archive.zip?synthetic=signature"
        requests = []

        class Response(io.BytesIO):
            status = 200
            headers = {"Content-Length": str(len(raw))}

        class FakeOpener:
            def open(self, request, timeout):
                requests.append(request)
                if len(requests) == 1:
                    raise urllib.error.HTTPError(request.full_url, 302, "Found", {"Location": signed}, io.BytesIO())
                return Response(raw)

        with mock.patch.object(relay, "opener", return_value=FakeOpener()):
            relay.download_archive(identity(raw), "synthetic-token", self.root / "received.zip")
        self.assertEqual(len(requests), 2)
        self.assertEqual(requests[0].get_header("Authorization"), "Bearer synthetic-token")
        self.assertTrue(requests[0].full_url.startswith("https://api.github.com/"))
        self.assertEqual(requests[1].full_url, signed)
        self.assertIsNone(requests[1].get_header("Authorization"))
        self.assertIsNone(requests[1].get_header("Cookie"))
        self.assertEqual((self.root / "received.zip").read_bytes(), raw)

    def test_refuse_unapproved_redirect_origins_and_automatic_redirect(self):
        for url in ("http://example.blob.core.windows.net/a", "https://api.github.com/a",
                    "https://example.invalid/a", "https://example.blob.core.windows.net.evil.invalid/a",
                    "https://user:password@example.blob.core.windows.net/a",
                    "https://example.blob.core.windows.net:444/a",
                    "https://example.blob.core.windows.net/a#fragment"):
            with self.subTest(url=url), self.assertRaises(relay.RelayError):
                relay.storage_request(url)
        self.assertIsNone(relay.NoRedirect().redirect_request(None, None, 302, "Found", {}, "https://example.invalid"))

    def test_api_access_failure_stops_without_retry(self):
        fake = mock.Mock()
        fake.open.side_effect = urllib.error.HTTPError("https://api.github.com/redacted", 403,
                                                      "Forbidden", {}, io.BytesIO())
        with mock.patch.object(relay, "opener", return_value=fake):
            with self.assertRaisesRegex(relay.RelayError, "^api_request_failed$"):
                relay.read_json_api(f"/repos/{relay.REPOSITORY}/actions/runs/{relay.SOURCE_RUN}", "synthetic-token")
        self.assertEqual(fake.open.call_count, 1)

    def test_storage_redirect_is_not_retried(self):
        fake = mock.Mock()
        fake.open.side_effect = urllib.error.HTTPError("https://example.blob.core.windows.net/redacted", 302,
                                                      "Found", {"Location": "https://example.invalid"}, io.BytesIO())
        with mock.patch.object(relay, "download_url", return_value="https://example.blob.core.windows.net/a"), \
             mock.patch.object(relay, "opener", return_value=fake):
            with self.assertRaisesRegex(relay.RelayError, "^archive_download_failed$"):
                relay.download_archive(identity(b"x"), "synthetic-token", self.root / "bad.zip")
        self.assertEqual(fake.open.call_count, 1)


if __name__ == "__main__":
    unittest.main()
