import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("release_build", Path(__file__).with_name("build.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name) / "repo"
        self.root.mkdir()
        self.git("init", "-q")
        self.git("config", "user.name", "Release test")
        self.git("config", "user.email", "test@example.invalid")
        self.put("package.json", json.dumps({"name": "zframework.gameplaytag", "version": "1.1.0-rc.1", "unity": "2021.3"}))
        for n in ("LICENSE", "CHANGELOG.md", "RELEASE.md"):
            self.put(n, "test\n")
        self.put("README.md", "[Evidence](Tests~/report.md) [MIT](./LICENSE.md)\n")
        self.put("Tests~/report.md", "historical evidence\n")
        for folder in ("Runtime", "Editor"):
            self.put(folder + "/Example.cs", "// frozen\n")
            self.meta(folder + "/Example.cs")
            self.put(folder + "/Example.asmdef", json.dumps({"name": folder, "references": [], "allowUnsafeCode": False}))
            self.meta(folder + "/Example.asmdef")
        self.put("Tests/Runtime/ReleaseContractTests.cs", "// test\n")
        self.meta("Tests/Runtime/ReleaseContractTests.cs")
        self.put("Tests/Runtime/GameplayTags.Tests.asmdef", json.dumps({"name": "GameplayTags.Tests", "references": ["GameplayTags"], "optionalUnityReferences": ["TestAssemblies"]}))
        self.meta("Tests/Runtime/GameplayTags.Tests.asmdef")
        self.commit()
        self.baseline = self.git("rev-parse", "HEAD").strip()
        policy = {"version": "1.1.0-rc.1", "implementation_commit": self.baseline,
                  "evidence_commit": self.baseline,
                  "frozen_trees": {f: self.git("rev-parse", "HEAD:" + f).strip() for f in ("Runtime", "Editor")}}
        self.put("Tools~/Release/selection.json", json.dumps(policy))
        self.commit()

    def tearDown(self):
        self.tmp.cleanup()

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.root), *args], stderr=subprocess.PIPE).decode()

    def put(self, name, content):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def meta(self, name):
        import hashlib
        self.put(name + ".meta", "fileFormatVersion: 2\nguid: " + hashlib.md5(name.encode()).hexdigest() + "\n")

    def commit(self):
        self.git("add", ".")
        self.git("commit", "-qm", "fixture")

    def run_build(self, name="out"):
        return module.build(self.root, Path(self.tmp.name) / name)

    def test_reproducible_and_allowlisted(self):
        a = self.run_build("a")
        b = self.run_build("b")
        self.assertEqual(a["sha256"], b["sha256"])
        manifest = json.loads((Path(self.tmp.name) / "a/manifest.json").read_text())
        self.assertNotIn("Tests~/report.md", manifest["files"])
        self.assertNotIn("Tools~/Release/selection.json", manifest["files"])
        self.assertIn("Tests/Runtime/ReleaseContractTests.cs", manifest["files"])
        self.assertNotEqual(manifest["files"]["README.md"]["source_sha256"], manifest["files"]["README.md"]["package_sha256"])

    def test_no_output_overwrite(self):
        self.run_build()
        with self.assertRaises(FileExistsError):
            self.run_build()

    def test_uncommitted_runtime_rejected(self):
        self.put("Runtime/Example.cs", "// changed\n")
        with self.assertRaisesRegex(ValueError, "Uncommitted"):
            self.run_build()

    def test_committed_runtime_rejected(self):
        self.put("Runtime/Example.cs", "// changed\n")
        self.commit()
        with self.assertRaisesRegex(ValueError, "implementation changed"):
            self.run_build()

    def test_untracked_runtime_rejected(self):
        self.put("Runtime/Injected.cs", "// extra\n")
        with self.assertRaisesRegex(ValueError, "Unexpected files"):
            self.run_build()

    def test_evidence_rewrite_rejected(self):
        self.put("Tests~/report.md", "not the original\n")
        self.commit()
        with self.assertRaisesRegex(ValueError, "Historical evidence"):
            self.run_build()

    def test_duplicate_guid_rejected(self):
        self.put("Tests/Runtime/ReleaseContractTests.cs.meta", (self.root / "Runtime/Example.cs.meta").read_text())
        self.commit()
        with self.assertRaisesRegex(ValueError, "Duplicate Unity GUID"):
            self.run_build()

    def test_stable_version_not_silently_accepted(self):
        package = json.loads((self.root / "package.json").read_text())
        package["version"] = "1.1.0"
        self.put("package.json", json.dumps(package))
        policy = json.loads((self.root / "Tools~/Release/selection.json").read_text())
        policy["version"] = "1.1.0"
        self.put("Tools~/Release/selection.json", json.dumps(policy))
        self.commit()
        with self.assertRaisesRegex(ValueError, "RCs only"):
            self.run_build()

    def test_symlink_input_rejected(self):
        path = self.root / "RELEASE.md"
        path.unlink()
        path.symlink_to(self.root / "LICENSE")
        with self.assertRaisesRegex(ValueError, "symlink"):
            self.run_build()


if __name__ == "__main__":
    unittest.main(verbosity=2)
