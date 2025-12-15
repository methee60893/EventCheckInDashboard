using EventCheckInDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EventCheckInDashboard.Pages
{
    public class IndexModel : PageModel
    {
        private readonly EventService _service;
        public List<ActivityInfo> Activities { get; set; }
        public int GrandTotalRegistrations { get; set; } // เพิ่มยอดรวมทั้งงานไว้โชว์หน้าแรกได้

        public IndexModel(EventService service)
        {
            _service = service;
        }

        public void OnGet()
        {
            Activities = _service.GetActivities();
            GrandTotalRegistrations = 0;

            // Loop คำนวณยอดรวมของแต่ละกิจกรรมเพื่อมาโชว์ที่ Card หน้าแรก
            foreach (var act in Activities)
            {
                // แก้ไข: เรียก method GetDailyDetails แทน GetDailyStats (ตาม Code ใหม่)
                var stats = _service.GetDailyDetails(act.Id);

                // แก้ไข: ใช้ TotalCheckIn แทน RegistrationCount
                act.TotalRegistrations = stats.Sum(s => s.TotalCheckIn);

                // เก็บยอดรวมทั้งงาน
                GrandTotalRegistrations += act.TotalRegistrations;
            }
        }
        
    }
}