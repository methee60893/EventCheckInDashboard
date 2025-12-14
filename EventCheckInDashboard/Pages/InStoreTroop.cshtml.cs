using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using System;
using System.Linq;
using System.Dynamic;


namespace EventCheckInDashboard.Pages
{
    public class InStoreTroopModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<InStoreTroopModel> _logger;

        // --- Properties สำหรับส่งข้อมูลไปแสดงผล ---
        public int ActualRights { get; private set; }
        public int ActualMembers { get; private set; }
        public string? StackedBarChartJson { get; private set; }
        public string? PieChartJson { get; private set; }
        public List<dynamic> SegmentTableData { get; private set; } = new();
        public List<string> SegmentTableHeaders { get; private set; } = new();
        public List<dynamic> TierTableData { get; private set; } = new();
        public List<string> TierTableHeaders { get; private set; } = new();
        public string? ErrorMessage { get; private set; }
        public InStoreTroopModel(IConfiguration configuration, ILogger<InStoreTroopModel> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }


        public async Task OnGetAsync()
        {
            try
            {
                string? connectionString = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException("Connection string 'DefaultConnection' is missing or empty.");
                }
                int stationId = 6;

                string startDate = "2025-10-24";
                string endDate = "2025-10-26";

                await LoadSummaryAutualRightDataAsync(connectionString, stationId, startDate, endDate);
                await LoadSummaryMemberDataAsync(connectionString, stationId, startDate, endDate);
                await LoadChartDataAsync(connectionString, stationId, startDate, endDate);
                await LoadSegmentPivotTableAsync(connectionString, stationId);
                await LoadTierPivotTableAsync(connectionString, stationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching In-Store Troop dashboard data.");
                ErrorMessage = "เกิดข้อผิดพลาดในการดึงข้อมูลสำหรับหน้า In-Store Troop";
            }
        }
        private async Task LoadSummaryAutualRightDataAsync(string connectionString, int stationId, string startDate, string endDate)
        {
            string sql = @"SELECT SUM(CASE WHEN RewardTypeID IN (5,6,7,8) THEN 1 ELSE 0 END) AS TotalRights 
                            FROM [dbo].[RewardHistory] 
                            WHERE [StationId]=@StationId 
                            AND  CAST([UsedAt] AS DATE) BETWEEN @startDate AND @endDate; ";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@stationId", stationId);
            command.Parameters.AddWithValue("@startDate", startDate);
            command.Parameters.AddWithValue("@endDate", endDate);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                ActualRights = reader["TotalRights"] as int? ?? 0;

            }
        }
        private async Task LoadSummaryMemberDataAsync(string connectionString, int stationId, string startDate, string endDate)
        {
            string sql = @"SELECT COUNT(DISTINCT MemberID) AS TotalMembers FROM [dbo].[MemberRewards] 
                            WHERE [StationId]=@StationId 
                            AND  CAST([CreatedAt] AS DATE) BETWEEN @startDate AND @endDate;";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@stationId", stationId);
            command.Parameters.AddWithValue("@startDate", startDate);
            command.Parameters.AddWithValue("@endDate", endDate);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                ActualMembers = reader["TotalMembers"] as int? ?? 0;
            }
        }

        private async Task LoadChartDataAsync(string connectionString, int stationId, string startDate, string endDate)
        {
            // --- Query สำหรับ Pie Chart (แยกตาม Tier) ---
            var pieChartData = new List<object>();
            string pieSql = @"
                SELECT Tier, COUNT(DISTINCT MemberID) AS MemberCount
                FROM [dbo].[MemberRewards] WHERE StationId = @stationId
                AND  CAST([CreatedAt] AS DATE) BETWEEN @startDate AND @endDate
                GROUP BY Tier ORDER BY Tier;";

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand(pieSql, connection);
                command.Parameters.AddWithValue("@stationId", stationId);
                command.Parameters.AddWithValue("@startDate", startDate);
                command.Parameters.AddWithValue("@endDate", endDate);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    pieChartData.Add(new { Tier = reader["Tier"].ToString(), MemberCount = (int)reader["MemberCount"] });
                }
            }
            PieChartJson = JsonSerializer.Serialize(pieChartData);

            // --- Query สำหรับ Stacked Bar Chart (แยกตาม Segment จาก RewardTypeID) ---
            var dailyCounts = new Dictionary<string, (int Lancaster, int LOreal, int CultiMilano, int Pretz)>();
            string barSql = @"
                SELECT CAST(UsedAt AS DATE) as ActivityDate,
                    SUM(CASE WHEN RewardTypeID = 5 THEN 1 ELSE 0 END) AS Lancaster,
                    SUM(CASE WHEN RewardTypeID = 6 THEN 1 ELSE 0 END) AS LOreal,
                    SUM(CASE WHEN RewardTypeID = 7 THEN 1 ELSE 0 END) AS CultiMilano,
                    SUM(CASE WHEN RewardTypeID = 8 THEN 1 ELSE 0 END) AS Pretz
                FROM [dbo].[RewardHistory] 
                WHERE StationId = @stationId
                AND CAST([UsedAt] AS DATE) BETWEEN @startDate AND @endDate
                GROUP BY CAST(UsedAt AS DATE) ORDER BY ActivityDate;";

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand(barSql, connection);
                command.Parameters.AddWithValue("@stationId", stationId);
                command.Parameters.AddWithValue("@startDate", startDate);
                command.Parameters.AddWithValue("@endDate", endDate);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string date = ((DateTime)reader["ActivityDate"]).ToString("dd-MMM", System.Globalization.CultureInfo.InvariantCulture);
                    dailyCounts[date] = ((int)reader["Lancaster"], (int)reader["LOreal"], (int)reader["CultiMilano"], (int)reader["Pretz"]);
                }
            }

            var labels = new List<string> { "24-Oct", "25-Oct", "26-Oct" };
            var barData = new
            {
                labels = labels,
                datasets = new object[]
                {
                    new { label = "Lancaster", data = labels.Select(l => dailyCounts.ContainsKey(l) ? dailyCounts[l].Lancaster : 0).ToList(), backgroundColor = "rgba(234, 179, 8, 0.85)" },
                    new { label = "L'Oreal", data = labels.Select(l => dailyCounts.ContainsKey(l) ? dailyCounts[l].LOreal : 0).ToList(), backgroundColor = "rgba(59, 130, 246, 0.85)" },
                    new { label = "Culti Milano", data = labels.Select(l => dailyCounts.ContainsKey(l) ? dailyCounts[l].CultiMilano : 0).ToList(), backgroundColor = "rgba(34, 197, 94, 0.85)" },
                    new { label = "Pretz", data = labels.Select(l => dailyCounts.ContainsKey(l) ? dailyCounts[l].Pretz : 0).ToList(), backgroundColor = "rgba(12, 68, 245, 0.85)" }
                }
            };
            StackedBarChartJson = JsonSerializer.Serialize(barData);
        }

        private async Task LoadSegmentPivotTableAsync(string connectionString, int stationId)
        {
            string sql = @"-- ประกาศตัวแปร
                        DECLARE @cols AS NVARCHAR(MAX),
                                @query AS NVARCHAR(MAX),
                                @totalCol AS NVARCHAR(MAX),
                                @colsForSelect AS NVARCHAR(MAX),
                                @startDate DATE = '2025-10-24',
                                @endDate DATE = '2025-10-26';


                        -- สร้างรายการคอลัมน์วันที่แบบไดนามิก
                        SELECT @cols = STUFF((
                            SELECT DISTINCT ',' + QUOTENAME(CAST(UsedAt AS DATE)) 
                            FROM RewardHistory 
                            WHERE StationId = @StationId 
                            AND CAST(UsedAt AS DATE) BETWEEN @startDate AND @endDate
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                        -- ถ้าไม่มีข้อมูล ให้จบการทำงาน
                        IF @cols IS NULL RETURN;

                        -- สร้างส่วนที่ใช้คำนวณ Total (แนวนอน)
                        SELECT @totalCol = STUFF((
                            SELECT DISTINCT ' + ISNULL(' + QUOTENAME(CAST(UsedAt AS DATE)) + ', 0)' 
                            FROM RewardHistory 
                            WHERE StationId = @StationId
                            AND CAST(UsedAt AS DATE) BETWEEN @startDate AND @endDate
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 2, ''); -- เริ่มที่ 1, 2 เพื่อตัด ' + ' ตัวแรก

                        -- สร้างส่วนที่ใช้คำนวณ Total (แนวตั้ง)
                        SELECT @colsForSelect = STUFF((
                            SELECT DISTINCT ', SUM(ISNULL(' + QUOTENAME(CAST(UsedAt AS DATE)) + ', 0)) AS ' + QUOTENAME(FORMAT(UsedAt, 'dd MMM')) 
                            FROM RewardHistory 
                            WHERE StationId = @StationId 
                            AND CAST(UsedAt AS DATE) BETWEEN @startDate AND @endDate
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                        -- สร้าง Query หลัก
                        SET @query = N'
                        WITH SourceData AS (
                            SELECT 
                                CAST(UsedAt AS DATE) AS ActivityDate,
                                CASE 
                                    WHEN RewardTypeID = 5 THEN N''Lancaster''
                                    WHEN RewardTypeID = 6 THEN N''LOreal''
                                    WHEN RewardTypeID = 7 THEN N''Culti Milano''
                                    WHEN RewardTypeID = 8 THEN N''Pretz''
                                    ELSE N''อื่นๆ'' 
                                END AS Segment,
                                1 AS ValueToAggregate

                            FROM RewardHistory 
                            WHERE StationId = @p_StationId
                           
                        ),
                        PivotedData AS (
                            SELECT Segment, ' + @cols + '
                            FROM SourceData
                            -- เปลี่ยนจาก COUNT(ActivityDate) เป็น SUM(ValueToAggregate)
                            PIVOT (
                                SUM(ValueToAggregate) 
                                FOR ActivityDate IN (' + @cols + ')
                            ) AS pvt
                        )
                        SELECT 
                            ISNULL(Segment, N''TOTALBYDAY'') AS Segment, 
                            ' + @colsForSelect + ', 
                            SUM(' + @totalCol + ') AS [TOTAL BY SEGMENT]
                        FROM PivotedData
                        GROUP BY ROLLUP(Segment)
                        ORDER BY CASE WHEN Segment IS NULL THEN 1 ELSE 0 END, Segment DESC';

                        -- สั่งรัน Query แบบ Parameterized
                        -- (แก้ @StationId เป็นตัวแปรของคุณ)
                        EXEC sp_executesql @query, 
                                           N'@p_StationId INT', 
                                           @p_StationId = @StationId;";

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@stationId", stationId);
            await using var reader = await command.ExecuteReaderAsync();
            SegmentTableHeaders = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            while (await reader.ReadAsync())
            {
                var row = new ExpandoObject() as IDictionary<string, object>;
                foreach (var header in SegmentTableHeaders) { row[header] = reader[header]; }
                SegmentTableData.Add(row);
            }
        }

        private async Task LoadTierPivotTableAsync(string connectionString, int stationId)
        {
                                string sql = @"DECLARE @cols AS NVARCHAR(MAX), 
                                        @query AS NVARCHAR(MAX), 
                                        @totalCol AS NVARCHAR(MAX), 
                                        @colsForSelect AS NVARCHAR(MAX),
                                        @colsAlias AS NVARCHAR(MAX),
                                        @startDate DATE = '2025-10-24',
                                        @endDate DATE = '2025-10-26';

                                -- สร้างคอลัมน์สำหรับ PIVOT (รูปแบบ date)
                                SELECT @cols = STUFF((
                                    SELECT ',' + QUOTENAME(DateCol)
                                    FROM (
                                        SELECT DISTINCT CONVERT(NVARCHAR(10), CreatedAt, 120) AS DateCol
                                        FROM MemberRewards 
                                        WHERE StationId = @StationId 
                                          AND CAST(CreatedAt AS DATE) BETWEEN @startDate AND @endDate
                                    ) AS Dates
                                    ORDER BY DateCol
                                    FOR XML PATH(''), TYPE
                                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                IF @cols IS NULL OR @cols = '' RETURN;

                                -- สร้างสูตรสำหรับ TOTAL BY TIER
                                SELECT @totalCol = STUFF((
                                    SELECT ' + ISNULL(' + QUOTENAME(DateCol) + ', 0)'
                                    FROM (
                                        SELECT DISTINCT CONVERT(NVARCHAR(10), CreatedAt, 120) AS DateCol
                                        FROM MemberRewards 
                                        WHERE StationId = @StationId 
                                          AND CAST(CreatedAt AS DATE) BETWEEN @startDate AND @endDate
                                    ) AS Dates
                                    ORDER BY DateCol
                                    FOR XML PATH(''), TYPE
                                ).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

                                -- สร้างคอลัมน์ที่มี AS alias สำหรับใช้ใน GroupedData
                                SELECT @colsForSelect = STUFF((
                                    SELECT ', SUM(ISNULL(' + QUOTENAME(DateCol) + ', 0)) AS ' + QUOTENAME(DateLabel)
                                    FROM (
                                        SELECT DISTINCT 
                                            CONVERT(NVARCHAR(10), CreatedAt, 120) AS DateCol,
                                            FORMAT(CreatedAt, 'dd MMM') AS DateLabel
                                        FROM MemberRewards 
                                        WHERE StationId = @StationId 
                                          AND CAST(CreatedAt AS DATE) BETWEEN @startDate AND @endDate
                                    ) AS Dates
                                    ORDER BY DateCol
                                    FOR XML PATH(''), TYPE
                                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                -- สร้างรายชื่อคอลัมน์ alias สำหรับ SELECT สุดท้าย
                                SELECT @colsAlias = STUFF((
                                    SELECT ',' + QUOTENAME(DateLabel)
                                    FROM (
                                        SELECT DISTINCT 
                                            CONVERT(NVARCHAR(10), CreatedAt, 120) AS DateCol,
                                            FORMAT(CreatedAt, 'dd MMM') AS DateLabel
                                        FROM MemberRewards 
                                        WHERE StationId = @StationId 
                                          AND CAST(CreatedAt AS DATE) BETWEEN @startDate AND @endDate
                                    ) AS Dates
                                    ORDER BY DateCol
                                    FOR XML PATH(''), TYPE
                                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                IF @colsForSelect IS NULL SET @colsForSelect = '';
                                IF @colsAlias IS NULL SET @colsAlias = '';

                                SET @query = N'
                                WITH PivotedData AS (
                                    SELECT Tier, ' + @cols + '
                                    FROM (
                                       SELECT [dbo].[MemberRewards].Tier, CAST(UsedAt AS DATE) AS ActivityDate 
                                       FROM [dbo].[RewardHistory] 
                                       INNER JOIN [dbo].[MemberRewards] 
                                       ON [dbo].[RewardHistory].[MemberId] = [dbo].[MemberRewards].[MemberID] 
                                       WHERE [dbo].[RewardHistory].StationId = ' + CAST(@stationId AS VARCHAR) + '
                                         AND CAST(UsedAt AS DATE) BETWEEN ''' + CONVERT(VARCHAR(10), @startDate, 120) + ''' AND ''' + CONVERT(VARCHAR(10), @endDate, 120) + '''
                                       GROUP BY [dbo].[MemberRewards].Tier, [dbo].[RewardHistory].[MemberId], CAST(UsedAt AS DATE)
                                    ) AS SourceTable
                                    PIVOT (COUNT(ActivityDate) FOR ActivityDate IN (' + @cols + ')) AS pvt
                                ),
                                GroupedData AS (
                                    SELECT 
                                        ISNULL(Tier, N''TOTAL MEMBER BY DAY'') AS Tier, 
                                        ' + @colsForSelect + ', 
                                        SUM(' + @totalCol + ') AS [TOTAL BY TIER],
                                        CASE 
                                            WHEN Tier IS NULL THEN 1000
                                            WHEN Tier = ''CRYSTAL'' THEN 1
                                            WHEN Tier = ''NAVY'' THEN 2
                                            WHEN Tier = ''SCARLET'' THEN 3
                                            WHEN Tier = ''CROWN'' THEN 4
                                            WHEN Tier = ''VEGA'' THEN 5
                                            ELSE 999
                                        END AS TierOrder
                                    FROM PivotedData
                                    GROUP BY ROLLUP(Tier)
                                )
                                SELECT 
                                    Tier' + 
                                    CASE WHEN @colsAlias <> '' THEN ',' + @colsAlias ELSE '' END + 
                                    ',[TOTAL BY TIER]
                                FROM GroupedData
                                ORDER BY TierOrder;';

                                EXEC sp_executesql @query;";

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@stationId", stationId);
            await using var reader = await command.ExecuteReaderAsync();
            TierTableHeaders = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            while (await reader.ReadAsync())
            {
                var row = new ExpandoObject() as IDictionary<string, object>;
                foreach (var header in TierTableHeaders) { row[header] = reader[header]; }
                TierTableData.Add(row);
            }
        }
    }
}
