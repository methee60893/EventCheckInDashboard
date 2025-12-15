using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using EventCheckInDashboard.Services;
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

        // เปลี่ยนจาก Payment เป็น Redemption
        public Dictionary<string, int> TotalTierStats { get; set; }
        public Dictionary<string, int> TotalRedemptionStats { get; set; }

        public List<string> RedemptionHeaders { get; set; } // หัวตารางจะเปลี่ยนไปตามกิจกรรม
        public List<string> TierHeaders { get; set; }

        public ActivityReportModel(EventService service)
        {
            _service = service;
        }

        public IActionResult OnGet(string id)
        {
            var activities = _service.GetActivities();
            var currentAct = activities.FirstOrDefault(a => a.Id == id);
            if (currentAct == null) return RedirectToPage("/Index");

            CurrentActivityName = currentAct.Name;
            TotalQuota = currentAct.TotalQuota;

            // Get Data
            DailyStats = _service.GetDailyDetails(id, FilterDate);
            TotalUsed = DailyStats.Sum(x => x.TotalCheckIn);

            // Set Headers ตามที่กิจกรรมนั้นรองรับจริง
            RedemptionHeaders = currentAct.SupportedRedemptionTypes;
            TierHeaders = EventService.TierColors.Keys.ToList();

            // Calculate Totals (Horizontal)
            TotalTierStats = new Dictionary<string, int>();
            foreach (var tier in TierHeaders)
            {
                TotalTierStats[tier] = DailyStats.Sum(d => d.IsActiveDay && d.TierCounts.ContainsKey(tier) ? d.TierCounts[tier] : 0);
            }

            TotalRedemptionStats = new Dictionary<string, int>();
            foreach (var type in RedemptionHeaders)
            {
                TotalRedemptionStats[type] = DailyStats.Sum(d => d.IsActiveDay && d.RedemptionCounts.ContainsKey(type) ? d.RedemptionCounts[type] : 0);
            }

            return Page();
        }

        public IActionResult OnGetExport(string id)
        {
            var csvBytes = _service.ExportToCsv(id);
            string fileName = $"Activity_{id}_{DateTime.Now:yyyyMMddHHmm}.csv";
            return File(csvBytes, "text/csv", fileName);
        }
    }
}