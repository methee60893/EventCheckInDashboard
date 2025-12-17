using EventCheckInDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EventCheckInDashboard.Pages
{
    public class ActivityReportModel : PageModel
    {
        private readonly EventService _service;

        [BindProperty(SupportsGet = true)]
        public string Id { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? FilterDate { get; set; }

        public string CurrentActivityName { get; set; }
        public int TotalQuota { get; set; }
        public int TotalUsed { get; set; }

        public List<DailyDetail> DailyStats { get; set; }

        public Dictionary<string, int> TableTotalTierStats { get; set; } // 1. สำหรับตาราง (รวมทุกวัน)
        public Dictionary<string, int> PieChartTierStats { get; set; }   // 2. สำหรับกราฟ (ตาม Filter)

        public Dictionary<string, int> TotalTierStats { get; set; }


        public List<string> RedemptionHeaders { get; set; } // หัวตารางจะเปลี่ยนไปตามกิจกรรม
        public List<string> TierHeaders { get; set; }
        public Dictionary<string, int> TotalRedemptionStats { get; set; }

        public List<SelectListItem> DateList { get; set; }


        public ActivityReportModel(EventService service)
        {
            _service = service;
        }

        public IActionResult OnGet(string id)
        {
            // 1. ตรวจสอบ Activity
            var activities = _service.GetActivities();
            var currentAct = activities.FirstOrDefault(a => a.Id == id);
            if (currentAct == null) return RedirectToPage("/dashboardevent/Index");

            CurrentActivityName = currentAct.Name;
            TotalQuota = currentAct.TotalQuota;

            // 2. ดึงข้อมูล (เพิ่มการเช็ค null)
            DailyStats = _service.GetDailyDetails(id, null) ?? new List<DailyDetail>();

            // 3. สร้าง Dropdown (Logic 18-25 Dec & Lucky Giftmas)
            var startDate = new DateTime(2025, 12, 18);
            var endDate = new DateTime(2025, 12, 28);
            var availableDates = new List<DateTime>();

            for (var dt = startDate; dt <= endDate; dt = dt.AddDays(1))
            {
                availableDates.Add(dt);
            }

            if (!string.IsNullOrEmpty(id) && id.ToLower() == "lucky")
            {
                availableDates = availableDates.Where(d => d.Day == 18 || d.Day == 25).ToList();
            }

            DateList = availableDates.Select(d => new SelectListItem
            {
                Value = d.ToString("yyyy-MM-dd"),
                Text = d.ToString("dd MMM yyyy"),
                Selected = FilterDate.HasValue && d.Date == FilterDate.Value.Date
            }).ToList();

            DateList.Insert(0, new SelectListItem { Value = "", Text = "All Days (Overview)", Selected = !FilterDate.HasValue });

            // 4. เตรียม Headers (ป้องกัน null)
            TierHeaders = EventService.TierColors.Keys.ToList();
            RedemptionHeaders = currentAct.SupportedRedemptionTypes ?? new List<string>();

            // ---------------------------------------------------------
            // A) ข้อมูลสำหรับ "ตาราง" (TableTotal) -> ใช้ข้อมูลทั้งหมด (Grand Total)
            // ---------------------------------------------------------
            TableTotalTierStats = new Dictionary<string, int>();
            foreach (var tier in TierHeaders)
            {
                // ใช้ TryGetValue หรือ Check Key เพื่อความชัวร์
                TableTotalTierStats[tier] = DailyStats.Sum(d =>
                    (d.TierCounts != null && d.TierCounts.ContainsKey(tier)) ? d.TierCounts[tier] : 0);
            }

            // ---------------------------------------------------------
            // B) ข้อมูลสำหรับ "Pie Chart" และ "KPI" -> ใช้ข้อมูลตาม Filter
            // ---------------------------------------------------------
            IEnumerable<DailyDetail> filteredData = DailyStats;
            if (FilterDate.HasValue)
            {
                filteredData = DailyStats.Where(d => d.Date.Date == FilterDate.Value.Date);
            }

            // คำนวณยอด Used
            TotalUsed = filteredData.Sum(x => x.TotalCheckIn);

            // คำนวณ Tier สำหรับกราฟ
            PieChartTierStats = new Dictionary<string, int>();
            foreach (var tier in TierHeaders)
            {
                PieChartTierStats[tier] = filteredData.Sum(d =>
                    (d.TierCounts != null && d.TierCounts.ContainsKey(tier)) ? d.TierCounts[tier] : 0);
            }

            // คำนวณ Redemption Stats (สำหรับตาราง Source ถ้ามี)
            TotalRedemptionStats = new Dictionary<string, int>();
            foreach (var type in RedemptionHeaders)
            {
                TotalRedemptionStats[type] = filteredData.Sum(d =>
                    (d.IsActiveDay && d.RedemptionCounts != null && d.RedemptionCounts.ContainsKey(type))
                    ? d.RedemptionCounts[type]
                    : 0);
            }

            return Page();
        }

        public IActionResult OnGetExport(string id)
        {
            var csvBytes = _service.ExportToCsv(id);
            return File(csvBytes, "text/csv", $"Activity_{id}_{DateTime.Now:yyyyMMddHHmm}.csv");
        }
    }
}