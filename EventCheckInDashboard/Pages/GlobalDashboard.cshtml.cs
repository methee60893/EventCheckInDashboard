using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using EventCheckInDashboard.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EventCheckInDashboard.Pages
{
    public class GlobalDashboardModel : PageModel
    {
        private readonly EventService _service;

        [BindProperty(SupportsGet = true)]
        public DateTime? FilterDate { get; set; }

        public int TotalQuotaAll { get; set; }
        public int TotalUsedAll { get; set; }

        public List<DailyDetail> AggregatedDailyStats { get; set; }

        public Dictionary<string, int> TotalTierStats { get; set; }
        public Dictionary<string, int> TotalRedemptionStats { get; set; }

        public List<string> RedemptionHeaders { get; set; }
        public List<string> TierHeaders { get; set; }

        public GlobalDashboardModel()
        {
            _service = new EventService();
        }

        public void OnGet()
        {
            var activities = _service.GetActivities();
            TotalQuotaAll = activities.Sum(a => a.TotalQuota);

            var templateStats = _service.GetDailyDetails(activities.First().Id, FilterDate);

            // เตรียม Headers ทั้งหมดที่เป็นไปได้ในระบบ
            RedemptionHeaders = new List<string> {
                EventService.RedemptionTypes.RECEIPT,
                EventService.RedemptionTypes.CASH_CARD,
                EventService.RedemptionTypes.CARAT,
                EventService.RedemptionTypes.MEMBER_QUOTA
            };
            TierHeaders = EventService.TierColors.Keys.ToList();

            // Init Aggregation
            AggregatedDailyStats = templateStats.Select(t => new DailyDetail
            {
                Date = t.Date,
                IsActiveDay = true,
                RedemptionCounts = RedemptionHeaders.ToDictionary(k => k, v => 0),
                TierCounts = EventService.TierColors.Keys.ToDictionary(k => k, v => 0),
                TotalCheckIn = 0
            }).ToList();

            // Aggregate
            foreach (var act in activities)
            {
                var actStats = _service.GetDailyDetails(act.Id, FilterDate);

                for (int i = 0; i < AggregatedDailyStats.Count; i++)
                {
                    var globalDay = AggregatedDailyStats[i];
                    var actDay = actStats.FirstOrDefault(d => d.Date == globalDay.Date);

                    if (actDay != null && actDay.IsActiveDay)
                    {
                        globalDay.TotalCheckIn += actDay.TotalCheckIn;

                        // บวกยอด Redemption (ถ้ากิจกรรมนั้นมี Key นี้)
                        foreach (var type in RedemptionHeaders)
                        {
                            if (actDay.RedemptionCounts.ContainsKey(type))
                                globalDay.RedemptionCounts[type] += actDay.RedemptionCounts[type];
                        }

                        foreach (var t in TierHeaders)
                        {
                            if (actDay.TierCounts.ContainsKey(t))
                                globalDay.TierCounts[t] += actDay.TierCounts[t];
                        }
                    }
                }
            }

            TotalUsedAll = AggregatedDailyStats.Sum(x => x.TotalCheckIn);

            TotalTierStats = new Dictionary<string, int>();
            foreach (var tier in TierHeaders)
            {
                TotalTierStats[tier] = AggregatedDailyStats.Sum(d => d.TierCounts[tier]);
            }

            TotalRedemptionStats = new Dictionary<string, int>();
            foreach (var type in RedemptionHeaders)
            {
                TotalRedemptionStats[type] = AggregatedDailyStats.Sum(d => d.RedemptionCounts[type]);
            }
        }
    }
}