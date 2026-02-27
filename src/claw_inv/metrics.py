from __future__ import annotations

import math

import numpy as np
import pandas as pd


def _max_drawdown(equity: np.ndarray) -> float:
    peak = -np.inf
    mdd = 0.0
    for x in equity:
        peak = max(peak, x)
        dd = (x / peak) - 1.0
        mdd = min(mdd, dd)
    return float(mdd)


def compute_metrics(nav: pd.DataFrame) -> dict:
    """Compute basic metrics from NAV series.

    nav: columns date, nav
    """

    df = nav.copy().sort_values("date")
    df["ret"] = df["nav"].pct_change()
    rets = df["ret"].dropna().to_numpy(dtype=float)

    if len(df) < 2 or len(rets) == 0:
        return {
            "start": None,
            "end": None,
            "days": 0,
            "cagr": None,
            "vol": None,
            "sharpe": None,
            "max_drawdown": None,
        }

    start = df["date"].iloc[0]
    end = df["date"].iloc[-1]
    days = (end - start).days

    start_nav = float(df["nav"].iloc[0])
    end_nav = float(df["nav"].iloc[-1])

    years = max(days / 365.25, 1e-9)
    cagr = (end_nav / start_nav) ** (1.0 / years) - 1.0

    # assume daily frequency; annualize by sqrt(252)
    vol = float(np.std(rets, ddof=1) * math.sqrt(252.0)) if len(rets) > 1 else 0.0
    sharpe = float(cagr / vol) if vol > 0 else None

    equity = df["nav"].to_numpy(dtype=float)
    mdd = _max_drawdown(equity)

    return {
        "start": str(start.date()),
        "end": str(end.date()),
        "days": int(days),
        "cagr": float(cagr),
        "vol": float(vol),
        "sharpe": sharpe,
        "max_drawdown": float(mdd),
    }
