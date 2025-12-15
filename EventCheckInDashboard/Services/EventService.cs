using System;
using System.Collections.Generic;
using System.Linq;

namespace EventCheckInDashboard.Services
{
    // Model สำหรับข้อมูลแต่ละวัน
    public class DailyDetail
    {
        public DateTime Date { get; set; }
        public string DateDisplay => Date.ToString("dd MMM");
        public int TotalCheckIn { get; set; }
        public bool IsActiveDay { get; set; }

        // เปลี่ยนจาก PaymentCounts เป็น RedemptionCounts เพื่อความถูกต้อง
        public Dictionary<string, int> RedemptionCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> TierCounts { get; set; } = new Dictionary<string, int>();
    }

    public class ActivityInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int TotalQuota { get; set; }
        public int UsedQuota { get; set; }

        public int TotalRegistrations { get; set; } // ยอดลงทะเบียนรวม
        // เพิ่ม Property เพื่อระบุว่ากิจกรรมนี้รับสิทธิ์ด้วยวิธีไหนบ้าง
        public List<string> SupportedRedemptionTypes { get; set; }
    }

    public class EventService
    {
        public static readonly Dictionary<string, string> TierColors = new Dictionary<string, string>
        {
            { "NAVY", "#3B65AF" },
            { "SCARLET", "#FF1D25" },
            { "CROWN", "#FFC000" },
            { "VEGA", "#8D96A3" },
        };

        // ประเภทการรับสิทธิ์ตาม PDF Requirement
        public static class RedemptionTypes
        {
            public const string RECEIPT = "RECEIPT SPENDING"; // ใช้ใบเสร็จ
            public const string CASH_CARD = "CASH CARD";      // ซื้อ Cash Card
            public const string CARAT = "CARAT REDEEM";       // แลกกะรัต
            public const string MEMBER_QUOTA = "MEMBER QUOTA"; // สิทธิ์ตามหน้าบัตร
        }

        public List<ActivityInfo> GetActivities()
        {
            return new List<ActivityInfo>
            {
                new ActivityInfo { Id = "ginger", Name = "GINGER BREAD", TotalQuota = 800, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } },
                new ActivityInfo { Id = "luxe", Name = "THE LUXE CLAW", TotalQuota = 800, SupportedRedemptionTypes = new List<string> { RedemptionTypes.CASH_CARD } },
                new ActivityInfo { Id = "power", Name = "THE POWER CLAW", TotalQuota = 2400, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } },
                new ActivityInfo { Id = "giant", Name = "THE GIANT CLAW", TotalQuota = 400, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } },
                // Chill Bar รับได้ 3 ทาง
                new ActivityInfo { Id = "chill", Name = "GIFTIVAL CHILL BAR", TotalQuota = 1000, SupportedRedemptionTypes = new List<string> { RedemptionTypes.MEMBER_QUOTA, RedemptionTypes.RECEIPT, RedemptionTypes.CARAT } },
                new ActivityInfo { Id = "lucky", Name = "LUCKY GIFTMAS", TotalQuota = 500, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } }
            };
        }

        public List<DailyDetail> GetDailyDetails(string activityId, DateTime? filterDate = null)
        {
            var result = new List<DailyDetail>();
            DateTime startDate = new DateTime(2025, 12, 18);
            DateTime endDate = new DateTime(2025, 12, 25);
            var rnd = new Random(activityId.GetHashCode());

            // หาว่ากิจกรรมนี้รองรับ Type ไหนบ้าง
            var currentActivity = GetActivities().FirstOrDefault(a => a.Id == activityId);
            var supportedTypes = currentActivity?.SupportedRedemptionTypes ?? new List<string>();

            for (var dt = startDate; dt <= endDate; dt = dt.AddDays(1))
            {
                if (filterDate.HasValue && filterDate.Value.Date != dt.Date) continue;

                bool isOpen = true;
                if (activityId == "lucky" && dt.Day != 18 && dt.Day != 25) isOpen = false;

                var daily = new DailyDetail
                {
                    Date = dt,
                    IsActiveDay = isOpen,
                    RedemptionCounts = new Dictionary<string, int>(),
                    TierCounts = new Dictionary<string, int>()
                };

                // Initialize 0 for all supported types
                foreach (var type in supportedTypes) daily.RedemptionCounts[type] = 0;

                if (isOpen)
                {
                    // Generate Data เฉพาะ Type ที่กิจกรรมนั้นรองรับ
                    foreach (var type in supportedTypes)
                    {
                        daily.RedemptionCounts[type] = rnd.Next(5, 50);
                    }

                    foreach (var t in TierColors.Keys)
                    {
                        daily.TierCounts[t] = rnd.Next(2, 30);
                    }

                    daily.TotalCheckIn = daily.RedemptionCounts.Sum(x => x.Value);
                }
                else
                {
                    foreach (var t in TierColors.Keys) daily.TierCounts[t] = 0;
                    daily.TotalCheckIn = 0;
                }

                result.Add(daily);
            }

            return result;
        }
    }
}