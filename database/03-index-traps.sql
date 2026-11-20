USE SqlIndexAdvisorShop;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Status' AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    RAISERROR(N'Index traps already exist. Drop the database manually to recreate it.', 16, 1);
    RETURN;
END;
GO

CREATE NONCLUSTERED INDEX IX_Orders_Status
    ON dbo.Orders (Status);

CREATE NONCLUSTERED INDEX IX_Customers_Email
    ON dbo.Customers (Email);

CREATE NONCLUSTERED INDEX IX_Customers_EmailLookup
    ON dbo.Customers (Email);
GO
