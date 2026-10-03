#!/usr/bin/env python3
"""Verify published archive/member bytes without extraction or historical reads."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import tarfile

ROOT = Path(__file__).resolve().parent


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def verify():
    inventory = json.loads((ROOT / 'inventory.json').read_text())
    expected = inventory['files']
    seen = set()
    total = 0
    for name, record in inventory['archives'].items():
        require(PurePosixPath(name).name == name, 'Unsafe archive path')
        raw = (ROOT / name).read_bytes()
        require(len(raw) == record['size'], 'Archive size mismatch: ' + name)
        require(digest(raw) == record['sha256'], 'Archive hash mismatch: ' + name)
        count = 0
        with tarfile.open(ROOT / name, 'r:gz') as archive:
            for member in archive:
                path = PurePosixPath(member.name)
                require(member.isfile() and not path.is_absolute() and '..' not in path.parts,
                        'Unsafe archive member: ' + member.name)
                require(member.name in expected and member.name not in seen,
                        'Unexpected/duplicate archive member: ' + member.name)
                entry = expected[member.name]
                require(entry['archive'] == name and entry['member'] == member.name,
                        'Member mapping mismatch: ' + member.name)
                data = archive.extractfile(member).read()
                require(len(data) == entry['size'] == member.size,
                        'Member size mismatch: ' + member.name)
                require(digest(data) == entry['sha256'], 'Member hash mismatch: ' + member.name)
                seen.add(member.name)
                count += 1
                total += len(data)
        require(count == record['file_count'], 'Archive member count mismatch: ' + name)
    require(seen == set(expected), 'Missing inventoried archive members')
    return {
        'pass': True,
        'archives': len(inventory['archives']),
        'files': len(seen),
        'uncompressed_file_bytes': total,
        'scope': 'Published archive/member integrity only; historical binaries are omitted',
        'benchmarks_executed': False,
    }


if __name__ == '__main__':
    print(json.dumps(verify(), indent=2))
