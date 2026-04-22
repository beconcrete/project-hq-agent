using System.Globalization;
using System.Text.Json;

namespace HqAgent.Shared.Models;

public static class ContractFactsExtractor
{
    private static readonly string[] EffectiveDateKeys =
    [
        "effectiveDate", "startDate", "contractStartDate", "agreementDate", "commencementDate"
    ];

    private static readonly string[] ExpiryDateKeys =
    [
        "expiryDate", "expirationDate", "endDate", "contractEndDate", "assignmentEndDate", "terminationDate"
    ];

    private static readonly string[] AssignmentStartDateKeys =
    [
        "assignmentStartDate", "assignmentStart", "consultingStartDate", "startDate"
    ];

    private static readonly string[] AssignmentEndDateKeys =
    [
        "assignmentEndDate", "assignmentEnd", "consultingEndDate", "endDate", "expiryDate"
    ];

    private static readonly string[] NoticePeriodKeys =
    [
        "noticePeriodDays", "terminationNoticeDays", "noticeDays"
    ];

    private static readonly string[] AutoRenewalKeys =
    [
        "autoRenewal", "automaticRenewal", "renewsAutomatically"
    ];

    private static readonly string[] CounterpartyKeys =
    [
        "counterparty", "counterparties", "parties", "customer", "client", "vendor", "supplier"
    ];

    private static readonly string[] PersonKeys =
    [
        "peopleMentioned", "consultantsAssigned", "consultantNames", "consultant", "employee",
        "employees", "signatories", "customerContact", "internalOwner", "contacts"
    ];

    private static readonly string[] CustomerKeys =
    [
        "customer", "client", "customerName", "clientName"
    ];

    private static readonly string[] RiskKeys =
    [
        "riskFlags", "risks", "keyRisks"
    ];

    private static readonly string[] PaymentAmountKeys =
    [
        "paymentAmount", "amount", "fee", "fixedFee", "oneTimeFee", "contractValue",
        "hourlyRate", "dailyRate", "monthlyFee", "licenseFee", "subscriptionFee", "rate"
    ];

    private static readonly string[] PaymentCurrencyKeys =
    [
        "paymentCurrency", "currency", "feeCurrency", "rateCurrency", "contractCurrency"
    ];

    private static readonly string[] PaymentUnitKeys =
    [
        "paymentUnit", "rateUnit", "billingUnit", "unit", "priceUnit"
    ];

    private static readonly string[] PaymentTypeKeys =
    [
        "paymentType", "feeType", "pricingModel", "commercialModel", "billingModel"
    ];

    private static readonly string[] PaymentTermsKeys =
    [
        "paymentTerms", "invoicingTerms", "invoiceTerms", "billingTerms"
    ];

    public static NormalizedContractFacts Extract(ExtractionResult extraction)
    {
        if (string.IsNullOrWhiteSpace(extraction.ExtractedFields))
            return Empty();

        try
        {
            using var doc = JsonDocument.Parse(extraction.ExtractedFields);
            var root = doc.RootElement;

            var effectiveDate = FindDate(root, EffectiveDateKeys);
            var expiryDate = FindDate(root, ExpiryDateKeys);
            var noticePeriodDays = FindInt(root, NoticePeriodKeys);
            var noticeDeadline = expiryDate.HasValue && noticePeriodDays.HasValue
                ? expiryDate.Value.AddDays(-noticePeriodDays.Value)
                : FindDate(root, ["noticeDeadline", "renewalDeadline", "terminationNoticeDeadline"]);

            var counterparties = FindStrings(root, CounterpartyKeys);
            var customerName = FirstNonEmpty(FindStrings(root, CustomerKeys));
            var primaryCounterparty = !string.IsNullOrWhiteSpace(customerName)
                ? customerName
                : FirstNonEmpty(counterparties);

            var people = FindStrings(root, PersonKeys);
            var riskFlags = FindStrings(root, RiskKeys);
            var missingFields = BuildMissingFields(expiryDate, counterparties, people);
            var paymentAmount = FindDecimal(root, PaymentAmountKeys);
            var paymentCurrency = FirstNonEmpty(FindStrings(root, PaymentCurrencyKeys));
            var paymentUnit = FirstNonEmpty(FindStrings(root, PaymentUnitKeys));
            var paymentType = FirstNonEmpty(FindStrings(root, PaymentTypeKeys));

            if (string.IsNullOrWhiteSpace(paymentUnit))
                paymentUnit = InferPaymentUnit(root);

            if (string.IsNullOrWhiteSpace(paymentType))
                paymentType = InferPaymentType(root, paymentUnit);

            return new NormalizedContractFacts(
                effectiveDate,
                expiryDate,
                noticePeriodDays,
                noticeDeadline,
                FindBool(root, AutoRenewalKeys),
                primaryCounterparty,
                counterparties,
                people,
                customerName,
                FindDate(root, AssignmentStartDateKeys),
                FindDate(root, AssignmentEndDateKeys),
                paymentAmount,
                paymentCurrency,
                paymentUnit,
                paymentType,
                FirstNonEmpty(FindStrings(root, PaymentTermsKeys)),
                riskFlags,
                missingFields);
        }
        catch (JsonException)
        {
            return Empty(["extractedFieldsJson"]);
        }
    }

    private static NormalizedContractFacts Empty(IReadOnlyList<string>? missingFields = null) =>
        new(null, null, null, null, null, "", [], [], "", null, null, null, "", "", "", "", [], missingFields ?? []);

    private static IReadOnlyList<string> BuildMissingFields(
        DateTime? expiryDate,
        IReadOnlyList<string> counterparties,
        IReadOnlyList<string> people)
    {
        var missing = new List<string>();
        if (!expiryDate.HasValue) missing.Add("expiryDate");
        if (counterparties.Count == 0) missing.Add("counterparty");
        if (people.Count == 0) missing.Add("peopleMentioned");
        return missing;
    }

    private static DateTime? FindDate(JsonElement root, IReadOnlyCollection<string> keys)
    {
        foreach (var value in FindValues(root, keys))
        {
            if (value.ValueKind == JsonValueKind.String &&
                TryParseDate(value.GetString(), out var date))
                return date;
        }

        return null;
    }

    private static int? FindInt(JsonElement root, IReadOnlyCollection<string> keys)
    {
        foreach (var value in FindValues(root, keys))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return number;
            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return number;
        }

        return null;
    }

    private static decimal? FindDecimal(JsonElement root, IReadOnlyCollection<string> keys)
    {
        foreach (var value in FindValues(root, keys))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                return number;

            if (value.ValueKind != JsonValueKind.String)
                continue;

            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var cleaned = NormalizeNumberText(text);

            if (decimal.TryParse(
                    cleaned,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out number))
                return number;
        }

        return null;
    }

    private static bool? FindBool(JsonElement root, IReadOnlyCollection<string> keys)
    {
        foreach (var value in FindValues(root, keys))
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();
            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static IReadOnlyList<string> FindStrings(JsonElement root, IReadOnlyCollection<string> keys)
    {
        var values = new List<string>();
        foreach (var value in FindValues(root, keys))
            AddStrings(value, values);

        return values
            .Select(NormalizeWhitespace)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();
    }

    private static IEnumerable<JsonElement> FindValues(JsonElement element, IReadOnlyCollection<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (keys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    yield return property.Value;

                foreach (var child in FindValues(property.Value, keys))
                    yield return child;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var child in FindValues(item, keys))
                    yield return child;
            }
        }
    }

    private static void AddStrings(JsonElement value, List<string> values)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                values.Add(value.GetString() ?? "");
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    AddStrings(item, values);
                break;
            case JsonValueKind.Object:
                AddObjectName(value, values);
                break;
        }
    }

    private static void AddObjectName(JsonElement value, List<string> values)
    {
        foreach (var key in new[] { "name", "company", "legalName", "person", "employee", "consultant", "party" })
        {
            if (value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String)
            {
                values.Add(property.GetString() ?? "");
                return;
            }
        }

        foreach (var property in value.EnumerateObject())
            AddStrings(property.Value, values);
    }

    private static bool TryParseDate(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out date);
    }

    private static string FirstNonEmpty(IEnumerable<string> values) =>
        values.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";

    private static string NormalizeWhitespace(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeNumberText(string value)
    {
        var cleaned = new string(value
            .Where(c => char.IsDigit(c) || c is '.' or ',' or '-')
            .ToArray());

        var lastDot = cleaned.LastIndexOf('.');
        var lastComma = cleaned.LastIndexOf(',');
        var decimalSeparator = Math.Max(lastDot, lastComma);
        if (decimalSeparator < 0)
            return cleaned;

        var digitsAfter = cleaned.Length - decimalSeparator - 1;
        if (digitsAfter == 3 && cleaned.Count(c => c is '.' or ',') == 1)
            return cleaned.Replace(".", "").Replace(",", "");

        var integer = cleaned[..decimalSeparator].Replace(".", "").Replace(",", "");
        var decimals = cleaned[(decimalSeparator + 1)..].Replace(".", "").Replace(",", "");
        return $"{integer}.{decimals}";
    }

    private static string InferPaymentUnit(JsonElement root)
    {
        if (FindDecimal(root, ["hourlyRate"]) is not null) return "hour";
        if (FindDecimal(root, ["dailyRate"]) is not null) return "day";
        if (FindDecimal(root, ["monthlyFee"]) is not null) return "month";
        if (FindDecimal(root, ["oneTimeFee", "fixedFee"]) is not null) return "one_time";
        return "";
    }

    private static string InferPaymentType(JsonElement root, string paymentUnit)
    {
        if (FindDecimal(root, ["oneTimeFee", "fixedFee"]) is not null || paymentUnit == "one_time")
            return "fixed_fee";
        if (FindDecimal(root, ["hourlyRate", "dailyRate", "monthlyFee", "rate"]) is not null)
            return "rate";
        return "";
    }
}
