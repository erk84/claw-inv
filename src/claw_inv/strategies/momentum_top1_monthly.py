from __future__ import annotations

from claw_inv.strategies.base import Strategy


class MomentumTop1Monthly(Strategy):
    """Placeholder for a multi-fund momentum strategy.

    In the next iteration, this should:
    - load NAV series for a universe of funds
    - compute trailing momentum (e.g., 6-12 months)
    - rebalance monthly into the top fund

    For now it behaves like a single-series pass-through.
    """

    def __init__(self):
        super().__init__(name="momentum_top1_monthly")
