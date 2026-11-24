-- All timestamps come from the server's clock so the statistics window is not skewed by client clock drift.
-- sqlserver_start_time and create_date are local server time without an offset; the current offset is applied,
-- which is off by the DST shift if either happened before a daylight-saving change.
SELECT
    DB_NAME()                                                                       AS DatabaseName,
    SYSDATETIMEOFFSET()                                                             AS CollectedAt,
    TODATETIMEOFFSET(i.sqlserver_start_time, DATEPART(TZOFFSET, SYSDATETIMEOFFSET())) AS ServerStartTime,
    TODATETIMEOFFSET(d.create_date, DATEPART(TZOFFSET, SYSDATETIMEOFFSET()))        AS DatabaseCreatedAt
FROM sys.dm_os_sys_info i
CROSS JOIN sys.databases d
WHERE d.database_id = DB_ID();
