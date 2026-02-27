from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any

import requests


@dataclass(frozen=True)
class AvanzaFundHit:
    name: str
    isin: str
    orderbook_id: str
    rating: int | None
    risk: int | None


def avanza_search(name: str, *, timeout_s: float = 20.0) -> list[dict[str, Any]]:
    """Search funds by name.

    Uses: POST https://www.avanza.se/_api/fund-guide/search

    This endpoint appears to be public (no login required).
    """

    url = "https://www.avanza.se/_api/fund-guide/search"
    headers = {
        "Accept": "application/json",
        "Content-Type": "application/json",
        "X-Requested-With": "XMLHttpRequest",
        "User-Agent": "claw-inv/0.1",
    }

    r = requests.post(url, headers=headers, data=json.dumps({"name": name}), timeout=timeout_s)
    r.raise_for_status()
    payload = r.json()
    hits = payload.get("fundSearchViews", [])

    # Keep raw dicts for now (stable CLI output)
    return hits


def avanza_chart_timeseries(orderbook_id: str, time_period: str):
    """Attempt to fetch NAV time series from Avanza.

    Intended endpoint (as seen in Avanza frontend JS bundle):
      GET https://www.avanza.se/_api/fund-guide/chart/{orderbookId}/{timePeriod}

    However, this currently returns HTTP 400 from this runtime environment.
    Kept here as a placeholder so we can iterate quickly once the correct
    parameters/headers are confirmed.
    """

    url = f"https://www.avanza.se/_api/fund-guide/chart/{orderbook_id}/{time_period}"
    headers = {
        "Accept": "application/json",
        "X-Requested-With": "XMLHttpRequest",
        "User-Agent": "claw-inv/0.1",
        "Referer": "https://www.avanza.se/fonder/handla-fonder.html",
    }
    r = requests.get(url, headers=headers, timeout=20.0)

    if r.status_code == 400:
        raise RuntimeError(
            "Avanza chart endpoint returned 400. "
            "Likely missing/changed parameters, or requires a different endpoint. "
            f"url={url}"
        )

    r.raise_for_status()
    return r.json()
