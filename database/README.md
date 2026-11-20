# Synthetic Shop database

`SqlIndexAdvisorShop` is a small, fully synthetic online-shop database that gives the advisor something
realistic to analyze. Every value is generated; no real data is involved.

## Running it

Requires SQL Server 2019 or later, `sqlcmd`, and Windows authentication.

```powershell
.\run-all.ps1                                       # create on localhost\MSSQLSERVER02
.\run-all.ps1 -ServerInstance 'localhost\SQL2022'   # another instance
.\run-all.ps1 -Recreate                             # drop the database first, then rebuild
```

Without `-Recreate`, an existing database is never touched: every script stops at its guard. The scripts
are meant to be run through `run-all.ps1` (or `sqlcmd -b`), which stops at the first error.

| Script | Purpose |
|---|---|
| `01-create-database.sql` | Database with SIMPLE recovery and Query Store capturing all queries |
| `02-schema.sql` | `Customers`, `Products`, `Orders`, `OrderItems`: primary keys, foreign keys and checks only |
| `03-index-traps.sql` | Two deliberately bad indexes (see below) |
| `04-seed.sql` | 50k customers, 5k products, 500k orders, ~1.5M order lines (about 35 s) |
| `05-slow-query.sql` | The query the advisor should fix, run once with I/O and time statistics |

The seed is deterministic: values come from a hash of the row number, so every rebuild produces
identical data and before/after measurements are comparable.

## What the advisor should find

| Finding | Where | Evidence |
|---|---|---|
| Missing index | `Orders (CustomerId, OrderDate)` | The slow query scans all 500k orders (about 2,600 logical reads) to return 7 |
| Unindexed foreign keys | `Orders.CustomerId`, `OrderItems.ProductId` | SQL Server does not index foreign key columns automatically |
| Unused index | `IX_Orders_Status` | Maintained on every insert, never read |
| Duplicate index | `IX_Customers_Email` and `IX_Customers_EmailLookup` | Identical keys; one is pure overhead |

Order volume is skewed: a few customers place thousands of orders, most place fewer than ten. The slow
query uses a typical customer (1008); customer 1 is the heavy outlier.

## Caveats

- Ad-hoc queries on `Orders` or `Customers` can scan the narrow trap indexes and spoil the "unused"
  evidence. Rebuild with `-Recreate` before a demo.
- Index usage and missing-index statistics reset when SQL Server restarts.
