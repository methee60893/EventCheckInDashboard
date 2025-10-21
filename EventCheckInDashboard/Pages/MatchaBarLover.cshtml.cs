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
    public class MatchaBarLoverModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<MatchaBarLoverModel> _logger;

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
        public MatchaBarLoverModel(IConfiguration configuration, ILogger<MatchaBarLoverModel> logger)
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
                int stationId = 6;

                //await LoadSummaryAutualRightDataAsync(connectionString, stationId);
                //await LoadSummaryMemberDataAsync(connectionString, stationId);
                //await LoadChartDataAsync(connectionString, stationId);
                //await LoadSegmentPivotTableAsync(connectionString, stationId);
                //await LoadTierPivotTableAsync(connectionString, stationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching MatchaBarLover dashboard data.");
                ErrorMessage = "�Դ��ͼԴ��Ҵ㹡�ô֧����������Ѻ˹�� MatchaBarLover";
            }
        }
    }
}
