using EventCheckInDashboard.Data;
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
        private readonly AppDbContext _context;
        public EventService(AppDbContext context)
        {
            _context = context;
        }

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

        private int GetDbActivityId(string pageId) => pageId switch
        {
            "ginger" => 1,
            "luxe" => 2,
            "power" => 3,
            "giant" => 4,
            "chill" => 5,
            "lucky" => 6,
            _ => 0
        };

        public List<ActivityInfo> GetActivities()
        {
            return new List<ActivityInfo>
            {
                new ActivityInfo { Id = "ginger", Name = "GINGER BREAD", TotalQuota = 1000, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } },
                new ActivityInfo { Id = "luxe", Name = "THE LUXE CLAW", TotalQuota = 100, SupportedRedemptionTypes = new List<string> { RedemptionTypes.CASH_CARD } },
                new ActivityInfo { Id = "power", Name = "THE POWER CLAW", TotalQuota = 300, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } },
                new ActivityInfo { Id = "giant", Name = "THE GIANT CLAW", TotalQuota = 50, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } },
                // Chill Bar รับได้ 3 ทาง
                new ActivityInfo { Id = "chill", Name = "GIFTIVAL CHILL BAR", TotalQuota = 100, SupportedRedemptionTypes = new List<string> { RedemptionTypes.MEMBER_QUOTA, RedemptionTypes.RECEIPT, RedemptionTypes.CARAT } },
                new ActivityInfo { Id = "lucky", Name = "LUCKY GIFTMAS", TotalQuota = 9999, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } }
            };
        }

        //public List<DailyDetail> GetDailyDetails(string activityId, DateTime? filterDate = null)
        //{
        //    var result = new List<DailyDetail>();
        //    DateTime startDate = new DateTime(2025, 12, 18);
        //    DateTime endDate = new DateTime(2025, 12, 25);
        //    var rnd = new Random(activityId.GetHashCode());

        //    // หาว่ากิจกรรมนี้รองรับ Type ไหนบ้าง
        //    var currentActivity = GetActivities().FirstOrDefault(a => a.Id == activityId);
        //    var supportedTypes = currentActivity?.SupportedRedemptionTypes ?? new List<string>();

        //    for (var dt = startDate; dt <= endDate; dt = dt.AddDays(1))
        //    {
        //        if (filterDate.HasValue && filterDate.Value.Date != dt.Date) continue;

        //        bool isOpen = true;
        //        if (activityId == "lucky" && dt.Day != 18 && dt.Day != 25) isOpen = false;

        //        var daily = new DailyDetail
        //        {
        //            Date = dt,
        //            IsActiveDay = isOpen,
        //            RedemptionCounts = new Dictionary<string, int>(),
        //            TierCounts = new Dictionary<string, int>()
        //        };

        //        // Initialize 0 for all supported types
        //        foreach (var type in supportedTypes) daily.RedemptionCounts[type] = 0;

        //        if (isOpen)
        //        {
        //            // Generate Data เฉพาะ Type ที่กิจกรรมนั้นรองรับ
        //            foreach (var type in supportedTypes)
        //            {
        //                daily.RedemptionCounts[type] = rnd.Next(5, 50);
        //            }

        //            foreach (var t in TierColors.Keys)
        //            {
        //                daily.TierCounts[t] = rnd.Next(2, 30);
        //            }

        //            daily.TotalCheckIn = daily.RedemptionCounts.Sum(x => x.Value);
        //        }
        //        else
        //        {
        //            foreach (var t in TierColors.Keys) daily.TierCounts[t] = 0;
        //            daily.TotalCheckIn = 0;
        //        }

        //        result.Add(daily);
        //    }

        //    return result;
        //}


        public List<DailyDetail> GetDailyDetails(string activityId, DateTime? filterDate = null)
        {
            var result = new List<DailyDetail>();
            int dbActivityId = GetDbActivityId(activityId);

            // 1. ดึงข้อมูลจริงจาก DB
            var query = _context.EventTransactions.AsQueryable();

            if (dbActivityId > 0)
            {
                query = query.Where(t => t.ActivityID == dbActivityId);
            }

            if (filterDate.HasValue)
            {
                query = query.Where(t => t.EventDate.Date == filterDate.Value.Date);
            }

            // ดึงข้อมูลมาประมวลผล (หรือจะ GroupBy ใน SQL เลยก็ได้หากข้อมูลเยอะมาก)
            var transactions = query.Select(t => new {
                t.EventDate,
                t.PaymentMethod,
                t.MemberTier,
                t.RightsEarned
            }).ToList();

            // 2. กำหนดช่วงเวลา (ตาม Requirement เดิม)
            DateTime startDate = new DateTime(2025, 12, 18);
            DateTime endDate = new DateTime(2025, 12, 25);

            var currentActivity = GetActivities().FirstOrDefault(a => a.Id == activityId);
            var supportedTypes = currentActivity?.SupportedRedemptionTypes ?? new List<string>();

            for (var dt = startDate; dt <= endDate; dt = dt.AddDays(1))
            {
                // Filter เฉพาะวันที่เลือก (ถ้ามี)
                if (filterDate.HasValue && filterDate.Value.Date != dt.Date) continue;

                // ตรวจสอบวันเปิด-ปิด (ตาม Logic เดิม)
                bool isOpen = true;
                if (activityId == "lucky" && dt.Day != 18 && dt.Day != 25) isOpen = false;

                var daily = new DailyDetail
                {
                    Date = dt,
                    IsActiveDay = isOpen,
                    RedemptionCounts = new Dictionary<string, int>(),
                    TierCounts = new Dictionary<string, int>()
                };

                // Init values
                foreach (var type in supportedTypes) daily.RedemptionCounts[type] = 0;
                foreach (var t in TierColors.Keys) daily.TierCounts[t] = 0;

                if (isOpen)
                {
                    // Filter Transaction ของวันนั้นๆ
                    var dailyTrans = transactions.Where(t => t.EventDate.Date == dt.Date).ToList();

                    // Count by Redemption Type (PaymentMethod in DB)
                    foreach (var type in supportedTypes)
                    {
                        // Match string ระหว่าง Code ใน Service กับ Data ใน DB
                        daily.RedemptionCounts[type] = dailyTrans
                            .Where(t => t.PaymentMethod == type)
                            .Sum(t => t.RightsEarned > 0 ? t.RightsEarned : 1); // นับสิทธิ์ หรือ นับรายการ
                    }

                    // Count by Tier
                    foreach (var t in TierColors.Keys)
                    {
                        daily.TierCounts[t] = dailyTrans.Count(x => x.MemberTier?.ToUpper() == t);
                    }

                    daily.TotalCheckIn = dailyTrans.Count; // หรือ Sum RightsEarned ตาม Business Logic
                }

                result.Add(daily);
            }

            return result;
        }

        // เพิ่มฟังก์ชันสำหรับ Export Data
        public byte[] ExportToCsv(string activityId)
        {
            int dbActivityId = GetDbActivityId(activityId);
            var data = _context.EventTransactions
                .Where(t => dbActivityId == 0 || t.ActivityID == dbActivityId) // 0 = All
                .OrderBy(t => t.EventDate)
                .ToList();

            var csv = new System.Text.StringBuilder();
            // Header
            csv.AppendLine("Date,Time,Activity,MemberID,Tier,ReceiptNo,PaymentMethod,Spending,Rights");

            foreach (var item in data)
            {
                csv.AppendLine($"{item.EventDate:yyyy-MM-dd},{item.EventDate:HH:mm:ss},{item.ActivityName},{item.MemberID},{item.MemberTier},{item.ReceiptNo},{item.PaymentMethod},{item.SpendingAmount},{item.RightsEarned}");
            }

            return System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        }
    }
}