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

                // ดึงข้อมูลทั้ง 4 ส่วนพร้อมกัน
                await LoadSummaryDataAsync(connectionString);
                await LoadDonutChartDataAsync(connectionString);
                await LoadBarChartDataAsync(connectionString);
                await LoadPivotTableDataAsync(connectionString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching dashboard data.");
                ErrorMessage = "เกิดข้อผิดพลาดในการเชื่อมต่อหรือดึงข้อมูลจากฐานข้อมูล";
            }
        }

        // --- เมธอดสำหรับดึงข้อมูลสรุป (Info Box) ---
        private async Task LoadSummaryDataAsync(string connectionString)
        {
            string sql = "SELECT  CAST(UsedAt AS DATE) AS ActivityDate , COUNT(DISTINCT MemberID) AS TotalMembers, CAST(SUM(CASE WHEN RewardTypeID = 1 THEN FLOOR(Carat/360.0) ELSE 0 END) AS INT)  + SUM(CASE WHEN RewardTypeID = 2 THEN 1 ELSE 0 END) + SUM(CASE WHEN RewardTypeID = 3 THEN 1 ELSE 0 END) + SUM(CASE WHEN RewardTypeID = 4 THEN 1 ELSE 0 END) AS TotalRights\r\nFROM [dbo].[RewardHistory]\r\nGROUP BY CAST(UsedAt AS DATE)\r\nORDER BY ActivityDate;\r\n";

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                TotalActualRights = (int)reader["TotalRights"];
                TotalActualMembers = (int)reader["TotalMembers"];
            }
        }


        private async Task LoadDonutChartDataAsync(string connectionString)
        {
            var data = new List<object>();
            string sql = @"
                SELECT Tier, COUNT(DISTINCT MemberID) AS TotalMembers
                FROM [dbo].[MemberRewards]
                GROUP BY Tier
                ORDER BY TotalMembers DESC;";

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                data.Add(new
                {
                    Tier = reader["Tier"].ToString(),
                    TotalMembers = (int)reader["TotalMembers"]
                });
            }
            DonutChartDataJson = JsonSerializer.Serialize(data);
        }

        // --- เมธอดสำหรับดึงข้อมูล Bar Chart ---
        private async Task LoadBarChartDataAsync(string connectionString)
        {
            var dailyCounts = new Dictionary<string, int>();
            string sql = @"
                SELECT  CAST(UsedAt AS DATE) AS ActivityDate , 
                        COUNT(DISTINCT MemberID) AS TotalMembers, 
                        CAST(SUM(CASE WHEN RewardTypeID = 1 THEN FLOOR(Carat/360.0) ELSE 0 END) AS INT)  + SUM(CASE WHEN RewardTypeID = 2 THEN 1 ELSE 0 END) + SUM(CASE WHEN RewardTypeID = 3 THEN 1 ELSE 0 END) + SUM(CASE WHEN RewardTypeID = 4 THEN 1 ELSE 0 END) AS RightsCount
                FROM [dbo].[RewardHistory]
                GROUP BY CAST(UsedAt AS DATE)
                ORDER BY ActivityDate;
                ";

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
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
            var labels = new List<string> { "17-Oct", "18-Oct", "19-Oct", "20-Oct", "21-Oct", "22-Oct", "23-Oct" };
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
                        data = new List<int> { 800, 800, 800, 800, 800, 800, 800 },
                        backgroundColor = "rgba(102, 126, 234, 0.8)",
                        borderRadius = 8
                    },
                    new {
                        label = "ACTUAL",
                        data = actualData,
                        //data = new List<int> { 400, 0, 0, 0, 0, 0, 0 },
                        backgroundColor = "rgba(255, 140, 66, 0.8)",
                        borderRadius = 8
                    }
                }
            };

            BarChartDataJson = JsonSerializer.Serialize(chartData);
        }


        private async Task LoadPivotTableDataAsync(string connectionString)
        {
            string dynamicPivotSql = @"DECLARE @cols AS NVARCHAR(MAX), 
                                    @query AS NVARCHAR(MAX), 
                                    @totalByTierCol AS NVARCHAR(MAX), 
                                    @colsForSelect AS NVARCHAR(MAX);

                                    -- สร้าง columns สำหรับ PIVOT
                                    SELECT @cols = STUFF((
                                        SELECT DISTINCT ',' + QUOTENAME(CONVERT(NVARCHAR(10), UsedAt, 120)) 
                                        FROM [dbo].[RewardHistory] 
                                        ORDER BY 1 
                                        FOR XML PATH(''), TYPE
                                    ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                    IF @cols IS NULL BEGIN SET @cols = '' END;

                                    -- สร้าง formula สำหรับ TOTAL BY TIER
                                    SELECT @totalByTierCol = STUFF((
                                        SELECT DISTINCT ' + ISNULL(' + QUOTENAME(CONVERT(NVARCHAR(10), UsedAt, 120)) + ', 0)' 
                                        FROM [dbo].[RewardHistory] 
                                        ORDER BY 1 
                                        FOR XML PATH(''), TYPE
                                    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

                                    IF @totalByTierCol IS NULL BEGIN SET @totalByTierCol = '0' END;

                                    -- สร้าง columns สำหรับ SELECT (ไม่ใช้ SUM ในส่วนแรก)
                                    SELECT @colsForSelect = STUFF((
                                        SELECT DISTINCT ',' + QUOTENAME(CONVERT(NVARCHAR(10), UsedAt, 120)) + ' AS ' + QUOTENAME(FORMAT(UsedAt, 'dd MMM')) 
                                        FROM [dbo].[RewardHistory] 
                                        ORDER BY 1 
                                        FOR XML PATH(''), TYPE
                                    ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                    -- สร้าง columns สำหรับ SUM (ใช้ในส่วน TOTAL)
                                    DECLARE @colsForSum AS NVARCHAR(MAX);
                                    SELECT @colsForSum = STUFF((
                                        SELECT DISTINCT ', SUM(' + QUOTENAME(CONVERT(NVARCHAR(10), UsedAt, 120)) + ') AS ' + QUOTENAME(FORMAT(UsedAt, 'dd MMM')) 
                                        FROM [dbo].[RewardHistory] 
                                        ORDER BY 1 
                                        FOR XML PATH(''), TYPE
                                    ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                                    IF @colsForSelect IS NOT NULL
                                    BEGIN
                                        SET @query = '
                                        WITH PivotedData AS (
                                            SELECT Tier, ' + @cols + '
                                            FROM (
                                                SELECT [dbo].[MemberRewards].Tier, CAST(UsedAt AS DATE) AS RegDate 
                                                FROM [dbo].[RewardHistory] 
                                                INNER JOIN [dbo].[MemberRewards] 
                                                ON [dbo].[RewardHistory].[MemberId] = [dbo].[MemberRewards].[MemberID] 
                                                GROUP BY [dbo].[MemberRewards].Tier, [dbo].[RewardHistory].[MemberId], CAST(UsedAt AS DATE)
                                            ) AS SourceTable
                                            PIVOT (COUNT(RegDate) FOR RegDate IN (' + @cols + ')) AS PivotTable
                                        )
                                        SELECT 
                                            Tier,
                                            ' + @colsForSelect + ',
                                            ' + @totalByTierCol + ' AS [TOTAL BY TIER]
                                        FROM PivotedData
    
                                        UNION ALL
    
                                        SELECT 
                                            ''TOTAL MEMBER BY DAY'' AS Tier,
                                            ' + @colsForSum + ',
                                            SUM(' + @totalByTierCol + ') AS [TOTAL BY TIER]
                                        FROM PivotedData;';
    
                                        EXEC sp_executesql @query;
                                    END
                                    ELSE
                                    BEGIN
                                        SELECT 'NO DATA' AS Tier, 0 AS [TOTAL BY TIER];
                                    END

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
