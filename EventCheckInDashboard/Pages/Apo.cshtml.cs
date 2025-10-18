using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using System; // <-- Add this using directive
using System.Linq;

namespace EventCheckInDashboard.Pages
{
    public class ApoModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ApoModel> _logger;

        public int ActualTotalMembers { get; private set; }
        public string? StackedBarChartJson { get; private set; }
        public string? PieChartJson { get; private set; }
        public List<dynamic> DailyBreakdown { get; private set; } = new();
        public List<dynamic> TierBreakdown { get; private set; } = new();
        public string? ErrorMessage { get; private set; }

        public ApoModel(IConfiguration configuration, ILogger<ApoModel> logger)
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
                // *** ข้อควรระวัง: ฟิลเตอร์ข้อมูลเฉพาะกิจกรรมของ Apo โดยใช้ StationId = 5 ***
                int eventId = 5;

                await LoadAllDataAsync(connectionString, eventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching Apo's dashboard data.");
                ErrorMessage = "เกิดข้อผิดพลาดในการดึงข้อมูลสำหรับหน้า Apo Dashboard";
            }
        }

        private async Task LoadAllDataAsync(string connectionString, int eventId)
        {
            // --- Query รวมเพื่อดึงข้อมูลทั้งหมดในรอบเดียวเพื่อประสิทธิภาพที่ดีขึ้น ---
            string sql = @"
                -- 1. ยอดรวมทั้งหมด
                SELECT COUNT(DISTINCT MemberID) AS TotalMembers FROM [dbo].[MemberRewards] WHERE StationId = @EventId;

                -- 2. ข้อมูลสำหรับ Pie Chart และตารางด้านขวา (แยกตาม Tier)
                SELECT Tier, COUNT(DISTINCT MemberID) AS MemberCount
                FROM [dbo].[MemberRewards]
                WHERE StationId = @EventId
                GROUP BY Tier
                ORDER BY Tier;

                -- 3. ข้อมูลสำหรับ Stacked Bar Chart และตารางด้านซ้าย (แยกตามประเภทการแลก)
                SELECT
                    CAST(CreatedAt AS DATE) AS ActivityDate,
                    CAST(SUM(CASE WHEN RewardTypeID = 1 THEN FLOOR(Carat/360.0) ELSE 0 END) AS INT) AS Karat360,
                    SUM(CASE WHEN RewardTypeID = 2 THEN 1 ELSE 0 END) AS NewMembers, 
                    SUM(CASE WHEN RewardTypeID = 3 THEN 1 ELSE 0 END) AS CardX
                FROM [dbo].[MemberRewards]
                WHERE StationId = @EventId
                GROUP BY CAST(CreatedAt AS DATE)
                ORDER BY ActivityDate;
            ";

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EventId", eventId);

            await using var reader = await command.ExecuteReaderAsync();

            // --- 1. อ่านข้อมูลยอดรวม ---
            if (await reader.ReadAsync())
            {
                ActualTotalMembers = reader["TotalMembers"] as int? ?? 0;
            }

            // --- 2. อ่านข้อมูลแยกตาม Tier ---
            await reader.NextResultAsync();
            var pieChartData = new List<object>();
            while (await reader.ReadAsync())
            {
                var tierData = new
                {
                    Tier = reader["Tier"].ToString(),
                    MemberCount = (int)reader["MemberCount"]
                };
                TierBreakdown.Add(tierData);
                pieChartData.Add(tierData);
            }
            PieChartJson = JsonSerializer.Serialize(pieChartData);


            // --- 3. อ่านข้อมูลแยกตามประเภทการแลก ---
            await reader.NextResultAsync();
            var dailyData = new List<object>();
            if (await reader.ReadAsync()) // หน้านี้แสดงข้อมูลแค่วันเดียว
            {
                var karat = new { Label = "แลก360กะรัต", Value = (int)reader["Karat360"] };
                var newMem = new { Label = "สมาชิกใหม่", Value = (int)reader["NewMembers"] };
                var cardX = new { Label = "CARD X", Value = (int)reader["CardX"] };
                DailyBreakdown.Add(karat);
                DailyBreakdown.Add(newMem);
                DailyBreakdown.Add(cardX);

                dailyData.Add(new { Date = ((DateTime)reader["ActivityDate"]).ToString("dd-MMM"), Karat360 = karat.Value, NewMembers = newMem.Value, CardX = cardX.Value });
            }

            // สร้าง JSON สำหรับ Stacked Bar Chart
            var stackedBarData = new
            {
                labels = dailyData.Select(d => ((dynamic)d).Date).ToList(),
                datasets = new object[]
                {
                    new { label = "แลก360กะรัต", data = dailyData.Select(d => ((dynamic)d).Karat360).ToList(), backgroundColor = "rgba(180, 83, 9, 0.9)" },
                    new { label = "สมาชิกใหม่", data = dailyData.Select(d => ((dynamic)d).NewMembers).ToList(), backgroundColor = "rgba(220, 38, 38, 0.9)" },
                    new { label = "CARD X", data = dailyData.Select(d => ((dynamic)d).CardX).ToList(), backgroundColor = "rgba(99, 102, 241, 0.9)" }
                }
            };
            StackedBarChartJson = JsonSerializer.Serialize(stackedBarData);
        }
    }
}
