using HqAgent.Agents.HR.Services;
using HqAgent.Agents.SalesForecast.Services;
using HqAgent.Shared.Models;
using Xunit;

namespace HqAgent.Agents.Tests;

public class SalesForecastStructuredResponderTests
{
    private static readonly DateOnly Today = new(2026, 4, 27);

    [Fact]
    public async Task MonthlyOverview_StaysAtOverviewLevel()
    {
        var responder = CreateResponder();

        var response = await responder.TryRespondAsync(
            "What does May 2026 look like?",
            [],
            Today,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("Planned revenue", response!.Text);
        Assert.DoesNotContain("Anna Lindström", response.Text);
        Assert.DoesNotContain("Anna Svensson", response.Text);
    }

    [Fact]
    public async Task MonthlyBreakdown_UsesPlannedRevenueIncludingUnbookedEstimate()
    {
        var responder = CreateResponder();

        var response = await responder.TryRespondAsync(
            "Tell me what the rest of 2026 looks like, broken down per month",
            [],
            Today,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("September 2026", response!.Text);
        Assert.Contains("Planned revenue: 341,600 SEK", response.Text);
        Assert.Contains("Unbooked estimate: 95,200 SEK", response.Text);
    }

    [Fact]
    public async Task ConsultantDetail_ExplainsPartialMonthContract()
    {
        var responder = CreateResponder();

        var history = new List<ChatTurnEntity>
        {
            new() { Role = "user", Content = "What does May 2026 look like?" },
        };

        var response = await responder.TryRespondAsync(
            "Why does Anna Svensson have fewer hours?",
            history,
            Today,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("2026-05-15", response!.Text);
        Assert.Contains("88", response.Text);
        Assert.Contains("100%", response.Text);
    }

    [Fact]
    public async Task SwedishQuestion_ReturnsSwedishResponse()
    {
        var responder = CreateResponder();

        var response = await responder.TryRespondAsync(
            "Vad ser maj 2026 ut som?",
            [],
            Today,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("Planerad intäkt", response!.Text);
        Assert.DoesNotContain("Planned revenue", response.Text);
    }

    private static SalesForecastStructuredResponder CreateResponder()
    {
        return new SalesForecastStructuredResponder(
            new FakeSalesForecastIntelligence(),
            new FakeHrIntelligence());
    }

    private sealed class FakeSalesForecastIntelligence : ISalesForecastIntelligence
    {
        public Task<MonthlyForecastSummary> GetMonthlyForecastAsync(int year, int month, CancellationToken ct)
        {
            return Task.FromResult(month switch
            {
                5 => BuildMay(),
                9 => BuildSeptember(),
                10 => BuildOctober(),
                11 => BuildNovember(),
                12 => BuildDecember(),
                6 => BuildSimpleMonth(year, month, 431_200m, 431_200m, 0m, 352d, 2, 0),
                7 => BuildSimpleMonth(year, month, 450_800m, 450_800m, 0m, 368d, 2, 0),
                8 => BuildSimpleMonth(year, month, 420_000m, 420_000m, 0m, 336d, 2, 0),
                _ => throw new InvalidOperationException($"Unexpected month {month}"),
            });
        }

        public Task<ForecastResult?> GetConsultantForecastAsync(string consultantName, int year, int month, CancellationToken ct)
        {
            var summary = month switch
            {
                5 => BuildMay(),
                9 => BuildSeptember(),
                _ => null,
            };

            return Task.FromResult(summary?.Consultants.FirstOrDefault(c =>
                string.Equals(c.Name, consultantName, StringComparison.OrdinalIgnoreCase)));
        }

        private static MonthlyForecastSummary BuildMay()
        {
            return new MonthlyForecastSummary
            {
                Year = 2026,
                Month = 5,
                TotalBookedRevenue = 323_600m,
                TotalUnbookedEstimate = 0m,
                TotalPlannedRevenue = 323_600m,
                TotalBookedHours = 256d,
                TotalUnbookedHours = 0d,
                TotalPlannedHours = 256d,
                AverageBookedHourlyRate = 1_264m,
                AveragePlannedHourlyRate = 1_264m,
                BookedHeadcount = 2,
                UnbookedHeadcount = 0,
                Consultants =
                [
                    new ForecastResult
                    {
                        ConsultantId = "1",
                        Name = "Anna Lindström",
                        Status = ForecastStatus.Booked,
                        HourlyRateBasis = "contract",
                        HourlyRate = 1_350m,
                        BillableHours = 168d,
                        UtilizationApplied = 1.0m,
                        EstimatedRevenueSEK = 226_800m,
                        ContractStartDate = "2026-05-01",
                        ContractEndDate = "2026-08-31",
                        CalculationDetails = "Anna Lindström: The contract covers the full month from 2026-05-01 to 2026-05-31. 21 working days are included, giving 168 available hours before utilization. Booked work uses 100% utilization, then multiplies by 1350 SEK/hour."
                    },
                    new ForecastResult
                    {
                        ConsultantId = "2",
                        Name = "Anna Svensson",
                        Status = ForecastStatus.Booked,
                        HourlyRateBasis = "contract",
                        HourlyRate = 1_100m,
                        BillableHours = 88d,
                        UtilizationApplied = 1.0m,
                        EstimatedRevenueSEK = 96_800m,
                        ContractStartDate = "2026-05-15",
                        ContractEndDate = "2026-07-31",
                        CalculationDetails = "Anna Svensson: The contract only overlaps part of the month, from 2026-05-15 to 2026-05-31. 11 working days are included, giving 88 available hours before utilization. Booked work uses 100% utilization, then multiplies by 1100 SEK/hour."
                    }
                ]
            };
        }

        private static MonthlyForecastSummary BuildSeptember()
        {
            return new MonthlyForecastSummary
            {
                Year = 2026,
                Month = 9,
                TotalBookedRevenue = 246_400m,
                TotalUnbookedEstimate = 95_200m,
                TotalPlannedRevenue = 341_600m,
                TotalBookedHours = 176d,
                TotalUnbookedHours = 105.6d,
                TotalPlannedHours = 281.6d,
                AverageBookedHourlyRate = 1_400m,
                AveragePlannedHourlyRate = 1_213m,
                BookedHeadcount = 1,
                UnbookedHeadcount = 1,
                Consultants =
                [
                    new ForecastResult
                    {
                        ConsultantId = "1",
                        Name = "Anna Lindström",
                        Status = ForecastStatus.Booked,
                        HourlyRateBasis = "contract",
                        HourlyRate = 1_400m,
                        BillableHours = 176d,
                        UtilizationApplied = 1.0m,
                        EstimatedRevenueSEK = 246_400m,
                        ContractStartDate = "2026-09-01",
                        ContractEndDate = "2026-12-31",
                        CalculationDetails = "Booked full month."
                    },
                    new ForecastResult
                    {
                        ConsultantId = "2",
                        Name = "Anna Svensson",
                        Status = ForecastStatus.Unbooked,
                        HourlyRateBasis = "seniority-benchmark",
                        HourlyRate = 900m,
                        BillableHours = 105.6d,
                        UtilizationApplied = 0.60m,
                        EstimatedRevenueSEK = 95_200m,
                        CalculationDetails = "Benchmark revenue after contract end."
                    }
                ]
            };
        }

        private static MonthlyForecastSummary BuildOctober() =>
            BuildSimpleMonth(2026, 10, 387_200m, 257_600m, 129_600m, 340.4d, 1, 1);

        private static MonthlyForecastSummary BuildNovember() =>
            BuildSimpleMonth(2026, 11, 350_800m, 235_200m, 115_600m, 304d, 1, 1);

        private static MonthlyForecastSummary BuildDecember() =>
            BuildSimpleMonth(2026, 12, 343_280m, 257_600m, 85_680m, 207.2d, 1, 1);

        private static MonthlyForecastSummary BuildSimpleMonth(
            int year,
            int month,
            decimal plannedRevenue,
            decimal bookedRevenue,
            decimal unbookedRevenue,
            double plannedHours,
            int bookedHeadcount,
            int unbookedHeadcount)
        {
            return new MonthlyForecastSummary
            {
                Year = year,
                Month = month,
                TotalBookedRevenue = bookedRevenue,
                TotalUnbookedEstimate = unbookedRevenue,
                TotalPlannedRevenue = plannedRevenue,
                TotalBookedHours = bookedHeadcount == 0 ? 0d : plannedHours - (unbookedHeadcount == 0 ? 0d : 100d),
                TotalUnbookedHours = unbookedHeadcount == 0 ? 0d : 100d,
                TotalPlannedHours = plannedHours,
                AverageBookedHourlyRate = bookedRevenue == 0m ? 0m : Decimal.Round(bookedRevenue / (decimal)(bookedHeadcount == 0 ? 1 : plannedHours - (unbookedHeadcount == 0 ? 0d : 100d)), 2),
                AveragePlannedHourlyRate = plannedRevenue == 0m ? 0m : Decimal.Round(plannedRevenue / (decimal)plannedHours, 2),
                BookedHeadcount = bookedHeadcount,
                UnbookedHeadcount = unbookedHeadcount,
                Consultants =
                [
                    new ForecastResult
                    {
                        ConsultantId = "1",
                        Name = "Anna Lindström",
                        Status = ForecastStatus.Booked,
                        HourlyRateBasis = "contract",
                        HourlyRate = 1_400m,
                        BillableHours = bookedHeadcount == 0 ? 0d : plannedHours - (unbookedHeadcount == 0 ? 0d : 100d),
                        UtilizationApplied = 1.0m,
                        EstimatedRevenueSEK = bookedRevenue,
                        ContractStartDate = $"{year}-{month:D2}-01",
                        ContractEndDate = "2026-12-31",
                        CalculationDetails = "Booked."
                    },
                    new ForecastResult
                    {
                        ConsultantId = "2",
                        Name = "Anna Svensson",
                        Status = unbookedHeadcount == 0 ? ForecastStatus.Booked : ForecastStatus.Unbooked,
                        HourlyRateBasis = unbookedHeadcount == 0 ? "contract" : "seniority-benchmark",
                        HourlyRate = unbookedHeadcount == 0 ? 1_100m : 900m,
                        BillableHours = unbookedHeadcount == 0 ? 100d : 100d,
                        UtilizationApplied = unbookedHeadcount == 0 ? 1.0m : 0.85m,
                        EstimatedRevenueSEK = unbookedHeadcount == 0 ? plannedRevenue - bookedRevenue : unbookedRevenue,
                        CalculationDetails = unbookedHeadcount == 0 ? "Booked." : "Benchmark."
                    }
                ]
            };
        }
    }

    private sealed class FakeHrIntelligence : IHRIntelligence
    {
        private static readonly IReadOnlyList<EmployeeSummary> Employees =
        [
            new("1", "Anna Lindström", "anna.lindstrom@example.com", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), "active", null, 0m, 0m, "Senior", 30),
            new("2", "Anna Svensson", "anna.svensson@example.com", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), "active", null, 0m, 0m, "Junior", 30),
        ];

        public Task<IReadOnlyList<EmployeeSummary>> ListEmployeesAsync(CancellationToken ct) => Task.FromResult(Employees);
        public Task<IReadOnlyList<EmployeeSummary>> FindEmployeesAsync(string nameOrEmail, CancellationToken ct) => Task.FromResult(Employees);
        public Task<EmployeeSummary> AddEmployeeAsync(AddEmployeeRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<EmployeeSummary?> UpdateEmployeeAsync(string employeeId, UpdateEmployeeRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<EmployeeSummary?> OffboardEmployeeAsync(string employeeId, DateTimeOffset offboardDate, CancellationToken ct) => throw new NotSupportedException();
        public Task<SalaryResult?> CalculateSalaryAsync(string employeeId, decimal hoursBilled, CancellationToken ct) => throw new NotSupportedException();
        public Task<HRConfigSummary> GetHRConfigAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
