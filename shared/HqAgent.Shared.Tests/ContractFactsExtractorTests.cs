using HqAgent.Shared.Models;
using Xunit;

namespace HqAgent.Shared.Tests;

public class ContractFactsExtractorTests
{
    [Fact]
    public void ExtractsConsultingAssignmentFacts()
    {
        var extraction = new ExtractionResult(
            "Consulting Agreement",
            0.94,
            """
            {
              "customer": "Acme AB",
              "supplier": "Be Concrete AB",
              "consultantNames": ["Bjorn Eriksen"],
              "assignmentStartDate": "2026-05-01",
              "assignmentEndDate": "2026-10-31",
              "noticePeriodDays": 30,
              "autoRenewal": false,
              "hourlyRate": 1500,
              "currency": "SEK",
              "paymentTerms": "30 days net",
              "riskFlags": ["Customer may terminate for convenience"]
            }
            """,
            0.91,
            "gpt-4.1",
            false);

        var facts = ContractFactsExtractor.Extract(extraction);

        Assert.Equal("Acme AB", facts.PrimaryCounterparty);
        Assert.Equal(new DateTime(2026, 10, 31), facts.ExpiryDate);
        Assert.Equal(new DateTime(2026, 10, 1), facts.NoticeDeadline);
        Assert.Equal(30, facts.NoticePeriodDays);
        Assert.False(facts.AutoRenewal);
        Assert.Equal(1500m, facts.PaymentAmount);
        Assert.Equal("SEK", facts.PaymentCurrency);
        Assert.Equal("hour", facts.PaymentUnit);
        Assert.Equal("rate", facts.PaymentType);
        Assert.Equal("30 days net", facts.PaymentTerms);
        Assert.Contains("Bjorn Eriksen", facts.PeopleMentioned);
        Assert.Contains("Acme AB", facts.CounterpartyNames);
        Assert.Contains("Customer may terminate for convenience", facts.RiskFlags);
    }

    [Fact]
    public void ExtractsNdaFacts()
    {
        var extraction = new ExtractionResult(
            "Mutual NDA",
            0.97,
            """
            {
              "parties": [
                { "name": "Be Concrete AB" },
                { "name": "Contoso Ltd" }
              ],
              "effectiveDate": "2026-01-15",
              "confidentialityPeriod": "5 years",
              "signatories": ["Anna Andersson", "Bjorn Eriksen"]
            }
            """,
            0.9,
            "gpt-4.1",
            false);

        var facts = ContractFactsExtractor.Extract(extraction);

        Assert.Equal(new DateTime(2026, 1, 15), facts.EffectiveDate);
        Assert.Contains("Contoso Ltd", facts.CounterpartyNames);
        Assert.Contains("Bjorn Eriksen", facts.PeopleMentioned);
        Assert.Contains("expiryDate", facts.MissingFields);
    }

    [Fact]
    public void ExtractsOneTimeFeeFacts()
    {
        var extraction = new ExtractionResult(
            "Inspirational Talk Agreement",
            0.93,
            """
            {
              "customer": "Nox Consulting",
              "supplier": "Be Concrete AB",
              "peopleMentioned": ["Björn Eriksen"],
              "eventDate": "2026-04-20",
              "oneTimeFee": "20000 SEK",
              "currency": "SEK",
              "paymentTerms": "Due after delivery"
            }
            """,
            0.88,
            "gpt-4.1",
            false);

        var facts = ContractFactsExtractor.Extract(extraction);

        Assert.Equal("Nox Consulting", facts.PrimaryCounterparty);
        Assert.Equal(20000m, facts.PaymentAmount);
        Assert.Equal("SEK", facts.PaymentCurrency);
        Assert.Equal("one_time", facts.PaymentUnit);
        Assert.Equal("fixed_fee", facts.PaymentType);
        Assert.Contains("Björn Eriksen", facts.PeopleMentioned);
    }
}
