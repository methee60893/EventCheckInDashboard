using Microsoft.EntityFrameworkCore;
using EventCheckInDashboard.Models;

namespace EventCheckInDashboard.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<EventTransaction> EventTransactions { get; set; }
    }
}