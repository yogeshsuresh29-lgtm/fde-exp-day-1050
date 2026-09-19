-- Legacy banking schema (SQLite). Deliberately minimal and dated —
-- this represents "the system as the bank actually has it", not a
-- clean green-field design. No foreign key enforcement was ever
-- turned on in the original system either.

PRAGMA foreign_keys = OFF;

CREATE TABLE customers (
    customer_id     INTEGER PRIMARY KEY,
    full_name       TEXT NOT NULL,
    phone_raw       TEXT,              -- intentionally unnormalized, see seed.sql
    email           TEXT,
    created_date    TEXT               -- stored as free-text, not a real DATE type
);

CREATE TABLE accounts (
    account_id      INTEGER PRIMARY KEY,
    customer_id     INTEGER NOT NULL,
    account_type    TEXT NOT NULL,     -- 'checking' | 'savings'
    balance_cents   INTEGER NOT NULL,  -- money stored as integer cents, legacy convention
    status          TEXT NOT NULL DEFAULT 'active'
);

CREATE TABLE transactions (
    transaction_id  INTEGER PRIMARY KEY,
    account_id      INTEGER NOT NULL,
    amount_cents    INTEGER NOT NULL,  -- negative = debit, positive = credit
    description     TEXT,
    txn_date        TEXT NOT NULL
);
