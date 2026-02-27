from __future__ import annotations

import pandas as pd

from claw_inv.metrics import compute_metrics


def test_compute_metrics_basic():
    nav = pd.DataFrame(
        {
            "date": pd.to_datetime(["2020-01-01", "2021-01-01", "2022-01-01"]),
            "nav": [100.0, 110.0, 121.0],
        }
    )
    m = compute_metrics(nav)
    assert m["cagr"] is not None
    # ~10% CAGR
    assert 0.09 < m["cagr"] < 0.11
