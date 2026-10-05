using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace Immo24AiDemo;

// ============================================================================
// DOMAIN MODELS & DTOs
// ============================================================================

public enum CreditStatus { Unknown, Positive, NegativeEntries }
public enum RecommendationTier { TopCandidate, ReviewManually, AutoReject }

public record PropertyCriteria(
    string ListingId,
    string Title,
    decimal MonthlyWarmRent,
    bool PetsAllowed,
    int MaxHouseholdSize
);

public record SelfDisclosure(
    decimal MonthlyNetIncome,
    string EmploymentType,
    CreditStatus SchufaStatus,
    int HouseholdSize,
    bool HasPets,
    bool IsSmoker,
    bool AllDocumentsUploaded
);

public record ApplicantSubmissionRequest(
    string ApplicantName,
    string Email,
    string InquiryMessage,
    SelfDisclosure Disclosure,
    PropertyCriteria TargetProperty
);

public record AiInsightResult(
    int SoftSkillScore,
    string Summary,
    List<string> ExtractedHighlights,
    List<string> PotentialRedFlags
);

public record ApplicationEvaluationResult(
    Guid ApplicationId,
    string ListingId,
    string ApplicantName,
    int HardFactsScore,
    int AiSoftScore,
    int TotalScore,
    decimal RentToIncomeRatio,
    RecommendationTier Recommendation,
    AiInsightResult AiInsights,
    DateTimeOffset EvaluatedAt
);

// ============================================================================
// SERVICES & SCORING ENGINE
// ============================================================================

public interface IAiEvaluationService
{
    Task<AiInsightResult> AnalyzeInquiryAsync(ApplicantSubmissionRequest request, CancellationToken ct = default);
}

public class MockAiEvaluationService : IAiEvaluationService
{
    public async Task<AiInsightResult> AnalyzeInquiryAsync(ApplicantSubmissionRequest request, CancellationToken ct = default)
    {
        await Task.Delay(250, ct); // Simuliert kurze LLM-Latenz

        var msg = request.InquiryMessage.ToLowerInvariant();
        var highlights = new List<string>();
        var redFlags = new List<string>();
        int score = 60;

        if (msg.Length > 100)
        {
            score += 15;
            highlights.Add("Ausführliches, individuell formuliertes Anschreiben");
        }
        else
        {
            score -= 10;
            redFlags.Add("Sehr knappe Standard-Anfrage ohne persönlichen Bezug");
        }

        if (msg.Contains("langfristig") || msg.Contains("ruhig") || msg.Contains("nichtraucher"))
        {
            score += 15;
            highlights.Add("Signalisiert Interesse an langfristigem, ruhigem Mietverhältnis");
        }

        if (request.Disclosure.EmploymentType.Contains("Unbefristet", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
            highlights.Add("Stabiles Beschäftigungsverhältnis (Unbefristet)");
        }

        if (!request.Disclosure.AllDocumentsUploaded)
        {
            redFlags.Add("Bewerbermappe unvollständig (Nachweise fehlen teilweise)");
        }

        if (request.Disclosure.HasPets && !request.TargetProperty.PetsAllowed)
        {
            redFlags.Add("Haustier angegeben trotz Ausschluss im Exposé");
        }

        score = Math.Clamp(score, 0, 100);

        string summary = score >= 75
            ? "Sehr strukturierter und verlässlicher Gesamteindruck mit hoher Passung zum Objekt."
            : "Solide Anfrage, jedoch mit offenen Rückfragen zur Dokumentenvollständigkeit oder Objektpassung.";

        return new AiInsightResult(score, summary, highlights, redFlags);
    }
}

public interface IApplicantScoringPipeline
{
    Task<ApplicationEvaluationResult> ProcessApplicationAsync(ApplicantSubmissionRequest req, CancellationToken ct = default);
}

public class ApplicantScoringPipeline(IAiEvaluationService aiService) : IApplicantScoringPipeline
{
    public async Task<ApplicationEvaluationResult> ProcessApplicationAsync(
        ApplicantSubmissionRequest req,
        CancellationToken ct = default)
    {
        decimal rentRatio = req.Disclosure.MonthlyNetIncome > 0
            ? Math.Round((req.TargetProperty.MonthlyWarmRent / req.Disclosure.MonthlyNetIncome) * 100, 1)
            : 100m;

        int hardScore = CalculateHardScore(req.Disclosure, req.TargetProperty, rentRatio);
        var aiResult = await aiService.AnalyzeInquiryAsync(req, ct);
        int totalScore = (int)Math.Round((hardScore * 0.65) + (aiResult.SoftSkillScore * 0.35));
        var tier = DetermineRecommendation(totalScore, req.Disclosure, req.TargetProperty, rentRatio);

        return new ApplicationEvaluationResult(
            ApplicationId: Guid.NewGuid(),
            ListingId: req.TargetProperty.ListingId,
            ApplicantName: req.ApplicantName,
            HardFactsScore: hardScore,
            AiSoftScore: aiResult.SoftSkillScore,
            TotalScore: totalScore,
            RentToIncomeRatio: rentRatio,
            Recommendation: tier,
            AiInsights: aiResult,
            EvaluatedAt: DateTimeOffset.UtcNow
        );
    }

    private static int CalculateHardScore(SelfDisclosure d, PropertyCriteria p, decimal rentRatio)
    {
        int score = 100;
        if (rentRatio > 45) score -= 50;
        else if (rentRatio > 35) score -= 25;
        else if (rentRatio > 30) score -= 10;

        if (d.SchufaStatus == CreditStatus.NegativeEntries) score -= 60;
        else if (d.SchufaStatus == CreditStatus.Unknown) score -= 20;

        if (!d.AllDocumentsUploaded) score -= 15;
        if (d.HasPets && !p.PetsAllowed) score -= 30;
        if (d.HouseholdSize > p.MaxHouseholdSize) score -= 25;

        return Math.Clamp(score, 0, 100);
    }

    private static RecommendationTier DetermineRecommendation(
        int totalScore, SelfDisclosure d, PropertyCriteria p, decimal rentRatio)
    {
        if (d.SchufaStatus == CreditStatus.NegativeEntries || rentRatio > 50 || d.HouseholdSize > p.MaxHouseholdSize)
            return RecommendationTier.AutoReject;

        return totalScore switch
        {
            >= 80 => RecommendationTier.TopCandidate,
            >= 55 => RecommendationTier.ReviewManually,
            _ => RecommendationTier.AutoReject
        };
    }
}

// ============================================================================
// REPOSITORY (mit Seed-Daten für den Sofort-Start)
// ============================================================================

public interface IApplicationRepository
{
    void Save(ApplicationEvaluationResult result);
    IReadOnlyList<ApplicationEvaluationResult> GetByListing(string listingId);
}

public class InMemoryApplicationRepository : IApplicationRepository
{
    private readonly ConcurrentBag<ApplicationEvaluationResult> _store = [];

    public InMemoryApplicationRepository()
    {
        // Initiale Seed-Daten für das Dashboard
        _store.Add(new(
            Guid.NewGuid(), "IMMO-24-9981", "Julia Meyer & Lukas Weber",
            100, 95, 98, 26.5m, RecommendationTier.TopCandidate,
            new(95, "Sehr strukturierter und verlässlicher Gesamteindruck. Doppeltes Nettoeinkommen und vollständige Bewerbermappe.",
                ["Beide in unbefristeter Festanstellung", "Schufa-BonitätsCheck lückenlos positiv", "Nichtraucher, langfristige Mietabsicht betont"], []),
            DateTimeOffset.Now.AddHours(-3)));

        _store.Add(new(
            Guid.NewGuid(), "IMMO-24-9981", "Dr. Markus Lindner",
            90, 82, 87, 29.3m, RecommendationTier.TopCandidate,
            new(82, "Finanziell sehr stark aufgestellt, berufsbedingter Zuzug. Anschreiben kurz, aber präzise.",
                ["Hohes Einzel-Nettoeinkommen", "Arbeitgeberwechsel zum Universitätsklinikum belegt"],
                ["Aktuell noch in Probezeit beim neuen Arbeitgeber"]),
            DateTimeOffset.Now.AddHours(-5)));

        _store.Add(new(
            Guid.NewGuid(), "IMMO-24-9981", "Svenja Hartmann",
            70, 68, 69, 36.8m, RecommendationTier.ReviewManually,
            new(68, "Sympathisches und ausführliches Anschreiben, allerdings liegt die Mietbelastung leicht über dem Zielwert von 30%.",
                ["Individuelles Anschreiben mit starkem Bezug zur Wohngegend", "Positive Vorvermieterbescheinigung vorhanden"],
                ["Mietbelastungsquote bei 36,8%", "Gehaltsabrechnungen der letzten 2 Monate fehlen noch"]),
            DateTimeOffset.Now.AddHours(-8)));

        _store.Add(new(
            Guid.NewGuid(), "IMMO-24-9981", "Kevin Scholz",
            25, 30, 27, 52.0m, RecommendationTier.AutoReject,
            new(30, "Standard-Einzeiler ohne Unterlagen. Harte Kriterien des Exposés werden nicht erfüllt.",
                [], ["Mietbelastungsquote über 50%", "Haustier angegeben trotz Ausschluss im Exposé", "Keine Schufa-Auskunft hochgeladen"]),
            DateTimeOffset.Now.AddDays(-1)));
    }

    public void Save(ApplicationEvaluationResult result) => _store.Add(result);

    public IReadOnlyList<ApplicationEvaluationResult> GetByListing(string listingId) =>
        _store.Where(x => x.ListingId.Equals(listingId, StringComparison.OrdinalIgnoreCase))
              .OrderByDescending(x => x.TotalScore)
              .ToList();
}