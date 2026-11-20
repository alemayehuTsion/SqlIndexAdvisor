USE SqlIndexAdvisorShop;
GO

SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM dbo.Customers WITH (INDEX(PK_Customers)))
BEGIN
    RAISERROR(N'Data already seeded. Drop the database manually to recreate it.', 16, 1);
    RETURN;
END;
GO

-- Values derive from a hash of the row number instead of NEWID()/RAND(), so every run produces
-- identical data and before/after measurements compare like with like.
DROP TABLE IF EXISTS #Random;

WITH Ten AS
(
    SELECT v FROM (VALUES (0), (1), (2), (3), (4), (5), (6), (7), (8), (9)) AS t (v)
),
Numbers AS
(
    SELECT TOP (2500000) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS int) AS n
    FROM Ten a CROSS JOIN Ten b CROSS JOIN Ten c CROSS JOIN Ten d CROSS JOIN Ten e CROSS JOIN Ten f CROSS JOIN Ten g
)
SELECT
    n.n,
    CAST(SUBSTRING(h.Hash, 1, 4) AS int) & 2147483647 AS R1,
    CAST(SUBSTRING(h.Hash, 5, 4) AS int) & 2147483647 AS R2,
    CAST(SUBSTRING(h.Hash, 9, 4) AS int) & 2147483647 AS R3,
    CAST(SUBSTRING(h.Hash, 13, 4) AS int) & 2147483647 AS R4
INTO #Random
FROM Numbers n
CROSS APPLY (SELECT HASHBYTES('SHA2_256', CAST(n.n AS binary(4))) AS Hash) h;

CREATE UNIQUE CLUSTERED INDEX IX_Random_n ON #Random (n);
GO

INSERT INTO dbo.Customers WITH (TABLOCK) (Email, FirstName, LastName, City, CountryCode, CreatedAt)
SELECT
    CONCAT(N'customer', r.n, N'@example.com'),
    CHOOSE(r.R1 % 10 + 1, N'Alex', N'Sam', N'Jordan', N'Taylor', N'Morgan', N'Casey', N'Riley', N'Jamie', N'Avery', N'Quinn'),
    CHOOSE(r.R2 % 10 + 1, N'Smith', N'Johnson', N'Brown', N'Garcia', N'Miller', N'Davis', N'Wilson', N'Moore', N'Clark', N'Lewis'),
    CHOOSE(loc.Pick, N'London', N'Paris', N'Berlin', N'Madrid', N'Rome', N'Amsterdam', N'Toronto', N'Sydney'),
    CHOOSE(loc.Pick, 'GB', 'FR', 'DE', 'ES', 'IT', 'NL', 'CA', 'AU'),
    DATEADD(MINUTE, r.R4 % 1051200, CAST('2020-01-01' AS datetime2(0)))
FROM #Random r
CROSS APPLY (SELECT r.R3 % 8 + 1 AS Pick) loc
WHERE r.n <= 50000
ORDER BY r.n;

INSERT INTO dbo.Products WITH (TABLOCK) (Sku, Name, Category, UnitPrice, IsActive)
SELECT
    CONCAT('SKU-', RIGHT(CONCAT('0000', r.n), 5)),
    CONCAT(c.Category, N' item ', r.n),
    c.Category,
    CAST((r.R2 % 49900 + 100) / 100.0 AS decimal(10, 2)),
    CAST(IIF(r.R3 % 10 = 0, 0, 1) AS bit)
FROM #Random r
CROSS APPLY (SELECT CHOOSE(r.R1 % 8 + 1, N'Books', N'Electronics', N'Garden', N'Kitchen', N'Toys', N'Sports', N'Clothing', N'Beauty') AS Category) c
WHERE r.n <= 5000
ORDER BY r.n;

-- Squaring the uniform value skews orders toward low CustomerIds: a few heavy buyers, many light ones.
INSERT INTO dbo.Orders WITH (TABLOCK) (CustomerId, OrderDate, Status, TotalAmount)
SELECT
    1 + CAST(SQUARE(r.R1 / 2147483648.0) * 50000 AS int),
    d.OrderDate,
    CASE
        WHEN r.R3 % 100 < 5 THEN 'Pending'
        WHEN r.R3 % 100 < 10 THEN 'Cancelled'
        WHEN r.R3 % 100 < 30 THEN 'Paid'
        ELSE 'Shipped'
    END,
    0
FROM #Random r
CROSS APPLY (SELECT DATEADD(SECOND, r.R2 % 126230400, CAST('2022-01-01' AS datetime2(0))) AS OrderDate) d
WHERE r.n <= 500000
ORDER BY d.OrderDate;

-- The PK_Orders hint stops the optimizer from scanning the narrower IX_Orders_Status instead,
-- which would record reads against the index that must look unused.
INSERT INTO dbo.OrderItems WITH (TABLOCK) (OrderId, LineNumber, ProductId, Quantity, UnitPrice)
SELECT
    o.OrderId,
    l.LineNumber,
    p.ProductId,
    1 + item.R2 % 4,
    p.UnitPrice
FROM dbo.Orders o WITH (INDEX(PK_Orders))
JOIN #Random orderSeed ON orderSeed.n = o.OrderId
CROSS JOIN (VALUES (1), (2), (3), (4), (5)) AS l (LineNumber)
JOIN #Random item ON item.n = (o.OrderId - 1) * 5 + l.LineNumber
JOIN dbo.Products p ON p.ProductId = 1 + item.R1 % 5000
WHERE l.LineNumber <= 1 + orderSeed.R4 % 5;

UPDATE o
SET TotalAmount = t.Total
FROM dbo.Orders o WITH (INDEX(PK_Orders))
JOIN
(
    SELECT OrderId, SUM(Quantity * UnitPrice) AS Total
    FROM dbo.OrderItems
    GROUP BY OrderId
) t ON t.OrderId = o.OrderId;

DROP TABLE #Random;
GO
