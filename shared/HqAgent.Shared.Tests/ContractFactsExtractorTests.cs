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
}
