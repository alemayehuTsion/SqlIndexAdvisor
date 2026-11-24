-- Both timestamps come from the server's clock so the statistics window is not skewed by client clock drift.
-- sqlserver_start_time is local server time without an offset; the current offset is applied, which is off by
-- the DST shift if the server started before a daylight-saving change.
SELECT
    DB_NAME()                                                                       AS DatabaseName,
    SYSDATETIMEOFFSET()                                                             AS CollectedAt,
    TODATETIMEOFFSET(sqlserver_start_time, DATEPART(TZOFFSET, SYSDATETIMEOFFSET())) AS ServerStartTime
FROM sys.dm_os_sys_info;
