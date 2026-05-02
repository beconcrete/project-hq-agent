using System.Text;
using System.Text.Json;
using HqAgent.Shared.Models;

namespace HqAgent.Agents.HQ.Services;

public class EmbeddingTextBuilder
{
    public string Build(EmployeeEntity e)
    {
        var sb = new StringBuilder(e.FullName);
        if (!string.IsNullOrWhiteSpace(e.SeniorityLevel)) sb.Append($", {e.SeniorityLevel}");
        sb.Append($", {e.Status}");
        return sb.ToString();
    }

    public string Build(CustomerEntity c)
    {
        var sb = new StringBuilder(c.Name);
        if (!string.IsNullOrWhiteSpace(c.Country)) sb.Append($", {c.Country}");
        if (!string.IsNullOrWhiteSpace(c.PrimaryContactName)) sb.Append($", contact: {c.PrimaryContactName}");
        if (!string.IsNullOrWhiteSpace(c.PrimaryContactEmail)) sb.Append($", {c.PrimaryContactEmail}");
        if (!string.IsNullOrWhiteSpace(c.Notes)) sb.Append($", {c.Notes}");
        return sb.ToString();
    }

    public string Build(ProjectEntity p, IEnumerable<string> employeeNames)
    {
        var sb = new StringBuilder(p.Name);
        if (!string.IsNullOrWhiteSpace(p.CustomerName)) sb.Append($", customer: {p.CustomerName}");
        if (!string.IsNullOrWhiteSpace(p.Description)) sb.Append($", {p.Description}");
        sb.Append($", {p.Status}");
        var names = employeeNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names.Count > 0) sb.Append($", team: {string.Join(", ", names)}");
        return sb.ToString();
    }

    public string Build(ContractEntity c)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(c.DocumentType)) sb.Append(c.DocumentType);

        var counterparties = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.PrimaryCounterparty)) counterparties.Add(c.PrimaryCounterparty);
        counterparties.AddRange(DeserializeStrings(c.CounterpartyNames));
        counterparties.AddRange(DeserializeStrings(c.LinkedCustomerNames));
        var distinctCounterparties = counterparties
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctCounterparties.Count > 0)
            sb.Append($", {string.Join(", ", distinctCounterparties)}");

        if (c.ExpiryDate.HasValue)        sb.Append($", expires {c.ExpiryDate.Value:yyyy-MM-dd}");
        if (c.NoticePeriodDays.HasValue)  sb.Append($", notice {c.NoticePeriodDays} days");
        if (c.AutoRenewal.HasValue)       sb.Append(c.AutoRenewal.Value ? ", auto-renewal" : ", no auto-renewal");

        var people = DeserializeStrings(c.PeopleMentioned).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (people.Count > 0) sb.Append($", people: {string.Join(", ", people)}");

        if (c.PaymentAmount.HasValue)
            sb.Append($", {c.PaymentAmount:F0} {c.PaymentCurrency}/{c.PaymentUnit}");

        var flags = DeserializeStrings(c.RiskFlags).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (flags.Count > 0) sb.Append($", risk: {string.Join(", ", flags)}");

        return sb.ToString();
    }

    public static string Snippet(string text) =>
        text.Length <= 150 ? text : text[..150];

    private static string[] DeserializeStrings(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }
}
