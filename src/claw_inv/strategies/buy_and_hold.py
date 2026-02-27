from __future__ import annotations

from claw_inv.strategies.base import Strategy


class BuyAndHold(Strategy):
    def __init__(self):
        super().__init__(name="buy_and_hold")
