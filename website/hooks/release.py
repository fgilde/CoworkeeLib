import os
import re
from datetime import date

_HEADING = re.compile(r"^## (\d+(?:\.\d+)+) \((\d{4})-(\d{2})-(\d{2})\)", re.M)
_MONTHS_EN = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"]
_MONTHS_DE = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"]


def on_config(config):
    """The landing page shows the newest version heading of the changelog."""
    with open(os.path.join(config["docs_dir"], "reference", "changelog.md"), encoding="utf-8") as f:
        match = _HEADING.search(f.read())
    if match:
        version, *ymd = match.groups()
        d = date(*map(int, ymd))
        config["extra"]["release"] = {
            "version": version,
            "en": f"{d.day} {_MONTHS_EN[d.month - 1]} {d.year}",
            "de": f"{d.day}. {_MONTHS_DE[d.month - 1]} {d.year}",
        }
    return config
