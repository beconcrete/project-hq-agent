using HqAgent.Agents.HR.Services;
using HqAgent.Shared.Models;

namespace HqAgent.Agents.SalesForecast.Services;

public class SalesForecastStructuredResponder
{
    private readonly ISalesForecastIntelligence _forecast;
    private readonly IHRIntelligence _hr;

    public SalesForecastStructuredResponder(
        ISalesForecastIntelligence forecast,
        IHRIntelligence hr)
    {
        _forecast = forecast;
        _hr = hr;
    }

    public async Task<SalesForecastStructuredResponse?> TryRespondAsync(
        string message,
        IReadOnlyList<ChatTurnEntity> history,
        DateOnly today,
        CancellationToken ct)
    {
        var consultants = await _hr.ListEmployeesAsync(ct);
        var consultantNames = consultants.Select(c => c.FullName).ToArray();
        var request = SalesForecastQuestionInterpreter.TryInterpret(message, history, consultantNames, today);
        if (request is null)
            return null;

        return request.Mode switch
        {
            SalesForecastResponseMode.MonthlyOverview => await BuildMonthlyOverviewAsync(request, ct),
            SalesForecastResponseMode.MonthlyBreakdown => await BuildMonthlyBreakdownAsync(request, ct),
            SalesForecastResponseMode.BookedVsEstimated => await BuildBookedVsEstimatedAsync(request, ct),
            SalesForecastResponseMode.ConsultantDetail => await BuildConsultantDetailAsync(request, ct),
            _ => null,
        };
    }

    private async Task<SalesForecastStructuredResponse> BuildMonthlyOverviewAsync(
        SalesForecastStructuredRequest request,
        CancellationToken ct)
    {
        var period = request.Periods[0];
        var summary = await _forecast.GetMonthlyForecastAsync(period.Year, period.Month, ct);
        var validated = SalesForecastResponseValidator.ValidateMonthlySummary(summary);
        return SalesForecastResponseBuilder.BuildMonthlyOverview(request.Language, validated);
    }

    private async Task<SalesForecastStructuredResponse> BuildMonthlyBreakdownAsync(
        SalesForecastStructuredRequest request,
        CancellationToken ct)
    {
        var summaries = new List<MonthlyOverviewData>(request.Periods.Count);
        foreach (var period in request.Periods)
        {
            var summary = await _forecast.GetMonthlyForecastAsync(period.Year, period.Month, ct);
            summaries.Add(SalesForecastResponseValidator.ValidateMonthlySummary(summary));
        }

        return SalesForecastResponseBuilder.BuildMonthlyBreakdown(request.Language, summaries);
    }

    private async Task<SalesForecastStructuredResponse> BuildBookedVsEstimatedAsync(
        SalesForecastStructuredRequest request,
        CancellationToken ct)
    {
        var period = request.Periods[0];
        var summary = await _forecast.GetMonthlyForecastAsync(period.Year, period.Month, ct);
        var validated = SalesForecastResponseValidator.ValidateMonthlySummary(summary);
        return SalesForecastResponseBuilder.BuildBookedVsEstimated(request.Language, validated);
    }

    private async Task<SalesForecastStructuredResponse> BuildConsultantDetailAsync(
        SalesForecastStructuredRequest request,
        CancellationToken ct)
    {
        var period = request.Periods[0];
        var summary = await _forecast.GetMonthlyForecastAsync(period.Year, period.Month, ct);
        var consultant = request.ConsultantName
            ?? throw new InvalidOperationException("Consultant detail requests require a consultant name.");
        var forecast = await _forecast.GetConsultantForecastAsync(consultant, period.Year, period.Month, ct)
            ?? throw new InvalidOperationException($"Could not find forecast data for {consultant} in {period.Year}-{period.Month:D2}.");
        var validated = SalesForecastResponseValidator.ValidateConsultantDetail(consultant, summary, forecast);
        return SalesForecastResponseBuilder.BuildConsultantDetail(request.Language, validated);
    }
}
