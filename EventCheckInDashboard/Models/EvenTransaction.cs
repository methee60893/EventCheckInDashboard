using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EventCheckInDashboard.Models
{
    [Table("Event_Transactions")]
    public class EventTransaction
    {
        [Key]
        public long TransactionID { get; set; }
        public int ActivityID { get; set; }
        public string ActivityName { get; set; }
        public DateTime EventDate { get; set; }
        public string MemberID { get; set; }
        public string MemberTier { get; set; } // CROWN, VEGA, etc.
        public string StaffID { get; set; }
        public string ReceiptNo { get; set; }
        public string PaymentMethod { get; set; } // RECEIPT SPENDING, CASH CARD, etc.
        public decimal SpendingAmount { get; set; }
        public int RightsEarned { get; set; }
        public string RedeemType { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}