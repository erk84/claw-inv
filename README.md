# claw-inv (C#)

Backtesting playground for **fund investing strategies** (initial target: Swedish funds via Avanza).

## Goals

- Download/cached historical NAV data for a set of funds
- Run backtests for multiple strategies
- Compare metrics (CAGR, volatility, max drawdown, Sharpe)
- Search/optimize strategy parameters

## Status

This is the first C# skeleton.

- ✅ CLI scaffold (`claw-inv`)
- ✅ Metrics + CSV NAV loader
- ✅ Avanza fund search via public endpoint
- ⚠️ NAV time series downloader is not implemented yet (endpoint discovery next)

## Build / Run

Requires **.NET 8 SDK**.

```bash
dotnet build

# fund search (Avanza)
dotnet run --project src/ClawInv.Cli -- search "Avanza Zero"

# metrics from CSV (Date,NAV)
dotnet run --project src/ClawInv.Cli -- metrics --csv data/example.csv

# backtest (buy & hold)
dotnet run --project src/ClawInv.Cli -- backtest --csv data/example.csv --initial-capital 100000
```

## CSV format

CSV with headers:

- `Date` (YYYY-MM-DD)
- `NAV` (decimal)

