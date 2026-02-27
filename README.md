# claw-inv

A small Python project to **backtest fund investing strategies**.

## Goals

- Fetch historical fund data (intended: from Avanza)
- Run backtests for different strategies
- Compare metrics (CAGR, volatility, max drawdown)

## Status

This is an early skeleton.

- ✅ Fund name search via Avanza (public endpoint)
- ⚠️ NAV time series download: endpoint discovery in progress (see `claw_inv/data/avanza.py`)
- ✅ Backtest engine + a couple strategies

## Quickstart

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -e '.[dev]'

# search funds
claw-inv search "Avanza Zero"

# backtest using local CSV (Date,NAV)
claw-inv backtest --csv data/example_avanza_zero.csv --strategy buy_and_hold
```

## Data format (CSV)

CSV with columns:

- `Date` (YYYY-MM-DD)
- `NAV` (float)

