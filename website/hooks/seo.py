import html
import re

_PARAGRAPH = re.compile(r"<p>(.*?)</p>", re.S)
_TAG = re.compile(r"<[^>]+>")


def on_page_content(content, page, config, files):
    """Pages without a description in their front matter describe themselves with their first paragraph."""
    if page.meta.get("description"):
        return content
    for match in _PARAGRAPH.finditer(content):
        text = " ".join(html.unescape(_TAG.sub("", match.group(1))).split())
        if len(text) >= 40:
            page.meta["description"] = text if len(text) <= 160 else text[:157].rsplit(" ", 1)[0] + "…"
            break
    return content
