IF DB_ID(N'SqlIndexAdvisorShop') IS NOT NULL
BEGIN
    RAISERROR(N'Database SqlIndexAdvisorShop already exists. Drop it manually to recreate it.', 16, 1);
    RETURN;
END;

CREATE DATABASE SqlIndexAdvisorShop;
GO

-- SIMPLE recovery keeps the log small while seeding millions of rows; this is a throwaway demo database.
ALTER DATABASE SqlIndexAdvisorShop SET RECOVERY SIMPLE;

-- Query Store is only on by default from SQL Server 2022, and capture mode ALL records the demo query
-- even when it runs just once (AUTO would skip it).
ALTER DATABASE SqlIndexAdvisorShop SET QUERY_STORE = ON
(
    OPERATION_MODE = READ_WRITE,
    QUERY_CAPTURE_MODE = ALL
);
GO
