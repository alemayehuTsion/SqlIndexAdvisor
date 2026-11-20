USE SqlIndexAdvisorShop;
GO

IF OBJECT_ID(N'dbo.Customers', N'U') IS NOT NULL
BEGIN
    RAISERROR(N'Schema already exists. Drop the database manually to recreate it.', 16, 1);
    RETURN;
END;
GO


CREATE TABLE dbo.Customers
(
    CustomerId  int IDENTITY(1, 1) NOT NULL,
    Email       nvarchar(256)      NOT NULL,
    FirstName   nvarchar(50)       NOT NULL,
    LastName    nvarchar(50)       NOT NULL,
    City        nvarchar(100)      NOT NULL,
    CountryCode char(2)            NOT NULL,
    CreatedAt   datetime2(0)       NOT NULL,
    CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (CustomerId)
);

CREATE TABLE dbo.Products
(
    ProductId int IDENTITY(1, 1) NOT NULL,
    Sku       varchar(20)        NOT NULL,
    Name      nvarchar(200)      NOT NULL,
    Category  nvarchar(50)       NOT NULL,
    UnitPrice decimal(10, 2)     NOT NULL,
    IsActive  bit                NOT NULL,
    CONSTRAINT PK_Products PRIMARY KEY CLUSTERED (ProductId),
    CONSTRAINT CK_Products_UnitPrice CHECK (UnitPrice >= 0)
);

CREATE TABLE dbo.Orders
(
    OrderId     int IDENTITY(1, 1) NOT NULL,
    CustomerId  int                NOT NULL,
    OrderDate   datetime2(0)       NOT NULL,
    Status      varchar(20)        NOT NULL,
    TotalAmount decimal(12, 2)     NOT NULL,
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderId),
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (CustomerId),
    CONSTRAINT CK_Orders_Status CHECK (Status IN ('Pending', 'Paid', 'Shipped', 'Cancelled')),
    CONSTRAINT CK_Orders_TotalAmount CHECK (TotalAmount >= 0)
);

CREATE TABLE dbo.OrderItems
(
    OrderId    int            NOT NULL,
    LineNumber smallint       NOT NULL,
    ProductId  int            NOT NULL,
    Quantity   smallint       NOT NULL,
    UnitPrice  decimal(10, 2) NOT NULL,
    CONSTRAINT PK_OrderItems PRIMARY KEY CLUSTERED (OrderId, LineNumber),
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (OrderId),
    CONSTRAINT FK_OrderItems_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products (ProductId),
    CONSTRAINT CK_OrderItems_Quantity CHECK (Quantity > 0),
    CONSTRAINT CK_OrderItems_UnitPrice CHECK (UnitPrice >= 0)
);
GO
