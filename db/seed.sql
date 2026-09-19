-- Seed data. Phone numbers are intentionally inconsistent — this is
-- the "Data Realities" friction point participants normalize in
-- Session 2. Every format below has actually been seen in a real
-- legacy export at some point.

INSERT INTO customers (customer_id, full_name, phone_raw, email, created_date) VALUES
 (1, 'Maria Chen',        '(555) 123-4567',      'maria.chen@example.com',   '2014-03-11'),
 (2, 'James Okafor',      '555-987-6543',        'j.okafor@example.com',    '2016-07-22'),
 (3, 'Priya Natarajan',   '5551234432',          'priya.n@example.com',     '2011-01-05'),
 (4, 'Tomas Novak',       '+1 555 222 3344',     'tnovak@example.com',      '2019-11-30'),
 (5, 'Aisha Bello',       '555.876.1122',        NULL,                      '2020-06-14'),
 (6, 'Liam O''Connor',    '  555-444-9988  ',    'liam.oconnor@example.com','2013-09-02'),
 (7, 'Grace Kim',         '(555)6667777',        'grace.kim@example.com',   '2022-02-18'),
 (8, 'Diego Fernandez',   '15553219876',         'diego.f@example.com',     '2017-04-09'),
 (9, 'Nadia Petrova',     NULL,                  'nadia.p@example.com',     '2021-08-25'),
 (10,'Samuel Osei',       '555 123 0001 ext 4',  'samuel.osei@example.com', '2015-12-01');

INSERT INTO accounts (account_id, customer_id, account_type, balance_cents, status) VALUES
 (101, 1,  'checking', 452310, 'active'),
 (102, 1,  'savings',  1200000,'active'),
 (103, 2,  'checking', 8734,   'active'),
 (104, 3,  'checking', 231045, 'active'),
 (105, 4,  'savings',  9981200,'active'),
 (106, 5,  'checking', 15020,  'active'),
 (107, 6,  'checking', 0,      'frozen'),
 (108, 7,  'savings',  502300, 'active'),
 (109, 8,  'checking', 67450,  'active'),
 (110, 9,  'checking', 340000, 'active'),
 (111, 10, 'checking', 12899,  'active');

INSERT INTO transactions (transaction_id, account_id, amount_cents, description, txn_date) VALUES
 (1001, 101, -4500,  'Grocery Mart',           '2026-08-20'),
 (1002, 101, -12000, 'Rent payment',           '2026-08-01'),
 (1003, 101, 250000, 'Payroll deposit',        '2026-08-15'),
 (1004, 103, -899,   'Streaming service',      '2026-08-18'),
 (1005, 104, -22000, 'Wire transfer out',      '2026-08-22'),
 (1006, 105, 50000,  'Interest credit',        '2026-08-01'),
 (1007, 108, -1500,  'ATM withdrawal',         '2026-08-19'),
 (1008, 109, -6700,  'Utility bill',           '2026-08-17'),
 (1009, 110, -45000, 'Wire transfer out',      '2026-08-23'),
 (1010, 111, -300,   'Card fee',               '2026-08-05');
