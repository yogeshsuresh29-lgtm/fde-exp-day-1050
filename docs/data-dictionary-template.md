# Data Dictionary — Domain Mapping (Stage 1)

Fill this in as you explore `legacy_bank.db` with `sqlite3`. Two rows are done for you, showing the level of detail expected — not because they're the hardest ones, but so there's a concrete answer key for what "done" looks like.

```
sqlite3 legacy_bank.db
.schema customers
.schema accounts
.schema transactions
SELECT customer_id, full_name, phone_raw FROM customers;
```

| Table | Column | Type | Notes |
|---|---|---|---|
| `accounts` | `balance_cents` | INTEGER | Stored as cents, not dollars — legacy convention. Divide by 100 before displaying to a customer. |
| `customers` | `created_date` | TEXT | Free-text, not a real DATE type — don't assume it parses cleanly. |
| `customers` | `phone_raw` | | |
| `customers` | `email` | | |
| `accounts` | `account_type` | | |
| `accounts` | `status` | | |
| `transactions` | `amount_cents` | | |
| `transactions` | `description` | | |
| `transactions` | `txn_date` | | |

Add rows for anything else you find that isn't listed above — this table isn't meant to be exhaustive by construction, it's meant to capture what you actually discover.
