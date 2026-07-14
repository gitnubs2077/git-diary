#!/usr/bin/env python3
"""Allow Blazor's generated import map under a script-src without 'unsafe-inline'.

wwwroot/index.html ships a CSP whose script-src deliberately omits 'unsafe-inline',
so that an injected <script> or inline event handler cannot execute even if the
Markdown renderer is ever compromised (the GitHub PAT lives in localStorage, so
script execution == full repo compromise).

That leaves one problem. `dotnet publish` rewrites

    <script type="importmap"></script>

into a populated import map whose body embeds fingerprinted asset filenames, e.g.

    {"imports":{"./_framework/dotnet.js":"./_framework/dotnet.4mcvj2l9ge.js", ...}}

Import maps are governed by script-src. An inline one with no 'unsafe-inline' and
no matching hash is blocked, and Blazor then never boots — the app hangs forever on
the loading spinner with no error in the page. Because the body changes whenever any
asset fingerprint changes, the hash cannot be hardcoded in index.html; it has to be
recomputed against the published output. That is what this script does.

Run it against the PUBLISHED index.html (not the source one), after any step that
rewrites the file. Idempotent: re-running replaces a previously pinned hash.
"""

from __future__ import annotations

import base64
import hashlib
import pathlib
import re
import sys

# Matches the populated import map and captures its exact body. CSP hashes are
# computed over the byte-exact text content of the element, so the capture group
# must not be stripped, reindented, or otherwise normalized.
IMPORTMAP_RE = re.compile(
    r'<script\s+type="importmap"\s*>(?P<body>.*?)</script>',
    re.DOTALL | re.IGNORECASE,
)

# The CSP meta tag, captured so the script-src edit below can be scoped to the
# `content="..."` attribute and nothing else.
#
# Scoping matters: index.html carries a long prose comment ABOUT the CSP, which
# mentions "script-src" in running text. A naive /script-src[^;]*/ rewrite happily
# matches that sentence first and pins the hash inside an HTML comment — the file
# looks patched, the script reports success, and the deployed app is dead on
# arrival because the real directive never changed. (The base-href rewrite in
# deploy.yml has its own scar tissue from exactly this failure mode.)
CSP_META_RE = re.compile(
    r'(?P<prefix><meta\s+http-equiv="Content-Security-Policy"\s+content=")'
    r'(?P<policy>[^"]*)'
    r'(?P<suffix>")',
    re.IGNORECASE,
)
SCRIPT_SRC_RE = re.compile(r"(?P<prefix>script-src\s)(?P<value>[^;]*)")
EXISTING_HASH_RE = re.compile(r"\s*'sha256-[A-Za-z0-9+/=]+'")


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(f"usage: {argv[0]} <path-to-published-index.html>", file=sys.stderr)
        return 2

    path = pathlib.Path(argv[1])
    html = path.read_text(encoding="utf-8")

    match = IMPORTMAP_RE.search(html)
    if match is None:
        print("::error::No <script type=\"importmap\"> found in the published "
              "index.html. Blazor's publish step should have emitted one; if the "
              "SDK changed the boot mechanism, this script and the CSP in "
              "wwwroot/index.html both need revisiting.", file=sys.stderr)
        return 1

    body = match.group("body")
    if not body.strip():
        # The dev-time placeholder is empty. An empty inline script has nothing to
        # execute, browsers do not block it, and hashing it would be meaningless —
        # so this is only ever a signal that we were pointed at the wrong file.
        print("::error::The import map is empty, which means this is the SOURCE "
              "index.html, not the published one. Point this script at "
              "publish/wwwroot/index.html.", file=sys.stderr)
        return 1

    digest = base64.b64encode(hashlib.sha256(body.encode("utf-8")).digest()).decode("ascii")
    token = f"'sha256-{digest}'"

    csp = CSP_META_RE.search(html)
    if csp is None:
        print("::error::No Content-Security-Policy <meta> tag found in the "
              "published index.html. The CSP is the only thing standing between a "
              "Markdown-preview XSS and the user's PAT — refusing to deploy "
              "without it.", file=sys.stderr)
        return 1

    def patch_script_src(m: re.Match[str]) -> str:
        # Drop any stale hash from a previous run before appending the current one,
        # so this stays idempotent across repeated invocations on the same file.
        value = EXISTING_HASH_RE.sub("", m.group("value")).rstrip()
        return f"{m.group('prefix')}{value} {token}"

    policy, n = SCRIPT_SRC_RE.subn(patch_script_src, csp.group("policy"), count=1)
    if n != 1:
        print("::error::The CSP meta tag has no script-src directive. Without it "
              "every inline script — including an injected one — runs freely.",
              file=sys.stderr)
        return 1

    patched = html[: csp.start()] + csp.group("prefix") + policy + csp.group("suffix") + html[csp.end():]

    # Belt and braces: prove the hash landed inside the real directive rather than
    # in prose somewhere. This assertion is the whole reason the bug above is not
    # still here.
    check = CSP_META_RE.search(patched)
    assert check is not None and token in check.group("policy"), \
        "hash was not written into the CSP meta content attribute"

    path.write_text(patched, encoding="utf-8")
    print(f"Pinned import-map hash into script-src: {token}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
