from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import pandas as pd

from claw_inv.metrics import compute_metrics
from claw_inv.strategies.base import Strategy


@dataclass(frozen=True)
class BacktestConfig:
    initial_capital: float = 100_000.0


class Backtester:
    def __init__(self, cfg: BacktestConfig):
        self.cfg = cfg

    def metrics_from_nav(self, nav: pd.DataFrame) -> dict:
        return compute_metrics(nav)

    def run_single_series(self, nav: pd.DataFrame, strategy: Strategy) -> pd.DataFrame:
        """Backtest a single NAV series.

        For single-series strategies, most of the logic reduces to: portfolio tracks NAV.
        """

        nav = nav.copy()
        nav["ret"] = nav["nav"].pct_change().fillna(0.0)

        equity = np.empty(len(nav), dtype=float)
        equity[0] = self.cfg.initial_capital
        for i in range(1, len(nav)):
            equity[i] = equity[i - 1] * (1.0 + float(nav.loc[i, "ret"]))

        nav["equity"] = equity

        m = compute_metrics(nav[["date", "nav"]])
        report = pd.DataFrame(
            [
                {
                    "strategy": strategy.name,
                    **m,
                }
            ]
        )
        return report
