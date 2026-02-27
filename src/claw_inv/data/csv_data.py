from __future__ import annotations

from pathlib import Path

import pandas as pd


def read_nav_csv(path: Path) -> pd.DataFrame:
    """Read CSV with columns Date,NAV.

    Returns a DataFrame with columns: date (datetime64), nav (float).
    """

    df = pd.read_csv(path)

    expected = {"Date", "NAV"}
    missing = expected - set(df.columns)
    if missing:
        raise ValueError(f"Missing columns: {sorted(missing)}")

    out = df[["Date", "NAV"]].copy()
    out["date"] = pd.to_datetime(out["Date"], utc=True).dt.tz_convert(None)
    out["nav"] = pd.to_numeric(out["NAV"], errors="raise")
    out = out.drop(columns=["Date", "NAV"]).sort_values("date").dropna()

    # drop duplicates (keep last)
    out = out.drop_duplicates(subset=["date"], keep="last")
    return out.reset_index(drop=True)
