USE SqlIndexAdvisorShop;
GO

SET NOCOUNT ON;
SET STATISTICS IO, TIME ON;

EXEC sys.sp_executesql
    N'SELECT o.OrderId, o.OrderDate, o.Status, oi.LineNumber, p.Name AS ProductName, oi.Quantity, oi.UnitPrice
      FROM dbo.Orders o
      JOIN dbo.OrderItems oi ON oi.OrderId = o.OrderId
      JOIN dbo.Products p ON p.ProductId = oi.ProductId
      WHERE o.CustomerId = @CustomerId
        AND o.OrderDate >= @From
        AND o.OrderDate < @To
      ORDER BY o.OrderDate, oi.LineNumber;',
    N'@CustomerId int, @From datetime2(0), @To datetime2(0)',
    @CustomerId = 1008,
    @From = '2024-01-01',
    @To = '2025-01-01';

SET STATISTICS IO, TIME OFF;
GO
