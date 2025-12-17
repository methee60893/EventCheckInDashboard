using System;
using System.Collections.Generic;
using System.Linq;
using EventCheckInDashboard.Data;
using EventCheckInDashboard.Models;

namespace EventCheckInDashboard.Services
{
    // Model สำหรับข้อมูลแต่ละวัน
    public class DailyDetail
    {
        public DateTime Date { get; set; }
        public string DateDisplay => Date.ToString("dd MMM");
        public int TotalCheckIn { get; set; }
        public bool IsActiveDay { get; set; }

        public Dictionary<string, int> RedemptionCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> TierCounts { get; set; } = new Dictionary<string, int>();
    }

    public class ActivityInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int TotalQuota { get; set; }
        public int UsedQuota { get; set; }
        public int TotalRegistrations { get; set; }

        // ต้องมี Property นี้ เพื่อบอกว่ากิจกรรมนี้มีรายการอะไรบ้าง
        public List<string> SupportedRedemptionTypes { get; set; } = new List<string>();
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

        // ชื่อที่ใช้แสดงบนหน้าเว็บ (Dashboard)
        public static class RedemptionTypes
        {
            public const string RECEIPT = "RECEIPT SPENDING";
            public const string CASH_CARD = "CASH CARD";
            public const string CARD_X_RECEIPT = "CARD X";
            public const string CARAT = "CARAT REDEEM";
            public const string MEMBER_QUOTA = "MEMBER TIER";
        }

        public static class ActivityName
        {
            public const string ACT1 = "GINGER BREAD";
            public const string ACT2 = "THE LUXE CRAW";
            public const string ACT3 = "THE POWER CRAW";
            public const string ACT4 = "THE GIENT CRAW";
            public const string ACT5 = "GIFTIVAL CHILL BAR";
            public const string ACT6 = "LUCKY GIFTMAS";
        }

        public List<ActivityInfo> GetActivities()
        {
            // กำหนดค่าเริ่มต้นว่าแต่ละกิจกรรม มีปุ่มแลกแบบไหนบ้าง
            return new List<ActivityInfo>
            {
                new ActivityInfo { Id = "ginger", Name = "GIFTIVAL GINGER BREAD", TotalQuota = 1000, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } },
                new ActivityInfo { Id = "luxe", Name = "THE LUXE CLAW", TotalQuota = 100, SupportedRedemptionTypes = new List<string> { RedemptionTypes.CASH_CARD } },
                new ActivityInfo { Id = "power", Name = "THE POWER CLAW", TotalQuota = 300, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } },
                new ActivityInfo { Id = "giant", Name = "THE GIANT CLAW", TotalQuota = 50, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } },
                new ActivityInfo { Id = "chill", Name = "GIFTIVAL CHILL BAR", TotalQuota = 100, SupportedRedemptionTypes = new List<string> { RedemptionTypes.MEMBER_QUOTA, RedemptionTypes.CARD_X_RECEIPT, RedemptionTypes.CARAT } },
                new ActivityInfo { Id = "lucky", Name = "LUCKY GIFTMAS", TotalQuota = 9999, SupportedRedemptionTypes = new List<string> { RedemptionTypes.RECEIPT } }
            };
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

        // ฟังก์ชันช่วยแปลงค่าจาก DB เป็นค่า Dashboard
        // (แก้ไขปัญหา: DB เก็บ "TIER" แต่โค้ดหา "RECEIPT SPENDING")
        private string MapDbPaymentToDashboardType(string paymentMethod)
        {
            // แปลงค่า Null ให้เป็น String ว่างก่อน
            var pm = paymentMethod?.ToUpper() ?? "";



            // Logic การ Mapping (อิงจากข้อมูลตัวอย่าง CSV)
            if (pm.Contains("RECEIPT"))
            {
                return RedemptionTypes.RECEIPT;
            }
            if (pm.Contains("CARDX"))
            {
                return RedemptionTypes.CARD_X_RECEIPT;
            }
            if (pm.Contains("CASH CARD"))
            {
                return RedemptionTypes.CASH_CARD;
            }
            if (pm.Contains("CARAT"))
            {
                return RedemptionTypes.CARAT;
            }
            if (pm.Contains("TIER"))
            {
                return RedemptionTypes.MEMBER_QUOTA;
            }

            return "OTHER"; // กรณีไม่ตรงเงื่อนไข
        }

        public List<DailyDetail> GetDailyDetails(string activityId, DateTime? filterDate = null)
        {
            var result = new List<DailyDetail>();
            int dbActivityId = GetDbActivityId(activityId);

            // 1. ดึงข้อมูลจาก DB (เฉพาะ Field ที่จำเป็น)
            var transactions = new List<EventTransaction>();
            try
            {
                var query = _context.EventTransactions.AsQueryable();
                if (dbActivityId > 0) query = query.Where(t => t.ActivityID == dbActivityId);

                transactions = query.Select(t => new EventTransaction
                {
                    EventDate = t.EventDate,
                    PaymentMethod = t.PaymentMethod,
                    RedeemType = t.RedeemType, // ดึงเพิ่มมาช่วยเช็ค
                    MemberTier = t.MemberTier,
                    RightsEarned = t.RightsEarned
                }).ToList();
            }
            catch { } // ปล่อยผ่านกรณี DB มีปัญหา

            // 2. Setup วันที่และ Config
            DateTime startDate = new DateTime(2025, 12, 18);
            DateTime endDate = new DateTime(2025, 12, 28);

            var currentActivity = GetActivities().FirstOrDefault(a => a.Id == activityId);
            var supportedTypes = currentActivity?.SupportedRedemptionTypes ?? new List<string>();

            // 3. Loop สร้างข้อมูลรายวัน
            for (var dt = startDate; dt <= endDate; dt = dt.AddDays(1))
            {
                // ถ้า filter มา ก็ข้ามวันที่ไม่ใช่ (แต่ปกติเราจะใช้ list นี้วาดกราฟ Timeline ด้วย เลยมักไม่ filter ตรงนี้)
                // แต่ถ้า logic เดิมคุณ filter ในนี้ ก็คงไว้ได้ครับ
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

                // Initialize 0 ให้ครบทุกประเภท (สำคัญมาก! เพื่อให้ตารางมีแถวครบ)
                foreach (var type in supportedTypes) daily.RedemptionCounts[type] = 0;
                foreach (var t in TierColors.Keys) daily.TierCounts[t] = 0;

                if (isOpen)
                {
                    var dailyTrans = transactions.Where(t => t.EventDate.Date == dt.Date).ToList();

                    // --- [ส่วนที่แก้ไข] การนับยอดตามประเภท ---
                    foreach (var type in supportedTypes)
                    {
                        // นับ Transaction ที่ Map แล้วตรงกับ Type นี้
                        daily.RedemptionCounts[type] = dailyTrans
                            .Where(t => MapDbPaymentToDashboardType(t.PaymentMethod) == type)
                            .Sum(t => t.RightsEarned > 0 ? t.RightsEarned : 0);
                    }
                    // ----------------------------------------

                    foreach (var t in TierColors.Keys)
                    {
                        daily.TierCounts[t] = dailyTrans.Count(x => !string.IsNullOrEmpty(x.MemberTier) && x.MemberTier.ToUpper() == t);
                    }

                    daily.TotalCheckIn = daily.RedemptionCounts.Sum(x => x.Value);
                }

                result.Add(daily);
            }

            return result;
        }

        public byte[] ExportToCsv(string activityId)
        {
            int dbActivityId = GetDbActivityId(activityId);
            var data = _context.EventTransactions
                .Where(t => dbActivityId == 0 || t.ActivityID == dbActivityId)
                .OrderBy(t => t.EventDate)
                .ToList();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Date,Time,Activity,MemberID,Tier,ReceiptNo,PaymentMethod,RedeemType,Spending,Rights,StaffID");

            foreach (var item in data)
            {
                csv.AppendLine($"{item.EventDate:yyyy-MM-dd},{item.EventDate:HH:mm:ss},{item.ActivityName},{item.MemberID},{item.MemberTier},{item.ReceiptNo},{item.PaymentMethod},{item.RedeemType},{item.SpendingAmount},{item.RightsEarned},{item.StaffID}");
            }

            return System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        }
    }
}