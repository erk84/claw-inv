from __future__ import annotations

import json
from pathlib import Path
from typing import Optional

import pandas as pd
import typer

from claw_inv.data.avanza import avanza_search
from claw_inv.data.csv_data import read_nav_csv
from claw_inv.engine import BacktestConfig, Backtester
from claw_inv.strategies.buy_and_hold import BuyAndHold
from claw_inv.strategies.momentum_top1_monthly import MomentumTop1Monthly

app = typer.Typer(no_args_is_help=True)


@app.command()
def search(query: str):
    """Search funds by name on Avanza (public endpoint)."""
    res = avanza_search(query)
    typer.echo(json.dumps(res, indent=2, ensure_ascii=False))


@app.command()
def backtest(
    csv: Optional[Path] = typer.Option(None, help="Path to CSV with Date,NAV."),
    strategy: str = typer.Option("buy_and_hold", help="buy_and_hold | momentum_top1_monthly"),
    initial_capital: float = typer.Option(100_000.0, help="Initial portfolio value."),
):
    """Run a backtest using a local NAV CSV (single fund) or a strategy."""

    if csv is None:
        raise typer.BadParameter("For now, --csv is required (NAV downloader WIP).")

    nav = read_nav_csv(csv)

    if strategy == "buy_and_hold":
        strat = BuyAndHold()
    elif strategy == "momentum_top1_monthly":
        # For now this strategy just mirrors buy&hold for single series.
        strat = MomentumTop1Monthly()
    else:
        raise typer.BadParameter("Unknown strategy")

    cfg = BacktestConfig(initial_capital=initial_capital)
    bt = Backtester(cfg)
    report = bt.run_single_series(nav=nav, strategy=strat)

    typer.echo(report.to_string(index=False))


@app.command()
def metrics(csv: Path):
    """Compute performance metrics from NAV CSV (no strategy)."""
    nav = read_nav_csv(csv)
    bt = Backtester(BacktestConfig())
    m = bt.metrics_from_nav(nav)
    df = pd.DataFrame([m])
    typer.echo(df.to_string(index=False))
