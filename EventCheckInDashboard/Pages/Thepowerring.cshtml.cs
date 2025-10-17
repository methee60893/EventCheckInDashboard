using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Dynamic;
using System.Text.Json;

namespace EventCheckInDashboard.Pages
{
    public class ThepowerringModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ThepowerringModel> _logger;

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

        public ThepowerringModel(IConfiguration configuration, ILogger<ThepowerringModel> logger)
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
                int stationId = 2;

                await LoadSummaryDataAsync(connectionString, stationId);
                await LoadChartDataAsync(connectionString, stationId);
                await LoadSegmentPivotTableAsync(connectionString, stationId);
                await LoadTierPivotTableAsync(connectionString, stationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching Thepowerring dashboard data.");
                ErrorMessage = "�Դ��ͼԴ��Ҵ㹡�ô֧����������Ѻ˹�� The Power Ring";
            }
        }

        private async Task LoadSummaryDataAsync(string connectionString, int stationId)
        {
            string sql = "SELECT  CAST(UsedAt AS DATE) AS ActivityDate , COUNT(DISTINCT MemberID) AS TotalMembers, CAST(SUM(CASE WHEN RewardTypeID = 1 THEN FLOOR(Carat/360.0) ELSE 0 END) AS INT)  + SUM(CASE WHEN RewardTypeID = 2 THEN 1 ELSE 0 END) + SUM(CASE WHEN RewardTypeID = 3 THEN 1 ELSE 0 END) AS TotalRights\r\nFROM [dbo].[RewardHistory]\r\nWHERE [StationId]=@StationId GROUP BY CAST(UsedAt AS DATE)\r\nORDER BY ActivityDate;\r\n;";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@StationId", stationId);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                ActualRights = reader["TotalRights"] as int? ?? 0;
                ActualMembers = reader["TotalMembers"] as int? ?? 0;
            }
        }

        private async Task LoadChartDataAsync(string connectionString, int stationId)
        {
            // --- Query ����Ѻ Pie Chart (�¡��� Tier) ---
            var pieChartData = new List<object>();
            string pieSql = @"
                SELECT Tier, COUNT(DISTINCT MemberID) AS MemberCount
                FROM MemberRewards WHERE StationId = @StationId
                GROUP BY Tier ORDER BY Tier;";

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand(pieSql, connection);
                command.Parameters.AddWithValue("@StationId", stationId);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    pieChartData.Add(new { Tier = reader["Tier"].ToString(), MemberCount = (int)reader["MemberCount"] });
                }
            }
            PieChartJson = JsonSerializer.Serialize(pieChartData);

            // --- Query ����Ѻ Stacked Bar Chart (�¡��� Segment �ҡ RewardTypeID) ---
            var dailyCounts = new Dictionary<string, (int Karat, int NewMem, int CardX)>();
            string barSql = @"
                SELECT CAST(CreatedAt AS DATE) as ActivityDate,
                    SUM(CASE WHEN RewardTypeID = 1 THEN 1 ELSE 0 END) AS Karat360,
                    SUM(CASE WHEN RewardTypeID = 2 THEN 1 ELSE 0 END) AS NewMembers,
                    SUM(CASE WHEN RewardTypeID = 3 THEN 1 ELSE 0 END) AS CardX
                FROM MemberRewards WHERE StationId = @StationId
                GROUP BY CAST(CreatedAt AS DATE) ORDER BY ActivityDate;";

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand(barSql, connection);
                command.Parameters.AddWithValue("@StationId", stationId);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string date = ((DateTime)reader["ActivityDate"]).ToString("dd-MMM");
                    dailyCounts[date] = ((int)reader["Karat360"], (int)reader["NewMembers"], (int)reader["CardX"]);
                }
            }

            var labels = new List<string> { "17-Oct", "18-Oct", "19-Oct", "20-Oct", "21-Oct", "22-Oct", "23-Oct" };
            var barData = new
            {
                labels = labels,
                datasets = new object[]
                {
                    new { label = "�š360���ѵ", data = labels.Select(l => dailyCounts.ContainsKey(l) ? dailyCounts[l].Karat : 0).ToList(), backgroundColor = "rgba(234, 179, 8, 0.85)" },
                    new { label = "��Ҫԡ����", data = labels.Select(l => dailyCounts.ContainsKey(l) ? dailyCounts[l].NewMem : 0).ToList(), backgroundColor = "rgba(59, 130, 246, 0.85)" },
                    new { label = "CARD X", data = labels.Select(l => dailyCounts.ContainsKey(l) ? dailyCounts[l].CardX : 0).ToList(), backgroundColor = "rgba(34, 197, 94, 0.85)" }
                }
            };
            StackedBarChartJson = JsonSerializer.Serialize(barData);
        }

        private async Task LoadSegmentPivotTableAsync(string connectionString, int stationId)
        {
            string sql = @"
                DECLARE @cols AS NVARCHAR(MAX), 
                          @query AS NVARCHAR(MAX), 
                          @totalCol AS NVARCHAR(MAX), 
                          @colsForSelect AS NVARCHAR(MAX);


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

                            -- **����¹�ҡ ISNULL �� SUM(ISNULL(...))**
                            SELECT @colsForSelect = STUFF((
                                SELECT DISTINCT ', SUM(ISNULL(' + QUOTENAME(CAST(CreatedAt AS DATE)) + ', 0)) AS ' + QUOTENAME(FORMAT(CreatedAt, 'dd MMM')) 
                                FROM MemberRewards 
                                WHERE StationId = @StationId 
                                FOR XML PATH(''), TYPE
                            ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

                            SET @query = N'
                            WITH SourceData AS (
                                SELECT CAST(CreatedAt AS DATE) AS ActivityDate,
                                       CASE 
                                            WHEN RewardTypeID = 1 THEN N''�š360���ѵ'' 
                                            WHEN RewardTypeID = 2 THEN N''��Ҫԡ����'' 
                                            WHEN RewardTypeID = 3 THEN N''CARD X'' 
                                            ELSE N''����'' 
                                       END AS Segment
                                FROM MemberRewards 
                                WHERE StationId = ' + CAST(@StationId AS VARCHAR) + '
                            ),
                            PivotedData AS (
                                SELECT Segment, ' + @cols + '
                                FROM SourceData
                                PIVOT (COUNT(ActivityDate) FOR ActivityDate IN (' + @cols + ')) AS pvt
                            )
                            SELECT 
                                ISNULL(Segment, N''TOTAL BY DAY'') AS Segment, 
                                ' + @colsForSelect + ', 
                                SUM(' + @totalCol + ') AS [TOTAL BY SEGMENT]
                            FROM PivotedData
                            GROUP BY ROLLUP(Segment) 
                            ORDER BY CASE WHEN Segment = N''TOTAL BY DAY'' THEN 1 ELSE 0 END, Segment DESC';

                            EXEC sp_executesql @query;";

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@StationId", stationId);
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
                                SELECT Tier, CAST(CreatedAt AS DATE) AS ActivityDate 
                                FROM MemberRewards 
                                WHERE StationId = ' + CAST(@StationId AS VARCHAR) + '
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
            command.Parameters.AddWithValue("@StationId", stationId);
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
