using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace EventCheckInDashboard.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<IndexModel> _logger;

        public string? DonutChartDataJson { get; private set; }
        public string? BarChartDataJson { get; private set; }
        public List<dynamic> TableData { get; private set; } = new List<dynamic>();
        public List<string> TableHeaders { get; private set; } = new List<string>();
        public int TotalActualRights { get; private set; }
        public int TotalActualMembers { get; private set; }
        public string? ErrorMessage { get; private set; }
        public string? PieChartDataStoreJson { get; private set; }

        public IndexModel(IConfiguration configuration, ILogger<IndexModel> logger)
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

                string startDate = "2025-10-24";
                string endDate = "2025-10-26";

                // ดึงข้อมูลทั้ง 4 ส่วนพร้อมกัน
                await LoadSummaryAutualRightDataAsync(connectionString, startDate, endDate);
                await LoadAllPieChartDataAsync(connectionString, startDate, endDate);
                await LoadBarChartDataAsync(connectionString, startDate, endDate);
                await LoadPivotTableDataAsync(connectionString, startDate, endDate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching dashboard data.");
                ErrorMessage = "เกิดข้อผิดพลาดในการเชื่อมต่อหรือดึงข้อมูลจากฐานข้อมูล";
            }
        }

        // --- เมธอดสำหรับดึงข้อมูลสรุป (Info Box) ---
        private async Task LoadSummaryAutualRightDataAsync(string connectionString,string startDate,string endDate)
        {
            string sql = @"SELECT CAST(SUM(CASE WHEN RewardTypeID = 14 THEN FLOOR(Carat/800.0) ELSE 0 END) AS INT) + CAST(SUM(CASE WHEN RewardTypeID = 15 THEN FLOOR(Carat/400.0) ELSE 0 END) AS INT) + SUM(CASE WHEN RewardTypeID IN (5,6,7,8,9,10,11,12,13,16,17) THEN 1 ELSE 0 END) AS TotalRights 
                            FROM [dbo].[RewardHistory] 
                            WHERE CAST([UsedAt] AS DATE) BETWEEN @startDate AND @endDate ;";

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@startDate", startDate);
            command.Parameters.AddWithValue("@endDate", endDate);
            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                TotalActualRights = (int)reader["TotalRights"];
            }
        }


        private async Task LoadAllPieChartDataAsync(string connectionString, string startDate, string endDate)
        {
            var dataStore = new Dictionary<string, List<object>>();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // --- 1. ดึงข้อมูลภาพรวม (Summary) ---
            var summaryData = new List<object>();
            string summarySql = @"
                    SELECT Tier, COUNT(DISTINCT MemberID) AS TotalMembers
                    FROM [dbo].[MemberRewards]
                    WHERE CAST([CreatedAt] AS DATE) BETWEEN @startDate AND @endDate 
                    GROUP BY Tier
                    ORDER BY TotalMembers DESC;";

            await using var summaryCommand = new SqlCommand(summarySql, connection);
            summaryCommand.Parameters.AddWithValue("@startDate", startDate);
            summaryCommand.Parameters.AddWithValue("@endDate", endDate);
            await using (var reader = await summaryCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    summaryData.Add(new
                    {
                        Tier = reader["Tier"].ToString(),
                        TotalMembers = (int)reader["TotalMembers"]
                    });
                }
            }
            // เพิ่มข้อมูลภาพรวมลงใน Store
            dataStore["Summary"] = summaryData;

            // --- 2. ดึงข้อมูลรายวัน (ใช้ตรรกะเดียวกับ Pivot Table) ---
            string dailySql = @"
                WITH FirstReward AS (
                    SELECT 
                        MemberID, 
                        Tier, 
                        CAST(CreatedAt AS DATE) AS RegDate,
                        ROW_NUMBER() OVER(PARTITION BY MemberID ORDER BY CreatedAt ASC) as rn
                    FROM [dbo].[MemberRewards]
                    WHERE CAST([CreatedAt] AS DATE) BETWEEN @startDate AND @endDate 
                )
                SELECT 
                    RegDate, 
                    Tier, 
                    COUNT(MemberID) AS TotalMembers
                FROM FirstReward
                WHERE rn = 1
                GROUP BY RegDate, Tier
                ORDER BY RegDate, Tier;";

            var dailyResults = new Dictionary<string, List<object>>();

            await using var dailyCommand = new SqlCommand(dailySql, connection);
            dailyCommand.Parameters.AddWithValue("@startDate", startDate);
            dailyCommand.Parameters.AddWithValue("@endDate", endDate);
            await using (var reader = await dailyCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (reader["RegDate"] != DBNull.Value)
                    {
                        // จัดรูปแบบวันที่ให้เป็น "dd-MMM" (เช่น "23-Oct")
                        string dateKey = ((DateTime)reader["RegDate"]).ToString("dd-MMM", System.Globalization.CultureInfo.InvariantCulture);

                        if (!dailyResults.ContainsKey(dateKey))
                        {
                            dailyResults[dateKey] = new List<object>();
                        }

                        dailyResults[dateKey].Add(new
                        {
                            Tier = reader["Tier"].ToString(),
                            TotalMembers = (int)reader["TotalMembers"]
                        });
                    }
                }
            }

            // เพิ่มข้อมูลรายวันที่จัดกลุ่มแล้ว ลงใน Store หลัก
            foreach (var entry in dailyResults)
            {
                dataStore[entry.Key] = entry.Value;
            }

            // --- 3. แปลง dataStore ทั้งหมดเป็น JSON ---
            PieChartDataStoreJson = JsonSerializer.Serialize(dataStore);
        }

        // --- เมธอดสำหรับดึงข้อมูล Bar Chart ---
        private async Task LoadBarChartDataAsync(string connectionString, string startDate, string endDate)
        {
            var dailyCounts = new Dictionary<string, int>();
            string sql = @"
                SELECT  CAST(UsedAt AS DATE) AS ActivityDate , 
                        COUNT(DISTINCT MemberID) AS TotalMembers, 
                        CAST(SUM(CASE WHEN RewardTypeID = 14 THEN FLOOR(Carat/800.0) ELSE 0 END) AS INT) + CAST(SUM(CASE WHEN RewardTypeID = 15 THEN FLOOR(Carat/400.0) ELSE 0 END) AS INT) + SUM(CASE WHEN RewardTypeID IN (5,6,7,8,9,10,11,12,13,16,17) THEN 1 ELSE 0 END) AS RightsCount
                        FROM [dbo].[RewardHistory]
                        WHERE CAST(UsedAt AS DATE) BETWEEN @startDate AND @endDate 
                        GROUP BY CAST(UsedAt AS DATE)
                        ORDER BY ActivityDate;
                ";

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@startDate", startDate);
            command.Parameters.AddWithValue("@endDate", endDate);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (reader["ActivityDate"] != DBNull.Value)
                {
                    string date = ((DateTime)reader["ActivityDate"]).ToString("dd-MMM", System.Globalization.CultureInfo.InvariantCulture);
                    dailyCounts[date] = (int)reader["RightsCount"];
                }
            }

            // สร้างข้อมูลสำหรับ Chart.js
            var labels = new List<string> { "24-Oct", "25-Oct", "26-Oct" };
            var actualData = new List<int>();
            foreach (var label in labels)
            {
                actualData.Add(dailyCounts.ContainsKey(label) ? dailyCounts[label] : 0);
            }

            var chartData = new
            {
                labels = labels,
                datasets = new[] {
                    new {
                        label = "TARGET",
                        data = new List<int> { 730, 730, 730 },
                        backgroundColor = "rgba(102, 126, 234, 0.8)",
                        borderRadius = 8
                    },
                    new {
                        label = "ACTUAL",
                        data = actualData,
                        //data = new List<int> { 400, 0, 0 },
                        backgroundColor = "rgba(255, 140, 66, 0.8)",
                        borderRadius = 8
                    }
                }
            };

            BarChartDataJson = JsonSerializer.Serialize(chartData);
        }


        private async Task LoadPivotTableDataAsync(string connectionString, string startDate, string endDate)
        {
            string dynamicPivotSql = @"DECLARE @cols AS NVARCHAR(MAX),
                                        @query AS NVARCHAR(MAX),
                                        @totalByTierCol AS NVARCHAR(MAX), 
                                        @colsForSelect AS NVARCHAR(MAX),
                                        @colsForSum AS NVARCHAR(MAX),
                                        @colsAlias AS NVARCHAR(MAX),
                                        @startDate DATE = '2025-10-24',
                                        @endDate DATE = '2025-10-26';

                                SELECT @cols = STUFF((
                                    SELECT DISTINCT ',' + QUOTENAME(CONVERT(NVARCHAR(10), [CreatedAt], 120)) 
                                    FROM [dbo].[MemberRewards] 
                                    WHERE CAST(CreatedAt AS DATE) BETWEEN @startDate AND @endDate
                                    ORDER BY 1 
                                    FOR XML PATH(''), TYPE
                                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                IF @cols IS NULL SET @cols = '';

                                SELECT @totalByTierCol = STUFF((
                                    SELECT DISTINCT ' + ISNULL(' + QUOTENAME(CONVERT(NVARCHAR(10), [CreatedAt], 120)) + ', 0)' 
                                    FROM [dbo].[MemberRewards] 
                                    WHERE CAST(CreatedAt AS DATE) BETWEEN @startDate AND @endDate
                                    ORDER BY 1 
                                    FOR XML PATH(''), TYPE
                                ).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

                                IF @totalByTierCol IS NULL SET @totalByTierCol = '0';

                                SELECT @colsForSelect = STUFF((
                                    SELECT DISTINCT ',' + QUOTENAME(CONVERT(NVARCHAR(10), [CreatedAt], 120)) + ' AS ' + QUOTENAME(FORMAT([CreatedAt], 'dd MMM')) 
                                    FROM [dbo].[MemberRewards] 
                                    WHERE CAST(CreatedAt AS DATE) BETWEEN @startDate AND @endDate
                                    ORDER BY 1 
                                    FOR XML PATH(''), TYPE
                                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                -- สร้างตัวแปรสำหรับชื่อคอลัมน์ที่เป็น alias แล้ว
                                SELECT @colsAlias = STUFF((
                                    SELECT DISTINCT ',' + QUOTENAME(FORMAT([CreatedAt], 'dd MMM')) 
                                    FROM [dbo].[MemberRewards] 
                                    WHERE CAST(CreatedAt AS DATE) BETWEEN @startDate AND @endDate
                                    ORDER BY 1 
                                    FOR XML PATH(''), TYPE
                                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                SELECT @colsForSum = STUFF((
                                    SELECT DISTINCT ', SUM(ISNULL(' + QUOTENAME(CONVERT(NVARCHAR(10), [CreatedAt], 120)) + ', 0)) AS ' + QUOTENAME(FORMAT([CreatedAt], 'dd MMM')) 
                                    FROM [dbo].[MemberRewards] 
                                    WHERE CAST(CreatedAt AS DATE) BETWEEN @startDate AND @endDate
                                    ORDER BY 1 
                                    FOR XML PATH(''), TYPE
                                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                IF @cols IS NULL OR @colsForSelect IS NULL OR @cols = ''
                                BEGIN
                                    SELECT 'NO DATA' AS Tier, 0 AS [TOTAL BY TIER];
                                    RETURN;
                                END

                                IF @colsForSelect IS NULL SET @colsForSelect = '';
                                IF @colsForSum IS NULL SET @colsForSum = '';
                                IF @colsAlias IS NULL SET @colsAlias = '';

                                SET @query = '
                                WITH PivotedData AS (
                                    SELECT Tier, ' + @cols + '
                                    FROM (
                                        SELECT Tier, RegDate
                                        FROM (
                                            SELECT 
                                                MemberID, 
                                                Tier, 
                                                CAST(CreatedAt AS DATE) AS RegDate,
                                                ROW_NUMBER() OVER(PARTITION BY MemberID ORDER BY CreatedAt ASC) as rn
                                            FROM [dbo].[MemberRewards]
                                            WHERE CAST(CreatedAt AS DATE) BETWEEN ''' + CONVERT(VARCHAR(10), @startDate, 120) + ''' AND ''' + CONVERT(VARCHAR(10), @endDate, 120) + '''
                                        ) AS FirstReward
                                        WHERE rn = 1
                                    ) AS SourceTable
                                    PIVOT (
                                        COUNT(RegDate) 
                                        FOR RegDate IN (' + @cols + ')
                                    ) AS PivotTable
                                ),
                                OrderedData AS (
                                    SELECT 
                                        Tier,
                                        ' + @colsForSelect + ',
                                        ' + @totalByTierCol + ' AS [TOTAL BY TIER],
                                        CASE Tier
                                            WHEN ''CRYSTAL'' THEN 1
                                            WHEN ''NAVY'' THEN 2
                                            WHEN ''SCARLET'' THEN 3
                                            WHEN ''CROWN'' THEN 4
                                            WHEN ''VEGA'' THEN 5
                                            ELSE 999
                                        END AS TierOrder
                                    FROM PivotedData
                                    UNION ALL
                                    SELECT 
                                        ''TOTAL MEMBER BY DAY'' AS Tier,
                                        ' + @colsForSum + ',
                                        SUM(' + @totalByTierCol + ') AS [TOTAL BY TIER],
                                        1000 AS TierOrder
                                    FROM PivotedData
                                )
                                SELECT 
                                    Tier,
                                    ' + @colsAlias + ',
                                    [TOTAL BY TIER]
                                FROM OrderedData
                                ORDER BY TierOrder;';

                                EXEC sp_executesql @query;
                                    ";


            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(dynamicPivotSql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            TableHeaders = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();

            while (await reader.ReadAsync())
            {
                var row = new ExpandoObject() as IDictionary<string, object>;
                foreach (var header in TableHeaders)
                {
                    row[header] = reader[header];
                }
                TableData.Add(row);
            }
        }
    }
}
