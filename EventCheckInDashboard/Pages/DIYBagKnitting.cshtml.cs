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
    public class DIYBagKnittingModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DIYBagKnittingModel> _logger;

        // --- Properties ����Ѻ�觢�������ʴ��� ---
        public int ActualRights { get; private set; }
        public int ActualMembers { get; private set; }
        public string? StackedBarChartJson { get; private set; }
        public string? PieChartJson { get; private set; }
        public List<dynamic> SegmentTableData { get; private set; } = new();
        public List<string> SegmentTableHeaders { get; private set; } = new();
        public List<dynamic> TierTableData { get; private set; } = new();
        public List<string> TierTableHeaders { get; private set; } = new();
        public string? ErrorMessage { get; private set; }
        public DIYBagKnittingModel(IConfiguration configuration, ILogger<DIYBagKnittingModel> logger)
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
                int stationId = 8;

                await LoadSummaryAutualRightDataAsync(connectionString, stationId);
                await LoadSummaryMemberDataAsync(connectionString, stationId);
                await LoadChartDataAsync(connectionString, stationId);
                await LoadSegmentPivotTableAsync(connectionString, stationId);
                await LoadTierPivotTableAsync(connectionString, stationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching DIYBagKnitting dashboard data.");
                ErrorMessage = "�Դ��ͼԴ��Ҵ㹡�ô֧����������Ѻ˹�� DIYBagKnitting";
            }
        }

        private async Task LoadSummaryAutualRightDataAsync(string connectionString, int stationId)
        {
            string sql = "SELECT SUM(CASE WHEN RewardTypeID = 12 THEN 1 ELSE 0 END) AS TotalRights FROM [dbo].[RewardHistory] WHERE [StationId]=@StationId ;";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@stationId", stationId);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                ActualRights = reader["TotalRights"] as int? ?? 0;

            }
        }
        private async Task LoadSummaryMemberDataAsync(string connectionString, int stationId)
        {
            string sql = "SELECT COUNT(DISTINCT MemberID) AS TotalMembers FROM [dbo].[MemberRewards] WHERE [StationId]=@StationId ;";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@stationId", stationId);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                ActualMembers = reader["TotalMembers"] as int? ?? 0;
            }
        }

        private async Task LoadChartDataAsync(string connectionString, int stationId)
        {
            // --- Query ����Ѻ Pie Chart (�¡��� Tier) ---
            var pieChartData = new List<object>();
            string pieSql = @"
                SELECT Tier, COUNT(DISTINCT MemberID) AS MemberCount
                FROM [dbo].[MemberRewards] WHERE StationId = @stationId
                GROUP BY Tier ORDER BY Tier;";

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand(pieSql, connection);
                command.Parameters.AddWithValue("@stationId", stationId);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    pieChartData.Add(new { Tier = reader["Tier"].ToString(), MemberCount = (int)reader["MemberCount"] });
                }
            }
            PieChartJson = JsonSerializer.Serialize(pieChartData);

            // --- Query ����Ѻ Stacked Bar Chart (�¡��� Segment �ҡ RewardTypeID) ---
            var dailyCounts = new Dictionary<string, int>();
            string barSql = @"
                SELECT CAST(UsedAt AS DATE) as ActivityDate,
                    SUM(CASE WHEN RewardTypeID = 12 THEN 1 ELSE 0 END) AS bill20000
                FROM [dbo].[RewardHistory] 
                WHERE StationId = @stationId
                GROUP BY CAST(UsedAt AS DATE) ORDER BY ActivityDate;";

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand(barSql, connection);
                command.Parameters.AddWithValue("@stationId", stationId);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string date = ((DateTime)reader["ActivityDate"]).ToString("dd-MMM", System.Globalization.CultureInfo.InvariantCulture);
                    dailyCounts[date] = ((int)reader["bill20000"]);
                }
            }

            var labels = new List<string> { "24-Oct", "25-Oct", "26-Oct" };
            var barData = new
            {
                labels = labels,
                datasets = new object[]
                {
                    new { label = "����� 20,000", data = labels.Select(l => dailyCounts.ContainsKey(l) ? dailyCounts[l] : 0).ToList(), backgroundColor = "rgba(234, 179, 8, 0.85)" }
                }
            };
            StackedBarChartJson = JsonSerializer.Serialize(barData);
        }

        private async Task LoadSegmentPivotTableAsync(string connectionString, int stationId)
        {
            string sql = @"-- ��С�ȵ����
                        DECLARE @cols AS NVARCHAR(MAX),
                                @query AS NVARCHAR(MAX),
                                @totalCol AS NVARCHAR(MAX),
                                @colsForSelect AS NVARCHAR(MAX);

                        -- ���ҧ��¡�ä�������ѹ���Ẻ䴹��ԡ
                        SELECT @cols = STUFF((
                            SELECT DISTINCT ',' + QUOTENAME(CAST(UsedAt AS DATE)) 
                            FROM RewardHistory 
                            WHERE StationId = @StationId 
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                        -- �������բ����� ��騺��÷ӧҹ
                        IF @cols IS NULL RETURN;

                        -- ���ҧ��ǹ�����ӹǳ Total (�ǹ͹)
                        SELECT @totalCol = STUFF((
                            SELECT DISTINCT ' + ISNULL(' + QUOTENAME(CAST(UsedAt AS DATE)) + ', 0)' 
                            FROM RewardHistory 
                            WHERE StationId = @StationId 
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 2, ''); -- �������� 1, 2 ���͵Ѵ ' + ' ����á

                        -- ���ҧ��ǹ�����ӹǳ Total (�ǵ��)
                        SELECT @colsForSelect = STUFF((
                            SELECT DISTINCT ', SUM(ISNULL(' + QUOTENAME(CAST(UsedAt AS DATE)) + ', 0)) AS ' + QUOTENAME(FORMAT(UsedAt, 'dd MMM')) 
                            FROM RewardHistory 
                            WHERE StationId = @StationId 
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                        -- ���ҧ Query ��ѡ
                        SET @query = N'
                        WITH SourceData AS (
                            SELECT 
                                CAST(UsedAt AS DATE) AS ActivityDate,
                                CASE 
                                    WHEN RewardTypeID = 16 THEN N''����� 20,000''
                                    ELSE N''����'' 
                                END AS Segment,
                                1 AS ValueToAggregate

                            FROM RewardHistory 
                            WHERE StationId = @p_StationId -- �� Parameter ᷹��õ��ʵ�ԧ
                        ),
                        PivotedData AS (
                            SELECT Segment, ' + @cols + '
                            FROM SourceData
                            -- ����¹�ҡ COUNT(ActivityDate) �� SUM(ValueToAggregate)
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

                        -- ����ѹ Query Ẻ Parameterized
                        -- (�� @StationId �繵���âͧ�س)
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
            string sql = @"DECLARE @cols AS NVARCHAR(MAX), @query AS NVARCHAR(MAX), @totalCol AS NVARCHAR(MAX), @colsForSelect AS NVARCHAR(MAX);

                        SELECT @cols = STUFF((
                            SELECT DISTINCT ',' + QUOTENAME(CAST(CreatedAt AS DATE)) 
                            FROM MemberRewards 
                            WHERE StationId = @StationId 
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                        IF @cols IS NULL RETURN;

                        SELECT @totalCol = STUFF((
                            SELECT DISTINCT ' + ISNULL(' + QUOTENAME(CAST(CreatedAt AS DATE)) + ', 0)' 
                            FROM MemberRewards 
                            WHERE StationId = @StationId 
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

                        -- **��䢵ç���: ���� SUM() ��ͺ ISNULL**
                        SELECT @colsForSelect = STUFF((
                            SELECT DISTINCT ', SUM(ISNULL(' + QUOTENAME(CAST(CreatedAt AS DATE)) + ', 0)) AS ' + QUOTENAME(FORMAT(CreatedAt, 'dd MMM')) 
                            FROM MemberRewards 
                            WHERE StationId = @StationId 
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                        SET @query = N'
                        WITH PivotedData AS (
                            SELECT Tier, ' + @cols + '
                            FROM (
                               SELECT [dbo].[MemberRewards].Tier, CAST(UsedAt AS DATE) AS ActivityDate 
                               FROM [dbo].[RewardHistory] 
                               INNER JOIN [dbo].[MemberRewards] 
                               ON [dbo].[RewardHistory].[MemberId] = [dbo].[MemberRewards].[MemberID] 
                               WHERE [dbo].[RewardHistory].StationId = ' + CAST(@stationId AS VARCHAR) + '
                               GROUP BY [dbo].[MemberRewards].Tier, [dbo].[RewardHistory].[MemberId], CAST(UsedAt AS DATE)
                            ) AS SourceTable
                            PIVOT (COUNT(ActivityDate) FOR ActivityDate IN (' + @cols + ')) AS pvt
                        )
                        SELECT 
                            ISNULL(Tier, N''TOTAL MEMBER BY DAY'') AS Tier, 
                            ' + @colsForSelect + ', 
                            SUM(' + @totalCol + ') AS [TOTAL BY TIER]
                        FROM PivotedData
                        GROUP BY ROLLUP(Tier);';

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
