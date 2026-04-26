namespace HqAgent.Shared.Models;

public enum ForecastStatus
{
    Booked,
    Unbooked,
}

public class ForecastResult
{
    public string ConsultantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SeniorityLevel { get; set; } = string.Empty;
    public string ForecastBasis { get; set; } = string.Empty;
    public ForecastStatus Status { get; set; }
    public double BillableHours { get; set; }
    public double AvailableHoursInMonth { get; set; }
    public double HoursBeforeUtilization { get; set; }
    public int WorkingDaysInMonth { get; set; }
    public int WorkingDaysIncluded { get; set; }
    public decimal UtilizationApplied { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal EstimatedRevenueSEK { get; set; }
    public string? ContractStartDate { get; set; }
    public string? ContractEndDate { get; set; }
    public string CalculationDetails { get; set; } = string.Empty;
}

public class MonthlyForecastSummary
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalBookedRevenue { get; set; }
    public decimal TotalUnbookedEstimate { get; set; }
    public int BookedHeadcount { get; set; }
    public int UnbookedHeadcount { get; set; }
    public List<ForecastResult> Consultants { get; set; } = [];
}
