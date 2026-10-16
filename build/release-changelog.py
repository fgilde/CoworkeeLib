"""Turns the "Unreleased" section of the changelogs (EN + DE) into the section of a release.

    python build/release-changelog.py 0.0.1.26 [2026-10-16]

Run by the release workflow; prints the files it changed. Idempotent: without an Unreleased heading,
or when the version has a section already, a file stays as it is.
"""
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

DOCS = Path(__file__).resolve().parent.parent / "website" / "docs" / "reference"
UNRELEASED = {"changelog.md": "Unreleased", "changelog.de.md": "Unveröffentlicht"}


def release(text: str, heading: str, version: str, day: str) -> str:
    if re.search(rf"^## {re.escape(version)}( |$)", text, re.M):
        return text
    return re.sub(rf"^## {re.escape(heading)}[ \t]*(?=\r?$)", f"## {version} ({day})", text, count=1, flags=re.M)


def main(args: list[str]) -> int:
    if not args or not re.fullmatch(r"\d+(\.\d+)+", args[0].removeprefix("v")):
        print(__doc__, file=sys.stderr)
        return 2
    version = args[0].removeprefix("v")
    day = args[1] if len(args) > 1 else datetime.now(timezone.utc).date().isoformat()
    for name, heading in UNRELEASED.items():
        path = DOCS / name
        text = path.read_bytes().decode("utf-8")
        changed = release(text, heading, version, day)
        if changed != text:
            path.write_bytes(changed.encode("utf-8"))
            print(path)
    return 0


if __name__ == "__main__":
    assert release("# C\n\n## Unreleased\n\n- a\n\n## 1.0 (2026-01-01)\n", "Unreleased", "1.1", "2026-02-02") == "# C\n\n## 1.1 (2026-02-02)\n\n- a\n\n## 1.0 (2026-01-01)\n"
    assert release("## 1.1 (2026-02-02)\n## Unreleased\n", "Unreleased", "1.1", "2026-02-03") == "## 1.1 (2026-02-02)\n## Unreleased\n"
    assert release("## Unreleased\r\n- a\r\n", "Unreleased", "1.1", "2026-02-02") == "## 1.1 (2026-02-02)\r\n- a\r\n"
    assert release("## 1.0 (2026-01-01)\n", "Unreleased", "1.1", "2026-02-02") == "## 1.0 (2026-01-01)\n"
    sys.exit(main(sys.argv[1:]))
